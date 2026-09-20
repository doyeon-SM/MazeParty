using System.Linq;
using MazeParty.Gameplay.Minigames.Race;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class RaceRulesTests
    {
        [TestCase("aad", 2)]
        [TestCase("ada", 3)]
        [TestCase("dddd", 1)]
        public void Alternation_CountsOnlyValidNewDirectionChanges(
            string inputs,
            int expectedSteps)
        {
            var previous = RaceStepInput.None;
            var steps = 0;
            foreach (var inputCharacter in inputs)
            {
                var input = inputCharacter == 'a'
                    ? RaceStepInput.Left
                    : RaceStepInput.Right;
                if (!RaceRules.IsAlternatingStep(previous, input))
                {
                    continue;
                }
                previous = input;
                steps++;
            }

            Assert.That(steps, Is.EqualTo(expectedSteps));
        }

        [Test]
        public void RoundLeaderboard_UsesProgressThenServerProcessingOrder()
        {
            var leaderboard = RaceRules.BuildRoundLeaderboard(
                new[] { 250, 400, 400, 120 },
                new ulong[] { 8, 15, 12, 7 });

            Assert.That(
                leaderboard.Select(entry => entry.PlayerSlot),
                Is.EqualTo(new[] { 2, 1, 0, 3 }));
            Assert.That(
                RaceRules.BuildRoundPoints(leaderboard),
                Is.EqualTo(new[] { 1, 2, 3, 0 }));
        }

        [Test]
        public void FinalLeaderboard_UsesTotalPointsThenStableSlotOrder()
        {
            var leaderboard = RaceRules.BuildFinalLeaderboard(
                new[] { 4, 7, 7, 1 });

            Assert.That(
                leaderboard.Select(entry => entry.PlayerSlot),
                Is.EqualTo(new[] { 1, 2, 0, 3 }));
            Assert.That(
                leaderboard.Select(entry => entry.Rank),
                Is.EqualTo(new[] { 1, 2, 3, 4 }));
        }
    }
}
