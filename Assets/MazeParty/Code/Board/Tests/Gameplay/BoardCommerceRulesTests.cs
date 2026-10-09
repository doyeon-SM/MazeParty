using System.Collections.Generic;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardCommerceRulesTests
    {
        [Test]
        public void ItemShopStock_IsUniqueDeterministicAndEachOfferSellsOnce()
        {
            var seeds = new[]
            {
                int.MinValue,
                -1,
                0,
                1,
                1250,
                int.MaxValue
            };

            foreach (var seed in seeds)
            {
                var first = new ItemShopStock(seed);
                var repeated = new ItemShopStock(seed);
                var uniqueOffers = new HashSet<PrototypeItemId>();

                for (var index = 0; index < ItemShopRules.OfferCount; index++)
                {
                    var offer = first.GetOffer(index);
                    Assert.That(PrototypeItemCatalog.IsValid(offer), Is.True);
                    Assert.That(
                        uniqueOffers.Add(offer),
                        Is.True,
                        "seed=" + seed + ", index=" + index);
                    Assert.That(
                        repeated.GetOffer(index),
                        Is.EqualTo(offer),
                        "seed=" + seed + ", index=" + index);
                    Assert.That(first.TrySell(index), Is.True);
                    Assert.That(first.TrySell(index), Is.False);
                }

                Assert.That(uniqueOffers.Count, Is.EqualTo(ItemShopRules.OfferCount));
                Assert.That(first.IsSoldOut, Is.True);
            }
        }

        [Test]
        public void ItemShopStock_RestorePreservesUniqueOffersAndSoldMaskExactly()
        {
            var offers = new[]
            {
                PrototypeItemId.DoubleDice,
                PrototypeItemId.Pistol,
                PrototypeItemId.Sniper,
                PrototypeItemId.Grenade,
                PrototypeItemId.Cloak
            };

            var restored = ItemShopStock.Restore(offers, 0b10101);

            for (var index = 0; index < offers.Length; index++)
            {
                Assert.That(restored.GetOffer(index), Is.EqualTo(offers[index]));
                Assert.That(restored.IsSold(index), Is.EqualTo((0b10101 & (1 << index)) != 0));
            }
        }

        [Test]
        public void ItemShopStock_RestoreRepairsDuplicatesDeterministicallyWithoutMovingSoldBits()
        {
            var offers = new[]
            {
                PrototypeItemId.DoubleDice,
                PrototypeItemId.Pistol,
                PrototypeItemId.DoubleDice,
                PrototypeItemId.Grenade,
                PrototypeItemId.Pistol
            };

            const byte soldMask = 0b10101;
            const int seed = 731;
            var first = ItemShopStock.Restore(offers, soldMask, seed);
            var repeated = ItemShopStock.Restore(offers, soldMask, seed);
            var uniqueOffers = new HashSet<PrototypeItemId>();

            Assert.That(first.Seed, Is.EqualTo(seed));
            Assert.That(first.GetOffer(0), Is.EqualTo(PrototypeItemId.DoubleDice));
            Assert.That(first.GetOffer(1), Is.EqualTo(PrototypeItemId.Pistol));
            Assert.That(first.GetOffer(3), Is.EqualTo(PrototypeItemId.Grenade));

            for (var index = 0; index < ItemShopRules.OfferCount; index++)
            {
                var offer = first.GetOffer(index);
                Assert.That(PrototypeItemCatalog.IsValid(offer), Is.True);
                Assert.That(uniqueOffers.Add(offer), Is.True, "index=" + index);
                Assert.That(repeated.GetOffer(index), Is.EqualTo(offer));
                Assert.That(
                    first.IsSold(index),
                    Is.EqualTo((soldMask & (1 << index)) != 0),
                    "index=" + index);
            }

            Assert.That(first.SoldMask, Is.EqualTo(soldMask));
        }

        [Test]
        public void Ranking_UsesKeysGoldWinsAndCompetitionRanksForTies()
        {
            var ordered = PlayerRankingRules.Calculate(new[]
            {
                new PlayerRankingStats(1, 0, 0),
                new PlayerRankingStats(0, 100, 100),
                new PlayerRankingStats(0, 10, 5),
                new PlayerRankingStats(0, 10, 1)
            });
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, ordered);

            var tied = PlayerRankingRules.Calculate(new[]
            {
                new PlayerRankingStats(2, 10, 0),
                new PlayerRankingStats(2, 10, 0),
                new PlayerRankingStats(1, 100, 9),
                new PlayerRankingStats(0, 500, 20)
            });
            CollectionAssert.AreEqual(new[] { 1, 1, 3, 4 }, tied);

            var middleTied = PlayerRankingRules.Calculate(new[]
            {
                new PlayerRankingStats(3, 0, 0),
                new PlayerRankingStats(2, 10, 1),
                new PlayerRankingStats(2, 10, 1),
                new PlayerRankingStats(1, 100, 9)
            });
            CollectionAssert.AreEqual(new[] { 1, 2, 2, 4 }, middleTied);

            var allTied = PlayerRankingRules.Calculate(new[]
            {
                new PlayerRankingStats(1, 10, 2),
                new PlayerRankingStats(1, 10, 2),
                new PlayerRankingStats(1, 10, 2),
                new PlayerRankingStats(1, 10, 2)
            });
            CollectionAssert.AreEqual(new[] { 1, 1, 1, 1 }, allTied);
        }
    }
}
