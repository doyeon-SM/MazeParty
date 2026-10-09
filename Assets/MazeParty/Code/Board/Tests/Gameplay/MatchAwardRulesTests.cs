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

        [Test]
        public void CategoryValues_MapToTheMatchingAwardStatistic()
        {
            var stats = new MatchAwardStats(1, 2, 3, 4, 5, 6, 7);
            var cases = new[]
            {
                (MatchAwardCategory.PeakGoldHeld, 1),
                (MatchAwardCategory.TotalGoldEarned, 2),
                (MatchAwardCategory.MinigameWins, 3),
                (MatchAwardCategory.MinigameLastPlaces, 4),
                (MatchAwardCategory.ItemUses, 5),
                (MatchAwardCategory.DamageTaken, 6),
                (MatchAwardCategory.PlayerDamageDealt, 7)
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    MatchAwardRules.GetValue(stats, testCase.Item1),
                    Is.EqualTo(testCase.Item2),
                    testCase.Item1.ToString());
            }
        }

        [Test]
        public void PlayerDamageCredit_ExcludesSelfAndEnvironment()
        {
            var cases = new[]
            {
                (DamageKind.Item, true, false, true),
                (DamageKind.Item, true, true, false),
                (DamageKind.Item, false, false, false),
                (DamageKind.Environment, true, false, false)
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    MatchAwardRules.CountsAsPlayerDamage(
                        testCase.Item1,
                        testCase.Item2,
                        testCase.Item3),
                    Is.EqualTo(testCase.Item4),
                    testCase.ToString());
            }
        }

        [Test]
        public void AppliedDamage_UsesActualHealthLoss()
        {
            var cases = new[]
            {
                (CurrentHealth: 100, RequestedDamage: 20, Expected: 20),
                (CurrentHealth: 20, RequestedDamage: 80, Expected: 20),
                (CurrentHealth: 0, RequestedDamage: 80, Expected: 0),
                (CurrentHealth: 100, RequestedDamage: -1, Expected: 0)
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    MatchAwardRules.GetAppliedDamageAmount(
                        testCase.CurrentHealth,
                        testCase.RequestedDamage),
                    Is.EqualTo(testCase.Expected),
                    testCase.ToString());
            }
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
