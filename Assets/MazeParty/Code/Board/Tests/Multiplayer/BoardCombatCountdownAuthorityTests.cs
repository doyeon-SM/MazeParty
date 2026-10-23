using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardCombatCountdownAuthorityTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _root;
        private NetworkMatchState _match, _previous;
        private NetworkPlayerAvatar[] _players;
        private bool _warning;

        [SetUp]
        public void SetUp()
        {
            var warning = typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning",
                BindingFlags.Static | BindingFlags.NonPublic);
            _warning = (bool)warning.GetValue(null);
            warning.SetValue(null, true);
            _previous = NetworkMatchState.Instance;
            _root = new GameObject("Board combat countdown fixture");
            _match = Child("Match").AddComponent<NetworkMatchState>();
            typeof(NetworkMatchState).GetProperty("Instance").SetValue(null, _match);
            typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(_match, true);
            Set(_match, "_gameplayEnabled", true);
            Set(_match, "_flowState", (byte)BoardFlowState.CombatResolve);
            Set(_match, "_combatActive", true);
            Set(_match, "_combatParticipantMask", (byte)3);
            Set(_match, "_combatAliveMask", (byte)3);
            _players = new NetworkPlayerAvatar[4];
            for (var slot = 0; slot < 4; slot++)
            {
                var player = Child("Player " + slot).AddComponent<NetworkPlayerAvatar>();
                typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(player, true);
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(player, true);
                Set(player, "_slot", slot);
                Set(player, "_combatState", (byte)NetworkCombatState.Active);
                Set(player, "_combatHealth", 100);
                _players[slot] = player;
            }
            typeof(NetworkMatchState).GetField("_avatarLookupCache", Private).SetValue(_match, _players);
        }

        [TearDown]
        public void TearDown()
        {
            typeof(NetworkMatchState).GetProperty("Instance").SetValue(null, _previous);
            foreach (var behaviour in _root.GetComponentsInChildren<NetworkBehaviour>())
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(behaviour, false);
            Object.DestroyImmediate(_root);
            typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning",
                BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _warning);
        }

        [Test]
        public void Countdown_OpensCombatInputAtThreeSecondsAndRejectsEarlyPunches()
        {
            foreach (var elapsed in new[] { 0d, 0.999d, 1d, 2d, 2.9999d, 3d, 3.001d, 62.999d, 63d })
            {
                var remaining = 63d - elapsed;
                Set(_match, "_combatEndsAt", _match.SynchronizedNow + remaining);
                Assert.That(_match.CombatCountdownRemaining, Is.EqualTo(System.Math.Max(0d, 3d - elapsed)).Within(0.000001d));
                Assert.That(_match.CombatRemaining, Is.EqualTo(System.Math.Min(60d, remaining)).Within(0.000001d));
                Assert.That(_match.IsCombatCountdown, Is.EqualTo(elapsed < 3d));
                Assert.That(_match.CanAvatarUseCombatInput(_players[0]), Is.EqualTo(elapsed >= 3d && elapsed < 63d), elapsed.ToString());
                Assert.That(_match.CanAvatarUseCombatInput(_players[2]), Is.False, "Nonparticipant");
                if (elapsed < 3d)
                    Assert.That(_match.TryCombatPunchOnServer(_players[0], Vector3.zero, Vector3.forward), Is.False);
            }
            Set(_match, "_combatEndsAt", _match.SynchronizedNow + 60d);
            Set(_match, "_combatAliveMask", (byte)2);
            Assert.That(_match.CanAvatarUseCombatInput(_players[0]), Is.False, "Eliminated player");
            Set(_match, "_combatAliveMask", (byte)3);
            typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(_match, false);
            Assert.That(_match.TryCombatPunchOnServer(_players[0], Vector3.zero, Vector3.forward), Is.False, "Client authority");
        }

        [Test]
        public void EachQueuedFight_StartsWithASeparatePreparationAndFullFightDuration()
        {
            for (var slot = 0; slot < 4; slot++)
            {
                var coordinate = new Vector2Int(slot / 2, 0);
                var tile = Child("Tile " + slot).AddComponent<BoardTile>();
                tile.Configure(coordinate, BoardTileType.Normal);
                Set(_players[slot], "_hasLogicalTile", true);
                Set(_players[slot], "_logicalTileCoordinate", coordinate);
                ((BoardTraversalState)typeof(NetworkPlayerAvatar).GetField("_traversal", Private)
                    .GetValue(_players[slot])).Begin(tile, 0);
            }
            var queue = (Queue<BoardCombatGroup>)typeof(NetworkMatchState).GetField("_combatQueue", Private).GetValue(_match);
            queue.Enqueue(new BoardCombatGroup(Vector2Int.zero, 3, 0d, false));
            queue.Enqueue(new BoardCombatGroup(Vector2Int.right, 12, 1d, false));
            for (var sequence = 1; sequence <= 2; sequence++)
            {
                Invoke("BeginNextCombatOrFinishOnServer", _match.SynchronizedNow);
                Assert.That(_match.CombatSequenceIndex, Is.EqualTo(sequence));
                Assert.That(_match.CombatCountdownRemaining, Is.EqualTo(3d).Within(0.000001d));
                Assert.That(_match.CombatRemaining, Is.EqualTo(60d));
                Assert.That(_match.CombatParticipantMask, Is.EqualTo(sequence == 1 ? 3 : 12));
            }
        }

        [Test]
        public void PauseAndReconnect_PreservePreparationAndRunningTime()
        {
            foreach (var pauseField in new[] { "_playerPauseActive", "_reconnectPaused" })
            foreach (var remaining in new[] { 62.5d, 60d, 27d })
            {
                var now = _match.SynchronizedNow;
                Set(_match, "_combatEndsAt", now + remaining);
                Invoke("PauseCombatAndPersonalProtectionOnServer", now);
                Set(_match, pauseField, true);
                Assert.That(_match.CombatCountdownRemaining, Is.EqualTo(System.Math.Max(0d, remaining - 60d)).Within(0.000001d));
                Assert.That(_match.CombatRemaining, Is.EqualTo(System.Math.Min(60d, remaining)).Within(0.000001d));
                Assert.That(_match.CanAvatarUseCombatInput(_players[0]), Is.False);
                Invoke("ResumeCombatAndPersonalProtectionOnServer", _match.SynchronizedNow);
                Set(_match, pauseField, false);
                Assert.That(_match.CombatCountdownRemaining, Is.EqualTo(System.Math.Max(0d, remaining - 60d)).Within(0.000001d));
                Assert.That(_match.CanAvatarUseCombatInput(_players[0]), Is.EqualTo(remaining <= 60d));
            }
        }

        [Test]
        public void SharedHud_ShowsPreparationOnlyForTheLocalLivingParticipant()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigameCommonHud.prefab");
            var instance = Object.Instantiate(prefab, _root.transform);
            var view = instance.GetComponent<MinigameStartCountdownView>();
            var prompt = instance.GetComponentsInChildren<LocalizedText>(true)
                .Single(label => label.SourceText == "FIGHT!").GetComponent<Text>();
            Set(_match, "_combatEndsAt", _match.SynchronizedNow + 63d);
            foreach (var localSlot in new[] { -1, 0, 1, 2, 3 })
            {
                for (var slot = 0; slot < 4; slot++)
                    typeof(NetworkBehaviour).GetProperty("IsOwner").SetValue(_players[slot], slot == localSlot);
                typeof(MinigameStartCountdownView).GetMethod("Update", Private).Invoke(view, null);
                Assert.That(view.IsVisible, Is.EqualTo(localSlot == 0 || localSlot == 1), "Local slot " + localSlot);
                Assert.That(prompt.gameObject.activeInHierarchy, Is.EqualTo(view.IsVisible));
            }
            Set(_match, "_combatAliveMask", (byte)2);
            view.RefreshForPlayer(_match, 0);
            Assert.That(view.IsVisible, Is.False);
            Set(_match, "_combatAliveMask", (byte)3);
            foreach (var sample in new[] { (63d, 3), (62d, 2), (61d, 1), (60d, 0) })
            {
                Set(_match, "_combatEndsAt", _match.SynchronizedNow + sample.Item1);
                view.RefreshForPlayer(_match, 0);
                Assert.That(view.DisplayedNumeral, Is.EqualTo(sample.Item2));
                Assert.That(prompt.gameObject.activeInHierarchy, Is.EqualTo(sample.Item2 > 0));
            }
            view.SetCountdown(3, true, false);
            Assert.That(view.IsVisible, Is.True, "Shared minigame countdown still works");
            Assert.That(prompt.gameObject.activeSelf, Is.False, "Minigames do not show board combat prompt");
        }

        private GameObject Child(string name)
        {
            var child = new GameObject(name, typeof(NetworkObject));
            child.transform.SetParent(_root.transform);
            return child;
        }

        private void Invoke(string method, double now) =>
            typeof(NetworkMatchState).GetMethod(method, Private).Invoke(_match, new object[] { now });
        private static void Set<T>(object target, string field, T value) where T : unmanaged =>
            ((NetworkVariable<T>)target.GetType().GetField(field, Private).GetValue(target)).Value = value;
    }
}
