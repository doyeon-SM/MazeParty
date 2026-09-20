using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.RedLightGreenLight;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class RedLightGreenLightRulesTests
    {
        [Test]
        public void SignalSchedule_IsCanonicalBoundedAlternatingAndCoversRound()
        {
            const ulong seed = 0x123456789ABCDEF0UL;
            var first = RedLightGreenLightSignalScheduleGenerator.Generate(seed, 1);
            var repeated = RedLightGreenLightSignalScheduleGenerator.Generate(seed, 1);
            var nextRound = RedLightGreenLightSignalScheduleGenerator.Generate(seed, 2);

            Assert.That(repeated.Windows, Is.EqualTo(first.Windows));
            Assert.That(nextRound.Windows, Is.Not.EqualTo(first.Windows));
            Assert.That(first.Windows[0].StartsAtSeconds, Is.Zero);
            Assert.That(first.Windows[0].Phase, Is.EqualTo(RedLightGreenLightSignalPhase.Green));

            for (var index = 0; index < first.Windows.Count; index++)
            {
                var window = first.Windows[index];
                Assert.That(window.Phase, Is.EqualTo((RedLightGreenLightSignalPhase)(index % 3)));
                if (index > 0)
                {
                    Assert.That(window.StartsAtSeconds, Is.EqualTo(first.Windows[index - 1].EndsAtSeconds));
                }

                if (window.Phase == RedLightGreenLightSignalPhase.Green)
                {
                    Assert.That(window.DurationSeconds, Is.InRange(1.5d, 4d));
                }
                else if (window.Phase == RedLightGreenLightSignalPhase.TurnWarning)
                {
                    Assert.That(window.DurationSeconds, Is.EqualTo(0.35d));
                }
                else
                {
                    Assert.That(window.DurationSeconds, Is.InRange(1d, 2.5d));
                }
            }

            Assert.That(first.Windows[first.Windows.Count - 1].EndsAtSeconds, Is.GreaterThanOrEqualTo(60d));
            Assert.DoesNotThrow(() => first.GetWindowAt(60d));
        }

        [Test]
        public void MovementIntent_RespectsSignalBoundaryAndIgnoresExternalPushes()
        {
            var round = new RedLightGreenLightRoundState(77UL, 1);
            var red = FirstWindow(round, RedLightGreenLightSignalPhase.Red);
            var warning = FirstWindow(
                round,
                RedLightGreenLightSignalPhase.TurnWarning);

            var justBefore = round.SubmitMovementIntent(0, true, red.RedDetectionStartsAtSeconds - 0.0001d);
            var atBoundary = round.SubmitMovementIntent(0, true, red.RedDetectionStartsAtSeconds);
            var duringWarning = round.SubmitMovementIntent(
                1,
                true,
                warning.StartsAtSeconds + 0.2d);
            Assert.That(round.SetForwardProgress(2, 14.5f), Is.True);
            var externalPush = round.SubmitMovementIntent(
                2,
                false,
                red.RedDetectionStartsAtSeconds + 0.01d);

            Assert.That(justBefore.Status, Is.EqualTo(RedLightGreenLightMovementIntentStatus.AllowedDuringRedGrace));
            Assert.That(justBefore.WasViolation, Is.False);
            Assert.That(atBoundary.Status, Is.EqualTo(RedLightGreenLightMovementIntentStatus.FirstViolation));
            Assert.That(atBoundary.WasViolation, Is.True);
            Assert.That(round.GetPlayer(0).ViolationCount, Is.EqualTo(1));
            Assert.That(
                duringWarning.Status,
                Is.EqualTo(
                    RedLightGreenLightMovementIntentStatus.AllowedBySignal));
            Assert.That(round.GetPlayer(1).ViolationCount, Is.Zero);
            Assert.That(
                externalPush.Status,
                Is.EqualTo(
                    RedLightGreenLightMovementIntentStatus.NoVoluntaryMovement));
            Assert.That(round.GetPlayer(2).ForwardProgressMeters, Is.EqualTo(14.5f));
            Assert.That(round.GetPlayer(2).ViolationCount, Is.Zero);
        }

        [Test]
        public void Violations_FirstWarnThenSecondEliminatesAndFreezesPlayer()
        {
            var round = new RedLightGreenLightRoundState(101UL, 1);
            var firstRed = WindowAt(
                round,
                RedLightGreenLightSignalPhase.Red,
                0);
            var secondRed = WindowAt(
                round,
                RedLightGreenLightSignalPhase.Red,
                1);
            var first = round.SubmitMovementIntent(
                0,
                true,
                firstRed.RedDetectionStartsAtSeconds);
            var player = round.GetPlayer(0);

            Assert.That(first.BecameWarned, Is.True);
            Assert.That(player.State, Is.EqualTo(RedLightGreenLightPlayerState.Warned));
            Assert.That(player.ShouldHideTorso, Is.True);
            Assert.That(player.CanMove, Is.True);
            Assert.That(player.MovementSpeedMetersPerSecond, Is.EqualTo(2.75f));

            var duplicate = round.SubmitMovementIntent(
                0,
                true,
                firstRed.RedDetectionStartsAtSeconds + 0.001d);
            var resolution = round.SubmitMovementIntent(
                0,
                true,
                secondRed.RedDetectionStartsAtSeconds);

            Assert.That(
                duplicate.Status,
                Is.EqualTo(
                    RedLightGreenLightMovementIntentStatus
                        .AlreadyPenalizedDuringCurrentRed));
            Assert.That(resolution.BecameEliminated, Is.True);
            Assert.That(player.State, Is.EqualTo(RedLightGreenLightPlayerState.Eliminated));
            Assert.That(player.ViolationCount, Is.EqualTo(2));
            Assert.That(player.CanMove, Is.False);
            Assert.That(player.MovementSpeedMetersPerSecond, Is.Zero);
            Assert.That(round.SetForwardProgress(0, 50f), Is.False);
        }

        [Test]
        public void FirstFinisher_EndsImmediatelyAndRanksSurvivorsByProgressBeforeEliminated()
        {
            var round = new RedLightGreenLightRoundState(102UL, 2);
            round.SetForwardProgress(0, 18f);
            round.SetForwardProgress(1, 30f);
            round.SetForwardProgress(2, 55f);
            round.SetForwardProgress(3, 45f);

            var firstRed = WindowAt(
                round,
                RedLightGreenLightSignalPhase.Red,
                0);
            var secondRed = WindowAt(
                round,
                RedLightGreenLightSignalPhase.Red,
                1);
            round.SubmitMovementIntent(
                2,
                true,
                firstRed.RedDetectionStartsAtSeconds);
            round.SubmitMovementIntent(
                2,
                true,
                secondRed.RedDetectionStartsAtSeconds);

            Assert.That(
                round.TryFinish(
                    0,
                    60f,
                    secondRed.RedDetectionStartsAtSeconds + 0.01d),
                Is.True);

            Assert.That(round.IsComplete, Is.True);
            Assert.That(round.EndReason, Is.EqualTo(RedLightGreenLightRoundEndReason.FirstFinisher));
            Assert.That(PlayerSlots(round.Result), Is.EqualTo(new[] { 0, 3, 1, 2 }));
            Assert.That(Points(round.Result), Is.EqualTo(new[] { 3, 2, 1, 0 }));
            Assert.That(
                round.TryFinish(
                    3,
                    60f,
                    secondRed.RedDetectionStartsAtSeconds + 0.02d),
                Is.False);
            Assert.Throws<System.ArgumentException>(
                () => RedLightGreenLightRoundScoring.Score(
                    new[]
                    {
                        Outcome(0, RedLightGreenLightPlayerState.Healthy, 60f),
                        Outcome(0, RedLightGreenLightPlayerState.Healthy, 50f),
                        Outcome(2, RedLightGreenLightPlayerState.Healthy, 40f),
                        Outcome(3, RedLightGreenLightPlayerState.Healthy, 30f)
                    }));
        }

        [Test]
        public void Timeout_RanksEverySurvivorByCurrentForwardProgressThenEliminatedPlayers()
        {
            var round = new RedLightGreenLightRoundState(103UL, 3);
            round.SetForwardProgress(0, 10f);
            round.SetForwardProgress(1, 40f);
            round.SetForwardProgress(2, 20f);
            round.SetForwardProgress(3, 50f);
            var firstRed = WindowAt(
                round,
                RedLightGreenLightSignalPhase.Red,
                0);
            var secondRed = WindowAt(
                round,
                RedLightGreenLightSignalPhase.Red,
                1);
            round.SubmitMovementIntent(
                3,
                true,
                firstRed.RedDetectionStartsAtSeconds);
            round.SubmitMovementIntent(
                3,
                true,
                secondRed.RedDetectionStartsAtSeconds);

            Assert.That(round.TryEndForTimeout(59.999d), Is.False);
            Assert.That(round.TryEndForTimeout(60d), Is.True);

            Assert.That(round.EndReason, Is.EqualTo(RedLightGreenLightRoundEndReason.TimeLimit));
            Assert.That(PlayerSlots(round.Result), Is.EqualTo(new[] { 1, 2, 0, 3 }));
            Assert.That(round.TryEndForTimeout(61d), Is.False);
        }

        [Test]
        public void MatchLeaderboard_UsesProgressForPointTiesAcrossThreeRounds()
        {
            var first = ScoreProgress(100f, 90f, 10f, 0f);
            var second = ScoreProgress(50f, 40f, 100f, 0f);
            var third = ScoreProgress(10f, 100f, 90f, 0f);

            var leaderboard = RedLightGreenLightMatchScoring.BuildLeaderboard(new[] { first, second, third });

            Assert.That(LeaderboardSlots(leaderboard), Is.EqualTo(new[] { 1, 2, 0, 3 }));
            Assert.That(
                new[]
                {
                    leaderboard[0].Rank,
                    leaderboard[1].Rank,
                    leaderboard[2].Rank,
                    leaderboard[3].Rank
                },
                Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(
                new[]
                {
                    leaderboard[0].TotalPoints,
                    leaderboard[1].TotalPoints,
                    leaderboard[2].TotalPoints,
                    leaderboard[3].TotalPoints
                },
                Is.EqualTo(new[] { 6, 6, 6, 0 }));
            Assert.Throws<System.ArgumentException>(
                () => RedLightGreenLightMatchScoring.BuildLeaderboard(
                    new[] { first, second }));
        }

        private static RedLightGreenLightSignalWindow FirstWindow(
            RedLightGreenLightRoundState round,
            RedLightGreenLightSignalPhase phase)
        {
            return WindowAt(round, phase, 0);
        }

        private static RedLightGreenLightSignalWindow WindowAt(
            RedLightGreenLightRoundState round,
            RedLightGreenLightSignalPhase phase,
            int phaseIndex)
        {
            var foundIndex = 0;
            for (var index = 0; index < round.SignalSchedule.Windows.Count; index++)
            {
                var window = round.SignalSchedule.Windows[index];
                if (window.Phase == phase)
                {
                    if (foundIndex == phaseIndex)
                    {
                        return window;
                    }

                    foundIndex++;
                }
            }

            throw new AssertionException("Expected signal phase was not generated.");
        }

        private static RedLightGreenLightRoundOutcome Outcome(
            int playerSlot,
            RedLightGreenLightPlayerState state,
            float progress)
        {
            return new RedLightGreenLightRoundOutcome(playerSlot, state, progress);
        }

        private static RedLightGreenLightRoundResult ScoreProgress(params float[] progress)
        {
            var outcomes = new RedLightGreenLightRoundOutcome[progress.Length];
            for (var slot = 0; slot < progress.Length; slot++)
            {
                outcomes[slot] = Outcome(slot, RedLightGreenLightPlayerState.Healthy, progress[slot]);
            }

            return RedLightGreenLightRoundScoring.Score(outcomes);
        }

        private static int[] PlayerSlots(RedLightGreenLightRoundResult result)
        {
            var slots = new int[result.Standings.Count];
            for (var index = 0; index < slots.Length; index++)
            {
                slots[index] = result.Standings[index].PlayerSlot;
            }

            return slots;
        }

        private static int[] Points(RedLightGreenLightRoundResult result)
        {
            var points = new int[result.Standings.Count];
            for (var index = 0; index < points.Length; index++)
            {
                points[index] = result.Standings[index].Points;
            }

            return points;
        }

        private static int[] LeaderboardSlots(IReadOnlyList<RedLightGreenLightLeaderboardEntry> leaderboard)
        {
            var slots = new int[leaderboard.Count];
            for (var index = 0; index < slots.Length; index++)
            {
                slots[index] = leaderboard[index].PlayerSlot;
            }

            return slots;
        }
    }
}
