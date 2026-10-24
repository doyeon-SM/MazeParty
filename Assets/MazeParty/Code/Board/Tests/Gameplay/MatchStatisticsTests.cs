using System;
using MazeParty.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class MatchStatisticsTests
    {
        [Test]
        public void Events_PreserveEveryChangeWithinTurn_AndFreezeRejectsFurtherMutations()
        {
            var data = MatchStatistics.Begin(10);
            data.RecordBalance(0, 0, 0, 10, 0);
            data.RecordBalance(0, 1, 1, 0, 0);
            data.RecordBalance(0, 1, 0, 15, 15);
            data.RecordBalance(0, 1, 0, 15, 15);
            data.RecordBalance(0, 1, 2, 5, 15);
            Assert.That(data.balances.Count, Is.EqualTo(4));
            CollectionAssert.AreEqual(new[] { 0, 1, 0, 2 }, data.balances.ConvertAll(x => x.keys));
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, data.balances.ConvertAll(x => x.sequence));
            foreach (var player in data.players) player.rank = 1;
            data.frozen = true;
            data.RecordBalance(0, 2, 8, 100, 100);
            data.RecordDamage(0, 1, 2, 5, 20);
            data.RecordPlacement(0, 2, 1);
            Assert.That(data.balances.Count, Is.EqualTo(4));
            Assert.That(data.damage, Is.Empty);
            Assert.That(data.placements, Is.All.Zero);
        }

        [Test]
        public void PlacementSettlement_CountsTiesAndAllRanks_OncePerTurn()
        {
            var data = MatchStatistics.Begin(10);
            var ranks = new[] { 1, 2, 2, 4 };
            for (var slot = 0; slot < 4; slot++)
            {
                data.RecordPlacement(slot, 1, ranks[slot]);
                data.RecordPlacement(slot, 1, ranks[slot]);
                Assert.That(data.placements[slot * 4 + ranks[slot] - 1], Is.EqualTo(1));
            }
            data.RecordPlacement(0, 2, 3);
            Assert.That(data.placements[2], Is.EqualTo(1));
            Assert.That(data.IsValid(), Is.True);
        }

        [Test]
        public void Recovery_RoundTripsAndRemapsBothDamageParticipantsAndMovement()
        {
            var data = MatchStatistics.Begin(10);
            data.RecordBalance(0, 0, 0, 10, 0);
            data.RecordBalance(2, 1, 3, 20, 30);
            data.RecordPlacement(2, 1, 1);
            data.RecordDamage(2, 0, 1, 5, 50);
            data.RecordDamage(-1, 2, 1, 0, 10);
            data.players[2].moves = 24;
            Assert.That(MatchStatistics.TryDecode(JsonUtility.ToJson(data), out var restored), Is.True);
            restored.RemapSlots(new[] { 3, 1, 0, 2 });
            Assert.That(restored.balances[1].slot, Is.Zero);
            Assert.That(restored.placements[0], Is.EqualTo(1));
            Assert.That(restored.players[0].moves, Is.EqualTo(24));
            Assert.That(restored.damage[0].source, Is.Zero);
            Assert.That(restored.damage[0].target, Is.EqualTo(3));
            Assert.That(restored.damage[1].source, Is.EqualTo(-1));
            Assert.That(restored.damage[1].target, Is.Zero);
            Assert.That(restored.IsValid(), Is.True);
            Assert.Throws<ArgumentException>(() => restored.RemapSlots(new[] { 0, 0, 2, 3 }));
        }

        [Test]
        public void CorruptHistory_IsRejectedInsteadOfRestoredAsInventedData()
        {
            foreach (var corrupt in new Action<MatchStatistics>[] {
                x => x.players = null,
                x => x.placements[0] = -1,
                x => x.placements[0] = 11,
                x => x.damage.Add(new StatisticsDamage { source = 4, target = 0, turn = 1, amount = 20 }),
                x => x.balances.Add(new StatisticsBalance { slot = 0, turn = 30, sequence = 1 }),
                x => x.balances.Add(new StatisticsBalance { slot = 0, turn = 1, sequence = -1 }),
                x => x.version = 999 })
            {
                var data = MatchStatistics.Begin(10); corrupt(data);
                Assert.That(MatchStatistics.TryDecode(JsonUtility.ToJson(data), out _), Is.False);
            }
        }

        [Test]
        public void Recovery_RejectsFutureHistoryAndFrozenCeremonyAtEarlierCheckpoints()
        {
            var data = MatchStatistics.Begin(10);
            data.RecordBalance(0, 4, 1, 20, 30);
            data.RecordDamage(0, 1, 4, 5, 20);
            data.RecordPlacement(0, 4, 1);
            Assert.That(data.FitsCheckpoint(4, 10), Is.True);
            Assert.That(data.FitsCheckpoint(3, 10), Is.False);
            Assert.That(data.FitsCheckpoint(4, 15), Is.False);
            data.frozen = true;
            Assert.That(data.FitsCheckpoint(4, 10), Is.False);
        }
    }
}
