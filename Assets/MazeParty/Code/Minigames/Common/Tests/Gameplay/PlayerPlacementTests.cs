using MazeParty.Gameplay.Minigames;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class PlayerPlacementTests
    {
        [Test]
        public void TryCreate_NormalizesACompletePlacementSetByPlayerSlot()
        {
            var placements = new[]
            {
                new PlayerPlacement(2, 1),
                new PlayerPlacement(0, 4),
                new PlayerPlacement(3, 2),
                new PlayerPlacement(1, 3)
            };

            Assert.That(PlayerPlacementSet.TryCreate(
                placements,
                out var set,
                out var error), Is.True);
            Assert.That(error, Is.EqualTo(PlayerPlacementValidationError.None));
            Assert.That(set.Count, Is.EqualTo(4));
            Assert.That(set.GetRankForSlot(0), Is.EqualTo(4));
            Assert.That(set.GetRankForSlot(1), Is.EqualTo(3));
            Assert.That(set.GetRankForSlot(2), Is.EqualTo(1));
            Assert.That(set.GetRankForSlot(3), Is.EqualTo(2));
        }

        [Test]
        public void TiedScores_UseCompetitionRanksAndTheFullSharedReward()
        {
            var cases = new[] { new[] { 9, 6, 6, 2 }, new[] { 0, 0, 0, 0 }, new[] { 9, 9, 2, 2 } };
            var expected = new[] { new[] { 1, 2, 2, 4 }, new[] { 1, 1, 1, 1 }, new[] { 1, 1, 3, 3 } };
            var rewards = new[] { new[] { 10, 6, 6, 0 }, new[] { 10, 10, 10, 10 }, new[] { 10, 10, 3, 3 } };
            for (var c = 0; c < cases.Length; c++)
            {
                var placements = new PlayerPlacement[4];
                for (var slot = 0; slot < 4; slot++)
                    placements[slot] = new PlayerPlacement(slot, CompetitionRanking.Rank(cases[c], slot));
                Assert.That(PlayerPlacementSet.TryCreate(placements, out var set, out _), Is.True);
                for (var slot = 0; slot < 4; slot++)
                {
                    Assert.That(set.GetRankForSlot(slot), Is.EqualTo(expected[c][slot]));
                    Assert.That(MinigameRewardRules.GetFinalPlacementGold(set.GetRankForSlot(slot)), Is.EqualTo(rewards[c][slot]));
                }
            }
        }

        [Test]
        public void TryCreate_RejectsMalformedFinalPlacements()
        {
            (PlayerPlacement[] Placements,
                PlayerPlacementValidationError ExpectedError)[] cases =
            {
                (null, PlayerPlacementValidationError.MissingPlacements),
                (new[]
                {
                    new PlayerPlacement(0, 1),
                    new PlayerPlacement(1, 2),
                    new PlayerPlacement(2, 3)
                },
                PlayerPlacementValidationError.IncorrectPlacementCount),
                (new[]
                {
                    new PlayerPlacement(4, 1),
                    new PlayerPlacement(1, 2),
                    new PlayerPlacement(2, 3),
                    new PlayerPlacement(3, 4)
                },
                PlayerPlacementValidationError.PlayerSlotOutOfRange),
                (new[]
                {
                    new PlayerPlacement(0, 0),
                    new PlayerPlacement(1, 2),
                    new PlayerPlacement(2, 3),
                    new PlayerPlacement(3, 4)
                },
                PlayerPlacementValidationError.RankOutOfRange),
                (new[]
                {
                    new PlayerPlacement(0, 1),
                    new PlayerPlacement(0, 2),
                    new PlayerPlacement(2, 3),
                    new PlayerPlacement(3, 4)
                },
                PlayerPlacementValidationError.DuplicatePlayerSlot),
                (new[]
                {
                    new PlayerPlacement(0, 1),
                    new PlayerPlacement(1, 1),
                    new PlayerPlacement(2, 2),
                    new PlayerPlacement(3, 4)
                },
                PlayerPlacementValidationError.InvalidCompetitionRank)
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    PlayerPlacementSet.TryCreate(
                        testCase.Placements,
                        out _,
                        out var error),
                    Is.False,
                    testCase.ExpectedError.ToString());
                Assert.That(
                    error,
                    Is.EqualTo(testCase.ExpectedError),
                    testCase.ExpectedError.ToString());
            }
        }
    }
}
