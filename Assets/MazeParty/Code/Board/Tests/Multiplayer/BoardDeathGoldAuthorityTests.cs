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
    public sealed class BoardDeathGoldAuthorityTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _root;
        private NetworkMatchState _match;
        private NetworkMatchState _previousMatch;
        private NetworkPlayerAvatar[] _players;
        private bool _previousWarning;
        private AchievementMatchProgress[] _achievements;

        [SetUp]
        public void SetUp()
        {
            var warning = typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning",
                BindingFlags.Static | BindingFlags.NonPublic);
            _previousWarning = (bool)warning.GetValue(null);
            warning.SetValue(null, true);
            _previousMatch = NetworkMatchState.Instance;
            _root = new GameObject("Death reward authority fixture");
            var matchObject = new GameObject("Match", typeof(NetworkObject));
            matchObject.transform.SetParent(_root.transform);
            _match = matchObject.AddComponent<NetworkMatchState>();
            Authority(_match, true);
            typeof(NetworkMatchState).GetProperty(nameof(NetworkMatchState.Instance)).SetValue(null, _match);
            Set(_match, "_gameplayEnabled", true);
            typeof(NetworkMatchState).GetField("_achievementMatchId", Private).SetValue(_match, Guid.NewGuid().ToString("N"));
            _achievements = (AchievementMatchProgress[])typeof(NetworkMatchState).GetField("_achievementPlayers", Private).GetValue(_match);
            for (var slot = 0; slot < 4; slot++) _achievements[slot] = new AchievementMatchProgress();
            _players = new NetworkPlayerAvatar[4];
            for (var slot = 0; slot < 4; slot++)
            {
                var playerObject = new GameObject("Player " + slot, typeof(NetworkObject));
                playerObject.transform.SetParent(_root.transform);
                var avatar = playerObject.AddComponent<NetworkPlayerAvatar>();
                Authority(avatar, true);
                Set(avatar, "_slot", slot);
                Set(avatar, "_gold", 10);
                Set(avatar, "_currentHealth", 100);
                _players[slot] = avatar;
            }
            typeof(NetworkMatchState).GetField("_avatarLookupCache", Private).SetValue(_match, _players);
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
        public void BoardDeath_RejectsClientMutationAndCreditsThirtyPercentExactlyOnce()
        {
            var victim = _players[0];
            Set(victim, "_gold", 99);
            Authority(victim, false);
            Assert.That(victim.ApplyDamage(new DamageRequest(100, DamageKind.Item,
                _players[1].gameObject, itemId: PrototypeItemId.Sniper)), Is.EqualTo(DamageResult.Ignored));
            Assert.That(_match.DeathGoldTransfers.Count, Is.Zero);
            Assert.That(_achievements[1].flags, Is.Zero);
            Assert.That(victim.Gold, Is.EqualTo(99));

            Authority(victim, true);
            ExpectPresentationRpcs(2);
            Assert.That(victim.ApplyDamage(new DamageRequest(100, DamageKind.Item,
                _players[1].gameObject, itemId: PrototypeItemId.Sniper)), Is.EqualTo(DamageResult.Applied));
            Assert.That(victim.Gold, Is.EqualTo(70));
            Assert.That(_players[1].Gold, Is.EqualTo(39));
            Assert.That(_match.DeathGoldTransfers.Count, Is.EqualTo(1));
            Assert.That(_match.DeathGoldTransfers[0].Amount, Is.EqualTo(29));
            Assert.That(_achievements[1].flags, Is.EqualTo(PartyAchievements.Bit(PartyAchievement.Sniper)));
            Assert.That(victim.ApplyDamage(new DamageRequest(100, DamageKind.Item,
                _players[1].gameObject, itemId: PrototypeItemId.Sniper)), Is.EqualTo(DamageResult.Ignored));
            Assert.That(_players[1].Gold, Is.EqualTo(39));
            Assert.That(_match.DeathGoldTransfers.Count, Is.EqualTo(1));
        }

        [Test]
        public void UncreditedDeath_PaysOtherThreeWhileCombatEliminationPaysNobody()
        {
            foreach (var selfHit in new[] { false, true })
            {
                var victim = _players[0];
                foreach (var player in _players) Set(player, "_gold", 10);
                Set(victim, "_currentHealth", 100);
                Set(victim, "_personalProtectionEndsAt", 0d);
                Set(victim, "_pausedPersonalProtectionRemaining", 0d);
                typeof(NetworkPlayerAvatar).GetField("_boardDeathEndsAt", Private).SetValue(victim, 0d);
                _match.DeathGoldTransfers.Clear();
                ExpectPresentationRpcs(1);
                victim.ApplyDamage(new DamageRequest(100,
                    selfHit ? DamageKind.Item : DamageKind.Environment,
                    selfHit ? victim.gameObject : null, itemId: PrototypeItemId.Mine));
                Assert.That(_players.Select(player => player.Gold).ToArray(),
                    Is.EqualTo(new[] { 4, 12, 12, 12 }));
                Assert.That(_match.DeathGoldTransfers.Count, Is.EqualTo(3));
                Assert.That(_achievements[0].flags,
                    Is.EqualTo(selfHit ? PartyAchievements.Bit(PartyAchievement.OwnMine) : 0L));
                for (var index = 0; index < 3; index++)
                    Assert.That(_match.DeathGoldTransfers[index].Amount, Is.EqualTo(2));
            }

            _match.DeathGoldTransfers.Clear();
            var before = _players.Select(player => player.Gold).ToArray();
            Set(_players[0], "_combatState", (byte)NetworkCombatState.Active);
            Set(_players[0], "_combatHealth", 5);
            ExpectPresentationRpcs(1);
            Assert.That(_players[0].ApplyCombatPunchOnServer(_players[1], Vector3.zero, 5), Is.True);
            Assert.That(_players.Select(player => player.Gold).ToArray(), Is.EqualTo(before));
            Assert.That(_match.DeathGoldTransfers.Count, Is.Zero);
        }

        [Test]
        public void LandingPreparation_WaitsForLastCoinAndPausePreservesFlight()
        {
            var flow = new BoardFlowStateMachine();
            flow.Start(0d);
            flow.Tick(7d);
            for (var slot = 0; slot < 4; slot++) flow.TryReportPlayerArrived(slot, 10d);
            flow.Tick(15d);
            flow.TryCompleteCombat(15d, 0.1d);
            typeof(NetworkMatchState).GetField("_flow", Private).SetValue(_match, flow);
            typeof(NetworkMatchState).GetField("_nextLandingEffectSlot", Private).SetValue(_match, 4);
            _match.DeathGoldTransfers.Add(new BoardKillGoldSnapshot
                { Id = 1, Amount = 2, RecipientSlot = 1, StartedAt = 20d });
            Invoke("PauseDeathGoldPresentationOnServer", 20.3d);
            Invoke("ResumeDeathGoldPresentationOnServer", 120.3d);
            Assert.That(_match.DeathGoldTransfers[0].StartedAt, Is.EqualTo(120d).Within(0.000001d));
            var end = _match.DeathGoldTransfers[0].StartedAt + BoardDeathGoldPresentationRules.Duration(2);
            foreach (var elapsed in new[] { end - 0.000001d, end })
            {
                Set(_match, "_deathGoldPausedAt", elapsed);
                var pending = (bool)typeof(NetworkMatchState).GetMethod(
                    "HasBoardDeathInProgressOnServer", Private).Invoke(_match, null);
                var defer = (bool)typeof(NetworkMatchState).GetMethod(
                    "ShouldDeferLandingEffectResolutionOnServer", Private).Invoke(_match, new object[] { pending });
                flow.Tick(130d, false, pending, defer);
                Assert.That(flow.State, Is.EqualTo(elapsed < end
                    ? BoardFlowState.LandingEffectResolve : BoardFlowState.MinigameIntroReady));
            }
            Assert.That(flow.GetStateRemaining(130d), Is.EqualTo(60d));
        }

        [Test]
        public void GrenadeBlast_DamagesThrowerWithoutSelfCredit_AndHonorsWallCover()
        {
            var owner = _players[0];
            owner.transform.position = new Vector3(0f, 100f, 2f);
            owner.gameObject.AddComponent<CharacterController>();
            for (var i = 1; i < 4; i++) _players[i].transform.position = new Vector3(100f, 100f, i * 10f);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(_root.transform);
            wall.transform.position = new Vector3(0f, 100f, 1f);
            var grenade = PrototypeItemCatalog.Get(PrototypeItemId.Grenade);
            var explode = typeof(NetworkMatchState).GetMethod("ExplodeBoardItem", Private);
            foreach (var covered in new[] { true, false })
            {
                wall.SetActive(covered);
                Physics.SyncTransforms();
                ExpectPresentationRpcs(covered ? 1 : 2);
                explode.Invoke(_match, new object[] { new Vector3(0f, 100f, 0f), 0, grenade });
                Assert.That(owner.CurrentHealth, Is.EqualTo(covered ? 100 : 100 - grenade.Damage));
                Assert.That(_match.DeathGoldTransfers.Count, Is.Zero);
                Assert.That(_achievements[0].flags, Is.Zero);
            }
        }

        private static void ExpectPresentationRpcs(int count)
        {
            // This EditMode fixture exercises authority without opening a network session.
            // NGO rejects the presentation-only RPCs; the server ledger still executes.
            for (var index = 0; index < count; index++)
                LogAssert.Expect(LogType.Error,
                    "Rpc methods can only be invoked after starting the NetworkManager!");
        }

        private void Invoke(string method, double now) =>
            typeof(NetworkMatchState).GetMethod(method, Private).Invoke(_match, new object[] { now });

        private static void Authority(NetworkBehaviour behaviour, bool server)
        {
            typeof(NetworkBehaviour).GetProperty(nameof(NetworkBehaviour.IsServer)).SetValue(behaviour, server);
            typeof(NetworkBehaviour).GetProperty(nameof(NetworkBehaviour.IsSpawned)).SetValue(behaviour, true);
        }

        private static void Set<T>(object target, string field, T value) where T : unmanaged =>
            ((NetworkVariable<T>)target.GetType().GetField(field, Private).GetValue(target)).Value = value;
    }
}
