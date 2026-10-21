using NUnit.Framework;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MinigameServerRulesTests
    {
        [Test]
        public void EpochRolloverRejectsUninitializedAndStaleInputs()
        {
            foreach (var before in new[] { 0U, 1U, 416U, uint.MaxValue })
            {
                var current = MinigameServerRules.NextInputEpoch(before);
                Assert.That(MinigameServerRules.IsCurrentInputEpoch(0U, current), Is.False);
                Assert.That(MinigameServerRules.IsCurrentInputEpoch(before, current), Is.False);
                Assert.That(MinigameServerRules.IsCurrentInputEpoch(current, current), Is.True);
            }
        }

        [Test]
        public void PhaseClockPreservesPauseAndExactDeadlineBoundaries()
        {
            Assert.That(MinigameServerRules.Remaining(10d, 4d, false, false, 9.999d), Is.GreaterThan(0d));
            Assert.That(MinigameServerRules.Remaining(10d, 4d, false, false, 10d), Is.Zero);
            Assert.That(MinigameServerRules.Remaining(10d, 4d, false, false, 11d), Is.Zero);
            Assert.That(MinigameServerRules.Remaining(10d, 4d, false, true, 100d), Is.EqualTo(4d));
            Assert.That(MinigameServerRules.Remaining(104d, 4d, false, false, 100d), Is.EqualTo(4d));
            Assert.That(MinigameServerRules.Remaining(104d, 4d, true, true, 100d), Is.Zero);
        }
    }
}
