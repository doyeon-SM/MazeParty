using System;
using MazeParty.Gameplay.Minigames.BouncingBalls;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BouncingBallsRulesTests
    {
        [Test]
        public void SeedStartsThreeNeutralCenterBallsWithDeterministicDirections()
        {
            var first = new BouncingBallsMatchState(1701);
            var repeated = new BouncingBallsMatchState(1701);
            var different = new BouncingBallsMatchState(1702);

            Assert.That(first.RoundNumber, Is.EqualTo(1));
            Assert.That(first.RoundElapsedSeconds, Is.Zero);
            for (var id = 0; id < BouncingBallsRules.BallCount; id++)
            {
                var ball = first.GetBall(id);
                var repeatedBall = repeated.GetBall(id);
                Assert.That(ball.X, Is.Zero);
                Assert.That(ball.Y, Is.Zero);
                Assert.That(ball.OwnerSlot,
                    Is.EqualTo(BouncingBallsRules.NoOwnerSlot));
                Assert.That(ball.VelocityX,
                    Is.EqualTo(repeatedBall.VelocityX));
                Assert.That(ball.VelocityY,
                    Is.EqualTo(repeatedBall.VelocityY));
                Assert.That(
                    Math.Sqrt(
                        (ball.VelocityX * ball.VelocityX) +
                        (ball.VelocityY * ball.VelocityY)),
                    Is.EqualTo(BouncingBallsRules.BallSpeed)
                        .Within(0.0000001d));
            }

            Assert.That(
                first.GetBall(0).VelocityX,
                Is.Not.EqualTo(different.GetBall(0).VelocityX));
        }

        [Test]
        public void FixedStepSimulationIsIndependentOfUpdateChunking()
        {
            var batched = new BouncingBallsMatchState(2481);
            var incremental = new BouncingBallsMatchState(2481);
            batched.SetShieldInput(0, 1);
            incremental.SetShieldInput(0, 1);
            batched.SetShieldInput(2, -1);
            incremental.SetShieldInput(2, -1);

            batched.AdvanceTo(5d);
            for (var quarter = 1; quarter <= 20; quarter++)
            {
                incremental.AdvanceTo(quarter * 0.25d);
            }

            AssertSameSimulation(batched, incremental);
            Assert.That(
                batched.GetShield(0).Center,
                Is.EqualTo(BouncingBallsRules.ShieldMaximumOffset));
            Assert.That(
                batched.GetShield(2).Center,
                Is.EqualTo(-BouncingBallsRules.ShieldMaximumOffset));
        }

        [Test]
        public void GoalRespawnsOnlyScoredBallAtConcedingCurrentShieldCenter()
        {
            var match = new BouncingBallsMatchState(3127);
            for (var slot = 0; slot < BouncingBallsRules.PlayerCount; slot++)
            {
                match.SetShieldInput(slot, slot % 2 == 0 ? 1 : -1);
            }

            for (var step = 1;
                 step < BouncingBallsRules.SimulationHz *
                     BouncingBallsRules.RoundSeconds;
                 step++)
            {
                var previousGoals = match.GoalSequence;
                var previousScoreTotal = SumScores(match);
                var previousConcededTotal = SumConceded(match);
                match.AdvanceTo(
                    step * BouncingBallsRules.SimulationStepSeconds);
                if (match.GoalSequence == previousGoals)
                {
                    continue;
                }

                // Multiple balls can enter goals in the same fixed step.
                // Inspect a single-goal step to validate exact accounting.
                if (match.GoalSequence != previousGoals + 1UL)
                {
                    continue;
                }

                var goal = match.LastGoal.Value;
                var respawned = match.GetBall(goal.BallId);
                var shield = match.GetShield(goal.DefenderSlot);
                Assert.That(goal.Sequence, Is.EqualTo(match.GoalSequence));
                Assert.That(goal.RoundNumber, Is.EqualTo(1));
                Assert.That(respawned.OwnerSlot,
                    Is.EqualTo(goal.DefenderSlot));
                Assert.That(match.GetConceded(goal.DefenderSlot),
                    Is.GreaterThanOrEqualTo(1));
                Assert.That(SumConceded(match),
                    Is.EqualTo(previousConcededTotal + 1));
                Assert.That(SumScores(match),
                    Is.EqualTo(previousScoreTotal +
                        (goal.AwardedPoint ? 1 : 0)));
                if (goal.AwardedPoint)
                {
                    Assert.That(match.GetScore(goal.ScorerSlot),
                        Is.GreaterThanOrEqualTo(1));
                }

                switch (goal.DefenderSlot)
                {
                    case 0:
                        Assert.That(respawned.X, Is.EqualTo(shield.Center));
                        Assert.That(respawned.Y,
                            Is.EqualTo(-BouncingBallsRules.ShieldRailDistance));
                        Assert.That(respawned.VelocityY,
                            Is.GreaterThan(0d));
                        break;
                    case 1:
                        Assert.That(respawned.X,
                            Is.EqualTo(BouncingBallsRules.ShieldRailDistance));
                        Assert.That(respawned.Y, Is.EqualTo(shield.Center));
                        Assert.That(respawned.VelocityX,
                            Is.LessThan(0d));
                        break;
                    case 2:
                        Assert.That(respawned.X, Is.EqualTo(shield.Center));
                        Assert.That(respawned.Y,
                            Is.EqualTo(BouncingBallsRules.ShieldRailDistance));
                        Assert.That(respawned.VelocityY,
                            Is.LessThan(0d));
                        break;
                    default:
                        Assert.That(respawned.X,
                            Is.EqualTo(-BouncingBallsRules.ShieldRailDistance));
                        Assert.That(respawned.Y, Is.EqualTo(shield.Center));
                        Assert.That(respawned.VelocityX,
                            Is.GreaterThan(0d));
                        break;
                }

                return;
            }

            Assert.Fail("Expected at least one goal during the first round.");
        }

        [Test]
        public void MatchUsesExactTwoRoundDeadlinesAndCumulativeScores()
        {
            var match = new BouncingBallsMatchState(8128);
            match.AdvanceTo(59.999d);
            Assert.That(match.IsRoundComplete, Is.False);
            Assert.That(match.IsComplete, Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => match.AdvanceTo(59d));

            match.AdvanceTo(60d);
            Assert.That(match.IsRoundComplete, Is.True);
            Assert.That(match.IsComplete, Is.False);
            Assert.That(match.RoundElapsedSeconds,
                Is.EqualTo(BouncingBallsRules.RoundSeconds));

            var scoresAfterOne = new int[BouncingBallsRules.PlayerCount];
            for (var slot = 0; slot < scoresAfterOne.Length; slot++)
            {
                scoresAfterOne[slot] = match.GetScore(slot);
            }

            match.BeginNextRound();
            Assert.That(match.RoundNumber, Is.EqualTo(2));
            Assert.That(match.RoundElapsedSeconds, Is.Zero);
            for (var id = 0; id < BouncingBallsRules.BallCount; id++)
            {
                var ball = match.GetBall(id);
                Assert.That(ball.X, Is.Zero);
                Assert.That(ball.Y, Is.Zero);
                Assert.That(ball.OwnerSlot,
                    Is.EqualTo(BouncingBallsRules.NoOwnerSlot));
            }

            match.AdvanceTo(60d);
            Assert.That(match.IsComplete, Is.True);
            for (var slot = 0; slot < scoresAfterOne.Length; slot++)
            {
                Assert.That(match.GetScore(slot),
                    Is.GreaterThanOrEqualTo(scoresAfterOne[slot]));
            }

            var seenRanks = new bool[BouncingBallsRules.PlayerCount + 1];
            for (var slot = 0; slot < BouncingBallsRules.PlayerCount;
                 slot++)
            {
                var rank = match.GetFinalRank(slot);
                Assert.That(rank, Is.InRange(1, 4));
                Assert.That(seenRanks[rank], Is.False);
                seenRanks[rank] = true;
            }
        }

        [Test]
        public void RankingUsesGoalsThenFewerConcededThenServerSlot()
        {
            var ranks = BouncingBallsRanking.BuildRanksBySlot(
                new[] { 5, 5, 2, 2 },
                new[] { 4, 1, 0, 0 });

            Assert.That(ranks, Is.EqualTo(new[] { 2, 1, 3, 4 }));
            Assert.Throws<ArgumentException>(() =>
                BouncingBallsRanking.BuildRanksBySlot(
                    new[] { 1, 2 },
                    new[] { 1, 2 }));
        }

        private static void AssertSameSimulation(
            BouncingBallsMatchState expected,
            BouncingBallsMatchState actual)
        {
            Assert.That(actual.RoundElapsedSeconds,
                Is.EqualTo(expected.RoundElapsedSeconds));
            Assert.That(actual.GoalSequence,
                Is.EqualTo(expected.GoalSequence));
            for (var slot = 0; slot < BouncingBallsRules.PlayerCount;
                 slot++)
            {
                Assert.That(actual.GetShield(slot).Center,
                    Is.EqualTo(expected.GetShield(slot).Center));
                Assert.That(actual.GetScore(slot),
                    Is.EqualTo(expected.GetScore(slot)));
                Assert.That(actual.GetConceded(slot),
                    Is.EqualTo(expected.GetConceded(slot)));
            }

            for (var id = 0; id < BouncingBallsRules.BallCount; id++)
            {
                var left = expected.GetBall(id);
                var right = actual.GetBall(id);
                Assert.That(right.X, Is.EqualTo(left.X));
                Assert.That(right.Y, Is.EqualTo(left.Y));
                Assert.That(right.VelocityX, Is.EqualTo(left.VelocityX));
                Assert.That(right.VelocityY, Is.EqualTo(left.VelocityY));
                Assert.That(right.OwnerSlot, Is.EqualTo(left.OwnerSlot));
            }
        }

        private static int SumScores(BouncingBallsMatchState match)
        {
            var total = 0;
            for (var slot = 0;
                 slot < BouncingBallsRules.PlayerCount;
                 slot++)
            {
                total += match.GetScore(slot);
            }

            return total;
        }

        private static int SumConceded(
            BouncingBallsMatchState match)
        {
            var total = 0;
            for (var slot = 0;
                 slot < BouncingBallsRules.PlayerCount;
                 slot++)
            {
                total += match.GetConceded(slot);
            }

            return total;
        }
    }
}
