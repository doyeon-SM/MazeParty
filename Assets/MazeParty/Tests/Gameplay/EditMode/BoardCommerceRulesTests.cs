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
        }
    }
}
