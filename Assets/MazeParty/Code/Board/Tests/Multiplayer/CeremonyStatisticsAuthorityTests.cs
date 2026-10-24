using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class CeremonyStatisticsAuthorityTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void Statistics_RecordsAuthoritativeMutationsAndFreezesAtFinalResults()
        {
            var previous = NetworkMatchState.Instance;
            var warning = typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning", BindingFlags.Static | BindingFlags.NonPublic);
            var oldWarning = warning.GetValue(null);
            var root = new GameObject("Statistics authority fixture");
            try
            {
                warning.SetValue(null, true);
                var matchObject = new GameObject("Match", typeof(NetworkObject)); matchObject.transform.SetParent(root.transform);
                var match = matchObject.AddComponent<NetworkMatchState>();
                typeof(NetworkMatchState).GetProperty("Instance").SetValue(null, match);
                Authority(match, true); Set(match, "_gameplayEnabled", true); Set(match, "_turn", 1);
                var players = new NetworkPlayerAvatar[4];
                for (var slot = 0; slot < 4; slot++)
                {
                    var go = new GameObject("Player " + slot, typeof(NetworkObject)); go.transform.SetParent(root.transform);
                    players[slot] = go.AddComponent<NetworkPlayerAvatar>(); Authority(players[slot], true);
                    Set(players[slot], "_slot", slot); Set(players[slot], "_gold", 100);
                    Set(match, "_awardCeremonyFinalRank" + slot, (byte)(slot + 1));
                }
                typeof(NetworkMatchState).GetField("_avatarLookupCache", Private).SetValue(match, players);
                var stats = MatchStatistics.Begin(15);
                typeof(NetworkMatchState).GetField("_statistics", Private).SetValue(match, stats);
                for (var slot = 0; slot < 4; slot++) match.RecordStatisticsBalanceOnServer(players[slot], 0);
                players[0].ApplyGoldDeltaOnServer(20);
                players[0].ApplyKeyDeltaOnServer(1);
                players[0].RecordMinigamePlacementOnServer(2);
                match.RecordStatisticsDamageOnServer(players[1], players[0], 50, 5);
                match.RecordStatisticsMoveOnServer(players[0]);
                Assert.That(stats.balances.Count, Is.EqualTo(6));
                Assert.That(stats.balances[5].keys, Is.EqualTo(1));
                Assert.That(stats.balances[5].gold, Is.EqualTo(120));
                Assert.That(stats.placements[1], Is.EqualTo(1));
                Assert.That(stats.damage[0].source, Is.EqualTo(0));
                Assert.That(stats.damage[0].target, Is.EqualTo(1));
                Assert.That(stats.players[0].moves, Is.EqualTo(1));

                Authority(match, false);
                match.RecordStatisticsDamageOnServer(players[1], players[0], 70, 5);
                match.RecordStatisticsMoveOnServer(players[0]);
                Assert.That(stats.damage.Count, Is.EqualTo(1));
                Assert.That(stats.players[0].moves, Is.EqualTo(1));
                Authority(match, true);
                typeof(NetworkMatchState).GetMethod("FinalizeStatisticsOnServer", Private).Invoke(match, null);
                Assert.That(stats.frozen, Is.True);
                Assert.That(match.CeremonyStatistics, Is.SameAs(stats));
                match.RecordStatisticsDamageOnServer(players[1], players[0], 70, 5);
                match.RecordStatisticsMoveOnServer(players[0]);
                Assert.That(stats.damage.Count, Is.EqualTo(1));
                Assert.That(stats.players[0].moves, Is.EqualTo(1));
                Assert.That(MatchStatistics.TryDecode(JsonUtility.ToJson(stats), out _), Is.True);
            }
            finally
            {
                typeof(NetworkMatchState).GetProperty("Instance").SetValue(null, previous);
                foreach (var b in root.GetComponentsInChildren<NetworkBehaviour>(true)) typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(b, false);
                Object.DestroyImmediate(root); warning.SetValue(null, oldWarning);
            }
        }

        [Test]
        public void AuthoredStatisticsPopups_OpenAndCloseLocallyWithoutChangingReturnDeadline()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MazeParty/Prefabs/Board/UI/CeremonyStatisticsCanvas.prefab");
            var root = new GameObject("Statistics local popup fixture");
            try
            {
                var match = root.AddComponent<NetworkMatchState>();
                var deadline = (NetworkVariable<double>)typeof(NetworkMatchState).GetField("_awardCeremonyAutoReturnEndsAt", Private).GetValue(match);
                var before = deadline.Value;
                var first = Object.Instantiate(prefab, root.transform).GetComponent<CeremonyStatisticsView>();
                var second = Object.Instantiate(prefab, root.transform).GetComponent<CeremonyStatisticsView>();
                first.Bindings.RootCanvas.enabled = second.Bindings.RootCanvas.enabled = true;
                first.Open();
                Assert.That(first.IsOpen, Is.True); Assert.That(second.IsOpen, Is.False);
                second.Open(); first.Close();
                Assert.That(first.IsOpen, Is.False); Assert.That(second.IsOpen, Is.True);
                Assert.That(deadline.Value, Is.EqualTo(before));
                second.Close();
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void Authority(NetworkBehaviour b, bool server)
        {
            typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(b, server);
            typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(b, true);
        }
        private static void Set<T>(object target, string field, T value) where T : unmanaged =>
            ((NetworkVariable<T>)target.GetType().GetField(field, Private).GetValue(target)).Value = value;
    }
}
