using System;
using MazeParty.Gameplay.Minigames;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class MinigameRewardRulesTests
    {
        [TestCase(1, 10)]
        [TestCase(2, 6)]
        [TestCase(3, 3)]
        [TestCase(4, 0)]
        public void FinalPlacementGold_UsesSharedEconomySchedule(
            int rank,
            int expectedGold)
        {
            Assert.That(
                MinigameRewardRules.GetFinalPlacementGold(rank),
                Is.EqualTo(expectedGold));
        }

        [TestCase(0)]
        [TestCase(5)]
        public void FinalPlacementGold_RejectsInvalidRank(int rank)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MinigameRewardRules.GetFinalPlacementGold(rank));
        }
    }
}
