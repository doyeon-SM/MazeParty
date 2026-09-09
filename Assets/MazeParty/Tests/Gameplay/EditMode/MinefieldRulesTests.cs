using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.Minefield;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class MinefieldRulesTests
    {
        [Test]
        public void MatchConstants_AreThreeRoundsFourPlayersAndThreeTwoOneZeroPoints()
        {
            Assert.That(MinefieldRules.RoundCount, Is.EqualTo(3));
            Assert.That(MinefieldRules.PlayerCount, Is.EqualTo(4));
            Assert.That(MinefieldRules.MineHitsToEliminate, Is.EqualTo(2));
            Assert.That(
                new[]
                {
                    MinefieldRules.GetPointsForRank(1),
                    MinefieldRules.GetPointsForRank(2),
                    MinefieldRules.GetPointsForRank(3),
                    MinefieldRules.GetPointsForRank(4)
                },
                Is.EqualTo(new[] { 3, 2, 1, 0 }));
        }

        [Test]
        public void FirstMineHit_CripplesPlayerHidesTorsoAndUsesWalkingSpeed()
        {
            var state = new MinefieldPlayerRoundState(2);

            Assert.That(state.MovementSpeedMultiplier, Is.EqualTo(1f));

            var resolution = state.ApplyMineHit();

            Assert.That(resolution.WasApplied, Is.True);
            Assert.That(resolution.BecameCrippled, Is.True);
            Assert.That(resolution.BecameEliminated, Is.False);
            Assert.That(state.State, Is.EqualTo(MinefieldPlayerState.Crippled));
            Assert.That(state.MineHitCount, Is.EqualTo(1));
            Assert.That(state.ShouldHideTorso, Is.True);
            Assert.That(state.CanMove, Is.True);
            Assert.That(
                state.MovementSpeedMultiplier,
                Is.EqualTo(FootstepRules.WalkSpeedMultiplier));
        }

        [Test]
        public void SecondMineHit_EliminatesPlayer()
        {
            var state = new MinefieldPlayerRoundState(0);
            state.ApplyMineHit();

            var resolution = state.ApplyMineHit();

            Assert.That(resolution.WasApplied, Is.True);
            Assert.That(resolution.BecameEliminated, Is.True);
            Assert.That(state.State, Is.EqualTo(MinefieldPlayerState.Eliminated));
            Assert.That(state.MineHitCount, Is.EqualTo(2));
            Assert.That(state.IsTerminal, Is.True);
            Assert.That(state.CanMove, Is.False);
        }

        [Test]
        public void TerminalPlayer_IgnoresLaterHitsAndCannotChangeOutcome()
        {
            var eliminated = new MinefieldPlayerRoundState(0);
            eliminated.ApplyMineHit();
            eliminated.ApplyMineHit();

            var ignored = eliminated.ApplyMineHit();

            Assert.That(ignored.WasApplied, Is.False);
            Assert.That(eliminated.MineHitCount, Is.EqualTo(2));
            Assert.That(eliminated.TryFinish(), Is.False);

            var finished = new MinefieldPlayerRoundState(1);
            Assert.That(finished.TryFinish(), Is.True);
            Assert.That(finished.ApplyMineHit().WasApplied, Is.False);
            Assert.That(finished.State, Is.EqualTo(MinefieldPlayerState.Finished));
        }

        [Test]
        public void RoundScoring_PutsFinishersFirstThenEarlierEliminations()
        {
            var result = MinefieldRoundScoring.Score(
                new[]
                {
                    MinefieldRoundOutcome.Finish(2, 20),
                    MinefieldRoundOutcome.Eliminate(0, 2),
                    MinefieldRoundOutcome.Finish(1, 10),
                    MinefieldRoundOutcome.Eliminate(3, 1)
                });

            AssertStanding(result, 0, 1, 3, MinefieldTerminalKind.Finished);
            AssertStanding(result, 1, 2, 2, MinefieldTerminalKind.Finished);
            AssertStanding(result, 2, 3, 1, MinefieldTerminalKind.Eliminated);
            AssertStanding(result, 3, 0, 0, MinefieldTerminalKind.Eliminated);
        }

        [Test]
        public void RoundScoring_WhenNobodyFinishes_EarlierDeathRanksHigher()
        {
            var result = MinefieldRoundScoring.Score(
                new[]
                {
                    MinefieldRoundOutcome.Eliminate(3, 40),
                    MinefieldRoundOutcome.Eliminate(1, 10),
                    MinefieldRoundOutcome.Eliminate(0, 30),
                    MinefieldRoundOutcome.Eliminate(2, 20)
                });

            Assert.That(PlayerSlots(result), Is.EqualTo(new[] { 1, 2, 0, 3 }));
            Assert.That(Points(result), Is.EqualTo(new[] { 3, 2, 1, 0 }));
        }

        [Test]
        public void RoundScoring_UsesLowerSlotForSimultaneousEvents()
        {
            var result = MinefieldRoundScoring.Score(
                new[]
                {
                    MinefieldRoundOutcome.Eliminate(3, 20),
                    MinefieldRoundOutcome.Finish(2, 10),
                    MinefieldRoundOutcome.Eliminate(1, 20),
                    MinefieldRoundOutcome.Finish(0, 10)
                });

            Assert.That(PlayerSlots(result), Is.EqualTo(new[] { 0, 2, 1, 3 }));
        }

        [Test]
        public void RoundScoring_RejectsMissingOrDuplicatePlayers()
        {
            Assert.Throws<ArgumentException>(
                () => MinefieldRoundScoring.Score(
                    new[]
                    {
                        MinefieldRoundOutcome.Finish(0, 1),
                        MinefieldRoundOutcome.Finish(1, 2),
                        MinefieldRoundOutcome.Finish(2, 3)
                    }));

            Assert.Throws<ArgumentException>(
                () => MinefieldRoundScoring.Score(
                    new[]
                    {
                        MinefieldRoundOutcome.Finish(0, 1),
                        MinefieldRoundOutcome.Finish(0, 2),
                        MinefieldRoundOutcome.Finish(2, 3),
                        MinefieldRoundOutcome.Finish(3, 4)
                    }));
        }

        [Test]
        public void MatchLeaderboard_AggregatesExactlyThreeRoundsAndBreaksTiesBySlot()
        {
            var leaderboard = MinefieldMatchScoring.BuildLeaderboard(
                new[]
                {
                    ScoreFinishOrder(0, 1, 2, 3),
                    ScoreFinishOrder(2, 0, 3, 1),
                    ScoreFinishOrder(1, 3, 2, 0)
                });

            Assert.That(LeaderboardSlots(leaderboard), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(LeaderboardRanks(leaderboard), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(LeaderboardPoints(leaderboard), Is.EqualTo(new[] { 5, 5, 5, 3 }));
        }

        [Test]
        public void MatchLeaderboard_RejectsAnythingOtherThanThreeRounds()
        {
            var round = ScoreFinishOrder(0, 1, 2, 3);

            Assert.Throws<ArgumentException>(
                () => MinefieldMatchScoring.BuildLeaderboard(new[] { round, round }));
        }

        [Test]
        public void Layout_SameServerSeedAndRoundProducesCanonicalGoldenCells()
        {
            var forbidden = new[]
            {
                new MinefieldCell(0, 0),
                new MinefieldCell(4, 3)
            };

            var first = MinefieldLayoutGenerator.Generate(
                5,
                4,
                6,
                0x123456789ABCDEF0UL,
                2,
                forbidden);
            var second = MinefieldLayoutGenerator.Generate(
                5,
                4,
                6,
                0x123456789ABCDEF0UL,
                2,
                forbidden);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(
                first,
                Is.EqualTo(
                    new[]
                    {
                        new MinefieldCell(2, 0),
                        new MinefieldCell(3, 0),
                        new MinefieldCell(1, 1),
                        new MinefieldCell(2, 1),
                        new MinefieldCell(4, 1),
                        new MinefieldCell(3, 3)
                    }));
        }

        [Test]
        public void Layout_ChangesPerRoundAndNeverUsesForbiddenOrDuplicateCells()
        {
            var forbidden = new HashSet<MinefieldCell>
            {
                new MinefieldCell(0, 0),
                new MinefieldCell(1, 0),
                new MinefieldCell(2, 0)
            };

            var roundOne = MinefieldLayoutGenerator.Generate(
                6,
                6,
                12,
                77UL,
                1,
                forbidden);
            var roundTwo = MinefieldLayoutGenerator.Generate(
                6,
                6,
                12,
                77UL,
                2,
                forbidden);

            Assert.That(roundTwo, Is.Not.EqualTo(roundOne));
            Assert.That(new HashSet<MinefieldCell>(roundOne).Count, Is.EqualTo(12));
            foreach (var mine in roundOne)
            {
                Assert.That(forbidden.Contains(mine), Is.False);
            }
        }

        [Test]
        public void Layout_RejectsInvalidRoundAndMineCountBeyondAvailableCells()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MinefieldLayoutGenerator.Generate(2, 2, 1, 1UL, 0));
            Assert.Throws<ArgumentException>(
                () => MinefieldLayoutGenerator.Generate(
                    2,
                    2,
                    2,
                    1UL,
                    1,
                    new[]
                    {
                        new MinefieldCell(0, 0),
                        new MinefieldCell(0, 1),
                        new MinefieldCell(1, 0)
                    }));
        }

        private static MinefieldRoundResult ScoreFinishOrder(params int[] playerSlots)
        {
            var outcomes = new MinefieldRoundOutcome[playerSlots.Length];
            for (var index = 0; index < playerSlots.Length; index++)
            {
                outcomes[index] = MinefieldRoundOutcome.Finish(
                    playerSlots[index],
                    (ulong)index);
            }

            return MinefieldRoundScoring.Score(outcomes);
        }

        private static void AssertStanding(
            MinefieldRoundResult result,
            int standingIndex,
            int expectedSlot,
            int expectedPoints,
            MinefieldTerminalKind expectedKind)
        {
            var standing = result.Standings[standingIndex];
            Assert.That(standing.Rank, Is.EqualTo(standingIndex + 1));
            Assert.That(standing.PlayerSlot, Is.EqualTo(expectedSlot));
            Assert.That(standing.Points, Is.EqualTo(expectedPoints));
            Assert.That(standing.Outcome.TerminalKind, Is.EqualTo(expectedKind));
        }

        private static int[] PlayerSlots(MinefieldRoundResult result)
        {
            var slots = new int[result.Standings.Count];
            for (var index = 0; index < slots.Length; index++)
            {
                slots[index] = result.Standings[index].PlayerSlot;
            }

            return slots;
        }

        private static int[] Points(MinefieldRoundResult result)
        {
            var points = new int[result.Standings.Count];
            for (var index = 0; index < points.Length; index++)
            {
                points[index] = result.Standings[index].Points;
            }

            return points;
        }

        private static int[] LeaderboardSlots(
            IReadOnlyList<MinefieldLeaderboardEntry> leaderboard)
        {
            var slots = new int[leaderboard.Count];
            for (var index = 0; index < slots.Length; index++)
            {
                slots[index] = leaderboard[index].PlayerSlot;
            }

            return slots;
        }

        private static int[] LeaderboardRanks(
            IReadOnlyList<MinefieldLeaderboardEntry> leaderboard)
        {
            var ranks = new int[leaderboard.Count];
            for (var index = 0; index < ranks.Length; index++)
            {
                ranks[index] = leaderboard[index].Rank;
            }

            return ranks;
        }

        private static int[] LeaderboardPoints(
            IReadOnlyList<MinefieldLeaderboardEntry> leaderboard)
        {
            var points = new int[leaderboard.Count];
            for (var index = 0; index < points.Length; index++)
            {
                points[index] = leaderboard[index].TotalPoints;
            }

            return points;
        }
    }
}
