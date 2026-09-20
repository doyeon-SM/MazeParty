using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.GiftGrab;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class GiftGrabRulesTests
    {
        [Test]
        public void ActiveClock_SpawnsAtExactBoundariesAndAlwaysRunsToSixty()
        {
            var round = new GiftGrabRoundState(1);

            Assert.That(round.SpawnedGiftCount, Is.EqualTo(10));
            Assert.That(
                round.GetGift(9).State,
                Is.EqualTo(GiftGrabGiftState.Loose));
            Assert.That(
                round.GetGift(10).State,
                Is.EqualTo(GiftGrabGiftState.Unspawned));

            round.AdvanceTo(14.999d);
            round.InterruptForPause(14.999d);
            Assert.That(round.SpawnedGiftCount, Is.EqualTo(10));

            round.AdvanceTo(15d);
            round.AdvanceTo(15d);
            Assert.That(round.SpawnedGiftCount, Is.EqualTo(13));
            Assert.That(
                round.GetGift(12).State,
                Is.EqualTo(GiftGrabGiftState.Loose));
            Assert.That(
                round.GetGift(13).State,
                Is.EqualTo(GiftGrabGiftState.Unspawned));

            round.AdvanceTo(29.999d);
            Assert.That(round.SpawnedGiftCount, Is.EqualTo(13));
            round.AdvanceTo(30d);
            Assert.That(round.SpawnedGiftCount, Is.EqualTo(16));
            round.AdvanceTo(44.999d);
            Assert.That(round.SpawnedGiftCount, Is.EqualTo(16));
            round.AdvanceTo(45d);
            Assert.That(round.SpawnedGiftCount, Is.EqualTo(19));

            for (var giftId = 0;
                 giftId < GiftGrabRules.TotalGiftCount;
                 giftId++)
            {
                var slot = giftId % GiftGrabRules.PlayerCount;
                Assert.That(
                    round.ResolvePickup(slot, giftId, 45d).WasPickedUp,
                    Is.True);
                Assert.That(
                    round.ResolveDeposit(slot, 45d).WasDeposited,
                    Is.True);
            }

            Assert.That(TotalStoredGiftCount(round), Is.EqualTo(19));
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
            Assert.That(TotalStoredGiftCount(round), Is.EqualTo(19));
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
        public void ThrowAndPush_ApplyExactImmunityCooldownStunAndDrops()
        {
            var round = new GiftGrabRoundState(1);

            round.ResolvePickup(2, 2, 0d);
            round.ResolveThrow(2, 0d);
            var landed = round.ResolveGiftLanded(2, 0.05d);
            Assert.That(landed.WasLanded, Is.True);
            Assert.That(landed.PickupLockedPlayerSlot, Is.EqualTo(2));
            Assert.That(landed.PickupLockEndsAtSeconds, Is.EqualTo(0.3d));
            Assert.That(
                round.ResolvePickup(2, 2, 0.299d).Status,
                Is.EqualTo(GiftGrabPickupStatus.IgnoredRegrabLock));
            Assert.That(
                round.ResolvePickup(2, 2, 0.3d).WasPickedUp,
                Is.True);

            round.ResolvePickup(0, 0, 0.3d);
            round.ResolveThrow(0, 0.3d);
            Assert.That(
                round.ResolveThrownHit(0, 0, 0.549d).Status,
                Is.EqualTo(
                    GiftGrabThrownHitStatus.IgnoredThrowerImmunity));
            var exactBoundaryHit = round.ResolveThrownHit(0, 0, 0.55d);
            Assert.That(exactBoundaryHit.WasHit, Is.True);
            Assert.That(
                round.GetPlayer(0).StunnedUntilSeconds,
                Is.EqualTo(1.55d));

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
                Is.EqualTo(1));
            Assert.That(
                round.GetGift(3).PickupLockedPlayerSlot,
                Is.EqualTo(3));

            var missed = round.ResolvePush(3, -1, 0.6d);
            Assert.That(missed.Status, Is.EqualTo(GiftGrabPushStatus.Missed));
            Assert.That(missed.CooldownEndsAtSeconds, Is.EqualTo(1.25d));
            round.InterruptForPause(0.6d);
            Assert.That(
                round.ResolvePush(3, 2, 1.249d).Status,
                Is.EqualTo(GiftGrabPushStatus.IgnoredCooldown));

            var pushed = round.ResolvePush(3, 2, 1.25d);
            Assert.That(pushed.WasHit, Is.True);
            Assert.That(pushed.DroppedGiftId, Is.EqualTo(2));
            Assert.That(
                round.GetPlayer(2).StunnedUntilSeconds,
                Is.EqualTo(1.75d));
            Assert.That(
                round.GetGift(2).PickupLockEndsAtSeconds,
                Is.EqualTo(1.5d));

            Assert.That(
                round.ResolvePickup(1, 4, 1.599d).Status,
                Is.EqualTo(GiftGrabPickupStatus.IgnoredStunned));
            Assert.That(round.GetPlayer(1).CanMove, Is.False);
            Assert.That(
                round.ResolvePickup(1, 4, 1.6d).WasPickedUp,
                Is.True);
            Assert.That(
                round.GetPlayer(1).CurrentMoveSpeed,
                Is.EqualTo(GiftGrabRules.CarryMoveSpeed));
        }

        [Test]
        public void Scoring_UsesStoredThenGiftSecondsAndTwoRoundTieBreaks()
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
                Is.EqualTo(new[] { 1, 2, 0, 3 }));
            Assert.That(
                LeaderboardPoints(leaderboard),
                Is.EqualTo(new[] { 3, 3, 3, 3 }));
            Assert.That(
                TotalStoredCounts(leaderboard),
                Is.EqualTo(new[] { 13, 12, 12, 10 }));
            Assert.That(leaderboard[1].FinalRoundRank, Is.EqualTo(2));
            Assert.That(leaderboard[2].FinalRoundRank, Is.EqualTo(4));
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
