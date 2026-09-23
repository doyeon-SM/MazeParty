using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class MatchAwardRulesTests
    {
        [Test]
        public void Progress_TracksPeakGrossGainsAndSuccessfulGameplayEvents()
        {
            var progress = new MatchAwardProgress();
            progress.Reset(10);

            progress.RecordGoldBalance(10, 25);
            progress.RecordGoldBalance(25, 4);
            progress.RecordGoldBalance(4, 12);
            progress.RecordMinigameLastPlace();
            progress.RecordItemUse();
            progress.RecordItemUse();
            progress.RecordDamageTaken(80);
            progress.RecordPlayerDamageDealt(50);

            var stats = progress.ToStats(3);
            Assert.That(stats.PeakGoldHeld, Is.EqualTo(25));
            Assert.That(stats.TotalGoldEarned, Is.EqualTo(23));
            Assert.That(stats.MinigameWins, Is.EqualTo(3));
            Assert.That(stats.MinigameLastPlaces, Is.EqualTo(1));
            Assert.That(stats.ItemUses, Is.EqualTo(2));
            Assert.That(stats.DamageTaken, Is.EqualTo(80));
            Assert.That(stats.PlayerDamageDealt, Is.EqualTo(50));
        }

        [Test]
        public void Progress_SaturatesAccumulatedValuesWithoutWrapping()
        {
            var progress = new MatchAwardProgress();
            progress.Reset(10);
            progress.RecordGoldBalance(0, int.MaxValue);
            progress.RecordGoldBalance(0, 1);
            progress.RecordDamageTaken(int.MaxValue);
            progress.RecordDamageTaken(1);
            progress.RecordPlayerDamageDealt(int.MaxValue);
            progress.RecordPlayerDamageDealt(1);

            var stats = progress.ToStats(0);
            Assert.That(stats.TotalGoldEarned, Is.EqualTo(int.MaxValue));
            Assert.That(stats.DamageTaken, Is.EqualTo(int.MaxValue));
            Assert.That(stats.PlayerDamageDealt, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void WinnerMask_AwardsEveryLeaderIncludingAllZeroFallback()
        {
            var players = new[]
            {
                Stats(peakGoldHeld: 20),
                Stats(peakGoldHeld: 50),
                Stats(peakGoldHeld: 50),
                Stats(peakGoldHeld: 35)
            };

            Assert.That(MatchAwardRules.TryGetWinnerMask(
                players,
                MatchAwardCategory.PeakGoldHeld,
                out var winnerMask,
                out var winningValue), Is.True);
            Assert.That(winnerMask, Is.EqualTo(0b0000_0110));
            Assert.That(winningValue, Is.EqualTo(50));

            Assert.That(MatchAwardRules.TryGetWinnerMask(
                players,
                MatchAwardCategory.ItemUses,
                out winnerMask,
                out winningValue), Is.True);
            Assert.That(winnerMask, Is.EqualTo(0b0000_1111));
            Assert.That(winningValue, Is.Zero);
        }

        [Test]
        public void CategorySelection_FillsSecondAwardFromZeroValueFallback()
        {
            var players = new[]
            {
                Stats(peakGoldHeld: 10),
                Stats(peakGoldHeld: 10),
                Stats(peakGoldHeld: 10),
                Stats(peakGoldHeld: 10)
            };

            Assert.That(MatchAwardRules.TrySelectTwoDistinctCategories(
                players,
                44,
                out var first,
                out var second), Is.True);
            Assert.That(first, Is.EqualTo(MatchAwardCategory.PeakGoldHeld));
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(MatchAwardRules.TryGetWinnerMask(
                players,
                second,
                out var fallbackWinners,
                out var fallbackValue), Is.True);
            Assert.That(fallbackWinners, Is.EqualTo(0b0000_1111));
            Assert.That(fallbackValue, Is.Zero);
        }

        [Test]
        public void CategorySelection_IsSeededDistinctAndExcludesZeroCategories()
        {
            var players = new[]
            {
                Stats(peakGoldHeld: 10, itemUses: 1),
                Stats(peakGoldHeld: 10),
                Stats(peakGoldHeld: 10),
                Stats(peakGoldHeld: 10)
            };

            Assert.That(MatchAwardRules.TrySelectTwoDistinctCategories(
                players,
                9125,
                out var first,
                out var second), Is.True);
            Assert.That(first, Is.Not.EqualTo(second));
            CollectionAssert.AreEquivalent(
                new[]
                {
                    MatchAwardCategory.PeakGoldHeld,
                    MatchAwardCategory.ItemUses
                },
                new[] { first, second });

            Assert.That(MatchAwardRules.TrySelectTwoDistinctCategories(
                players,
                9125,
                out var repeatedFirst,
                out var repeatedSecond), Is.True);
            Assert.That(repeatedFirst, Is.EqualTo(first));
            Assert.That(repeatedSecond, Is.EqualTo(second));
        }

        [TestCase(MatchAwardCategory.PeakGoldHeld, 1, "MOST GOLD HELD")]
        [TestCase(MatchAwardCategory.TotalGoldEarned, 2, "MOST GOLD EARNED")]
        [TestCase(MatchAwardCategory.MinigameWins, 3, "MOST MINIGAME WINS")]
        [TestCase(MatchAwardCategory.MinigameLastPlaces, 4, "MOST MINIGAME LAST PLACES")]
        [TestCase(MatchAwardCategory.ItemUses, 5, "MOST ITEMS USED")]
        [TestCase(MatchAwardCategory.DamageTaken, 6, "MOST DAMAGE TAKEN")]
        [TestCase(MatchAwardCategory.PlayerDamageDealt, 7, "MOST PLAYER DAMAGE DEALT")]
        public void CategoryMapping_ProvidesValueAndDisplayContract(
            MatchAwardCategory category,
            int expectedValue,
            string expectedDisplayName)
        {
            var stats = new MatchAwardStats(1, 2, 3, 4, 5, 6, 7);

            Assert.That(
                MatchAwardRules.GetValue(stats, category),
                Is.EqualTo(expectedValue));
            Assert.That(
                MatchAwardRules.GetDisplayName(category),
                Is.EqualTo(expectedDisplayName));
        }

        [TestCase(DamageKind.Item, true, false, true)]
        [TestCase(DamageKind.Item, true, true, false)]
        [TestCase(DamageKind.Item, false, false, false)]
        [TestCase(DamageKind.Environment, true, false, false)]
        public void PlayerDamageCredit_ExcludesSelfAndEnvironment(
            DamageKind damageKind,
            bool hasPlayerSource,
            bool isSelfDamage,
            bool expected)
        {
            Assert.That(MatchAwardRules.CountsAsPlayerDamage(
                damageKind,
                hasPlayerSource,
                isSelfDamage), Is.EqualTo(expected));
        }

        [TestCase(100, 20, 20)]
        [TestCase(20, 80, 20)]
        [TestCase(0, 80, 0)]
        [TestCase(100, -1, 0)]
        public void AppliedDamage_UsesActualHealthLoss(
            int currentHealth,
            int requestedDamage,
            int expected)
        {
            Assert.That(MatchAwardRules.GetAppliedDamageAmount(
                currentHealth,
                requestedDamage), Is.EqualTo(expected));
        }

        private static MatchAwardStats Stats(
            int peakGoldHeld = 0,
            int totalGoldEarned = 0,
            int minigameWins = 0,
            int minigameLastPlaces = 0,
            int itemUses = 0,
            int damageTaken = 0,
            int playerDamageDealt = 0)
        {
            return new MatchAwardStats(
                peakGoldHeld,
                totalGoldEarned,
                minigameWins,
                minigameLastPlaces,
                itemUses,
                damageTaken,
                playerDamageDealt);
        }
    }
}
