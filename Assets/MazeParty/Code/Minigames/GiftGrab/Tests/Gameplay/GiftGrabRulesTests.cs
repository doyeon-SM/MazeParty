using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.GiftGrab;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class GiftGrabRulesTests
    {
        [TestCase(1d, true)]
        [TestCase(0.751d, true)]
        [TestCase(0.75d, false)]
        [TestCase(0.5d, true)]
        [TestCase(0.25d, false)]
        [TestCase(0d, true)]
        public void DropPickupBlink_ChangesTwiceDuringOneSecondLock(
            double remaining, bool visible)
        {
            Assert.That(
                GiftGrabRules.IsDropPickupBlinkVisible(remaining),
                Is.EqualTo(visible));
        }

        [Test]
        public void ActiveClock_SpawnsAtExactBoundariesAndAlwaysRunsToSixty()
        {
            var round = new GiftGrabRoundState(1);

            Assert.That(round.SpawnedGiftCount, Is.EqualTo(5));
            Assert.That(
                round.GetGift(4).State,
                Is.EqualTo(GiftGrabGiftState.Loose));
            Assert.That(
                round.GetGift(5).State,
                Is.EqualTo(GiftGrabGiftState.Unspawned));

            round.AdvanceTo(14.999d);
            round.InterruptForPause(14.999d);
            Assert.That(round.SpawnedGiftCount, Is.EqualTo(5));

            round.AdvanceTo(15d);
            round.AdvanceTo(15d);
            Assert.That(round.SpawnedGiftCount, Is.EqualTo(8));
            Assert.That(
                round.GetGift(7).State,
                Is.EqualTo(GiftGrabGiftState.Loose));
            Assert.That(
                round.GetGift(8).State,
                Is.EqualTo(GiftGrabGiftState.Unspawned));

            round.AdvanceTo(29.999d);
            Assert.That(round.SpawnedGiftCount, Is.EqualTo(8));
            round.AdvanceTo(30d);
            Assert.That(round.SpawnedGiftCount, Is.EqualTo(11));
            round.AdvanceTo(44.999d);
            Assert.That(round.SpawnedGiftCount, Is.EqualTo(11));
            round.AdvanceTo(45d);
            Assert.That(round.SpawnedGiftCount, Is.EqualTo(14));
            Assert.That(round.ResolvePickup(0, 13, 45.599d).Status,
                Is.EqualTo(GiftGrabPickupStatus.IgnoredRegrabLock));

            for (var giftId = 0;
                 giftId < GiftGrabRules.TotalGiftCount;
                 giftId++)
            {
                var slot = giftId % GiftGrabRules.PlayerCount;
                Assert.That(
                    round.ResolvePickup(slot, giftId, 45.6d).WasPickedUp,
                    Is.True);
                Assert.That(
                    round.ResolveDeposit(slot, 45.6d).WasDeposited,
                    Is.True);
            }

            Assert.That(TotalStoredGiftCount(round), Is.EqualTo(14));
            Assert.That(round.IsComplete, Is.False);
            round.AdvanceTo(50d);
            var storedSeconds =
                round.GetPlayer(0).CumulativeStoredGiftSeconds;
            round.AdvanceTo(50d - 0.0000000005d);
            Assert.That(
                round.GetPlayer(0).CumulativeStoredGiftSeconds,
                Is.EqualTo(storedSeconds).Within(0.000000001d));
            Assert.That(round.TryEndForTimeout(59.999d), Is.False);
            Assert.That(round.TryEndForTimeout(60d), Is.True);
            Assert.That(
                round.EndReason,
                Is.EqualTo(GiftGrabRoundEndReason.TimeLimit));
            Assert.That(TotalStoredGiftCount(round), Is.EqualTo(14));
        }

        [Test]
        public void PickupDepositStealAndNeutralReturn_CountOnlyStoredTime()
        {
            var round = new GiftGrabRoundState(1);

            var pickup = round.ResolvePickup(0, 0, 0d);
            Assert.That(pickup.WasPickedUp, Is.True);
            Assert.That(
                round.GetPlayer(0).CurrentMoveSpeed,
                Is.EqualTo(GiftGrabRules.CarryMoveSpeed));
            Assert.That(round.ResolveDeposit(0, 1d).WasDeposited, Is.True);

            round.AdvanceTo(3d);
            Assert.That(round.GetPlayer(0).StoredGiftCount, Is.EqualTo(1));
            Assert.That(
                round.GetPlayer(0).CumulativeStoredGiftSeconds,
                Is.EqualTo(2d).Within(0.000001d));
            Assert.That(
                round.ResolvePickup(0, 0, 3d).Status,
                Is.EqualTo(GiftGrabPickupStatus.IgnoredOwnStoredGift));

            var stolen = round.ResolvePickup(1, 0, 3d);
            Assert.That(stolen.WasPickedUp, Is.True);
            Assert.That(stolen.PreviousStoredOwnerSlot, Is.Zero);
            Assert.That(round.GetPlayer(0).StoredGiftCount, Is.Zero);
            Assert.That(round.GetPlayer(1).HeldGiftId, Is.Zero);

            round.AdvanceTo(5d);
            Assert.That(
                round.GetPlayer(0).CumulativeStoredGiftSeconds,
                Is.EqualTo(2d).Within(0.000001d));
            Assert.That(
                round.GetPlayer(1).CumulativeStoredGiftSeconds,
                Is.Zero);
            Assert.That(round.ResolveDeposit(1, 5d).WasDeposited, Is.True);
            round.AdvanceTo(6d);
            Assert.That(
                round.GetPlayer(1).CumulativeStoredGiftSeconds,
                Is.EqualTo(1d).Within(0.000001d));

            var returned = round.ResolveOutOfBounds(0, 6d);
            Assert.That(returned.WasReturned, Is.True);
            Assert.That(returned.PreviousStoredOwnerSlot, Is.EqualTo(1));
            Assert.That(round.GetPlayer(1).StoredGiftCount, Is.Zero);
            Assert.That(
                round.GetGift(0).State,
                Is.EqualTo(GiftGrabGiftState.Loose));
            Assert.That(
                round.GetPlayer(1).CurrentMoveSpeed,
                Is.EqualTo(GiftGrabRules.NormalMoveSpeed));
        }

        [Test]
        public void StolenDelivery_CountsOnlyDirectOpponentBaseToOwnBase_AndCanRepeatAfterResteal()
        {
            var round = new GiftGrabRoundState(1);
            round.ResolvePickup(0, 0, 0); round.ResolveDeposit(0, 1);
            Assert.That(round.GetPlayer(0).StolenDeliveries, Is.Zero);
            round.ResolvePickup(1, 0, 2); round.ResolveDeposit(1, 3); round.ResolveDeposit(1, 3);
            Assert.That(round.GetPlayer(1).StolenDeliveries, Is.EqualTo(1));
            round.ResolvePickup(0, 0, 4); round.ResolveDeposit(0, 5);
            round.ResolvePickup(1, 0, 6); round.ResolveDeposit(1, 7);
            Assert.That(round.GetPlayer(1).StolenDeliveries, Is.EqualTo(2));
            round.ResolvePickup(0, 0, 8); round.ResolveThrow(0, 9); round.ResolveGiftLanded(0, 9.1);
            round.ResolvePickup(0, 0, 11); round.ResolveDeposit(0, 12);
            Assert.That(round.GetPlayer(0).StolenDeliveries, Is.EqualTo(1), "Dropping/throwing clears stolen provenance.");
        }

        [Test]
        public void ThrowAndPush_ApplyExactImmunityCooldownStunAndDrops()
        {
            var landingRound = new GiftGrabRoundState(1);

            landingRound.ResolvePickup(2, 2, 0d);
            landingRound.ResolveThrow(2, 0d);
            var landed = landingRound.ResolveGiftLanded(2, 0.05d);
            Assert.That(landed.WasLanded, Is.True);
            Assert.That(landed.PickupLockedPlayerSlot,
                Is.EqualTo(GiftGrabRules.AllPlayersPickupLock));
            Assert.That(landed.PickupLockEndsAtSeconds, Is.EqualTo(1.05d));
            Assert.That(
                landingRound.ResolvePickup(1, 2, 1.049d).Status,
                Is.EqualTo(GiftGrabPickupStatus.IgnoredRegrabLock));
            Assert.That(
                landingRound.ResolvePickup(2, 2, 1.05d).WasPickedUp,
                Is.True);

            var round = new GiftGrabRoundState(1);

            round.ResolvePickup(2, 2, 0d);
            round.ResolvePickup(0, 0, 0.3d);
            round.ResolveThrow(0, 0.3d);
            Assert.That(
                round.ResolveThrownHit(0, 0, 0.549d).Status,
                Is.EqualTo(
                    GiftGrabThrownHitStatus.IgnoredThrowerImmunity));
            var exactBoundaryHit = round.ResolveThrownHit(0, 0, 0.55d);
            Assert.That(exactBoundaryHit.WasHit, Is.True);
            Assert.That(
                round.GetGift(0).State,
                Is.EqualTo(GiftGrabGiftState.Loose));
            Assert.That(
                round.GetPlayer(0).StunnedUntilSeconds,
                Is.EqualTo(1.05d));

            round.ResolvePickup(1, 1, 0.55d);
            round.ResolvePickup(3, 3, 0.55d);
            round.ResolveThrow(3, 0.55d);
            var thrownHit = round.ResolveThrownHit(3, 1, 0.6d);
            Assert.That(thrownHit.WasHit, Is.True);
            Assert.That(thrownHit.DroppedGiftId, Is.EqualTo(1));
            Assert.That(round.GetPlayer(1).HeldGiftId, Is.EqualTo(-1));
            Assert.That(round.GetPlayer(1).CurrentMoveSpeed, Is.Zero);
            Assert.That(
                round.GetGift(1).PickupLockedPlayerSlot,
                Is.EqualTo(GiftGrabRules.AllPlayersPickupLock));
            Assert.That(round.GetGift(1).PickupLockEndsAtSeconds,
                Is.EqualTo(1.6d).Within(0.000001d));
            Assert.That(
                round.GetGift(3).State,
                Is.EqualTo(GiftGrabGiftState.Loose));

            Assert.That(
                round.BeginPushCast(3, 1.049d).Status,
                Is.EqualTo(GiftGrabPushStatus.IgnoredThrowRecovery));
            Assert.That(
                round.ResolvePickup(1, 4, 1.099d).Status,
                Is.EqualTo(GiftGrabPickupStatus.IgnoredStunned));
            Assert.That(round.GetPlayer(1).CanMove, Is.False);
            Assert.That(
                round.ResolvePickup(1, 4, 1.1d).WasPickedUp,
                Is.True);

            var started = round.BeginPushCast(3, 1.1d);
            Assert.That(started.WasStarted, Is.True);
            Assert.That(started.CooldownEndsAtSeconds, Is.EqualTo(2.1d));
            Assert.That(
                round.GetPlayer(3).PushCastEndsAtSeconds,
                Is.EqualTo(1.2d).Within(0.000001d));
            Assert.That(round.GetPlayer(3).CanMove, Is.False);
            Assert.That(round.GetPlayer(3).CanAct, Is.False);
            Assert.That(
                round.ResolvePickup(3, 4, 1.1d).Status,
                Is.EqualTo(GiftGrabPickupStatus.IgnoredPushCasting));
            Assert.That(
                round.BeginPushCast(3, 1.1d).Status,
                Is.EqualTo(GiftGrabPushStatus.IgnoredCastInProgress));
            round.InterruptForPause(1.1d);
            Assert.That(
                round.ResolvePush(3, -1, 1.199d).Status,
                Is.EqualTo(GiftGrabPushStatus.IgnoredCastInProgress));

            var missed = round.ResolvePush(3, -1, 1.2d);
            Assert.That(missed.Status, Is.EqualTo(GiftGrabPushStatus.Missed));
            Assert.That(missed.CooldownEndsAtSeconds, Is.EqualTo(2.1d));
            Assert.That(round.GetPlayer(3).CanMove, Is.True);
            Assert.That(
                round.ResolvePickup(0, 1, 1.599d).Status,
                Is.EqualTo(GiftGrabPickupStatus.IgnoredRegrabLock));
            Assert.That(
                round.ResolvePickup(0, 1, 1.6d).WasPickedUp,
                Is.True);
            Assert.That(
                round.BeginPushCast(3, 2.099d).Status,
                Is.EqualTo(GiftGrabPushStatus.IgnoredCooldown));

            var secondCast = round.BeginPushCast(3, 2.1d);
            Assert.That(secondCast.WasStarted, Is.True);
            Assert.That(
                round.ResolvePush(3, 2, 2.199d).Status,
                Is.EqualTo(GiftGrabPushStatus.IgnoredCastInProgress));
            var pushed = round.ResolvePush(3, 2, 2.2d);
            Assert.That(pushed.WasHit, Is.True);
            Assert.That(pushed.DroppedGiftId, Is.EqualTo(2));
            Assert.That(
                round.GetPlayer(2).StunnedUntilSeconds,
                Is.EqualTo(2.7d).Within(0.000001d));
            Assert.That(
                round.GetGift(2).PickupLockEndsAtSeconds,
                Is.EqualTo(3.2d).Within(0.000001d));

            Assert.That(
                round.BeginPushCast(3, 3.099d).Status,
                Is.EqualTo(GiftGrabPushStatus.IgnoredCooldown));
            Assert.That(round.BeginPushCast(3, 3.1d).WasStarted, Is.True);
            Assert.That(
                round.GetPlayer(1).CurrentMoveSpeed,
                Is.EqualTo(GiftGrabRules.CarryMoveSpeed));
        }

        [Test]
        public void ThrowRecovery_BlocksMovementPickupAndPushUntilExactBoundary()
        {
            var round = new GiftGrabRoundState(1);
            Assert.That(round.ResolvePickup(0, 0, 0d).WasPickedUp, Is.True);
            Assert.That(round.ResolveThrow(0, 0d).WasThrown, Is.True);
            Assert.That(round.GetPlayer(0).IsStunned, Is.False);
            Assert.That(round.GetPlayer(0).CanMove, Is.False);
            Assert.That(round.GetPlayer(0).CanAct, Is.False);
            Assert.That(round.GetPlayer(0).ThrowRecoveryEndsAtSeconds,
                Is.EqualTo(GiftGrabRules.ThrowRecoverySeconds));
            Assert.That(round.ResolvePickup(0, 1, 0.499d).Status,
                Is.EqualTo(GiftGrabPickupStatus.IgnoredThrowRecovery));
            Assert.That(round.BeginPushCast(0, 0.499d).Status,
                Is.EqualTo(GiftGrabPushStatus.IgnoredThrowRecovery));
            Assert.That(round.GetPlayer(0).CanMove, Is.False);
            round.AdvanceTo(0.5d);
            Assert.That(round.GetPlayer(0).CanMove, Is.True);
            Assert.That(round.ResolvePickup(0, 1, 0.5d).WasPickedUp,
                Is.True);
        }

        [Test]
        public void StunProtection_RejectsBothHitSourcesUntilExactEnd_WhileRecoveredPlayerMovesAndKeepsGift()
        {
            foreach (var push in new[] { false, true })
            foreach (var impact in new[] { .25d, .5d, .999d, 1d })
            {
                var round = new GiftGrabRoundState(1);
                round.ResolvePickup(1, 0, 0d); round.ResolveThrow(1, 0d);
                Assert.That(round.ResolveThrownHit(0, 0, 0d).WasHit, Is.True);
                round.ResolvePickup(2, 1, 0d); round.ResolveThrow(2, 0d);
                if (push) Assert.That(round.BeginPushCast(3, impact - GiftGrabRules.PushCastSeconds).WasStarted, Is.True);
                round.AdvanceTo(impact);
                var player = round.GetPlayer(0);
                Assert.That(player.CanMove, Is.EqualTo(impact >= .5d));
                if (impact >= .5d) Assert.That(round.ResolvePickup(0, 2, impact).WasPickedUp, Is.True);
                // Observe late: protection must use impact/cast completion time, not receipt time.
                var hit = push ? round.ResolvePush(3, 0, 1.1d).WasHit :
                    round.ResolveThrownHit(1, 0, impact, 1.1d).WasHit;
                Assert.That(hit, Is.EqualTo(impact >= 1d), push + " at " + impact);
                Assert.That(player.StunnedUntilSeconds, Is.EqualTo(impact >= 1d ? 1.5d : .5d).Within(1e-9));
                Assert.That(player.HeldGiftId, Is.EqualTo(impact >= .5d && impact < 1d ? 2 : -1));
                Assert.That(player.StunProtectionUntilSeconds, Is.EqualTo(impact >= 1d ? 2d : 1d).Within(1e-9));
            }
        }

        [Test]
        public void ThrownHit_UsesImpactTimeForImmunityAndStunBoundary()
        {
            var round = new GiftGrabRoundState(1);
            Assert.That(round.ResolvePickup(0, 0, 0d).WasPickedUp,
                Is.True);
            Assert.That(round.ResolveThrow(0, 0d).WasThrown, Is.True);

            var immuneAtImpact = round.ResolveThrownHit(
                0,
                0,
                GiftGrabRules.ThrowSelfHitImmunitySeconds - 0.001d,
                GiftGrabRules.ThrowSelfHitImmunitySeconds + 0.02d);
            Assert.That(
                immuneAtImpact.Status,
                Is.EqualTo(
                    GiftGrabThrownHitStatus.IgnoredThrowerImmunity));

            var exactImpact = round.ResolveThrownHit(
                0,
                0,
                GiftGrabRules.ThrowSelfHitImmunitySeconds,
                GiftGrabRules.ThrowSelfHitImmunitySeconds + 0.02d);
            Assert.That(exactImpact.WasHit, Is.True);

            var stunEndsAt =
                GiftGrabRules.ThrowSelfHitImmunitySeconds +
                GiftGrabRules.ThrowStunSeconds;
            Assert.That(
                round.GetPlayer(0).StunnedUntilSeconds,
                Is.EqualTo(stunEndsAt));
            round.AdvanceTo(stunEndsAt - 0.001d);
            Assert.That(round.GetPlayer(0).CanMove, Is.False);
            round.AdvanceTo(stunEndsAt);
            Assert.That(round.GetPlayer(0).CanMove, Is.True);
        }

        [Test]
        public void ThrownImpact_ConsumesGiftWhileFlightTimeoutStillLandsLoose()
        {
            var round = new GiftGrabRoundState(1);

            Assert.That(round.ResolvePickup(0, 0, 0d).WasPickedUp, Is.True);
            Assert.That(round.ResolveThrow(0, 0d).WasThrown, Is.True);
            var consumed = round.ResolveThrownConsumed(0, 0.1d);
            Assert.That(consumed.WasConsumed, Is.True);
            Assert.That(
                round.GetGift(0).State,
                Is.EqualTo(GiftGrabGiftState.Consumed));
            Assert.That(
                round.ResolvePickup(1, 0, 0.1d).Status,
                Is.EqualTo(GiftGrabPickupStatus.IgnoredUnavailable));
            Assert.That(
                round.ResolveGiftLanded(0, 0.1d).Status,
                Is.EqualTo(GiftGrabGiftLandingStatus.IgnoredNotThrown));

            Assert.That(round.ResolvePickup(0, 1, 0.1d).Status,
                Is.EqualTo(GiftGrabPickupStatus.IgnoredThrowRecovery));
            Assert.That(round.ResolvePickup(0, 1, 0.5d).WasPickedUp, Is.True);
            Assert.That(round.ResolveThrow(0, 0.5d).WasThrown, Is.True);
            var timedOut = round.ResolveGiftLanded(1, 1d);
            Assert.That(timedOut.WasLanded, Is.True);
            Assert.That(
                round.GetGift(1).State,
                Is.EqualTo(GiftGrabGiftState.Loose));
        }

        [Test]
        public void Scoring_PreservesRoundRulesAndSharesEqualFinalPoints()
        {
            var scored = GiftGrabRoundScoring.Score(new[]
            {
                GiftGrabRoundOutcome.Create(1, 5, 20d),
                GiftGrabRoundOutcome.Create(3, 4, 100d),
                GiftGrabRoundOutcome.Create(0, 5, 20d),
                GiftGrabRoundOutcome.Create(2, 5, 30d)
            });
            Assert.That(
                RoundSlots(scored),
                Is.EqualTo(new[] { 2, 0, 1, 3 }));
            Assert.That(
                RoundPoints(scored),
                Is.EqualTo(new[] { 3, 2, 1, 0 }));

            var first = ScoreStoredCounts(10, 8, 5, 1);
            var second = ScoreStoredCounts(2, 5, 7, 9);
            var leaderboard = GiftGrabMatchScoring.BuildLeaderboard(
                new[] { first, second });

            Assert.That(
                LeaderboardSlots(leaderboard),
                Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(
                LeaderboardPoints(leaderboard),
                Is.EqualTo(new[] { 3, 3, 3, 3 }));
            Assert.That(
                TotalStoredCounts(leaderboard),
                Is.EqualTo(new[] { 12, 13, 12, 10 }));
            Assert.That(leaderboard[1].FinalRoundRank, Is.EqualTo(3));
            Assert.That(leaderboard[2].FinalRoundRank, Is.EqualTo(2));
            foreach (var entry in leaderboard) Assert.That(entry.Rank, Is.EqualTo(1));
            Assert.Throws<System.ArgumentException>(() =>
                GiftGrabMatchScoring.BuildLeaderboard(new[] { first }));
        }

        private static GiftGrabRoundResult ScoreStoredCounts(
            int slot0,
            int slot1,
            int slot2,
            int slot3)
        {
            return GiftGrabRoundScoring.Score(new[]
            {
                GiftGrabRoundOutcome.Create(0, slot0, 0d),
                GiftGrabRoundOutcome.Create(1, slot1, 0d),
                GiftGrabRoundOutcome.Create(2, slot2, 0d),
                GiftGrabRoundOutcome.Create(3, slot3, 0d)
            });
        }

        private static int TotalStoredGiftCount(GiftGrabRoundState round)
        {
            var count = 0;
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                count += round.GetPlayer(slot).StoredGiftCount;
            }

            return count;
        }

        private static int[] RoundSlots(GiftGrabRoundResult result)
        {
            var values = new int[result.Standings.Count];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = result.Standings[index].PlayerSlot;
            }

            return values;
        }

        private static int[] RoundPoints(GiftGrabRoundResult result)
        {
            var values = new int[result.Standings.Count];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = result.Standings[index].Points;
            }

            return values;
        }

        private static int[] LeaderboardSlots(
            IReadOnlyList<GiftGrabLeaderboardEntry> leaderboard)
        {
            var values = new int[leaderboard.Count];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = leaderboard[index].PlayerSlot;
            }

            return values;
        }

        private static int[] LeaderboardPoints(
            IReadOnlyList<GiftGrabLeaderboardEntry> leaderboard)
        {
            var values = new int[leaderboard.Count];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = leaderboard[index].TotalPoints;
            }

            return values;
        }

        private static int[] TotalStoredCounts(
            IReadOnlyList<GiftGrabLeaderboardEntry> leaderboard)
        {
            var values = new int[leaderboard.Count];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = leaderboard[index].TotalStoredGiftCount;
            }

            return values;
        }
    }
}
