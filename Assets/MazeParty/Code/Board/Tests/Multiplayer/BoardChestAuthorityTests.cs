using System;
using System.Linq;
using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardChestAuthorityTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _root;
        private NetworkMatchState _match, _previous;
        private NetworkPlayerAvatar _player;
        private BoardTopology _topology;
        private bool _warning;
        [SetUp]
        public void SetUp()
        {
            var warning = typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning", BindingFlags.Static | BindingFlags.NonPublic);
            _warning = (bool)warning.GetValue(null); warning.SetValue(null, true);
            _previous = NetworkMatchState.Instance;
            _root = new GameObject("Chest authority fixture");
            _match = Child("Match").AddComponent<NetworkMatchState>();
            Authority(_match, true);
            typeof(NetworkMatchState).GetProperty("Instance").SetValue(null, _match);
            Set(_match, "_gameplayEnabled", true);
            _player = Child("Player").AddComponent<NetworkPlayerAvatar>();
            Authority(_player, true);
            Set(_player, "_slot", 0); Set(_player, "_boardReady", true);
            Set(_player, "_currentHealth", 100); Set(_player, "_maxHealth", 100);
            Set(_player, "_hasLogicalTile", true);
            Set(_player, "_choiceResolution", (byte)ItemChoiceResolution.DoNotUse);
            var eye = Child("Eye").transform;
            eye.position = new Vector3(0, 1, 0);
            typeof(NetworkPlayerAvatar).GetField("eyePivot", Private).SetValue(_player, eye);
            var tiles = Enumerable.Range(0, 6).Select(i =>
            {
                var tile = Child("Tile " + i).AddComponent<BoardTile>();
                tile.Configure(new Vector2Int(i, 0), i == 1 ? BoardTileType.Start :
                    i == 2 ? BoardTileType.Respawn : BoardTileType.Normal);
                tile.transform.position = new Vector3(i * 8, 0, 3);
                return tile;
            }).ToArray();
            _topology = _root.AddComponent<BoardTopology>();
            _topology.Configure(tiles, Array.Empty<BoardGate>());
            typeof(NetworkMatchState).GetField("_boardTopology", Private).SetValue(_match, _topology);
            var visual = Child("Chest").AddComponent<BoardChestVisual>();
            visual.transform.position = new Vector3(0, 0, 3);
            var trigger = visual.gameObject.AddComponent<BoxCollider>();
            trigger.center = Vector3.up; trigger.size = Vector3.one * 1.5f; trigger.isTrigger = true;
            var now = _match.SynchronizedNow;
            var flow = new BoardFlowStateMachine(); flow.Start(now - BoardFlowStateMachine.TurnOverviewDurationSeconds - BoardFlowStateMachine.DescendingDurationSeconds - 1d); flow.Tick(now);
            typeof(NetworkMatchState).GetField("_flow", Private).SetValue(_match, flow);
            Invoke("SyncFlowSnapshot", now);
            Set(_match, "_chest0", Chest(0)); Set(_match, "_chest1", Chest(1));
            Physics.SyncTransforms();
        }
        [TearDown]
        public void TearDown()
        {
            typeof(NetworkMatchState).GetProperty("Instance").SetValue(null, _previous);
            foreach (var behaviour in _root.GetComponentsInChildren<NetworkBehaviour>())
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(behaviour, false);
            UnityEngine.Object.DestroyImmediate(_root);
            typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _warning);
        }
        [Test]
        public void EvenTurnRefresh_IsServerOnly_RespawnsBothOnceAndPreservesOddTurns()
        {
            Set(_match, "_chest0", default(BoardChestSnapshot)); Set(_match, "_chest1", default(BoardChestSnapshot));
            Invoke("RefreshChestsForTurnOnServer", 1);
            Assert.That(_match.GetChestSnapshot(0).Active, Is.False);
            Authority(_match, false); Invoke("RefreshChestsForTurnOnServer", 2);
            Assert.That(_match.GetChestSnapshot(0).Active, Is.False);
            Authority(_match, true); Invoke("RefreshChestsForTurnOnServer", 2);
            var first = _match.GetChestSnapshot(0); var second = _match.GetChestSnapshot(1);
            Assert.That(first.Active && second.Active, Is.True);
            Assert.That(first.Location, Is.Not.EqualTo(second.Location));
            first.Consumed = true; first.Revision++; Set(_match, "_chest0", first);
            foreach (var turn in new[] { 2, 3 })
            {
                Invoke("RefreshChestsForTurnOnServer", turn);
                Assert.That(_match.GetChestSnapshot(0), Is.EqualTo(first));
                Assert.That(_match.GetChestSnapshot(1), Is.EqualTo(second));
            }
            Invoke("RefreshChestsForTurnOnServer", 4);
            var moved = _match.GetChestSnapshot(1);
            Assert.That(moved.Location, Is.Not.EqualTo(second.Location));
            Assert.That(_match.GetChestSnapshot(0).Consumed, Is.False);
            Assert.That(moved.AppearedTurn, Is.EqualTo(4));
            Invoke("RefreshChestsForTurnOnServer", 4);
            Assert.That(_match.GetChestSnapshot(1), Is.EqualTo(moved));
        }
        [Test]
        public void PlacementAndPool_AreSeeded_ExcludeShopsRespawnAndStartUseItems()
        {
            var shops = new[] { new Vector2Int(3, 0), new Vector2Int(4, 0), new Vector2Int(5, 0) };
            var expected = new[] { Vector2Int.zero, Vector2Int.right };
            for (var seed = 0; seed < 100; seed++)
            {
                var locations = BoardChestRules.ChooseLocations(_topology.Tiles, shops, null, seed);
                Assert.That(locations.OrderBy(c => c.x), Is.EqualTo(expected));
                Assert.That(BoardChestRules.ChooseLocations(_topology.Tiles.Reverse(), shops, null, seed), Is.EqualTo(locations));
                var id = BoardChestRules.DrawItem(PrototypeItemCatalog.All, seed);
                Assert.That(PrototypeItemCatalog.Get(id).UseTiming, Is.EqualTo(BoardItemUseTiming.Immediate));
                Assert.That(BoardChestRules.DrawItem(PrototypeItemCatalog.All, seed), Is.EqualTo(id));
            }
            Assert.That(PrototypeItemCatalog.All.Where(i => i.UseTiming == BoardItemUseTiming.TurnStart).Select(i => i.Id),
                Is.EquivalentTo(new[] { PrototypeItemId.DoubleDice, PrototypeItemId.LowDice, PrototypeItemId.HighDice }));
        }
        [Test]
        public void Opening_RejectsUnauthorizedStaleRemoteDeadPausedUnchosenOrOccludedRequests_AndCommitsOnce()
        {
            Authority(_match, false);
            Assert.That(_match.TryOpenChestOnServer(_player, 0, 1), Is.False);
            Authority(_match, true);
            Assert.That(_match.TryOpenChestOnServer(_player, 0, 0), Is.False);
            Assert.That(_match.TryOpenChestOnServer(_player, 2, 1), Is.False);
            Set(_player, "_currentHealth", 0);
            Assert.That(_match.TryOpenChestOnServer(_player, 0, 1), Is.False);
            Set(_player, "_currentHealth", 100);
            Set(_player, "_choiceResolution", (byte)ItemChoiceResolution.Pending);
            Assert.That(_match.TryOpenChestOnServer(_player, 0, 1), Is.False);
            Set(_player, "_choiceResolution", (byte)ItemChoiceResolution.DoNotUse);
            Set(_match, "_playerPauseActive", true);
            Assert.That(_match.TryOpenChestOnServer(_player, 0, 1), Is.False);
            Set(_match, "_playerPauseActive", false);
            Set(_player, "_logicalTileCoordinate", Vector2Int.right);
            Assert.That(_match.TryOpenChestOnServer(_player, 0, 1), Is.False);
            Set(_player, "_logicalTileCoordinate", Vector2Int.zero);
            _player.transform.position = Vector3.left * 50;
            Assert.That(_match.TryOpenChestOnServer(_player, 0, 1), Is.False);
            _player.transform.position = Vector3.zero;
            var wall = Child("Blocking wall").AddComponent<BoxCollider>();
            wall.transform.position = new Vector3(0, 1, 1.5f); Physics.SyncTransforms();
            Assert.That(_match.TryOpenChestOnServer(_player, 0, 1), Is.False);
            wall.enabled = false; Physics.SyncTransforms();
            var channel = (BoardSwapChannel)typeof(NetworkPlayerAvatar).GetField("_swapChannel", Private).GetValue(_player);
            Assert.That(channel.Begin(0, 1, 2), Is.True);
            Assert.That(_player.IsSwapping, Is.True);
            LogAssert.Expect(LogType.Error, "Rpc methods can only be invoked after starting the NetworkManager!");
            Assert.That(_match.TryOpenChestOnServer(_player, 0, 1), Is.True);
            Assert.That(_player.IsSwapping, Is.False);
            var acquired = _player.GetSelectedItemOnServer();
            Assert.That(acquired, Is.Not.EqualTo(PrototypeItemId.None));
            Assert.That(_match.GetChestSnapshot(0).Consumed, Is.True);
            Assert.That(_match.TryOpenChestOnServer(_player, 0, 1), Is.False);
            Assert.That(_match.TryOpenChestOnServer(_player, 0, 2), Is.False);
            Assert.That(_player.GetSelectedItemOnServer(), Is.EqualTo(acquired));
            // The same player may open a different chest in the same turn.
            var other = Chest(0); Set(_match, "_chest1", other);
            typeof(BoardChestVisual).GetProperty("ChestIndex").SetValue(_root.GetComponentInChildren<BoardChestVisual>(), 1);
            LogAssert.Expect(LogType.Error, "Rpc methods can only be invoked after starting the NetworkManager!");
            Assert.That(_match.TryOpenChestOnServer(_player, 1, 1), Is.True);
        }
        [Test]
        public void Replacement_DiscardsOnlySelectedInventoryItem_PreservesCloakDamageAndRoll_ConsumesTemporaryCharges()
        {
            Set(_player, "_occupiedItemMask", (byte)7);
            Set(_player, "_itemSlot0", (byte)PrototypeItemId.Pistol);
            Set(_player, "_itemSlot1", (byte)PrototypeItemId.Cloak);
            Set(_player, "_itemSlot2", (byte)PrototypeItemId.Mine);
            Set(_player, "_selectedItemSlot", 0);
            Set(_player, "_equippedItem", (byte)PrototypeItemId.Pistol);
            Set(_player, "_itemCharges", 2); Set(_player, "_cloaked", true);
            Authority(_player, false);
            Assert.That(_player.EquipChestItemOnServer(PrototypeItemId.Sniper), Is.False);
            Authority(_player, true);
            Assert.That(_player.EquipChestItemOnServer(PrototypeItemId.DoubleDice), Is.False);
            Assert.That(_player.EquipChestItemOnServer(PrototypeItemId.Sniper), Is.True);
            Assert.That(Get<byte>(_player, "_occupiedItemMask"), Is.EqualTo(6));
            Assert.That(_player.GetSelectedItemOnServer(), Is.EqualTo(PrototypeItemId.Sniper));
            Assert.That(Get<int>(_player, "_itemCharges"), Is.EqualTo(PrototypeItemCatalog.Get(PrototypeItemId.Sniper).Charges));
            Assert.That(_player.IsCloaked, Is.True);
            Set(_match, "_shieldEndsAt", 0d);
            LogAssert.Expect(LogType.Error, "Rpc methods can only be invoked after starting the NetworkManager!");
            Assert.That(_player.ApplyDamage(new DamageRequest(10, DamageKind.Item, null)), Is.EqualTo(DamageResult.Applied));
            Assert.That(_player.CurrentHealth, Is.EqualTo(90));
            Assert.That(BoardItemPresentationRules.ShouldHighlightFirearmTarget(true, 90, _player.IsCloaked, false, 0), Is.False);
            Set(_player, "_doubleDice", true); Set(_player, "_firstDieResult", 5);
            Assert.That(_player.EquipChestItemOnServer(PrototypeItemId.Grenade), Is.True);
            Assert.That(_player.RecordDieResultOnServer(1, 6), Is.EqualTo(11));
            Assert.That(_player.GetSelectedItemOnServer(), Is.EqualTo(PrototypeItemId.Grenade));
            Set(_player, "_itemCharges", 1);
            Assert.That(_player.SpendItemChargeOnServer(PrototypeItemCatalog.Get(PrototypeItemId.Grenade)), Is.True);
            Assert.That(_player.GetSelectedItemOnServer(), Is.EqualTo(PrototypeItemId.None));
            Assert.That(Get<byte>(_player, "_occupiedItemMask"), Is.EqualTo(6));
            Assert.That(_player.IsCloaked, Is.True);
        }
        [Test]
        public void MapKnowledge_KeepsPublicMarkerUntilDirectObservation_AndResetsForNewGeneration()
        {
            var first = new BoardChestMapKnowledge(); var other = new BoardChestMapKnowledge();
            var state = Chest(0);
            Assert.That(first.ShouldShow(0, state, false), Is.True);
            state.Consumed = true; state.Revision++;
            Assert.That(first.ShouldShow(0, state, false), Is.True);
            Assert.That(first.ShouldShow(0, state, true), Is.False);
            Assert.That(other.ShouldShow(0, state, false), Is.True);
            Assert.That(first.ShouldShow(0, state, false), Is.False);
            state.AppearedTurn = 4; state.Consumed = false; state.Revision++;
            Assert.That(first.ShouldShow(0, state, false), Is.True);
            state.Consumed = true; first.ShouldShow(0, state, true);
            first.ShouldShow(0, default, false);
            Assert.That(first.ShouldShow(0, Chest(0), false), Is.True);
        }
        [Test]
        public void Cloak_SelectionOrChestAcquisition_ActivatesWithoutUseInput_ConsumesOnceOnServer()
        {
            var seed = Enumerable.Range(0, 1000).First(value =>
                BoardChestRules.DrawItem(PrototypeItemCatalog.All, unchecked(value * 397 ^ 2 * 31)) == PrototypeItemId.Cloak);
            Set(_match, "_boardEffectSeed", seed);
            Set(_match, "_choiceEndsAt", _match.SynchronizedNow + 30d);
            foreach (var fromChest in new[] { false, true })
            {
                Set(_match, "_chest0", Chest(0));
                Set(_player, "_cloaked", false);
                Set(_player, "_occupiedItemMask", (byte)3);
                Set(_player, "_itemSlot0", (byte)(fromChest ? PrototypeItemId.Pistol : PrototypeItemId.Cloak));
                Set(_player, "_itemSlot1", (byte)PrototypeItemId.Mine);
                Set(_player, "_selectedItemSlot", fromChest ? 0 : -1);
                Set(_player, "_equippedItem", (byte)(fromChest ? PrototypeItemId.Pistol : PrototypeItemId.None));
                Set(_player, "_itemCharges", fromChest ? 2 : 0);
                Set(_player, "_firstDieResult", 0);
                Set(_player, "_choiceResolution", (byte)(fromChest ? ItemChoiceResolution.ItemSelected : ItemChoiceResolution.Pending));
                Func<bool> request = () => fromChest
                    ? _match.TryOpenChestOnServer(_player, 0, 1)
                    : _match.TryResolveItemChoiceOnServer(_player, 0, false);

                Authority(_player, false);
                Assert.That(request(), Is.False);
                Assert.That(_player.IsCloaked, Is.False);
                Assert.That(Get<byte>(_player, "_occupiedItemMask"), Is.EqualTo(3));
                Authority(_player, true);
                LogAssert.Expect(LogType.Error, "Rpc methods can only be invoked after starting the NetworkManager!");
                if (fromChest)
                    LogAssert.Expect(LogType.Error, "Rpc methods can only be invoked after starting the NetworkManager!");
                Assert.That(request(), Is.True);
                Assert.That(_player.IsCloaked, Is.True, "No use input, animation or timer is required.");
                Assert.That(_player.HasResolvedItemChoice, Is.True);
                Assert.That(Get<int>(_player, "_itemCharges"), Is.Zero);
                Assert.That(_player.GetSelectedItemOnServer(), Is.EqualTo(PrototypeItemId.None));
                Assert.That(Get<byte>(_player, "_occupiedItemMask"), Is.EqualTo(2));
                Assert.That(Get<byte>(_player, "_itemSlot1"), Is.EqualTo((byte)PrototypeItemId.Mine));
                Assert.That(request(), Is.False, "A repeated choice or chest request must not spend another item.");
                Assert.That(_match.TryUseSelectedItemOnServer(_player, _player.EyePivot.position, _player.EyePivot.forward), Is.False);
                Assert.That(_player.RecordDieResultOnServer(0, 6), Is.EqualTo(6));
            }
        }

        [Test]
        public void HeavyHit_KnocksDownOnlySurvivorsAtFifty_PreservesPauseAndExpiresAtOneSecond()
        {
            Set(_match, "_shieldEndsAt", 0d);
            foreach (var damage in new[] { 49, 50, 100 })
            {
                Set(_player, "_currentHealth", 100);
                Set(_player, "_knockdownEndsAt", 0d);
                LogAssert.Expect(LogType.Error, "Rpc methods can only be invoked after starting the NetworkManager!");
                _player.ApplyDamage(new DamageRequest(damage, DamageKind.Environment, null));
                Assert.That(_player.IsKnockedDown, Is.EqualTo(damage == 50));
                if (damage != 50) continue;
                var deadline = Get<double>(_player, "_knockdownEndsAt");
                Assert.That((bool)typeof(NetworkMatchState).GetMethod("CanProcessActionRequest", Private).Invoke(_match, new object[] { _player }), Is.False);
                var pause = typeof(NetworkPlayerAvatar).GetMethod("PauseBoardKnockdownOnServer", Private);
                var resume = typeof(NetworkPlayerAvatar).GetMethod("ResumeBoardKnockdownOnServer", Private);
                pause.Invoke(_player, new object[] { deadline - .5d });
                Assert.That(_player.KnockdownRemaining, Is.EqualTo(.5d).Within(.00001d));
                resume.Invoke(_player, new object[] { deadline + 20d });
                Assert.That(Get<double>(_player, "_knockdownEndsAt"), Is.EqualTo(deadline + 20.5d).Within(.00001d));
                pause.Invoke(_player, new object[] { deadline + 20.499d });
                Assert.That(_player.IsKnockedDown, Is.True);
                resume.Invoke(_player, new object[] { 0d });
                pause.Invoke(_player, new object[] { Get<double>(_player, "_knockdownEndsAt") });
                Assert.That(_player.IsKnockedDown, Is.False);
            }
        }

        [Test]
        public void DiceRoll_RestartAndTimeoutPreserveFaces_AndClientsCannotChooseResults()
        {
            Set(_match, "_boardEffectSeed", 82519);
            Set(_match, "_turn", 4);
            Set(_player, "_doubleDice", true);
            var first = _player.RollBoardFaceOnServer(0);
            var second = _player.RollBoardFaceOnServer(1);
            var randomState = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(9104);
                Assert.That(_player.RollBoardFaceOnServer(1), Is.EqualTo(second));
                Assert.That(_player.RollBoardFaceOnServer(0), Is.EqualTo(first));
                Assert.That(_player.RecordDieResultOnServer(1, second), Is.Zero);
                Assert.That(_player.CompletePartialDiceOnTimeout(), Is.EqualTo(first + second));
                Set(_player, "_firstDieResult", 0); Set(_player, "_secondDieResult", 0);
                Assert.That(_player.RollBoardFaceOnServer(0), Is.EqualTo(first));
                Assert.That(_player.RollBoardFaceOnServer(1), Is.EqualTo(second));
                foreach (var id in new[] { PrototypeItemId.LowDice, PrototypeItemId.HighDice })
                {
                    Set(_player, "_rangeDieItem", (byte)id);
                    var definition = PrototypeItemCatalog.Get(id);
                    Assert.That(_player.RollBoardFaceOnServer(0), Is.InRange(definition.DiceMinimum, definition.DiceMaximum));
                }
                Authority(_player, false);
                Assert.That(_player.RollBoardFaceOnServer(0), Is.Zero);
                Assert.That(_player.CompletePartialDiceOnTimeout(), Is.Zero);
            }
            finally { UnityEngine.Random.state = randomState; }
        }

        [Test]
        public void Grenade_OffBoardThrowConsumesOnce_AndSettlesWithoutFloorOrDisconnectingOwner()
        {
            _player.transform.position = new Vector3(1000, 10, 1000);
            _player.EyePivot.position = _player.transform.position + Vector3.up;
            Assert.That(_topology.FindContainingTile(_player.transform.position), Is.Null);
            Set(_player, "_equippedItem", (byte)PrototypeItemId.Grenade);
            Set(_player, "_itemCharges", 1);
            LogAssert.Expect(LogType.Error, "Rpc methods can only be invoked after starting the NetworkManager!");
            Assert.That(_match.TryUseSelectedItemOnServer(_player, _player.EyePivot.position, _player.EyePivot.forward), Is.True);
            var projectiles = (System.Collections.IList)typeof(NetworkMatchState).GetField("_boardGrenades", Private).GetValue(_match);
            Assert.That(projectiles.Count, Is.EqualTo(1));
            Assert.That(_player.GetSelectedItemOnServer(), Is.EqualTo(PrototypeItemId.None));
            Assert.That(_match.TryUseSelectedItemOnServer(_player, _player.EyePivot.position, _player.EyePivot.forward), Is.False);
            LogAssert.Expect(LogType.Error, "Rpc methods can only be invoked after starting the NetworkManager!");
            Invoke("TickBoardGrenades", 2f);
            Assert.That(projectiles.Count, Is.Zero);
            Assert.That(_player.IsSpawned && _player.IsBoardReady, Is.True);
            Assert.That(_player.CurrentHealth, Is.EqualTo(100));
            Assert.That(_match.GameplayEnabled, Is.True);
        }
        [Test]
        public void EmptyResourceTransfer_KeepsBothParticipantsAndFullPresentationWithoutChangingBalances()
        {
            var opponent = Child("Opponent").AddComponent<NetworkPlayerAvatar>();
            Authority(opponent, true);
            Set(opponent, "_slot", 1); Set(opponent, "_boardReady", true);
            Set(_player, "_gold", 0); Set(opponent, "_gold", 0);
            Set(_player, "_keyCount", 0); Set(opponent, "_keyCount", 0);
            foreach (var resource in new[] { BoardSpecialEventResource.Gold, BoardSpecialEventResource.Key })
            foreach (var operation in new[] { BoardSpecialEventOperation.OpponentGivesToActor, BoardSpecialEventOperation.OpponentStealsFromActor })
            {
                var beforeGold = _player.Gold + opponent.Gold;
                var beforeKeys = _player.KeyCount + opponent.KeyCount;
                var durations = (double[])typeof(NetworkMatchState).GetField("_landingEffectDurations", Private).GetValue(_match);
                durations[0] = BoardEffectShotTiming.Duration + BoardLandingEffectLayout.SpecialEventRouletteDurationSeconds +
                    BoardResourceTransferPresentationRules.TotalDurationSeconds;
                var fullDuration = durations[0];
                Invoke("ResolveSpecialEventTransferOnServer", 0, null,
                    new BoardSpecialEventRules.Resolution(BoardSpecialEventFamily.Transfer, BoardSpecialEventAudience.OneOpponent,
                        operation, resource, 1, 20));
                var snapshot = _match.ResourceTransferPresentation;
                Assert.That(snapshot.Active, Is.True);
                Assert.That(snapshot.Amount, Is.Zero);
                Assert.That(new[] { snapshot.SourceSlot, snapshot.DestinationSlot }, Is.EquivalentTo(new[] { 0, 1 }));
                Assert.That(durations[0], Is.EqualTo(fullDuration));
                Assert.That(BoardResourceTransferPresentationRules.GetPhase(snapshot, snapshot.StartedAt + 3d,
                    out _, out _), Is.EqualTo(BoardResourceTransferPhase.Source));
                Assert.That(_player.Gold + opponent.Gold, Is.EqualTo(beforeGold));
                Assert.That(_player.KeyCount + opponent.KeyCount, Is.EqualTo(beforeKeys));
            }
        }
        private BoardChestSnapshot Chest(int x) => new BoardChestSnapshot
            { Active = true, Location = new Vector2Int(x, 0), Revision = 1, AppearedTurn = 2 };
        private GameObject Child(string name)
        { var child = new GameObject(name); child.transform.SetParent(_root.transform); return child; }
        private void Invoke(string method, params object[] args) => typeof(NetworkMatchState).GetMethod(method, Private).Invoke(_match, args);
        private static void Authority(NetworkBehaviour b, bool server)
        { typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(b, server); typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(b, true); }
        private static void Set<T>(object target, string field, T value) where T : unmanaged =>
            ((NetworkVariable<T>)target.GetType().GetField(field, Private).GetValue(target)).Value = value;
        private static T Get<T>(object target, string field) where T : unmanaged =>
            ((NetworkVariable<T>)target.GetType().GetField(field, Private).GetValue(target)).Value;
    }
}
