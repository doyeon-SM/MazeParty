using System;
using MazeParty.Gameplay.Minigames.BombPassing;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BombPassingRulesTests
    {
        [Test]
        public void AuthoredCollisionLayoutContainsWallsAndPillars()
        {
            Assert.That(
                BombPassingArenaCollisionLayout.BlockingRectCount,
                Is.EqualTo(23));
            Assert.That(
                BombPassingArenaCollisionLayout.BlocksCircle(
                    4d,
                    1.7d,
                    0d),
                Is.True,
                "Horizontal wall footprint is missing.");
            Assert.That(
                BombPassingArenaCollisionLayout.BlocksCircle(
                    5.2d,
                    -5d,
                    0d),
                Is.True,
                "Vertical wall footprint is missing.");
            Assert.That(
                BombPassingArenaCollisionLayout.BlocksCircle(
                    -6.5d,
                    -4.78d,
                    0d),
                Is.True,
                "Pillar footprint is missing.");
            Assert.That(
                BombPassingArenaCollisionLayout.BlocksCircle(
                    0d,
                    0d,
                    BombPassingRules.PlayerRadius),
                Is.False);
            Assert.That(
                BombPassingArenaCollisionLayout.BlocksSegment(
                    4d,
                    1d,
                    4d,
                    2.4d),
                Is.True,
                "Authored walls must block attacks across their footprint.");
            Assert.That(
                BombPassingArenaCollisionLayout.BlocksSegment(
                    0d,
                    0d,
                    0d,
                    -1d),
                Is.False);
            Assert.That(
                BombPassingArenaCollisionLayout.BlocksSegment(
                    4.48d,
                    -3.5d,
                    4.585d,
                    -3.4d,
                    BombPassingRules.BombCollisionRadius),
                Is.False,
                "Rounded bomb clearance must not use square-expanded " +
                "wall corners.");
        }

        [Test]
        public void DefaultSpawnsClearAuthoredCollisionLayout()
        {
            var match = new BombPassingMatchState(22091);

            for (var slot = 0; slot < BombPassingRules.PlayerCount; slot++)
            {
                var player = match.GetPlayer(slot);
                Assert.That(
                    BombPassingArenaCollisionLayout.BlocksCircle(
                        player.X,
                        player.Z,
                        BombPassingRules.PlayerRadius),
                    Is.False,
                    "Spawn for P" + (slot + 1) +
                    " overlaps an authored obstacle.");
            }
        }

        [Test]
        public void MovementStopsAtAuthoredWallAndSlidesAlongIt()
        {
            var match = NewMatchWithPositions(
                new BombPassingPosition(4d, 0.4d),
                new BombPassingPosition(-6.5d, -6.5d),
                new BombPassingPosition(6.5d, 6.5d),
                new BombPassingPosition(-6.5d, 6.5d));
            match.SetMovementInput(0, 1d, 1d);

            match.AdvanceTo(0.5d);

            var player = match.GetPlayer(0);
            Assert.That(player.X, Is.GreaterThan(4.8d));
            Assert.That(player.Z, Is.LessThanOrEqualTo(1.07d));
            Assert.That(
                BombPassingArenaCollisionLayout.BlocksCircle(
                    player.X,
                    player.Z,
                    BombPassingRules.PlayerRadius),
                Is.False);
        }

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
            Assert.That(
                first.GetBomb().X * first.GetBomb().X +
                first.GetBomb().Z * first.GetBomb().Z,
                Is.GreaterThan(0d));
        }

        [Test]
        public void TransferUsesExactCastStunAndPostCastRecoveryBoundaries()
        {
            var match = NewMatchWithPositions(
                new BombPassingPosition(0.5d, 0d),
                new BombPassingPosition(0.5d, -1.8d),
                new BombPassingPosition(1.0d, -2.2d),
                new BombPassingPosition(-5d, -5d));
            match.AdvanceTo(BombPassingRules.SimulationStepSeconds);
            Assert.That(match.GetBomb().HolderSlot, Is.EqualTo(0));

            var castStartedAt = match.MatchElapsedSeconds;
            BombPassingInteraction? resolved = null;
            match.InteractionResolved += interaction =>
                resolved = interaction;
            match.SetFacing(0, 0d, -1d);
            Assert.That(match.TryStartAttack(0), Is.True);
            Assert.That(match.GetPlayer(0).IsAttackCasting, Is.True);
            Assert.That(match.TryStartAttack(0), Is.False);

            var actorBeforeCast = match.GetPlayer(0);
            match.SetMovementInput(0, 1d, 0d);
            match.SetFacing(0, 0d, 1d);
            match.AdvanceTo(
                castStartedAt +
                BombPassingRules.AttackCastSeconds - 0.001d);
            Assert.That(match.InteractionSequence, Is.Zero);
            Assert.That(match.GetBomb().HolderSlot, Is.EqualTo(0));
            Assert.That(match.GetPlayer(0).X,
                Is.EqualTo(actorBeforeCast.X).Within(0.000001d));
            Assert.That(match.GetPlayer(0).Z,
                Is.EqualTo(actorBeforeCast.Z).Within(0.000001d));

            var castCompletedAt = castStartedAt +
                BombPassingRules.AttackCastSeconds;
            match.AdvanceTo(castCompletedAt);
            Assert.That(resolved.HasValue, Is.True);
            Assert.That(resolved.Value.Kind,
                Is.EqualTo(BombPassingInteractionKind.Transfer));
            Assert.That(resolved.Value.TargetSlot, Is.EqualTo(1));
            Assert.That(match.GetBomb().HolderSlot, Is.EqualTo(1));
            Assert.That(match.GetPlayer(0).IsAttackCasting, Is.False);
            Assert.That(match.GetPlayer(1).StunRemainingSeconds,
                Is.EqualTo(BombPassingRules.StunSeconds)
                    .Within(0.000001d));
            Assert.That(match.CanStartAttack(0), Is.False);

            match.SetMovementInput(1, 0d, -1d);
            var stunnedAt = match.GetPlayer(1);
            match.AdvanceTo(
                castCompletedAt +
                BombPassingRules.StunSeconds - 0.001d);
            Assert.That(match.GetPlayer(1).Z,
                Is.EqualTo(stunnedAt.Z).Within(0.000001d));
            Assert.That(match.GetPlayer(1).IsStunned, Is.True);
            Assert.That(match.CanStartAttack(0), Is.False);
            Assert.That(match.GetPlayer(0).X,
                Is.GreaterThan(actorBeforeCast.X));

            var nextAttackAt = castCompletedAt +
                BombPassingRules.AttackRecoverySeconds;
            match.AdvanceTo(nextAttackAt);
            Assert.That(match.GetPlayer(1).IsStunned, Is.False);
            Assert.That(match.CanStartAttack(0), Is.True);
            Assert.That(match.TryStartAttack(0), Is.True);
            Assert.That(match.GetBomb().Z,
                Is.EqualTo(match.GetPlayer(1).Z));
        }

        [Test]
        public void EmptyHandCastUsesCompletionPositionsAndStartFacing()
        {
            var match = NewMatchWithPositions(
                new BombPassingPosition(5d, 5d),
                new BombPassingPosition(-6.5d, -6.5d),
                new BombPassingPosition(5d, 6.9d),
                new BombPassingPosition(-6.5d, 6.5d));
            match.SetMovementInput(0, 1d, 0d);
            match.SetMovementInput(2, 0d, -1d);
            match.SetFacing(0, 0d, 1d);

            Assert.That(match.TryStartAttack(0), Is.True);
            match.SetFacing(0, 0d, -1d);
            match.AdvanceTo(BombPassingRules.AttackCastSeconds);

            var hit = match.LastInteraction;
            Assert.That(hit.HasValue, Is.True);
            Assert.That(hit.Value.Kind,
                Is.EqualTo(BombPassingInteractionKind.Stun));
            Assert.That(hit.Value.TargetSlot, Is.EqualTo(2));
            Assert.That(match.GetPlayer(2).IsStunned, Is.True);
            Assert.That(match.GetPlayer(0).X, Is.EqualTo(5d));
            Assert.That(match.GetPlayer(0).Z, Is.EqualTo(5d));
            Assert.That(match.GetPlayer(1).IsStunned, Is.False);
            Assert.That(match.GetPlayer(3).IsStunned, Is.False);
            Assert.That(match.GetBomb().HolderSlot,
                Is.EqualTo(BombPassingRules.NoHolderSlot));
            Assert.That(BombPassingRules.IsInsideAttackHitbox(
                    5d, 5d, 0d, 1d, 5d, 4d),
                Is.False);
        }

        [Test]
        public void MissStillUsesExactCastAndRecoveryBoundaries()
        {
            var match = NewMatchWithPositions(
                new BombPassingPosition(5d, 5d),
                new BombPassingPosition(-6.5d, -6.5d),
                new BombPassingPosition(5d, -5d),
                new BombPassingPosition(-5d, 5d));
            match.SetMovementInput(0, 1d, 0d);

            Assert.That(match.TryStartAttack(0), Is.True);
            match.AdvanceTo(BombPassingRules.AttackCastSeconds - 0.001d);

            Assert.That(match.GetPlayer(0).IsAttackCasting, Is.True);
            Assert.That(match.GetPlayer(0).X, Is.EqualTo(5d));
            Assert.That(match.InteractionSequence, Is.Zero);

            match.AdvanceTo(BombPassingRules.AttackCastSeconds);

            Assert.That(match.GetPlayer(0).IsAttackCasting, Is.False);
            Assert.That(match.GetPlayer(0).X, Is.EqualTo(5d));
            Assert.That(match.LastInteraction, Is.Null);
            Assert.That(match.CanStartAttack(0), Is.False);

            match.AdvanceTo(BombPassingRules.AttackCooldownSeconds - 0.001d);

            Assert.That(match.GetPlayer(0).X, Is.GreaterThan(5d));
            Assert.That(match.CanStartAttack(0), Is.False);
            Assert.That(match.GetPlayer(0).AttackCooldownRemainingSeconds,
                Is.EqualTo(0.001d).Within(0.000001d));

            match.AdvanceTo(BombPassingRules.AttackCooldownSeconds);

            Assert.That(match.CanStartAttack(0), Is.True);
        }

        [Test]
        public void AttackCannotHitThroughAuthoredWall()
        {
            var match = NewMatchWithPositions(
                new BombPassingPosition(4d, 1d),
                new BombPassingPosition(4d, 2.4d),
                new BombPassingPosition(-6.5d, -6.5d),
                new BombPassingPosition(6.5d, -6.5d));
            match.SetFacing(0, 0d, 1d);

            Assert.That(match.TryStartAttack(0), Is.True);
            match.AdvanceTo(BombPassingRules.AttackCastSeconds);

            Assert.That(match.LastInteraction, Is.Null);
            Assert.That(match.GetPlayer(1).IsStunned, Is.False);
        }

        [Test]
        public void UnheldBombChaseDoesNotEnterAuthoredWall()
        {
            var match = NewMatchWithPositions(
                new BombPassingPosition(1.3d, 3d),
                new BombPassingPosition(-6.5d, -6.5d),
                new BombPassingPosition(6.5d, -6.5d),
                new BombPassingPosition(-6.5d, 6.5d));
            var chaseStartedAt =
                match.GetBomb().FuseSeconds *
                BombPassingRules.ChaseStartRemainingRatio;

            for (var sample = 1; sample <= 20; sample++)
            {
                match.AdvanceTo(chaseStartedAt + sample * 0.05d);
                var bomb = match.GetBomb();
                Assert.That(
                    BombPassingArenaCollisionLayout.BlocksCircle(
                        bomb.X,
                        bomb.Z,
                        BombPassingRules.BombCollisionRadius),
                    Is.False,
                    "Bomb entered authored geometry at sample " + sample +
                    ".");
            }
        }

        [Test]
        public void BombPickupRequiresLineOfSightAcrossAuthoredWall()
        {
            // This intentionally close target isolates pickup line of sight.
            // Runtime movement cannot place a player this near the wall.
            var match = NewMatchWithPositions(
                new BombPassingPosition(0d, 1.9d),
                new BombPassingPosition(-6.5d, -6.5d),
                new BombPassingPosition(6.5d, -6.5d),
                new BombPassingPosition(-6.5d, 6.5d));
            var chaseSampleTime =
                match.GetBomb().FuseSeconds *
                BombPassingRules.ChaseStartRemainingRatio + 0.8d;

            match.AdvanceTo(chaseSampleTime);

            var bomb = match.GetBomb();
            Assert.That(bomb.HolderSlot,
                Is.EqualTo(BombPassingRules.NoHolderSlot));
            Assert.That(
                BombPassingArenaCollisionLayout.BlocksSegment(
                    bomb.X,
                    bomb.Z,
                    match.GetPlayer(0).X,
                    match.GetPlayer(0).Z),
                Is.True);
            Assert.That(
                Math.Sqrt(
                    Math.Pow(bomb.X - match.GetPlayer(0).X, 2d) +
                    Math.Pow(bomb.Z - match.GetPlayer(0).Z, 2d)),
                Is.LessThanOrEqualTo(BombPassingRules.PickupRadius));
        }

        [Test]
        public void ActiveTimePauseAndFreshMatchResetPreserveCastContract()
        {
            var match = NewMatchWithPositions(
                new BombPassingPosition(5d, 5d),
                new BombPassingPosition(-6.5d, -6.5d),
                new BombPassingPosition(5d, -5d),
                new BombPassingPosition(-5d, 5d));
            Assert.That(match.TryStartAttack(0), Is.True);
            match.AdvanceTo(0.25d);
            var remainingAtPause =
                match.GetPlayer(0).AttackCastRemainingSeconds;

            match.AdvanceTo(0.25d);

            Assert.That(match.GetPlayer(0).AttackCastRemainingSeconds,
                Is.EqualTo(remainingAtPause));
            Assert.That(match.InteractionSequence, Is.Zero);

            var reset = NewMatchWithPositions(
                new BombPassingPosition(5d, 5d),
                new BombPassingPosition(-6.5d, -6.5d),
                new BombPassingPosition(5d, -5d),
                new BombPassingPosition(-5d, 5d));
            Assert.That(reset.GetPlayer(0).IsAttackCasting, Is.False);
            Assert.That(reset.GetPlayer(0).AttackCooldownRemainingSeconds,
                Is.Zero);
            Assert.That(reset.CanStartAttack(0), Is.True);
            Assert.That(reset.LastInteraction, Is.Null);
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
