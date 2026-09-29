using System;
using MazeParty.Gameplay.Minigames;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class MinigameRewardRulesTests
    {
        [Test]
        public void FinalPlacementGold_UsesSharedEconomySchedule()
        {
            var cases = new[]
            {
                (Rank: 1, ExpectedGold: 10),
                (Rank: 2, ExpectedGold: 6),
                (Rank: 3, ExpectedGold: 3),
                (Rank: 4, ExpectedGold: 0)
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    MinigameRewardRules.GetFinalPlacementGold(testCase.Rank),
                    Is.EqualTo(testCase.ExpectedGold),
                    "Rank " + testCase.Rank);
            }
        }

        [Test]
        public void FinalPlacementGold_RejectsInvalidRank()
        {
            foreach (var rank in new[] { 0, 5 })
            {
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => MinigameRewardRules.GetFinalPlacementGold(rank),
                    "Rank " + rank);
            }
        }
    }
}
