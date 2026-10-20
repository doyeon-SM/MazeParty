using System;
using System.Collections.Generic;
using System.Reflection;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class ConfiguredMinigameSeriesAuthorityTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void SingleRoundGamesRepeatWithoutIntermediateRewards_AndSettleOnlyFinalAggregateOnce()
        {
            var warning = typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning",
                BindingFlags.Static | BindingFlags.NonPublic);
            var previous = (bool)warning.GetValue(null);
            warning.SetValue(null, true);
            try
            {
                foreach (var id in new[] { ScheduledMinigameId.TerritoryPaint, ScheduledMinigameId.BombPassing,
                    ScheduledMinigameId.ArenaCombat, ScheduledMinigameId.Flight })
                {
                    var root = new GameObject("Configured series authority", typeof(NetworkObject));
                    try
                    {
                        var match = root.AddComponent<NetworkMatchState>();
                        var old = MatchGameSettings.Default.Get(id);
                        MatchGameSettings.Default.TryChangeGame(id, new MinigameSetting(true, 2, old.Seconds), out var settings);
                        typeof(NetworkMatchState).GetProperty("GameSettings").SetValue(match, settings);
                        SetNetwork(match, "_gameplayEnabled", true);
                        SetNetwork(match, "_flowState", (byte)BoardFlowState.MinigamePlaying);
                        SetNetwork(match, "_currentMinigame", (byte)id);
                        var players = new NetworkPlayerAvatar[4];
                        for (var slot = 0; slot < 4; slot++)
                        {
                            var playerRoot = new GameObject("Player " + slot, typeof(NetworkObject));
                            playerRoot.transform.SetParent(root.transform);
                            players[slot] = playerRoot.AddComponent<NetworkPlayerAvatar>();
                            typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(players[slot], true);
                            typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(players[slot], true);
                            SetNetwork(players[slot], "_slot", slot);
                            SetNetwork(players[slot], "_gold", 10);
                        }
                        typeof(NetworkMatchState).GetField("_avatarLookupCache", Private).SetValue(match, players);
                        var flow = new BoardFlowStateMachine(totalTurns: 10);
                        flow.RestoreCheckpoint(BoardFlowState.MinigameIntroReady, 1, 0d);
                        flow.TryBeginMinigameLoading(1d); flow.TryBeginMinigame(2d);
                        typeof(NetworkMatchState).GetField("_flow", Private).SetValue(match, flow);
                        typeof(NetworkMatchState).GetMethod("BeginConfiguredSeries", Private).Invoke(match, null);
                        var complete = typeof(NetworkMatchState).GetMethod("TryCompleteMinigameOnServer", Private);
                        var first = new[] { new PlayerPlacement(0, 1), new PlayerPlacement(1, 2),
                            new PlayerPlacement(2, 3), new PlayerPlacement(3, 4) };
                        var second = new[] { new PlayerPlacement(0, 2), new PlayerPlacement(1, 1),
                            new PlayerPlacement(2, 3), new PlayerPlacement(3, 4) };
                        Func<IReadOnlyList<PlayerPlacement>, bool> submit = placements =>
                            (bool)complete.Invoke(match, new object[] { id, placements });
                        Assert.That(submit(first), Is.False, "A client cannot settle results.");
                        typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(match, true);
                        Assert.That(submit(first), Is.True);
                        Assert.That(submit(first), Is.True, "A repeated callback while waiting must not pay or count twice.");
                        Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigamePlaying));
                        foreach (var avatar in players) Assert.That(avatar.Gold, Is.EqualTo(10));
                        SetNetwork(match, "_configuredSeriesRound", 2);
                        typeof(NetworkMatchState).GetField("_nextConfiguredRoundPending", Private).SetValue(match, false);
                        Assert.That(submit(second), Is.True);
                        Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigameResult));
                        var ranks = new[] { 1, 1, 3, 4 };
                        for (var slot = 0; slot < 4; slot++)
                        {
                            Assert.That(match.GetSeriesFinalRank(slot), Is.EqualTo(ranks[slot]));
                            Assert.That(players[slot].Gold, Is.EqualTo(10 + MinigameRewardRules.GetFinalPlacementGold(ranks[slot])));
                        }
                        Assert.That(submit(second), Is.False, "The already settled turn cannot pay again.");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(root); }
                }
            }
            finally { warning.SetValue(null, previous); }
        }
        private static void SetNetwork<T>(object owner, string name, T value)
        {
            ((NetworkVariable<T>)owner.GetType().GetField(name, Private).GetValue(owner)).Value = value;
        }
    }
}
