using MazeParty.Gameplay.Minigames;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class MinigameSpectatorSelectionTests
    {
        [Test]
        public void ManualCycleWrapsSkipsUnavailableAndRetainsOnlySurvivor()
        {
            var targets = new[] { false, true, false, true };
            foreach (var pair in new[] { new[] { -1, 1 }, new[] { 1, 3 }, new[] { 3, 1 } })
                Assert.That(MinigameSpectatorSelection.NextTarget(pair[0], targets), Is.EqualTo(pair[1]));
            targets[3] = false;
            Assert.That(MinigameSpectatorSelection.NextTarget(1, targets), Is.EqualTo(1));
            targets[1] = false;
            Assert.That(MinigameSpectatorSelection.NextTarget(1, targets), Is.EqualTo(-1));
            Assert.That(MinigameSpectatorSelection.NextTarget(-1, new bool[0]), Is.EqualTo(-1));
        }

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
