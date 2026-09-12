using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.StableFooting;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class StableFootingRulesTests
    {
        [Test]
        public void Schedule_IsDeterministicAndShrinksUnsafeTilesToOne()
        {
            const ulong seed = 0x123456789ABCDEF0UL;
            var first = StableFootingCycleScheduleGenerator.Generate(seed, 1);
            var repeated = StableFootingCycleScheduleGenerator.Generate(seed, 1);
            var nextRound = StableFootingCycleScheduleGenerator.Generate(seed, 2);

            Assert.That(first.Cycles.Count, Is.EqualTo(24));
            AssertSchedulesEqual(first, repeated);
            Assert.That(
                CycleSignature(nextRound.Cycles[0]),
                Is.Not.EqualTo(CycleSignature(first.Cycles[0])));

            var expectedActiveCount = StableFootingRules.TileCount;
            var allPermanentlyRemoved = new HashSet<int>();
            for (var index = 0; index < first.Cycles.Count; index++)
            {
                var cycle = first.Cycles[index];
                Assert.That(cycle.CycleNumber, Is.EqualTo(index + 1));
                Assert.That(cycle.ActiveTileCount, Is.EqualTo(expectedActiveCount));

                var safeCount = 0;
                var symbols = new HashSet<StableFootingSymbol>();
                foreach (var assignment in cycle.Assignments)
                {
                    Assert.That(
                        allPermanentlyRemoved.Contains(assignment.TileIndex),
                        Is.False);
                    symbols.Add(assignment.Symbol);
                    if (cycle.IsTileSafe(assignment.TileIndex))
                    {
                        safeCount++;
                    }
                }

                Assert.That(safeCount, Is.GreaterThanOrEqualTo(1));
                if (cycle.ActiveTileCount >= StableFootingRules.SymbolCount)
                {
                    Assert.That(
                        symbols.Count,
                        Is.EqualTo(StableFootingRules.SymbolCount));
                }

                var expectedRemovalCount = Math.Min(
                    StableFootingRules.PermanentTilesRemovedPerCycle,
                    expectedActiveCount - 1);
                Assert.That(
                    cycle.PermanentlyRemovedTileIndices.Count,
                    Is.EqualTo(expectedRemovalCount));
                foreach (var tileIndex in cycle.PermanentlyRemovedTileIndices)
                {
                    Assert.That(cycle.IsTileSafe(tileIndex), Is.False);
                    Assert.That(allPermanentlyRemoved.Add(tileIndex), Is.True);
                }

                expectedActiveCount -= expectedRemovalCount;
            }

            Assert.That(expectedActiveCount, Is.EqualTo(1));
            Assert.That(
                allPermanentlyRemoved.Contains(first.FinalTileIndex),
                Is.False);
        }

        [Test]
        public void CycleTiming_UsesExactPhaseBoundariesAndMoveFloor()
        {
            var schedule =
                StableFootingCycleScheduleGenerator.Generate(77UL, 1);
            var first = schedule.Cycles[0];

            Assert.That(
                schedule.GetPhaseAt(0.999d),
                Is.EqualTo(StableFootingCyclePhase.ShuffleReveal));
            Assert.That(
                schedule.GetPhaseAt(first.ShuffleRevealEndsAtSeconds),
                Is.EqualTo(StableFootingCyclePhase.Move));
            Assert.That(
                schedule.GetPhaseAt(first.MoveEndsAtSeconds),
                Is.EqualTo(StableFootingCyclePhase.Drop));
            Assert.That(
                schedule.GetPhaseAt(first.DropEndsAtSeconds),
                Is.EqualTo(StableFootingCyclePhase.Restore));
            Assert.That(
                schedule.GetPhaseAt(first.EndsAtSeconds),
                Is.EqualTo(StableFootingCyclePhase.ShuffleReveal));
            Assert.That(schedule.Cycles[0].MoveSeconds, Is.EqualTo(4d));
            Assert.That(schedule.Cycles[1].MoveSeconds, Is.EqualTo(3.75d));
            Assert.That(schedule.Cycles[8].MoveSeconds, Is.EqualTo(2d));
            Assert.That(schedule.Cycles[20].MoveSeconds, Is.EqualTo(2d));
            Assert.That(
                schedule.GetPhaseAt(StableFootingRules.RoundSeconds),
                Is.EqualTo(StableFootingCyclePhase.RoundComplete));
        }

        [Test]
        public void Falls_EliminateImmediatelyAndRankLaterServerEventsHigher()
        {
            var round = new StableFootingRoundState(101UL, 1);
            var firstFalls = round.ResolveFalls(new[] { 0, 1 }, 10d);

            Assert.That(firstFalls[0].WasApplied, Is.True);
            Assert.That(firstFalls[1].WasApplied, Is.True);
            Assert.That(round.GetPlayer(0).IsEliminated, Is.True);
            Assert.That(round.AlivePlayerCount, Is.EqualTo(2));
            Assert.That(round.IsComplete, Is.False);

            round.ResolveFalls(new[] { 2 }, 20d);

            Assert.That(round.IsComplete, Is.True);
            Assert.That(
                round.EndReason,
                Is.EqualTo(StableFootingRoundEndReason.LastSurvivor));
            Assert.That(
                PlayerSlots(round.Result),
                Is.EqualTo(new[] { 3, 2, 1, 0 }));
            Assert.That(
                Points(round.Result),
                Is.EqualTo(new[] { 3, 2, 1, 0 }));

            var simultaneous = new StableFootingRoundState(102UL, 1);
            simultaneous.ResolveFalls(new[] { 0, 1, 2, 3 }, 12d);
            Assert.That(
                simultaneous.EndReason,
                Is.EqualTo(StableFootingRoundEndReason.AllEliminated));
            Assert.That(
                PlayerSlots(simultaneous.Result),
                Is.EqualTo(new[] { 3, 2, 1, 0 }));
        }

        [Test]
        public void Timeout_TriggersAtExactSixtySecondBoundary()
        {
            var round = new StableFootingRoundState(103UL, 2);
            round.ResolveFalls(new[] { 2 }, 30d);

            Assert.That(round.TryEndForTimeout(59.999d), Is.False);
            Assert.That(round.TryEndForTimeout(60d), Is.True);
            Assert.That(round.IsComplete, Is.True);
            Assert.That(
                round.EndReason,
                Is.EqualTo(StableFootingRoundEndReason.TimeLimit));
            Assert.That(
                PlayerSlots(round.Result),
                Is.EqualTo(new[] { 0, 1, 3, 2 }));
            Assert.That(round.TryEndForTimeout(61d), Is.False);
        }

        [Test]
        public void MatchLeaderboard_RequiresThreeValidRoundsAndUsesFinalRankTieBreak()
        {
            var first = ScoreOrder(0, 1, 2, 3);
            var second = ScoreOrder(1, 2, 0, 3);
            var third = ScoreOrder(2, 0, 1, 3);

            var leaderboard = StableFootingMatchScoring.BuildLeaderboard(
                new[] { first, second, third });

            Assert.That(
                LeaderboardSlots(leaderboard),
                Is.EqualTo(new[] { 2, 0, 1, 3 }));
            Assert.That(
                LeaderboardPoints(leaderboard),
                Is.EqualTo(new[] { 6, 6, 6, 0 }));
            Assert.Throws<ArgumentException>(
                () => StableFootingMatchScoring.BuildLeaderboard(
                    new[] { first, second }));
            Assert.Throws<ArgumentException>(
                () => StableFootingRoundScoring.Score(
                    new[]
                    {
                        StableFootingRoundOutcome.Survive(0),
                        StableFootingRoundOutcome.Survive(0),
                        StableFootingRoundOutcome.Eliminate(2, 2d, 1UL),
                        StableFootingRoundOutcome.Eliminate(3, 1d, 2UL)
                    }));
        }

        private static StableFootingRoundResult ScoreOrder(
            params int[] slots)
        {
            return StableFootingRoundScoring.Score(
                new[]
                {
                    StableFootingRoundOutcome.Survive(slots[0]),
                    StableFootingRoundOutcome.Eliminate(slots[1], 3d, 3UL),
                    StableFootingRoundOutcome.Eliminate(slots[2], 2d, 2UL),
                    StableFootingRoundOutcome.Eliminate(slots[3], 1d, 1UL)
                });
        }

        private static void AssertSchedulesEqual(
            StableFootingRoundSchedule expected,
            StableFootingRoundSchedule actual)
        {
            Assert.That(actual.FinalTileIndex, Is.EqualTo(expected.FinalTileIndex));
            Assert.That(actual.Cycles.Count, Is.EqualTo(expected.Cycles.Count));
            for (var index = 0; index < expected.Cycles.Count; index++)
            {
                Assert.That(
                    CycleSignature(actual.Cycles[index]),
                    Is.EqualTo(CycleSignature(expected.Cycles[index])));
            }
        }

        private static string CycleSignature(StableFootingCycle cycle)
        {
            var assignments = string.Empty;
            foreach (var assignment in cycle.Assignments)
            {
                assignments += assignment + ";";
            }

            var removals = string.Empty;
            foreach (var tileIndex in cycle.PermanentlyRemovedTileIndices)
            {
                removals += tileIndex + ";";
            }

            return cycle.CycleNumber + "|" +
                cycle.StartsAtSeconds + "|" +
                cycle.MoveSeconds + "|" +
                cycle.SafeSymbol + "|" +
                assignments + "|" + removals;
        }

        private static int[] PlayerSlots(StableFootingRoundResult result)
        {
            var slots = new int[result.Standings.Count];
            for (var index = 0; index < slots.Length; index++)
            {
                slots[index] = result.Standings[index].PlayerSlot;
            }

            return slots;
        }

        private static int[] Points(StableFootingRoundResult result)
        {
            var points = new int[result.Standings.Count];
            for (var index = 0; index < points.Length; index++)
            {
                points[index] = result.Standings[index].Points;
            }

            return points;
        }

        private static int[] LeaderboardSlots(
            IReadOnlyList<StableFootingLeaderboardEntry> leaderboard)
        {
            var slots = new int[leaderboard.Count];
            for (var index = 0; index < slots.Length; index++)
            {
                slots[index] = leaderboard[index].PlayerSlot;
            }

            return slots;
        }

        private static int[] LeaderboardPoints(
            IReadOnlyList<StableFootingLeaderboardEntry> leaderboard)
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
