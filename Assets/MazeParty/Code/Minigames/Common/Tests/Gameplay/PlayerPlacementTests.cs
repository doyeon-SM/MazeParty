using System.Collections;
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

        [TestCaseSource(nameof(InvalidPlacementCases))]
        public void TryCreate_RejectsMalformedFinalPlacements(
            PlayerPlacement[] placements,
            PlayerPlacementValidationError expectedError)
        {
            Assert.That(PlayerPlacementSet.TryCreate(
                placements,
                out _,
                out var error), Is.False);
            Assert.That(error, Is.EqualTo(expectedError));
        }

        private static IEnumerable InvalidPlacementCases()
        {
            yield return new TestCaseData(
                null,
                PlayerPlacementValidationError.MissingPlacements);
            yield return new TestCaseData(
                new[]
                {
                    new PlayerPlacement(0, 1),
                    new PlayerPlacement(1, 2),
                    new PlayerPlacement(2, 3)
                },
                PlayerPlacementValidationError.IncorrectPlacementCount);
            yield return new TestCaseData(
                new[]
                {
                    new PlayerPlacement(4, 1),
                    new PlayerPlacement(1, 2),
                    new PlayerPlacement(2, 3),
                    new PlayerPlacement(3, 4)
                },
                PlayerPlacementValidationError.PlayerSlotOutOfRange);
            yield return new TestCaseData(
                new[]
                {
                    new PlayerPlacement(0, 0),
                    new PlayerPlacement(1, 2),
                    new PlayerPlacement(2, 3),
                    new PlayerPlacement(3, 4)
                },
                PlayerPlacementValidationError.RankOutOfRange);
            yield return new TestCaseData(
                new[]
                {
                    new PlayerPlacement(0, 1),
                    new PlayerPlacement(0, 2),
                    new PlayerPlacement(2, 3),
                    new PlayerPlacement(3, 4)
                },
                PlayerPlacementValidationError.DuplicatePlayerSlot);
            yield return new TestCaseData(
                new[]
                {
                    new PlayerPlacement(0, 1),
                    new PlayerPlacement(1, 1),
                    new PlayerPlacement(2, 3),
                    new PlayerPlacement(3, 4)
                },
                PlayerPlacementValidationError.DuplicateRank);
        }
    }
}
