using System.Linq;
using MazeParty.Gameplay.Minigames.Race;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class RaceRulesTests
    {
        [Test]
        public void Alternation_CountsOnlyValidNewDirectionChanges()
        {
            var cases = new[]
            {
                (Inputs: "aad", ExpectedSteps: 2),
                (Inputs: "ada", ExpectedSteps: 3),
                (Inputs: "dddd", ExpectedSteps: 1)
            };

            foreach (var testCase in cases)
            {
                var previous = RaceStepInput.None;
                var steps = 0;
                foreach (var inputCharacter in testCase.Inputs)
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

                Assert.That(
                    steps,
                    Is.EqualTo(testCase.ExpectedSteps),
                    testCase.Inputs);
            }
        }

        [Test]
        public void RoundLeaderboard_UsesProgressThenServerProcessingOrder()
        {
            var leaderboard = RaceRules.BuildRoundLeaderboard(
                new[] { 100, 160, 160, 48 },
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
