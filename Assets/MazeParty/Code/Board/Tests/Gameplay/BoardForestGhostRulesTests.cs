using System;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardForestGhostRulesTests
    {
        [Test]
        public void WeightedBucketsAndExcludedSeats_PreserveEachTwentyPercentChoice()
        {
            foreach (var turn in new[] { 1, 4, 5, 10, 15, 20 })
            {
                var counts = new int[3, 2];
                for (var roll = 0; roll < 100; roll++)
                    for (var give = 0; give < 2; give++)
                    {
                        var choice = BoardForestGhostRules.ResolveRolls(turn, 0, roll, give == 0, 15);
                        counts[(int)choice.Resource, give]++;
                        var strong = turn % 5 == 0;
                        var amount = choice.Resource == ForestGhostResource.Gold ? (strong ? 20 : 10) :
                            choice.Resource == ForestGhostResource.Health ? (strong ? 50 : 30) : 1;
                        Assert.That(choice.Amount, Is.EqualTo(give == 0 ? amount : -amount));
                    }
                var expected = turn % 5 == 0 ? new[] { 50, 30, 20 } : new[] { 60, 30, 10 };
                for (var kind = 0; kind < 3; kind++)
                    for (var sign = 0; sign < 2; sign++) Assert.That(counts[kind, sign], Is.EqualTo(expected[kind]));
            }
            for (byte mask = 0; mask < 16; mask++)
                for (var target = 0; target < 5; target++)
                    Assert.That(BoardForestGhostRules.ResolveRolls(1, target, 0, true, mask).Slot,
                        Is.EqualTo(target < 4 && (mask & (1 << target)) != 0 ? target : -1));
        }

        [Test]
        public void SeedAndTurn_ReproduceOutcomeAndStableEmptyTileIndex()
        {
            for (var seed = 1; seed < 100; seed++)
                for (var turn = 1; turn <= 20; turn++)
                {
                    var first = BoardForestGhostRules.Resolve(seed, turn, 14, 12);
                    var again = BoardForestGhostRules.Resolve(seed, turn, 14, 12);
                    Assert.That(again, Is.EqualTo(first));
                    Assert.That(first.EmptyIndex, Is.InRange(0, 11));
                    Assert.That(first.Slot, Is.Not.EqualTo(0));
                    Assert.That(first.Strong, Is.EqualTo(turn % 5 == 0));
                }
            Assert.That(BoardForestGhostRules.Resolve(1, 1, 0, 0).EmptyIndex, Is.EqualTo(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => BoardForestGhostRules.Resolve(1, 0, 15, 2));
        }
    }
}
