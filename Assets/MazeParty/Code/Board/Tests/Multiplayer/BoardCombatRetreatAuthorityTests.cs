using System;
using System.Collections.Generic;
using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardCombatRetreatAuthorityTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _root;
        private NetworkMatchState _match, _previous;
        private NetworkPlayerAvatar[] _players;
        private BoardTile[] _tiles;
        private BoardTopology _topology;
        private bool _warning;

        [SetUp]
        public void SetUp()
        {
            var warning = typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning", BindingFlags.Static | BindingFlags.NonPublic);
            _warning = (bool)warning.GetValue(null);
            warning.SetValue(null, true);
            _previous = NetworkMatchState.Instance;
            _root = new GameObject("Combat retreat fixture");
            _match = Child("Match").AddComponent<NetworkMatchState>();
            typeof(NetworkMatchState).GetProperty("Instance").SetValue(null, _match);
            typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(_match, true);
            Set(_match, "_gameplayEnabled", true);
            Set(_match, "_flowState", (byte)BoardFlowState.CombatResolve);
            Set(_match, "_combatActive", true);
            Set(_match, "_combatParticipantMask", (byte)7);
            Set(_match, "_combatAliveMask", (byte)7);
            Set(_match, "_combatSequenceIndex", 1);
            var flow = new BoardFlowStateMachine();
            flow.Start(0d); flow.Tick(7d);
            for (var slot = 0; slot < 4; slot++) flow.TryReportPlayerArrived(slot, 10d);
            flow.Tick(15d);
            Field(_match, "_flow", flow);
            _tiles = new BoardTile[4];
            var gates = new BoardGate[3];
            var positions = new[] { Vector3.zero, new Vector3(8, 0, 0), new Vector3(8, 0, 8), new Vector3(16, 0, 8) };
            for (var i = 0; i < 4; i++)
            {
                _tiles[i] = Child("Tile " + i).AddComponent<BoardTile>();
                _tiles[i].Configure(new Vector2Int(i, 0), BoardTileType.Normal);
                _tiles[i].transform.position = positions[i];
                if (i == 0) continue;
                gates[i - 1] = Child("Gate " + i).AddComponent<BoardGate>();
                gates[i - 1].Configure(_tiles[i - 1], _tiles[i]);
                gates[i - 1].transform.position = (positions[i - 1] + positions[i]) * .5f;
            }
            _topology = Child("Topology").AddComponent<BoardTopology>();
            _topology.Configure(_tiles, gates);
            _players = new NetworkPlayerAvatar[4];
            for (var slot = 0; slot < 4; slot++)
            {
                var player = Child("Player " + slot).AddComponent<NetworkPlayerAvatar>();
                typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(player, true);
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(player, true);
                Field(player, "_characterController", player.GetComponent<CharacterController>());
                Set(player, "_slot", slot);
                Set(player, "_combatState", (byte)NetworkCombatState.Active);
                Set(player, "_combatHealth", 100 - slot * 25);
                Set(player, "_currentHealth", 100);
                var last = slot == 3 ? 2 : 3;
                var history = new List<Vector2Int>();
                for (var tile = 0; tile <= last; tile++) history.Add(_tiles[tile].Coordinate);
                Traversal(player).RestoreHistory(_topology, history, 0);
                Field(player, "_topology", _topology);
                Set(player, "_hasLogicalTile", true);
                Set(player, "_logicalTileCoordinate", _tiles[last].Coordinate);
                player.transform.position = _tiles[last].GetRecoveryCenter(1f);
                _players[slot] = player;
            }
            Field(_match, "_avatarLookupCache", _players);
            var eliminated = (double[])typeof(NetworkMatchState).GetField("_combatEliminatedAt", Private).GetValue(_match);
            for (var i = 0; i < 4; i++) eliminated[i] = double.NaN;
        }

        [TearDown]
        public void TearDown()
        {
            typeof(NetworkMatchState).GetProperty("Instance").SetValue(null, _previous);
            foreach (var b in _root.GetComponentsInChildren<NetworkBehaviour>())
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(b, false);
            Object.DestroyImmediate(_root);
            typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _warning);
        }

        [Test]
        public void Resolution_MovesAllLosersWithoutSnapAndStartsChainOnlyAfterThreeSeconds()
        {
            var origin = _players[1].transform.position;
            Invoke("ResolveCurrentCombatOnServer", 100d);
            Assert.That(_match.CombatRetreat.Mask, Is.EqualTo(6));
            Assert.That(_players[1].transform.position, Is.EqualTo(origin), "Resolution cannot teleport the body");
            Assert.That(_players[1].LogicalBoardTileCoordinate, Is.EqualTo(_tiles[2].Coordinate));
            Assert.That(_players[2].LogicalBoardTileCoordinate, Is.EqualTo(_tiles[1].Coordinate));
            Assert.That(_match.CombatQueueCount, Is.EqualTo(1), "Retreat created a chain fight");
            var snapshot = _match.CombatRetreat;
            Invoke("ResolveCurrentCombatOnServer", 100.1d);
            Assert.That(_match.CombatRetreat, Is.EqualTo(snapshot), "Repeated resolution cannot retreat twice");
            foreach (var elapsed in new[] { 0d, .399d, .4d, 1d, 1.6d, 1.8d, 2.35d, 2.9999d })
            {
                Invoke("AdvanceCombatOnServer", 100d + elapsed);
                Assert.That(_players[1].transform.position, Is.EqualTo(snapshot.Path1.Evaluate(elapsed)));
                Assert.That(_players[2].transform.position, Is.EqualTo(snapshot.Path2.Evaluate(elapsed)));
                Assert.That(_match.IsCombatActive, Is.False, elapsed.ToString());
                Assert.That(_match.CombatSequenceIndex, Is.EqualTo(1));
                Assert.That(_match.CanAvatarUseCombatInput(_players[1]), Is.False);
                Invoke("BeginNextCombatOrFinishOnServer", 100d + elapsed);
                Assert.That(_match.IsCombatActive, Is.False, "Direct start must obey the aftermath gate");
            }
            Invoke("AdvanceCombatOnServer", 103d);
            Assert.That(_match.IsCombatRetreatActive, Is.False);
            Assert.That(_match.IsCombatActive, Is.True);
            Assert.That(_match.CombatParticipantMask, Is.EqualTo(10));
            Assert.That(_match.CombatSequenceIndex, Is.EqualTo(2));
            Assert.That(((NetworkVariable<double>)typeof(NetworkMatchState).GetField("_combatEndsAt", Private).GetValue(_match)).Value,
                Is.EqualTo(166d), "The next fight gets its own 3 second countdown and full 60 seconds");
        }

        [Test]
        public void Retreat_RetracesCornersAndUsesExistingFallbackWithoutClientAuthority()
        {
            var player = _players[2];
            typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(player, false);
            Assert.That(player.PrepareCombatRetreatOnServer(3, out _), Is.Zero);
            Assert.That(Traversal(player).CurrentTile, Is.SameAs(_tiles[3]));
            typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(player, true);
            foreach (var withHistory in new[] { true, false })
            {
                Traversal(player).RestoreHistory(_topology, withHistory
                    ? new[] { _tiles[0].Coordinate, _tiles[1].Coordinate, _tiles[2].Coordinate, _tiles[3].Coordinate }
                    : new[] { _tiles[3].Coordinate }, 0);
                player.transform.position = _tiles[3].GetRecoveryCenter(1f);
                Assert.That(player.PrepareCombatRetreatOnServer(3, out var path), Is.EqualTo(3));
                var expected = new[] { new Vector3(16, 1, 8), new Vector3(12, 1, 8), new Vector3(8, 1, 8),
                    new Vector3(8, 1, 4), new Vector3(8, 1, 0), new Vector3(4, 1, 0), new Vector3(0, 1, 0) };
                Assert.That(path.Points.Length, Is.EqualTo(expected.Length));
                for (var i = 0; i < expected.Length; i++) Assert.That(path.Points[i], Is.EqualTo(expected[i]));
                Assert.That(path.Evaluate(1d), Is.EqualTo(new Vector3(8, 1, 4)), "Arc length follows the corner, not a diagonal");
                Assert.That(player.transform.position, Is.EqualTo(expected[0]));
                Assert.That(path.Evaluate(1.6d), Is.EqualTo(expected[6]));
            }
            Traversal(player).Begin(_tiles[0], 0);
            player.transform.position = _tiles[0].GetRecoveryCenter(1f);
            Assert.That(player.PrepareCombatRetreatOnServer(3, out var blocked), Is.Zero);
            Assert.That(blocked.Evaluate(3d), Is.EqualTo(player.transform.position));
        }

        [Test]
        public void PauseAndReconnect_FreezeTheSamePathAndResumeWithoutRestartOrPrematureFight()
        {
            Invoke("ResolveCurrentCombatOnServer", 100d);
            Invoke("AdvanceCombatOnServer", 101d);
            var position = _players[1].transform.position;
            foreach (var pauseField in new[] { "_playerPauseActive", "_reconnectPaused" })
            {
                var state = _match.CombatRetreat;
                state.StartedAt = 100d; state.Paused = false;
                Set(_match, "_combatRetreat", state);
                Invoke("PauseCombatAndPersonalProtectionOnServer", 101d);
                Set(_match, pauseField, true);
                Assert.That(_match.CombatRetreatElapsed, Is.EqualTo(1d));
                Invoke("AdvanceCombatOnServer", 500d);
                Assert.That(_players[1].transform.position, Is.EqualTo(position));
                Assert.That(_match.IsCombatActive, Is.False);
                // The route remains on the match, so a replacement/reconnected avatar
                // can recover its exact presentation position from its slot.
                _players[1].transform.position = Vector3.zero;
                typeof(NetworkPlayerAvatar).GetMethod("TickCombatRetreatPresentation", Private).Invoke(_players[1], null);
                Assert.That(_players[1].transform.position, Is.EqualTo(position));
                Invoke("ResumeCombatAndPersonalProtectionOnServer", 500d);
                Set(_match, pauseField, false);
                Assert.That(_match.CombatRetreat.Elapsed(500d), Is.EqualTo(1d));
                Invoke("AdvanceCombatOnServer", 501.999d);
                Assert.That(_match.IsCombatActive, Is.False);
                // Reset the sampled pose for the next pause mechanism.
                _players[1].transform.position = position;
            }
            Invoke("AdvanceCombatOnServer", 502d);
            Assert.That(_match.IsCombatActive, Is.True);
        }

        [Test]
        public void ReplicatedSnapshot_RoundTripsAllPathsAndPauseClock()
        {
            Set(_match, "_combatParticipantMask", (byte)15);
            for (var slot = 0; slot < 4; slot++)
            {
                Traversal(_players[slot]).RestoreHistory(_topology,
                    new[] { _tiles[0].Coordinate, _tiles[1].Coordinate, _tiles[2].Coordinate, _tiles[3].Coordinate }, 0);
                Set(_players[slot], "_logicalTileCoordinate", _tiles[3].Coordinate);
            }
            Invoke("ResolveCurrentCombatOnServer", 100d);
            Invoke("PauseCombatAndPersonalProtectionOnServer", 100.75d);
            var snapshot = _match.CombatRetreat;
            using (var writer = new FastBufferWriter(2048, Allocator.Temp))
            {
                writer.WriteNetworkSerializable(snapshot);
                using (var reader = new FastBufferReader(writer, Allocator.Temp))
                {
                    reader.ReadNetworkSerializable(out BoardCombatRetreatSnapshot restored);
                    Assert.That(restored, Is.EqualTo(snapshot));
                    Assert.That(restored.Mask, Is.EqualTo(14));
                    Assert.That(restored.Elapsed(900d), Is.EqualTo(.75d));
                    for (var slot = 1; slot < 4; slot++)
                        Assert.That(restored.GetPath(slot).Evaluate(.75d), Is.EqualTo(snapshot.GetPath(slot).Evaluate(.75d)));
                }
            }
        }

        [Test]
        public void AuthoredSplitCameras_UseDelayedRouteForEveryLoserAndRestoreTopViewAtBoundary()
        {
            Invoke("ResolveCurrentCombatOnServer", 100d);
            var output = Child("Output camera").AddComponent<Camera>();
            var director = Child("Director").AddComponent<GameplayCameraDirector>();
            director.Configure(output, null, null, null);
            var presenter = Child("Presenter").AddComponent<BoardFlowCameraPresenter>();
            presenter.Configure(director);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MazeParty/Prefabs/Board/World/BoardEventSplitView.prefab");
            presenter.ConfigureEventSplitView(prefab.GetComponent<BoardEventSplitView>());
            var refresh = typeof(BoardFlowCameraPresenter).GetMethod("RefreshCombatRetreatPresentation", Private);
            var initialCameraPositions = new Vector3[4];
            foreach (var elapsed in new[] { 0d, .4d, .6d, 1d, 1.6d, 1.799d, 1.8d, 2.349d, 2.35d })
            {
                var snapshot = _match.CombatRetreat;
                snapshot.Paused = true; snapshot.PausedAt = 100d + elapsed;
                Set(_match, "_combatRetreat", snapshot);
                var visible = (bool)refresh.Invoke(presenter, new object[] { _match });
                Assert.That(visible, Is.EqualTo(elapsed < 2.35d));
                Assert.That(output.enabled, Is.EqualTo(!visible));
                Assert.That(_match.IsCombatRetreatKnockedDown(1), Is.EqualTo(elapsed < 1.8d));
                if (!visible) continue;
                var split = (BoardEventSplitView)typeof(BoardFlowCameraPresenter).GetField("_eventSplitView", Private).GetValue(presenter);
                split.transform.SetParent(_root.transform);
                var views = (Camera[])typeof(BoardEventSplitView).GetField("cameras", Private).GetValue(split);
                for (var index = 0; index < 4; index++)
                {
                    Assert.That(views[index].enabled, Is.EqualTo(index < 2));
                    if (index >= 2) continue;
                    var path = snapshot.GetPath(index + 1);
                    if (elapsed == 0d) initialCameraPositions[index] = views[index].transform.position;
                    var expected = initialCameraPositions[index] + path.Evaluate(elapsed - .2d) - path.Evaluate(0d);
                    Assert.That(Vector3.Distance(views[index].transform.position, expected), Is.LessThan(.00001f));
                    Assert.That(views[index].rect, Is.EqualTo(BoardEventSplitView.Viewport(2, index)));
                }
            }
        }

        private GameObject Child(string name)
        {
            var result = new GameObject(name, typeof(NetworkObject));
            result.transform.SetParent(_root.transform);
            return result;
        }
        private static BoardTraversalState Traversal(NetworkPlayerAvatar player) =>
            (BoardTraversalState)typeof(NetworkPlayerAvatar).GetField("_traversal", Private).GetValue(player);
        private void Invoke(string method, double now) => typeof(NetworkMatchState).GetMethod(method, Private).Invoke(_match, new object[] { now });
        private static void Field(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        private static void Set<T>(object target, string field, T value) where T : unmanaged =>
            ((NetworkVariable<T>)target.GetType().GetField(field, Private).GetValue(target)).Value = value;
    }
}
