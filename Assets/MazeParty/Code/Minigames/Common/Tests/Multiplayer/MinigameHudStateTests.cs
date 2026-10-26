using System;
using System.Reflection;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using NUnit.Framework;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MinigameHudStateTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _root;
        private NetworkMatchState _match, _previous;
        private bool _oldWarning;

        [SetUp]
        public void SetUp()
        {
            var warning = typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning", BindingFlags.Static | BindingFlags.NonPublic);
            _oldWarning = (bool)warning.GetValue(null); warning.SetValue(null, true);
            _root = new GameObject("Minigame HUD state fixture");
            _previous = NetworkMatchState.Instance;
            _match = Child("Match").AddComponent<NetworkMatchState>();
            NetworkCountdownTestAccess.SetInstance(typeof(NetworkMatchState), _match);
            Set(_match, "_flowState", (byte)BoardFlowState.MinigamePlaying);
        }

        [TearDown]
        public void TearDown()
        {
            NetworkCountdownTestAccess.SetInstance(typeof(NetworkMatchState), _previous);
            foreach (var behaviour in _root.GetComponentsInChildren<NetworkBehaviour>(true))
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(behaviour, false);
            Object.DestroyImmediate(_root);
            typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _oldWarning);
        }

        [Test]
        public void TaggerCountdown_KeepsFirstPersonAndRejectsInput_ThenNextTaggerOwnsCamera()
        {
            var state = Child("Tag").AddComponent<NetworkTagChaseState>();
            var view = state.gameObject.AddComponent<TagChaseNetworkView>(); Field(view, "state", state);
            var first = Child("First").AddComponent<CinemachineCamera>();
            var shared = Child("Shared").AddComponent<CinemachineCamera>();
            Field(view, "taggerCamera", first); Field(view, "sharedRunnerCamera", shared);
            Field(view, "_cameraDirector", Child("Director").AddComponent<GameplayCameraDirector>());
            Field(view, "_localSlot", 0);
            Set(state, "_matchActive", true); Set(state, "_taggerSlot", (byte)0);
            Set(state, "_phaseEndsAt", Time.unscaledTimeAsDouble + 30d);
            foreach (var phase in new[] { NetworkTagChasePhase.Countdown, NetworkTagChasePhase.Running })
            {
                Set(state, "_phase", (byte)phase);
                Call(view, "RefreshCamera", _match);
                Assert.That(Get<CinemachineCamera>(view, "_registeredCamera"), Is.SameAs(first));
                for (var slot = 0; slot < 4; slot++)
                    Assert.That(state.CanAcceptInputForSlot(slot), Is.EqualTo(phase == NetworkTagChasePhase.Running));
            }
            Set(state, "_phase", (byte)NetworkTagChasePhase.Countdown);
            Set(state, "_taggerSlot", (byte)1);
            Call(view, "RefreshCamera", _match);
            Assert.That(Get<CinemachineCamera>(view, "_registeredCamera"), Is.SameAs(shared));
            Field(view, "_localSlot", 1);
            Call(view, "RefreshCamera", _match);
            Assert.That(Get<CinemachineCamera>(view, "_registeredCamera"), Is.SameAs(first));
        }

        [Test]
        public void ArenaCountdown_KeepsFirstPersonWithoutCombatInput_EliminationSwitchesToSpectator()
        {
            var player = Avatar(0, true);
            var state = Child("Arena").AddComponent<NetworkArenaCombatState>();
            typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(state, true);
            var view = state.gameObject.AddComponent<ArenaCombatNetworkView>(); Field(view, "state", state);
            var first = Child("First").AddComponent<CinemachineCamera>();
            var spectator = Child("Spectator").AddComponent<CinemachineCamera>();
            view.Configure(state, first, spectator, Child("World"), new Transform[4]);
            Field(view, "_cameraDirector", Child("Director").AddComponent<GameplayCameraDirector>());
            Set(_match, "_gameplayEnabled", true);
            Set(_match, "_currentMinigame", (byte)ScheduledMinigameId.ArenaCombat);
            Set(state, "_matchActive", true);
            Set(state, "_phaseEndsAt", Time.unscaledTimeAsDouble + 30d);
            foreach (var phase in new[] { NetworkArenaCombatPhase.Countdown, NetworkArenaCombatPhase.Playing })
            {
                Set(state, "_phase", (byte)phase);
                Call(view, "Update");
                Assert.That(Get<CinemachineCamera>(view, "_registeredCamera"), Is.SameAs(first));
                Assert.That(state.CanAcceptInputForSlot(0), Is.EqualTo(phase == NetworkArenaCombatPhase.Playing));
            }
            Set(state, "_eliminatedMask", (byte)1);
            Call(view, "Update");
            Assert.That(Get<CinemachineCamera>(view, "_registeredCamera"), Is.SameAs(spectator));
        }

        [Test]
        public void GiftHud_UsesLiveDepositsAndResetsWithRound_WithoutUsingAccumulatedPoints()
        {
            var state = Child("Gift").AddComponent<NetworkGiftGrabState>();
            var view = state.gameObject.AddComponent<GiftGrabNetworkView>(); Field(view, "state", state);
            var hudRoot = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MazeParty/Prefabs/Minigames/Common/UI/BouncingBallsHud.prefab"), _root.transform);
            var hud = hudRoot.GetComponent<BouncingBallsHudBindings>(); view.ConfigureHud(hud);
            var snapshots = Get<NetworkList<GiftGrabPlayerNetworkSnapshot>>(state, "_playerSnapshots");
            Call(state, "EnsureSnapshotCountsOnServer");
            Set(state, "_scores", 0x04030201U); Set(state, "_totalStoredGiftCounts", 0x0A0B0C0DU);
            foreach (var count in new byte[] { 0, 5, 2 })
            {
                for (var slot = 0; slot < 4; slot++)
                    snapshots[slot] = new GiftGrabPlayerNetworkSnapshot { StoredGiftCount = (byte)(count + slot) };
                Call(view, "RefreshGiftHud", _match);
                for (var slot = 0; slot < 4; slot++)
                    Assert.That(hud.ScoreTexts[slot].text, Is.EqualTo((count + slot).ToString()));
            }
            Call(state, "BeginRoundOnServer", 2, 100d);
            Call(view, "RefreshGiftHud", _match);
            for (var slot = 0; slot < 4; slot++)
            {
                Assert.That(hud.ScoreTexts[slot].text, Is.EqualTo("0"));
                Assert.That(state.GetScore(slot), Is.EqualTo(slot + 1));
            }
            Call(view, "SetWorldPresentationActive", false);
            Assert.That(hud.RootCanvas.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void OpponentHealth_TracksAvatarAndHidesAfterCombatIncludingCeremony()
        {
            var state = Child("Arena").AddComponent<NetworkArenaCombatState>();
            typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(state, true);
            Set(state, "_matchActive", true);
            Set(state, "_phase", (byte)NetworkArenaCombatPhase.Playing);
            Set(_match, "_gameplayEnabled", true);
            Set(_match, "_currentMinigame", (byte)ScheduledMinigameId.ArenaCombat);
            Avatar(0, true);
            var view = state.gameObject.AddComponent<ArenaCombatNetworkView>(); Field(view, "state", state);
            var world = Child("Arena World");
            Field(view, "arenaPresentation", world);
            Field(view, "_cameraDirector", Child("Director").AddComponent<GameplayCameraDirector>());
            view.ConfigureHealthBar(AssetDatabase.LoadAssetAtPath<ArenaCombatHealthBarView>(
                "Assets/MazeParty/Prefabs/Minigames/ArenaCombat/UI/ArenaCombatHealthBar.prefab"));
            var opponent = Avatar(1, false);
            foreach (var health in new[] { 75, 40 })
            {
                opponent.AvatarVisual.SetNameplateVisible(false);
                Set(opponent, "_combatHealth", health);
                Call(view, "Update");
                var bar = Get<ArenaCombatHealthBarView[]>(view, "_healthBars")[1];
                Assert.That(bar.gameObject.activeInHierarchy, Is.True);
                Assert.That(Get<Text>(bar, "healthText").text, Is.EqualTo(health + "/" + BoardCombatRules.TemporaryHealth));
            }
            var original = Get<ArenaCombatHealthBarView[]>(view, "_healthBars")[1];
            typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(opponent, false);
            var replacement = Avatar(1, false);
            Set(replacement, "_combatHealth", 60);
            Call(view, "Update");
            Assert.That(Get<ArenaCombatHealthBarView[]>(view, "_healthBars")[1], Is.SameAs(original));
            Assert.That(Get<PlayerAvatarVisual>(original, "_target"), Is.SameAs(replacement.AvatarVisual));
            Set(state, "_eliminatedMask", (byte)2);
            Call(view, "Update");
            Assert.That(original.gameObject.activeSelf, Is.False);
            Set(state, "_eliminatedMask", (byte)0);

            // The selected ID intentionally remains ArenaCombat after the final
            // round, including through the award ceremony and the next board turn.
            foreach (var sample in new[] {
                (BoardFlowState.MinigamePlaying, NetworkArenaCombatPhase.Playing, true, true),
                (BoardFlowState.MinigamePlaying, NetworkArenaCombatPhase.Complete, false, true),
                (BoardFlowState.MinigameResult, NetworkArenaCombatPhase.Complete, false, true),
                (BoardFlowState.MatchComplete, NetworkArenaCombatPhase.Complete, false, false),
                (BoardFlowState.TurnOverview, NetworkArenaCombatPhase.Complete, false, false),
                (BoardFlowState.Action, NetworkArenaCombatPhase.Playing, false, false),
                (BoardFlowState.MinigameLoading, NetworkArenaCombatPhase.Countdown, false, false),
                (BoardFlowState.MinigamePlaying, NetworkArenaCombatPhase.Countdown, true, true),
                (BoardFlowState.MinigamePlaying, NetworkArenaCombatPhase.Playing, true, true) })
            {
                Set(_match, "_flowState", (byte)sample.Item1);
                Set(state, "_phase", (byte)sample.Item2);
                Call(view, "Update");
                var context = sample.Item1 + " / " + sample.Item2;
                Assert.That(original.gameObject.activeSelf, Is.EqualTo(sample.Item3), context);
                Assert.That(world.activeSelf, Is.EqualTo(sample.Item4), context);
                Assert.That(Get<ArenaCombatHealthBarView[]>(view, "_healthBars")[0].gameObject.activeSelf,
                    Is.False, "The owner's first-person bar remains hidden.");
            }
            Set(_match, "_gameplayEnabled", false);
            Call(view, "Update");
            Assert.That(original.gameObject.activeSelf, Is.False);
            Assert.That(world.activeSelf, Is.False);
            Set(_match, "_gameplayEnabled", true);
            Call(view, "Update");
            Assert.That(original.gameObject.activeSelf, Is.True);
            Call(view, "OnDisable");
            Assert.That(original.gameObject.activeSelf, Is.False);
        }

        private GameObject Child(string name) { var go = new GameObject(name); go.transform.SetParent(_root.transform); return go; }
        private NetworkPlayerAvatar Avatar(int slot, bool owner)
        {
            var go = Child("Player " + slot);
            var avatar = go.AddComponent<NetworkPlayerAvatar>();
            var visual = go.AddComponent<PlayerAvatarVisual>(); visual.EnsureBuilt();
            Field(avatar, "_avatarVisual", visual);
            Field(avatar, "eyePivot", Child("Eye").transform);
            typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(avatar, true);
            typeof(NetworkBehaviour).GetProperty("IsOwner").SetValue(avatar, owner);
            Set(avatar, "_slot", slot); Set(avatar, "_combatHealth", BoardCombatRules.TemporaryHealth);
            Set(avatar, "_combatState", (byte)NetworkCombatState.Active);
            Get<NetworkPlayerAvatar[]>(_match, "_avatarLookupCache")[slot] = avatar;
            return avatar;
        }
        private static void Field(object o, string n, object v) => o.GetType().GetField(n, Private).SetValue(o, v);
        private static T Get<T>(object o, string n) => (T)o.GetType().GetField(n, Private).GetValue(o);
        private static void Set<T>(object o, string n, T v) => NetworkCountdownTestAccess.SetNetworkValue(o, n, v);
        private static void Call(object o, string n, params object[] a) => NetworkCountdownTestAccess.InvokePrivate(o, n, a);
    }
}
