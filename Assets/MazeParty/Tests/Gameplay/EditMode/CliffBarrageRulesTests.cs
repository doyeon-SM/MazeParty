using System;
using MazeParty.Gameplay.Minigames.CliffBarrage;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class CliffBarrageRulesTests
    {
        [Test]
        public void ExactSixtySecondBoundaryAndThreeRoundStandings()
        {
            var match = new CliffBarrageMatchState(208, 0, 0);
            for (var round = 1; round <=
                CliffBarrageRules.RoundCount; round++)
            {
                Assert.That(match.RoundNumber, Is.EqualTo(round));
                Assert.That(match.SurvivorCount, Is.EqualTo(4));
                match.AdvanceTo(60d -
                    CliffBarrageRules.SimulationStepSeconds);
                Assert.That(match.IsRoundComplete, Is.False);
                match.AdvanceTo(60d);
                Assert.That(match.IsRoundComplete, Is.True);
                Assert.That(match.RoundElapsedSeconds,
                    Is.EqualTo(60d));
                Assert.That(match.GetRoundRank(round, 0),
                    Is.EqualTo(1));
                if (round < CliffBarrageRules.RoundCount)
                {
                    Assert.Throws<InvalidOperationException>(() =>
                        match.GetFinalRank(0));
                    match.BeginNextRound();
                }
            }
            Assert.That(match.IsComplete, Is.True);
            Assert.That(match.GetScore(0), Is.EqualTo(9));
            Assert.That(match.GetFinalRank(0), Is.EqualTo(1));
            Assert.That(match.RoundTransitionSequence,
                Is.EqualTo(3UL));
            Assert.Throws<InvalidOperationException>(() =>
                match.BeginNextRound());
        }

        [Test]
        public void FirstHazardRemovesTorsoSecondAfterOneSecondEliminates()
        {
            var match = new CliffBarrageMatchState(19, 0, 0);
            Assert.That(match.TryApplyHazardHit(0), Is.True);
            Assert.That(match.GetPlayer(0).HitCount, Is.EqualTo(1));
            Assert.That(match.GetPlayer(0).RemainingLives,
                Is.EqualTo(1));
            Assert.That(match.GetPlayer(0).IsEliminated, Is.False);
            Assert.That(match.TryApplyHazardHit(0), Is.False);
            match.AdvanceTo(1d -
                CliffBarrageRules.SimulationStepSeconds);
            Assert.That(match.TryApplyHazardHit(0), Is.False);
            match.AdvanceTo(1d);
            Assert.That(match.TryApplyHazardHit(0), Is.True);
            Assert.That(match.GetPlayer(0).HitCount, Is.EqualTo(2));
            Assert.That(match.GetPlayer(0).IsEliminated, Is.True);
            Assert.That(match.TryApplyHazardHit(0), Is.False);
        }

        [Test]
        public void OutOfBoundsFallsImmediatelyRegardlessOfLives()
        {
            var match = new CliffBarrageMatchState(81, 0, 0);
            var player = match.GetPlayer(0);
            var length = Math.Sqrt(player.X * player.X +
                player.Z * player.Z);
            match.SetMovementInput(0, player.X / length,
                player.Z / length);
            match.AdvanceTo(0.65d);
            Assert.That(match.GetPlayer(0).IsEliminated, Is.False);
            match.AdvanceTo(0.7d);
            Assert.That(match.GetPlayer(0).IsEliminated, Is.True);
            Assert.That(match.GetPlayer(0).HitCount, Is.Zero);
            Assert.That(match.SurvivorCount, Is.EqualTo(3));
        }

        [Test]
        public void TimeoutRanksLivesThenCenterAndLaterElimination()
        {
            var match = new CliffBarrageMatchState(81, 0, 0);
            Assert.That(match.TryApplyHazardHit(0), Is.True);
            Assert.That(match.TryApplyHazardHit(1), Is.True);
            var falling = match.GetPlayer(2);
            var length = Math.Sqrt(falling.X * falling.X +
                falling.Z * falling.Z);
            match.SetMovementInput(2,
                falling.X / length, falling.Z / length);
            match.AdvanceTo(0.7d);
            Assert.That(match.GetPlayer(2).IsEliminated, Is.True);
            match.AdvanceTo(1d);
            Assert.That(match.TryApplyHazardHit(1), Is.True);
            Assert.That(match.GetPlayer(1).IsEliminated, Is.True);
            match.AdvanceTo(60d);
            Assert.That(match.GetRoundRank(1, 3), Is.EqualTo(1));
            Assert.That(match.GetRoundRank(1, 0), Is.EqualTo(2));
            Assert.That(match.GetRoundRank(1, 1), Is.EqualTo(3));
            Assert.That(match.GetRoundRank(1, 2), Is.EqualTo(4));

            match.BeginNextRound();
            for (var slot = 0; slot < 4; slot++)
            {
                Assert.That(match.GetPlayer(slot).HitCount, Is.Zero);
                Assert.That(match.GetPlayer(slot).IsEliminated,
                    Is.False);
            }
        }

        [Test]
        public void OneSurvivorEndsImmediatelyBeforeFurtherSameTickHazards()
        {
            var match = new CliffBarrageMatchState(43, 0, 0);
            for (var slot = 0; slot < 3; slot++)
            {
                Assert.That(match.TryApplyHazardHit(slot), Is.True);
            }
            match.AdvanceTo(1d);
            for (var slot = 0; slot < 3; slot++)
            {
                Assert.That(match.TryApplyHazardHit(slot), Is.True);
            }

            Assert.That(match.SurvivorCount, Is.EqualTo(1));
            Assert.That(match.IsRoundComplete, Is.True);
            Assert.That(match.RoundElapsedSeconds, Is.EqualTo(1d));
            Assert.That(match.TryApplyHazardHit(3), Is.False);
            Assert.That(match.GetPlayer(3).HitCount, Is.Zero);
            Assert.That(match.GetRoundRank(1, 3), Is.EqualTo(1));
        }

        [Test]
        public void PushUsesLastMovementHeadingAndCooldown()
        {
            var match = new CliffBarrageMatchState(13, 0, 0);
            var attacker = match.GetPlayer(0);
            var target = match.GetPlayer(1);
            var dx = target.X - attacker.X;
            var dz = target.Z - attacker.Z;
            var length = Math.Sqrt(dx * dx + dz * dz);
            match.SetMovementInput(0, dx / length, dz / length);
            match.AdvanceTo(0.9d);
            match.SetMovementInput(0, 0d, 0d);
            var before = match.GetPlayer(1);
            Assert.That(match.TryPush(0), Is.True);
            var after = match.GetPlayer(1);
            Assert.That(Distance(before, after),
                Is.EqualTo(CliffBarrageRules.PushDistance)
                    .Within(0.000001d));
            Assert.That(match.TryPush(0), Is.False);
        }

        [Test]
        public void LaserWarnsOneSecondAndFiresHalfSecondOnly()
        {
            var match = new CliffBarrageMatchState(46, 0, 1);
            var warningStep = -1;
            for (var step = 1; step <= 600; step++)
            {
                match.AdvanceTo(step *
                    CliffBarrageRules.SimulationStepSeconds);
                if (match.GetLaser(0).Phase ==
                    CliffBarrageLaserPhase.Warning)
                {
                    warningStep = step;
                    break;
                }
            }
            Assert.That(warningStep, Is.GreaterThan(0));
            for (var slot = 0; slot < 4; slot++)
            {
                Assert.That(match.GetPlayer(slot).HitCount,
                    Is.Zero, "Warning must not damage players.");
            }
            match.AdvanceTo((warningStep + 59) *
                CliffBarrageRules.SimulationStepSeconds);
            Assert.That(match.GetLaser(0).Phase,
                Is.EqualTo(CliffBarrageLaserPhase.Warning));
            match.AdvanceTo((warningStep + 60) *
                CliffBarrageRules.SimulationStepSeconds);
            Assert.That(match.GetLaser(0).Phase,
                Is.EqualTo(CliffBarrageLaserPhase.Firing));
            match.AdvanceTo((warningStep + 89) *
                CliffBarrageRules.SimulationStepSeconds);
            Assert.That(match.GetLaser(0).Phase,
                Is.EqualTo(CliffBarrageLaserPhase.Firing));
            match.AdvanceTo((warningStep + 90) *
                CliffBarrageRules.SimulationStepSeconds);
            Assert.That(match.GetLaser(0).Phase,
                Is.EqualTo(CliffBarrageLaserPhase.Inactive));
        }

        [Test]
        public void SweptProjectileContactCatchesBetweenFrameCrossing()
        {
            Assert.That(CliffBarrageRules.SegmentIntersectsCircle(
                0d, 0d, -1d, 0d, 1d, 0d, 0.45d), Is.True);
            Assert.That(CliffBarrageRules.SegmentIntersectsCircle(
                0d, 0d, -1d, 1d, 1d, 1d, 0.45d), Is.False);
        }

        [Test]
        public void SeededHazardsAreChunkIndependentAndNeverExceedPools()
        {
            var batched = new CliffBarrageMatchState(7305);
            var incremental = new CliffBarrageMatchState(7305);
            batched.AdvanceTo(12d);
            for (var step = 1; step <= 720; step++)
            {
                incremental.AdvanceTo(step *
                    CliffBarrageRules.SimulationStepSeconds);
                var activeProjectiles = 0;
                for (var index = 0; index <
                    CliffBarrageRules.MaximumProjectiles; index++)
                {
                    if (incremental.GetProjectile(index).Active)
                    {
                        activeProjectiles++;
                    }
                }
                var activeLasers = 0;
                for (var index = 0; index <
                    CliffBarrageRules.MaximumLasers; index++)
                {
                    if (incremental.GetLaser(index).Phase !=
                        CliffBarrageLaserPhase.Inactive)
                    {
                        activeLasers++;
                    }
                }
                Assert.That(activeProjectiles, Is.LessThanOrEqualTo(5));
                Assert.That(activeLasers, Is.LessThanOrEqualTo(2));
            }
            for (var slot = 0; slot < 4; slot++)
            {
                var first = batched.GetPlayer(slot);
                var second = incremental.GetPlayer(slot);
                Assert.That(second.X, Is.EqualTo(first.X));
                Assert.That(second.Z, Is.EqualTo(first.Z));
                Assert.That(second.HitCount,
                    Is.EqualTo(first.HitCount));
                Assert.That(second.IsEliminated,
                    Is.EqualTo(first.IsEliminated));
            }
            for (var index = 0; index < 5; index++)
            {
                var first = batched.GetProjectile(index);
                var second = incremental.GetProjectile(index);
                Assert.That(second.Active, Is.EqualTo(first.Active));
                Assert.That(second.X, Is.EqualTo(first.X));
                Assert.That(second.Z, Is.EqualTo(first.Z));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                incremental.AdvanceTo(-1d));
        }

        private static double Distance(
            CliffBarragePlayerSnapshot left,
            CliffBarragePlayerSnapshot right)
        {
            var dx = left.X - right.X;
            var dz = left.Z - right.Z;
            return Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
