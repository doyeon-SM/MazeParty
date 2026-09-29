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
                    new PlayerPlacement(2, 3),
                    new PlayerPlacement(3, 4)
                },
                PlayerPlacementValidationError.DuplicateRank)
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
