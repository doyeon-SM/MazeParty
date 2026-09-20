using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.WrongWay;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class WrongWayRulesTests
    {
        [Test]
        public void PromptSequence_IsCanonicalAndChangesBetweenRounds()
        {
            const ulong seed = 0x123456789ABCDEF0UL;
            var first = WrongWayPromptGenerator.Generate(seed, 1);
            var repeated = WrongWayPromptGenerator.Generate(seed, 1);
            var secondRound = WrongWayPromptGenerator.Generate(seed, 2);

            Assert.That(first, Has.Length.EqualTo(50));
            Assert.That(repeated, Is.EqualTo(first));
            Assert.That(secondRound, Is.Not.EqualTo(first));
            Assert.That(
                new ArraySegment<WrongWayDirection>(first, 0, 12),
                Is.EqualTo(
                    new[]
                    {
                        WrongWayDirection.Right,
                        WrongWayDirection.Up,
                        WrongWayDirection.Down,
                        WrongWayDirection.Left,
                        WrongWayDirection.Up,
                        WrongWayDirection.Down,
                        WrongWayDirection.Down,
                        WrongWayDirection.Down,
                        WrongWayDirection.Down,
                        WrongWayDirection.Up,
                        WrongWayDirection.Right,
                        WrongWayDirection.Up
                    }));
        }

        [Test]
        public void PlayerProgress_IsIndependentAndWrongInputLocksUntilBoundary()
        {
            var round = new WrongWayRoundState(77UL, 1);
            var sharedFirstPrompt = round.GetPromptForSlot(0);

            for (var playerSlot = 1;
                 playerSlot < WrongWayRules.PlayerCount;
                 playerSlot++)
            {
                Assert.That(
                    round.GetPromptForSlot(playerSlot),
                    Is.EqualTo(sharedFirstPrompt));
            }

            var resolution = round.SubmitInput(
                0,
                sharedFirstPrompt.Value,
                5d);

            Assert.That(
                resolution.Status,
                Is.EqualTo(WrongWayInputStatus.Correct));
            Assert.That(round.GetPlayer(0).CompletedSteps, Is.EqualTo(1));
            Assert.That(round.GetPlayer(1).CompletedSteps, Is.Zero);
            Assert.That(
                round.GetPromptForSlot(1),
                Is.EqualTo(sharedFirstPrompt));
            Assert.That(
                round.GetPromptForSlot(0),
                Is.EqualTo(round.Prompts[1]));

            var prompt = round.GetPromptForSlot(2).Value;
            var incorrect = DifferentDirection(prompt);

            var wrong = round.SubmitInput(2, incorrect, 10d);
            var ignored = round.SubmitInput(2, prompt, 10.499d);
            var accepted = round.SubmitInput(2, prompt, 10.5d);

            Assert.That(
                wrong.Status,
                Is.EqualTo(WrongWayInputStatus.Incorrect));
            Assert.That(wrong.CurrentStep, Is.Zero);
            Assert.That(wrong.InputLockedUntil, Is.EqualTo(10.5d));
            Assert.That(
                ignored.Status,
                Is.EqualTo(WrongWayInputStatus.IgnoredWhileLocked));
            Assert.That(ignored.ServerEventOrder, Is.Zero);
            Assert.That(
                accepted.Status,
                Is.EqualTo(WrongWayInputStatus.Correct));
            Assert.That(round.GetPlayer(2).CompletedSteps, Is.EqualTo(1));
            Assert.That(
                round.Prompts[0],
                Is.EqualTo(prompt),
                "The incorrect input must not consume the prompt.");
        }

        [Test]
        public void FirstPlayerAtFifty_EndsRoundImmediatelyAndRanksTiesByArrivalOrder()
        {
            var round = new WrongWayRoundState(99UL, 1);
            Advance(round, 1, 20);
            Advance(round, 2, 20);
            Advance(round, 3, 5);

            var finish = Advance(round, 0, 50);

            Assert.That(
                finish.Status,
                Is.EqualTo(WrongWayInputStatus.Finished));
            Assert.That(round.IsComplete, Is.True);
            Assert.That(
                round.EndReason,
                Is.EqualTo(WrongWayRoundEndReason.FirstFinisher));
            Assert.That(
                PlayerSlots(round.Result),
                Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(
                Points(round.Result),
                Is.EqualTo(new[] { 3, 2, 1, 0 }));
            Assert.That(round.GetPromptForSlot(0), Is.Null);

            var ignored = round.SubmitInput(
                3,
                round.GetPromptForSlot(3).Value,
                1d);
            Assert.That(
                ignored.Status,
                Is.EqualTo(WrongWayInputStatus.IgnoredRoundComplete));
            Assert.That(round.GetPlayer(3).CompletedSteps, Is.EqualTo(5));
        }

        [Test]
        public void SixtySecondTimeout_EndsAndRanksCurrentProgress()
        {
            var round = new WrongWayRoundState(123UL, 2);
            Advance(round, 2, 7);
            Advance(round, 0, 7);
            Advance(round, 1, 8);

            Assert.That(round.TryEndForTimeout(59.999d), Is.False);
            Assert.That(round.TryEndForTimeout(60d), Is.True);

            Assert.That(round.IsComplete, Is.True);
            Assert.That(
                round.EndReason,
                Is.EqualTo(WrongWayRoundEndReason.TimeLimit));
            Assert.That(
                PlayerSlots(round.Result),
                Is.EqualTo(new[] { 1, 2, 0, 3 }));
            Assert.That(round.TryEndForTimeout(61d), Is.False);
        }

        [Test]
        public void MatchLeaderboard_BreaksPointTiesByTotalStepsThenSecondRoundRank()
        {
            var roundOne = ScoreProgress(
                (0, 50),
                (1, 45),
                (2, 30),
                (3, 10));
            var roundTwo = ScoreProgress(
                (3, 50),
                (2, 40),
                (1, 25),
                (0, 20));

            var leaderboard = WrongWayMatchScoring.BuildLeaderboard(
                new[] { roundOne, roundTwo });

            Assert.That(
                LeaderboardSlots(leaderboard),
                Is.EqualTo(new[] { 2, 1, 0, 3 }));
            Assert.That(
                LeaderboardPoints(leaderboard),
                Is.EqualTo(new[] { 3, 3, 3, 3 }));
            Assert.That(
                LeaderboardSteps(leaderboard),
                Is.EqualTo(new[] { 70, 70, 70, 60 }));
            Assert.That(
                leaderboard[0].SecondRoundRank,
                Is.EqualTo(2));
            Assert.That(
                leaderboard[1].SecondRoundRank,
                Is.EqualTo(3));
            Assert.That(
                leaderboard[2].SecondRoundRank,
                Is.EqualTo(4));
            Assert.Throws<System.ArgumentException>(
                () => WrongWayMatchScoring.BuildLeaderboard(
                    new[] { roundOne }));
            Assert.Throws<System.ArgumentException>(
                () => ScoreProgress(
                    (0, 50),
                    (0, 45),
                    (2, 30),
                    (3, 10)));
        }

        private static WrongWayInputResolution Advance(
            WrongWayRoundState round,
            int playerSlot,
            int stepCount)
        {
            var resolution = default(WrongWayInputResolution);
            for (var index = 0; index < stepCount; index++)
            {
                resolution = round.SubmitInput(
                    playerSlot,
                    round.GetPromptForSlot(playerSlot).Value,
                    0d);
            }

            return resolution;
        }

        private static WrongWayRoundResult ScoreProgress(
            params (int PlayerSlot, int CompletedSteps)[] entries)
        {
            var outcomes =
                new WrongWayRoundOutcome[entries.Length];
            for (var index = 0; index < entries.Length; index++)
            {
                outcomes[index] = new WrongWayRoundOutcome(
                    entries[index].PlayerSlot,
                    entries[index].CompletedSteps,
                    (ulong)index + 1UL);
            }

            return WrongWayRoundScoring.Score(outcomes);
        }

        private static WrongWayDirection DifferentDirection(
            WrongWayDirection direction)
        {
            return direction == WrongWayDirection.Up
                ? WrongWayDirection.Down
                : WrongWayDirection.Up;
        }

        private static int[] PlayerSlots(
            WrongWayRoundResult result)
        {
            var slots = new int[result.Standings.Count];
            for (var index = 0; index < slots.Length; index++)
            {
                slots[index] =
                    result.Standings[index].PlayerSlot;
            }

            return slots;
        }

        private static int[] Points(
            WrongWayRoundResult result)
        {
            var points = new int[result.Standings.Count];
            for (var index = 0; index < points.Length; index++)
            {
                points[index] =
                    result.Standings[index].Points;
            }

            return points;
        }

        private static int[] LeaderboardSlots(
            IReadOnlyList<WrongWayLeaderboardEntry> leaderboard)
        {
            var slots = new int[leaderboard.Count];
            for (var index = 0; index < slots.Length; index++)
            {
                slots[index] = leaderboard[index].PlayerSlot;
            }

            return slots;
        }

        private static int[] LeaderboardPoints(
            IReadOnlyList<WrongWayLeaderboardEntry> leaderboard)
        {
            var points = new int[leaderboard.Count];
            for (var index = 0; index < points.Length; index++)
            {
                points[index] = leaderboard[index].TotalPoints;
            }

            return points;
        }

        private static int[] LeaderboardSteps(
            IReadOnlyList<WrongWayLeaderboardEntry> leaderboard)
        {
            var steps = new int[leaderboard.Count];
            for (var index = 0; index < steps.Length; index++)
            {
                steps[index] =
                    leaderboard[index].TotalCompletedSteps;
            }

            return steps;
        }
    }
}
