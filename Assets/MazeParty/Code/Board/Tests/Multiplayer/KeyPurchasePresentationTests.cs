using System;
using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class KeyPurchasePresentationTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _root;
        private NetworkMatchState _match;
        private NetworkMatchState _previousMatch;
        private NetworkPlayerAvatar _buyer;
        private KeyShopRuntimeState _shop;
        private BoardFlowStateMachine _flow;
        private bool _previousWarning;

        [SetUp]
        public void SetUp()
        {
            var warning = typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning",
                BindingFlags.Static | BindingFlags.NonPublic);
            _previousWarning = (bool)warning.GetValue(null);
            warning.SetValue(null, true);
            _previousMatch = NetworkMatchState.Instance;
            _root = new GameObject("Key purchase fixture");
            _match = Child("Match").AddComponent<NetworkMatchState>();
            Authority(_match, true);
            typeof(NetworkMatchState).GetProperty(nameof(NetworkMatchState.Instance)).SetValue(null, _match);
            Set(_match, "_gameplayEnabled", true);
            _buyer = Child("Buyer").AddComponent<NetworkPlayerAvatar>();
            Authority(_buyer, true);
            Set(_buyer, "_slot", 0);
            Set(_buyer, "_boardReady", true);
            Set(_buyer, "_gold", 100);
            Set(_buyer, "_currentHealth", 100);
            Set(_buyer, "_hasLogicalTile", true);
            Field("_avatarLookupCache").SetValue(_match, new[] { _buyer, null, null, null });
            var current = Child("Current tile").AddComponent<BoardTile>();
            current.Configure(Vector2Int.zero, BoardTileType.Normal);
            var next = Child("Next tile").AddComponent<BoardTile>();
            next.Configure(Vector2Int.right, BoardTileType.Normal);
            next.transform.position = Vector3.right * BoardTile.RoomSize;
            var topology = _root.AddComponent<BoardTopology>();
            topology.Configure(new[] { current, next }, Array.Empty<BoardGate>());
            Field("_boardTopology").SetValue(_match, topology);
            _shop = _root.AddComponent<KeyShopRuntimeState>();
            Assert.That(_shop.RestoreCheckpoint(KeyShopLifecycleState.Active, true,
                Vector2Int.zero, 1, topology.Tiles), Is.True);
            Field("_keyShopRuntime").SetValue(_match, _shop);
            Invoke("SyncKeyShopSnapshot");
            var now = _match.SynchronizedNow;
            _flow = new BoardFlowStateMachine();
            _flow.Start(now - BoardFlowStateMachine.TurnOverviewDurationSeconds - BoardFlowStateMachine.DescendingDurationSeconds - 1d);
            _flow.Tick(now);
            Field("_flow").SetValue(_match, _flow);
            Invoke("SyncFlowSnapshot", now);
        }

        [TearDown]
        public void TearDown()
        {
            typeof(NetworkMatchState).GetProperty(nameof(NetworkMatchState.Instance)).SetValue(null, _previousMatch);
            if (_root != null)
            {
                foreach (var behaviour in _root.GetComponentsInChildren<NetworkBehaviour>())
                    typeof(NetworkBehaviour).GetProperty(nameof(NetworkBehaviour.IsSpawned)).SetValue(behaviour, false);
                UnityEngine.Object.DestroyImmediate(_root);
            }
            typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning",
                BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _previousWarning);
        }

        [Test]
        public void Purchase_RejectsClientAndStaleRevision_CommitsOnceAndDelaysRelocationUntilAcquisition()
        {
            var progress = new AchievementMatchProgress();
            Field("_achievementMatchId").SetValue(_match, Guid.NewGuid().ToString("N"));
            ((AchievementMatchProgress[])Field("_achievementPlayers").GetValue(_match))[0] = progress;
            Set(_buyer, "_cloaked", true);
            var revision = _match.KeyShopRevision;
            Authority(_match, false);
            Assert.That(_match.TryPurchaseKeyOnServer(_buyer, revision), Is.False);
            Authority(_match, true);
            Assert.That(_match.TryPurchaseKeyOnServer(_buyer, revision - 1), Is.False);
            Assert.That(_buyer.Gold, Is.EqualTo(100));
            Assert.That(progress.keysBought, Is.Zero);
            Assert.That(_match.TryPurchaseKeyOnServer(_buyer, revision), Is.True);
            Assert.That(_buyer.Gold, Is.EqualTo(80));
            Assert.That(_buyer.KeyCount, Is.EqualTo(1));
            Assert.That(_match.KeyShopBuyerSlot, Is.Zero);
            Assert.That(progress.keysBought, Is.EqualTo(1));
            Assert.That(progress.flags & PartyAchievements.Bit(PartyAchievement.InvisibleCustomer), Is.Not.Zero);
            Assert.That(_match.CanAcceptActionInput, Is.False);
            Assert.That(_flow.IsPaused, Is.True);
            Assert.That(_match.TryPurchaseKeyOnServer(_buyer, revision), Is.False);
            Assert.That(_buyer.Gold, Is.EqualTo(80));

            var end = Get<double>(_match, "_keyShopRevealEndsAt");
            var transition = Get<double>(_match, "_keyShopTransitionSeconds");
            var reveal = end - transition - KeyPurchasePresentationRules.ShopRevealSeconds;
            var remaining = _match.ActionRemaining;
            Invoke("AdvanceKeyShopRevealOnServer", reveal - 0.000001d);
            Assert.That(_match.KeyShopLocation, Is.EqualTo(Vector2Int.zero));
            Invoke("AdvanceKeyShopRevealOnServer", reveal);
            Assert.That(_match.KeyShopLocation, Is.EqualTo(Vector2Int.right));
            Assert.That(_match.KeyShopRevision, Is.EqualTo(revision + 1));
            Invoke("AdvanceKeyShopRevealOnServer", end - 0.000001d);
            Assert.That(_match.IsKeyShopRevealActive, Is.True);
            Assert.That(_flow.IsPaused, Is.True);
            Invoke("AdvanceKeyShopRevealOnServer", end);
            Assert.That(_match.IsKeyShopRevealActive, Is.False);
            Assert.That(_flow.IsPaused, Is.False);
            Assert.That(progress.keysBought, Is.EqualTo(1));
            Assert.That(_flow.GetActionRemaining(end), Is.EqualTo(remaining).Within(0.000001d));
            Assert.That(_buyer.KeyCount, Is.EqualTo(1));
        }

        [Test]
        public void NestedPause_PreservesAcquisitionPhaseAndOriginalActionClock()
        {
            Assert.That(_match.TryPurchaseKeyOnServer(_buyer, _match.KeyShopRevision), Is.True);
            var transition = Get<double>(_match, "_keyShopTransitionSeconds");
            var end = Get<double>(_match, "_keyShopRevealEndsAt");
            var start = end - KeyPurchasePresentationRules.TotalDuration(transition);
            var pauseAt = start + transition + 0.5d;
            var actionRemaining = _match.ActionRemaining;
            Invoke("SuspendSimulationOnServer", pauseAt);
            Set(_match, "_reconnectPaused", true);
            Assert.That(_match.KeyPurchasePhase, Is.EqualTo(KeyPurchasePresentationPhase.AcquireKey));
            Assert.That(_match.KeyShopRevealRemaining, Is.EqualTo(end - pauseAt).Within(0.000001d));
            Invoke("AdvanceKeyShopRevealOnServer", end + 100d);
            Assert.That(_match.KeyShopLocation, Is.EqualTo(Vector2Int.zero));
            Assert.That(_match.ActionRemaining, Is.EqualTo(actionRemaining));
            Set(_match, "_reconnectPaused", false);
            Invoke("ResumeSimulationOnServer", pauseAt + 100d);
            var shiftedEnd = Get<double>(_match, "_keyShopRevealEndsAt");
            Assert.That(shiftedEnd, Is.EqualTo(end + 100d).Within(0.000001d));
            Assert.That(_flow.IsPaused, Is.True);
            Invoke("AdvanceKeyShopRevealOnServer", shiftedEnd);
            Assert.That(_flow.GetActionRemaining(shiftedEnd), Is.EqualTo(actionRemaining).Within(0.000001d));
        }

        [Test]
        public void Timeline_ChangesPhaseAtExactBoundariesAndReservesCameraLead()
        {
            foreach (var transition in new[] { 0.25d, 3d, 5d })
            {
                var edges = new[] { transition, transition + KeyPurchasePresentationRules.AcquisitionSeconds,
                    transition + KeyPurchasePresentationRules.AcquisitionSeconds + KeyPurchasePresentationRules.ShopRevealSeconds,
                    KeyPurchasePresentationRules.TotalDuration(transition) };
                for (var index = 0; index < edges.Length; index++)
                {
                    Assert.That(KeyPurchasePresentationRules.GetPhase(edges[index] - 0.000001d, transition, out _),
                        Is.EqualTo((KeyPurchasePresentationPhase)(index + 1)));
                    Assert.That(KeyPurchasePresentationRules.GetPhase(edges[index], transition, out _),
                        Is.EqualTo((KeyPurchasePresentationPhase)(index + 2)));
                }
                KeyPurchasePresentationRules.GetPhase(transition +
                    BoardResourceTransferPresentationRules.CameraLeadSeconds, transition, out var motion);
                Assert.That(motion, Is.EqualTo(0f).Within(0.000001f));
            }
        }

        [Test]
        public void InitialAppearance_RevealsWithoutBuyerAndResumesThePausedFlow()
        {
            _shop.ResetToInactive();
            Invoke("SyncKeyShopSnapshot");
            var now = _match.SynchronizedNow;
            _flow = new BoardFlowStateMachine();
            _flow.Start(now - BoardFlowStateMachine.TurnOverviewDurationSeconds - 0.25d,
                KeyShopRuntimeState.InitialPlacementTurn);
            _flow.Tick(now);
            Field("_flow").SetValue(_match, _flow);
            Invoke("SyncFlowSnapshot", now);
            Invoke("TryBeginInitialKeyShopPlacementOnServer", KeyShopRuntimeState.InitialPlacementTurn);
            Assert.That(_match.IsKeyShopRevealActive, Is.True);
            Assert.That(_match.KeyShopBuyerSlot, Is.EqualTo(-1));
            Assert.That(_match.KeyPurchasePhase, Is.EqualTo(KeyPurchasePresentationPhase.RevealShop));
            Assert.That(_flow.IsPaused, Is.True);
            Assert.That(_match.KeyShopHasLocation, Is.True);
            Assert.That(_buyer.Gold, Is.EqualTo(100));
            Assert.That(_buyer.KeyCount, Is.Zero);
            var end = Get<double>(_match, "_keyShopRevealEndsAt");
            var remaining = _match.StateRemaining;
            Invoke("AdvanceKeyShopRevealOnServer", end - 0.000001d);
            Assert.That(_flow.IsPaused, Is.True);
            Invoke("AdvanceKeyShopRevealOnServer", end);
            Assert.That(_match.IsKeyShopRevealActive, Is.False);
            Assert.That(_flow.IsPaused, Is.False);
            Assert.That(_flow.State, Is.EqualTo(BoardFlowState.Descending));
            Assert.That(_flow.GetStateRemaining(end), Is.EqualTo(Math.Max(0d, remaining -
                Get<double>(_match, "_keyShopTransitionSeconds"))).Within(0.000001d),
                "The completed return shot must not replay the descending camera clock.");
        }

        [Test]
        public void ShopReveal_HoldsOneSecondThenZoomsForOneSecondBeforeReturning()
        {
            foreach (var hasBuyer in new[] { false, true })
            {
                const double transition = 3d;
                var start = KeyPurchasePresentationRules.RevealStartsAt(transition, hasBuyer);
                foreach (var elapsed in new[] { 0d, 0.999d, 1d, 1.5d, 1.999d })
                {
                    Assert.That(KeyPurchasePresentationRules.GetPhase(start + elapsed, transition, out _, hasBuyer),
                        Is.EqualTo(KeyPurchasePresentationPhase.RevealShop));
                    Assert.That(KeyPurchasePresentationRules.ZoomOutProgress(elapsed),
                        Is.EqualTo(Mathf.Clamp01((float)elapsed - 1f)).Within(0.000001f));
                }
                Assert.That(KeyPurchasePresentationRules.ZoomOutProgress(2d), Is.EqualTo(1f));
                Assert.That(KeyPurchasePresentationRules.GetPhase(start + 2d, transition, out _, hasBuyer),
                    Is.EqualTo(KeyPurchasePresentationPhase.ReturnToFirstPerson));
                Assert.That(KeyPurchasePresentationRules.GetPhase(start + 2d + transition, transition, out _, hasBuyer),
                    Is.EqualTo(KeyPurchasePresentationPhase.Complete));
            }
        }

        private GameObject Child(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(_root.transform);
            return child;
        }
        private static FieldInfo Field(string name) => typeof(NetworkMatchState).GetField(name, Private);
        private void Invoke(string method, params object[] args) =>
            typeof(NetworkMatchState).GetMethod(method, Private).Invoke(_match, args);
        private static void Authority(NetworkBehaviour behaviour, bool server)
        {
            typeof(NetworkBehaviour).GetProperty(nameof(NetworkBehaviour.IsServer)).SetValue(behaviour, server);
            typeof(NetworkBehaviour).GetProperty(nameof(NetworkBehaviour.IsSpawned)).SetValue(behaviour, true);
        }
        private static T Get<T>(object target, string field) where T : unmanaged =>
            ((NetworkVariable<T>)target.GetType().GetField(field, Private).GetValue(target)).Value;
        private static void Set<T>(object target, string field, T value) where T : unmanaged =>
            ((NetworkVariable<T>)target.GetType().GetField(field, Private).GetValue(target)).Value = value;
    }
}
