using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardCommerceRulesTests
    {
        [Test]
        public void ItemShopStock_IsSeededAndEachOfferCanSellOnlyOnce()
        {
            var first = new ItemShopStock(1250);
            var repeated = new ItemShopStock(1250);

            for (var index = 0; index < ItemShopRules.OfferCount; index++)
            {
                Assert.That(PrototypeItemCatalog.IsValid(first.GetOffer(index)), Is.True);
                Assert.That(repeated.GetOffer(index), Is.EqualTo(first.GetOffer(index)));
                Assert.That(first.TrySell(index), Is.True);
                Assert.That(first.TrySell(index), Is.False);
            }

            Assert.That(first.IsSoldOut, Is.True);
        }

        [Test]
        public void ItemShopStock_RestorePreservesExactOffersAndSoldMask()
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
