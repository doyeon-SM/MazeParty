using NUnit.Framework;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MinigameLocalPlayerHighlightTests
    {
        [Test]
        public void WhiteOutline_IsLimitedToFirstTwoCountdownSeconds()
        {
            Assert.That(MinigameLocalPlayerHighlight.IsHighlightWindow(true, 3d),
                Is.True);
            Assert.That(MinigameLocalPlayerHighlight.IsHighlightWindow(true, 3.2d),
                Is.True);
            Assert.That(MinigameLocalPlayerHighlight.IsHighlightWindow(true, 2d),
                Is.True);
            Assert.That(MinigameLocalPlayerHighlight.IsHighlightWindow(true, 1.001d),
                Is.True);
            Assert.That(MinigameLocalPlayerHighlight.IsHighlightWindow(true, 1d),
                Is.False);
            Assert.That(MinigameLocalPlayerHighlight.IsHighlightWindow(true, 0d),
                Is.False);
            Assert.That(MinigameLocalPlayerHighlight.IsHighlightWindow(false, 3d),
                Is.False);
        }
    }
}
