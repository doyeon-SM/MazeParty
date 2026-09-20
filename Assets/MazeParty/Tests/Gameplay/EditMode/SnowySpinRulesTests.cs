using System;
using MazeParty.Gameplay.Minigames.SnowySpin;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class SnowySpinRulesTests
    {
        [Test]
        public void HeldDirectionAcceleratesAndReleaseCoastsWithDrag()
        {
            var match = new SnowySpinMatchState(82);
            var initial = match.GetPlayer(0);
            var directionX = -initial.X / SnowySpinRules.SpawnRadius;
            var directionZ = -initial.Z / SnowySpinRules.SpawnRadius;
            match.SetMovementInput(0, directionX, directionZ);

            match.AdvanceTo(0.2d);
            var early = match.GetPlayer(0);
            match.AdvanceTo(0.4d);
            var late = match.GetPlayer(0);
            var earlySpeed = Speed(early);
            var lateSpeed = Speed(late);
            Assert.That(lateSpeed, Is.GreaterThan(earlySpeed));
            Assert.That(lateSpeed,
                Is.LessThanOrEqualTo(SnowySpinRules.MaximumSpeed));

            match.SetMovementInput(0, 0d, 0d);
            match.AdvanceTo(0.6d);
            var coast = match.GetPlayer(0);
            Assert.That(Speed(coast), Is.GreaterThan(0d));
            Assert.That(Speed(coast), Is.LessThan(lateSpeed));
            Assert.That(Distance(initial, coast),
                Is.GreaterThan(Distance(initial, late)));
        }

        [Test]
        public void ContactTransfersMomentumToAnUncontrolledBall()
        {
            var match = new SnowySpinMatchState(93);
            var first = match.GetPlayer(0);
            var second = match.GetPlayer(1);
            var dx = second.X - first.X;
            var dz = second.Z - first.Z;
            var length = Math.Sqrt(dx * dx + dz * dz);
            match.SetMovementInput(0, dx / length, dz / length);
            match.AdvanceTo(1.6d);

            var pushed = match.GetPlayer(1);
            Assert.That(Distance(second, pushed), Is.GreaterThan(0.05d));
            Assert.That(pushed.IsEliminated, Is.False);
        }

        [Test]
        public void LaterFallsRankHigherAndAnEarlyLastSurvivorEndsRound()
        {
            var match = new SnowySpinMatchState(24);
            DriveOutward(match, 0);
            match.AdvanceTo(3d);
            Assert.That(match.GetPlayer(0).IsEliminated, Is.True);
            DriveOutward(match, 1);
            match.AdvanceTo(6d);
            Assert.That(match.GetPlayer(1).IsEliminated, Is.True);
            DriveOutward(match, 2);
            match.AdvanceTo(9d);

            Assert.That(match.IsRoundComplete, Is.True);
            Assert.That(match.IsComplete, Is.False);
            Assert.That(match.RoundElapsedSeconds, Is.LessThan(9d));
            Assert.That(match.SurvivorCount, Is.EqualTo(1));
            Assert.That(match.GetRoundRank(1, 3), Is.EqualTo(1));
            Assert.That(match.GetRoundRank(1, 2), Is.EqualTo(2));
            Assert.That(match.GetRoundRank(1, 1), Is.EqualTo(3));
            Assert.That(match.GetRoundRank(1, 0), Is.EqualTo(4));
            Assert.That(match.GetScore(3), Is.EqualTo(3));
            Assert.That(match.GetScore(0), Is.Zero);
            Assert.That(match.RoundTransitionSequence, Is.EqualTo(1UL));
            Assert.Throws<InvalidOperationException>(
                () => match.GetFinalRank(3));

            var finishedAt = match.RoundElapsedSeconds;
            match.AdvanceTo(60d);
            Assert.That(match.RoundElapsedSeconds, Is.EqualTo(finishedAt));
        }

        [Test]
        public void AtExactTimeLimitSurvivorCloserToCenterRanksFirst()
        {
            var match = new SnowySpinMatchState(110);
            var player = match.GetPlayer(0);
            match.SetMovementInput(0,
                -player.X / SnowySpinRules.SpawnRadius,
                -player.Z / SnowySpinRules.SpawnRadius);
            match.AdvanceTo(0.4d);
            match.SetMovementInput(0, 0d, 0d);
            match.AdvanceTo(60d - SnowySpinRules.SimulationStepSeconds);
            Assert.That(match.IsRoundComplete, Is.False);
            match.AdvanceTo(60d);

            Assert.That(match.IsRoundComplete, Is.True);
            Assert.That(match.RoundElapsedSeconds,
                Is.EqualTo(SnowySpinRules.RoundDurationSeconds));
            Assert.That(match.GetRoundRank(1, 0), Is.EqualTo(1));
            Assert.That(match.GetPlayer(0).IsEliminated, Is.False);
            Assert.That(match.LastCompletedRound.Value.GetRank(0),
                Is.EqualTo(1));
        }

        [Test]
        public void ThreeExternallyGatedRoundsResetPositionsAndFinalizeOnce()
        {
            var match = new SnowySpinMatchState(500);
            for (var round = 1; round <= SnowySpinRules.RoundCount;
                round++)
            {
                Assert.That(match.RoundNumber, Is.EqualTo(round));
                Assert.That(match.RoundElapsedSeconds, Is.Zero);
                Assert.That(match.SurvivorCount,
                    Is.EqualTo(SnowySpinRules.PlayerCount));
                for (var slot = 0; slot < 3; slot++)
                {
                    DriveOutward(match, slot);
                }

                match.AdvanceTo(3d);
                Assert.That(match.IsRoundComplete, Is.True);
                Assert.That(match.GetRoundRank(round, 3), Is.EqualTo(1));
                Assert.That(match.LastCompletedRound.Value.RoundNumber,
                    Is.EqualTo(round));
                if (round < SnowySpinRules.RoundCount)
                {
                    Assert.Throws<InvalidOperationException>(() =>
                        match.GetFinalRank(3));
                    match.BeginNextRound();
                    Assert.That(match.GetPlayer(3).RoundRank, Is.Zero);
                    Assert.That(Speed(match.GetPlayer(3)), Is.Zero);
                }
            }

            Assert.That(match.IsComplete, Is.True);
            Assert.That(match.RoundTransitionSequence,
                Is.EqualTo((ulong)SnowySpinRules.RoundCount));
            Assert.That(match.GetScore(3), Is.EqualTo(9));
            Assert.That(match.GetFinalRank(3), Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(
                () => match.BeginNextRound());
        }

        [Test]
        public void SeededFixedStepSimulationIsIndependentOfAdvanceChunking()
        {
            var batched = new SnowySpinMatchState(7305);
            var incremental = new SnowySpinMatchState(7305);
            for (var slot = 0; slot < SnowySpinRules.PlayerCount;
                slot++)
            {
                Assert.That(incremental.GetPlayer(slot).X,
                    Is.EqualTo(batched.GetPlayer(slot).X));
                Assert.That(incremental.GetPlayer(slot).Z,
                    Is.EqualTo(batched.GetPlayer(slot).Z));
            }

            batched.SetMovementInput(0, 1d, 1d);
            incremental.SetMovementInput(0, 1d, 1d);
            batched.AdvanceTo(1.5d);
            for (var step = 1; step <= 15; step++)
            {
                incremental.AdvanceTo(step * 0.1d);
            }

            for (var slot = 0; slot < SnowySpinRules.PlayerCount;
                slot++)
            {
                Assert.That(incremental.GetPlayer(slot).X,
                    Is.EqualTo(batched.GetPlayer(slot).X));
                Assert.That(incremental.GetPlayer(slot).Z,
                    Is.EqualTo(batched.GetPlayer(slot).Z));
            }

            Assert.Throws<ArgumentOutOfRangeException>(
                () => incremental.AdvanceTo(1d));
        }

        private static void DriveOutward(SnowySpinMatchState match,
            int slot)
        {
            var ball = match.GetPlayer(slot);
            var length = Math.Sqrt(ball.X * ball.X + ball.Z * ball.Z);
            match.SetMovementInput(slot, ball.X / length, ball.Z / length);
        }

        private static double Speed(SnowySpinBallSnapshot ball)
        {
            return Math.Sqrt(ball.VelocityX * ball.VelocityX +
                ball.VelocityZ * ball.VelocityZ);
        }

        private static double Distance(SnowySpinBallSnapshot left,
            SnowySpinBallSnapshot right)
        {
            var x = left.X - right.X;
            var z = left.Z - right.Z;
            return Math.Sqrt(x * x + z * z);
        }
    }
}
