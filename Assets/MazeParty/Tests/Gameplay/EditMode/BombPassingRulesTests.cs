using System;
using MazeParty.Gameplay.Minigames.BombPassing;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BombPassingRulesTests
    {
        [Test]
        public void BombFuseIsSeededAtSpawnAndHalfwayUnheldBombChases()
        {
            var first = new BombPassingMatchState(22091);
            var repeated = new BombPassingMatchState(22091);
            var different = new BombPassingMatchState(22092);
            var initial = first.GetBomb();

            Assert.That(initial.BombNumber, Is.EqualTo(1));
            Assert.That(initial.X, Is.Zero);
            Assert.That(initial.Z, Is.Zero);
            Assert.That(initial.HolderSlot,
                Is.EqualTo(BombPassingRules.NoHolderSlot));
            Assert.That(initial.FuseSeconds,
                Is.InRange(
                    BombPassingRules.MinimumFuseSeconds,
                    BombPassingRules.MaximumFuseSeconds));
            Assert.That(initial.FuseSeconds,
                Is.EqualTo(repeated.GetBomb().FuseSeconds));
            Assert.That(initial.FuseSeconds,
                Is.Not.EqualTo(different.GetBomb().FuseSeconds));

            first.AdvanceTo(initial.FuseSeconds * 0.5d - 0.05d);
            Assert.That(first.GetBomb().IsChasing, Is.False);
            Assert.That(first.GetBomb().X, Is.Zero);
            Assert.That(first.GetBomb().Z, Is.Zero);

            first.AdvanceTo(initial.FuseSeconds * 0.5d + 0.05d);
            Assert.That(first.GetBomb().IsChasing, Is.True);
            Assert.That(first.GetBomb().X, Is.LessThan(0d));
            Assert.That(first.GetBomb().Z, Is.LessThan(0d));
        }

        [Test]
        public void TouchPickupTransferStunsRecipientAndCarrierMovesFaster()
        {
            var match = NewMatchWithPositions(
                new BombPassingPosition(0.5d, 0d),
                new BombPassingPosition(0.5d, 1.8d),
                new BombPassingPosition(1.0d, 2.2d),
                new BombPassingPosition(-5d, -5d));
            match.AdvanceTo(BombPassingRules.SimulationStepSeconds);
            Assert.That(match.GetBomb().HolderSlot, Is.EqualTo(0));

            match.SetFacing(0, 0d, 1d);
            var transfer = match.TryAttack(0);
            Assert.That(transfer.Kind,
                Is.EqualTo(BombPassingInteractionKind.Transfer));
            Assert.That(transfer.TargetSlot, Is.EqualTo(1));
            Assert.That(match.GetBomb().HolderSlot, Is.EqualTo(1));
            Assert.That(match.GetPlayer(1).StunRemainingSeconds,
                Is.EqualTo(BombPassingRules.StunSeconds)
                    .Within(0.000001d));
            Assert.That(match.TryAttack(0).WasApplied, Is.False);
            Assert.That(match.TryAttack(1).WasApplied, Is.False);

            match.SetMovementInput(1, 0d, 1d);
            match.AdvanceTo(0.4d);
            Assert.That(match.GetPlayer(1).Z, Is.EqualTo(1.8d));

            match.AdvanceTo(0.7d);
            Assert.That(match.GetPlayer(1).Z, Is.GreaterThan(1.8d));
            Assert.That(match.GetPlayer(1).IsStunned, Is.False);
            Assert.That(match.GetBomb().Z,
                Is.EqualTo(match.GetPlayer(1).Z));
        }

        [Test]
        public void EmptyHandAttackUsesNearestForwardHitboxAndStunsOnlyTarget()
        {
            var match = NewMatchWithPositions(
                new BombPassingPosition(0.5d, 0d),
                new BombPassingPosition(5d, 5d),
                new BombPassingPosition(5d, 6.5d),
                new BombPassingPosition(5.4d, 7d));
            match.AdvanceTo(BombPassingRules.SimulationStepSeconds);
            match.SetFacing(1, 0d, 1d);

            var hit = match.TryAttack(1);
            Assert.That(hit.Kind,
                Is.EqualTo(BombPassingInteractionKind.Stun));
            Assert.That(hit.TargetSlot, Is.EqualTo(2));
            Assert.That(match.GetPlayer(2).IsStunned, Is.True);
            Assert.That(match.GetPlayer(1).IsStunned, Is.False);
            Assert.That(match.GetPlayer(3).IsStunned, Is.False);
            Assert.That(match.GetBomb().HolderSlot, Is.EqualTo(0));
            Assert.That(BombPassingRules.IsInsideAttackHitbox(
                    5d, 5d, 0d, 1d, 5d, 4d),
                Is.False);
        }

        [Test]
        public void CarrierUsesBaseSpeedWhileEmptyHandUsesWalkingSpeed()
        {
            var match = NewMatchWithPositions(
                new BombPassingPosition(0.5d, 0d),
                new BombPassingPosition(3d, 3d),
                new BombPassingPosition(-5d, 5d),
                new BombPassingPosition(5d, -5d));
            match.AdvanceTo(BombPassingRules.SimulationStepSeconds);
            Assert.That(match.GetBomb().HolderSlot, Is.EqualTo(0));
            match.SetMovementInput(0, 1d, 0d);
            match.SetMovementInput(1, 1d, 0d);
            match.AdvanceTo(1d +
                BombPassingRules.SimulationStepSeconds);

            Assert.That(match.GetPlayer(0).X - 0.5d,
                Is.EqualTo(BombPassingRules.CarrierSpeed)
                    .Within(0.000001d));
            Assert.That(match.GetPlayer(1).X - 3d,
                Is.EqualTo(BombPassingRules.EmptyHandSpeed)
                    .Within(0.000001d));
            Assert.That(match.GetPlayer(0).FacingX,
                Is.EqualTo(1d));
        }

        [Test]
        public void ExplosionEliminatesOnlyHolderAndRespawnsAtCenterWithoutMovingOthers()
        {
            var match = NewMatchWithPositions(
                new BombPassingPosition(0.5d, 0d),
                new BombPassingPosition(5d, -5d),
                new BombPassingPosition(5d, 5d),
                new BombPassingPosition(-5d, 5d));
            var fuse = match.GetBomb().FuseSeconds;
            match.AdvanceTo(BombPassingRules.SimulationStepSeconds);
            Assert.That(match.GetBomb().HolderSlot, Is.EqualTo(0));

            match.AdvanceTo(fuse - 0.02d);
            Assert.That(match.ExplosionSequence, Is.Zero);
            Assert.That(match.GetPlayer(0).IsEliminated, Is.False);
            Assert.That(match.GetBomb().RemainingSeconds,
                Is.GreaterThan(0d));
            match.AdvanceTo(fuse + 0.02d);

            Assert.That(match.ExplosionSequence, Is.EqualTo(1UL));
            Assert.That(match.LastExplosion.Value.EliminatedSlot,
                Is.EqualTo(0));
            Assert.That(match.LastExplosion.Value.ElapsedSeconds,
                Is.GreaterThanOrEqualTo(fuse));
            Assert.That(match.LastExplosion.Value.ElapsedSeconds,
                Is.LessThan(
                    fuse + BombPassingRules.SimulationStepSeconds +
                    0.000001d));
            Assert.That(match.GetPlayer(0).Rank, Is.EqualTo(4));
            Assert.That(match.SurvivorCount, Is.EqualTo(3));
            Assert.That(match.IsComplete, Is.False);
            Assert.That(match.GetBomb().BombNumber, Is.EqualTo(2));
            Assert.That(match.GetBomb().X, Is.Zero);
            Assert.That(match.GetBomb().Z, Is.Zero);
            Assert.That(match.GetBomb().HolderSlot,
                Is.EqualTo(BombPassingRules.NoHolderSlot));
            Assert.That(match.GetPlayer(1).X, Is.EqualTo(5d));
            Assert.That(match.GetPlayer(1).Z, Is.EqualTo(-5d));
            Assert.That(match.GetBomb().FuseSeconds,
                Is.InRange(
                    BombPassingRules.MinimumFuseSeconds,
                    BombPassingRules.MaximumFuseSeconds));
        }

        [Test]
        public void RepeatedBombsFinishWithUniqueReverseEliminationRanks()
        {
            var match = new BombPassingMatchState(9911);
            Assert.Throws<InvalidOperationException>(
                () => match.GetFinalRank(0));
            match.AdvanceTo(100d);

            Assert.That(match.IsComplete, Is.True);
            Assert.That(match.SurvivorCount, Is.EqualTo(1));
            Assert.That(match.ExplosionSequence, Is.EqualTo(3UL));
            Assert.That(match.GetBomb().BombNumber, Is.EqualTo(3));
            var seen = new bool[BombPassingRules.PlayerCount + 1];
            for (var slot = 0; slot < BombPassingRules.PlayerCount;
                 slot++)
            {
                var rank = match.GetFinalRank(slot);
                Assert.That(rank, Is.InRange(1, 4));
                Assert.That(seen[rank], Is.False);
                seen[rank] = true;
            }
        }

        [Test]
        public void FixedStepMovementAndBombStateDoNotDependOnUpdateChunking()
        {
            var batched = new BombPassingMatchState(9211);
            var incremental = new BombPassingMatchState(9211);
            batched.SetMovementInput(0, 1d, 1d);
            incremental.SetMovementInput(0, 1d, 1d);
            batched.AdvanceTo(2d);
            for (var tenth = 1; tenth <= 20; tenth++)
            {
                incremental.AdvanceTo(tenth * 0.1d);
            }

            Assert.That(incremental.GetPlayer(0).X,
                Is.EqualTo(batched.GetPlayer(0).X));
            Assert.That(incremental.GetPlayer(0).Z,
                Is.EqualTo(batched.GetPlayer(0).Z));
            Assert.That(incremental.GetBomb().X,
                Is.EqualTo(batched.GetBomb().X));
            Assert.That(incremental.GetBomb().Z,
                Is.EqualTo(batched.GetBomb().Z));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => incremental.AdvanceTo(1d));
        }

        private static BombPassingMatchState NewMatchWithPositions(
            BombPassingPosition first,
            BombPassingPosition second,
            BombPassingPosition third,
            BombPassingPosition fourth)
        {
            return new BombPassingMatchState(
                3304,
                new[] { first, second, third, fourth });
        }
    }
}
