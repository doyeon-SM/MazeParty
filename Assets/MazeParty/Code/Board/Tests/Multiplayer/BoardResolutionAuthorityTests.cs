using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardResolutionAuthorityTests
    {
        [Test]
        public void MissingPlayerLookup_CannotSkipCombatOrCaptureAnEmptyLandingPlan()
        {
            var root = new GameObject("Board resolution authority fixture", typeof(NetworkObject));
            try
            {
                var match = root.AddComponent<NetworkMatchState>();
                typeof(NetworkBehaviour).GetProperty(nameof(NetworkBehaviour.IsServer))
                    .SetValue(match, true);
                var flow = new BoardFlowStateMachine();
                flow.Start(0d);
                flow.Tick(7d);
                for (var slot = 0; slot < 4; slot++) flow.TryReportPlayerArrived(slot, 10d);
                flow.Tick(15d);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(NetworkMatchState).GetField("_flow", flags).SetValue(match, flow);
                foreach (var method in new[] { "BeginCombatSequenceOnServer",
                    "BeginNextCombatOrFinishOnServer", "AdvanceCombatOnServer" })
                {
                    typeof(NetworkMatchState).GetMethod(method, flags)
                        .Invoke(match, new object[] { 100d });
                    Assert.That(flow.State, Is.EqualTo(BoardFlowState.CombatResolve), method);
                    Assert.That(typeof(NetworkMatchState).GetField("_landingEffectPlanReady", flags)
                        .GetValue(match), Is.EqualTo(false), method);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
        [Test]
        public void LandingSequence_WaitsForEachRevealAndResumesOrdinaryEffectsAfterEvent()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var warning = typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning",
                BindingFlags.Static | BindingFlags.NonPublic);
            var previousWarning = (bool)warning.GetValue(null);
            var previousMatch = NetworkMatchState.Instance;
            var root = new GameObject("Landing timeline fixture");
            try
            {
                warning.SetValue(null, true);
                var matchObject = new GameObject("Match", typeof(NetworkObject));
                matchObject.transform.SetParent(root.transform);
                var match = matchObject.AddComponent<NetworkMatchState>();
                typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(match, true);
                var players = new NetworkPlayerAvatar[4];
                var effects = (BoardLandingEffectType[])typeof(NetworkMatchState)
                    .GetField("_landingEffectPlan", flags).GetValue(match);
                var tiles = (BoardTile[])typeof(NetworkMatchState)
                    .GetField("_landingEffectTiles", flags).GetValue(match);
                var durations = (double[])typeof(NetworkMatchState)
                    .GetField("_landingEffectDurations", flags).GetValue(match);
                var order = (int[])typeof(NetworkMatchState)
                    .GetField("_landingEffectOrder", flags).GetValue(match);
                for (var slot = 0; slot < 4; slot++)
                {
                    var playerObject = new GameObject("Player " + slot, typeof(NetworkObject));
                    playerObject.transform.SetParent(root.transform);
                    var player = playerObject.AddComponent<NetworkPlayerAvatar>();
                    typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(player, true);
                    typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(player, true);
                    SetNetwork(player, "_slot", slot);
                    SetNetwork(player, "_gold", 10);
                    players[slot] = player;
                    var tileObject = new GameObject("Tile " + slot);
                    tileObject.transform.SetParent(root.transform);
                    tiles[slot] = tileObject.AddComponent<BoardTile>();
                    tiles[slot].Configure(new Vector2Int(slot, 0), BoardTileType.Normal);
                    order[slot] = slot;
                    durations[slot] = 2.5d;
                }
                effects[0] = BoardLandingEffectType.GoldGain;
                effects[1] = BoardLandingEffectType.SpecialEvent;
                effects[2] = BoardLandingEffectType.GoldLoss;
                durations[1] = 11.5d;
                var plan = (BoardSpecialEventRules.Resolution[])typeof(NetworkMatchState)
                    .GetField("_specialEventPlan", flags).GetValue(match);
                plan[1] = new BoardSpecialEventRules.Resolution(BoardSpecialEventFamily.Direct,
                    BoardSpecialEventAudience.Everyone, BoardSpecialEventOperation.Gain,
                    BoardSpecialEventResource.Gold, -1, 10);
                typeof(NetworkMatchState).GetField("_avatarLookupCache", flags).SetValue(match, players);
                typeof(NetworkMatchState).GetField("_landingEffectPlanReady", flags).SetValue(match, true);
                var flow = new BoardFlowStateMachine();
                flow.Start(0d); flow.Tick(7d);
                for (var slot = 0; slot < 4; slot++) flow.TryReportPlayerArrived(slot, 10d);
                flow.Tick(15d); flow.TryCompleteCombat(15d, 19d);
                typeof(NetworkMatchState).GetField("_flow", flags).SetValue(match, flow);
                var advance = typeof(NetworkMatchState).GetMethod("AdvanceLandingEffectResolutionOnServer", flags);
                System.Action<double> tick = elapsed => advance.Invoke(match, new object[] { 15d + elapsed });
                tick(0d); tick(0.499d);
                Assert.That(players[0].Gold, Is.EqualTo(10));
                tick(0.5d);
                Assert.That(players[0].Gold, Is.EqualTo(13));
                tick(2.499d);
                Assert.That(match.LastLandingEffectSlot, Is.EqualTo(0));
                tick(2.5d);
                Assert.That(match.LandingEffectFeedback.FeedbackKind, Is.EqualTo(BoardLandingEffectFeedbackKind.EventIntro));
                foreach (var elapsed in new[] { 4.999d, 5d, 5.5d, 6.5d, 7.5d, 8.499d })
                {
                    tick(elapsed);
                    Assert.That(players[1].Gold, Is.EqualTo(10), "event applied early at " + elapsed);
                    Assert.That(match.LastLandingEffectSlot, Is.EqualTo(1));
                    Assert.That(LandingEffectMessage.GetEffectType(match.LastLandingEffectEncodedMessage),
                        Is.EqualTo(BoardLandingEffectType.SpecialEvent));
                }
                tick(8.5d);
                Assert.That(players[1].Gold, Is.EqualTo(20));
                Assert.That(match.LandingEffectFeedback.AffectedMask, Is.EqualTo(15));
                tick(8.5d);
                Assert.That(players[1].Gold, Is.EqualTo(20), "The event must settle exactly once.");
                tick(13.999d);
                Assert.That(match.LastLandingEffectSlot, Is.EqualTo(1));
                Assert.That(players[2].Gold, Is.EqualTo(20));
                tick(14d); tick(14.499d);
                Assert.That(match.LastLandingEffectSlot, Is.EqualTo(2));
                Assert.That(players[2].Gold, Is.EqualTo(20));
                tick(14.5d);
                Assert.That(players[2].Gold, Is.EqualTo(17));
                tick(16.5d); tick(17d); tick(19d);
                Assert.That(typeof(NetworkMatchState).GetField("_nextLandingEffectSlot", flags)
                    .GetValue(match), Is.EqualTo(4));
            }
            finally
            {
                foreach (var behaviour in root.GetComponentsInChildren<NetworkBehaviour>())
                    typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(behaviour, false);
                typeof(NetworkMatchState).GetProperty(nameof(NetworkMatchState.Instance)).SetValue(null, previousMatch);
                Object.DestroyImmediate(root);
                warning.SetValue(null, previousWarning);
            }
        }

        private static void SetNetwork<T>(object target, string field, T value) where T : unmanaged =>
            ((NetworkVariable<T>)target.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target)).Value = value;

    }
}
