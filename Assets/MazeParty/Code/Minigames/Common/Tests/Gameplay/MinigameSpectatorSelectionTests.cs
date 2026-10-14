using MazeParty.Gameplay.Minigames;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class MinigameSpectatorSelectionTests
    {
        [Test]
        public void TargetLocksUntilEliminatedThenChoosesCurrentLeader()
        {
            var watchable = new[] { false, true, true, true };
            var progress = new[] { 0f, 12f, 8f, 4f };
            var target = MinigameSpectatorSelection.ChooseTarget(
                -1, watchable, progress);
            Assert.That(target, Is.EqualTo(1));

            progress[2] = 20f;
            Assert.That(MinigameSpectatorSelection.ChooseTarget(
                target, watchable, progress), Is.EqualTo(1));

            watchable[1] = false;
            Assert.That(MinigameSpectatorSelection.ChooseTarget(
                target, watchable, progress), Is.EqualTo(2));
        }
    }
}
