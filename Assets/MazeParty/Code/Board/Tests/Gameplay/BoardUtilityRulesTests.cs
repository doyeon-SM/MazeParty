using MazeParty.Gameplay;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardUtilityRulesTests
    {
        [Test]
        public void SwapChannel_RespectsAuthorityTimePauseAndCancellationBoundaries()
        {
            var cast = new BoardSwapChannel();
            Assert.That(cast.Begin(0, 0, 2), Is.False);
            Assert.That(cast.Begin(0, 4, 2), Is.False);
            Assert.That(cast.Begin(0, 1, 2), Is.True);
            Assert.That(cast.Begin(0, 2, 2), Is.False);
            Assert.That(cast.Tick(1.999, false, true, true), Is.EqualTo(BoardSwapProgress.Casting));
            var remaining = cast.Remaining;
            Assert.That(cast.Tick(60, true, true, true), Is.EqualTo(BoardSwapProgress.Casting));
            Assert.That(cast.Remaining, Is.EqualTo(remaining));
            Assert.That(cast.InterruptByDamage(0), Is.False, "Blocked damage must not cancel.");
            Assert.That(cast.Tick(.001, false, true, true), Is.EqualTo(BoardSwapProgress.Completed));
            Assert.That(cast.Tick(1, false, true, true), Is.EqualTo(BoardSwapProgress.Idle), "Completion is one-shot.");
            Assert.That(cast.Begin(0, 1, 2), Is.True);
            Assert.That(cast.InterruptByDamage(1), Is.True);
            Assert.That(cast.Active, Is.False);
            foreach (bool targetAvailable in new[] { true, false })
            {
                cast.Begin(0, 1, 2);
                Assert.That(cast.Tick(0, false, !targetAvailable, targetAvailable), Is.EqualTo(BoardSwapProgress.Cancelled));
                Assert.That(cast.Active, Is.False);
            }
        }
    }
}
