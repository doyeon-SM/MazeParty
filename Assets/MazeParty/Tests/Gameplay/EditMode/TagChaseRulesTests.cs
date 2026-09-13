using System.Linq;
using MazeParty.Gameplay.Minigames.TagChase;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class TagChaseRulesTests
    {
        [Test]
        public void TaggerOrder_IsSeededDeterministicPermutation()
        {
            var first = TagChaseRules.BuildTaggerOrder(987654321UL);
            var repeated = TagChaseRules.BuildTaggerOrder(987654321UL);

            Assert.That(first, Is.EqualTo(repeated));
            Assert.That(first, Has.Length.EqualTo(TagChaseRules.RoundCount));
            Assert.That(
                first.OrderBy(slot => slot),
                Is.EqualTo(new[] { 0, 1, 2, 3 }));
        }

        [Test]
        public void RoundScoring_DistinguishesTaggerWinCaughtAndSurvival()
        {
            var taggerWin = TagChaseRules.BuildRoundPoints(
                0,
                0b1110);
            Assert.That(taggerWin, Is.EqualTo(new[] { 3, 0, 0, 0 }));

            var runnersWin = TagChaseRules.BuildRoundPoints(
                0,
                0b0010);
            Assert.That(runnersWin, Is.EqualTo(new[] { 0, 1, 2, 2 }));
        }

        [Test]
        public void FinalLeaderboard_UsesScoreThenStableSlotTieBreak()
        {
            var leaderboard =
                TagChaseRules.BuildFinalLeaderboard(
                    new[] { 5, 7, 7, 2 });

            Assert.That(leaderboard[0].PlayerSlot, Is.EqualTo(1));
            Assert.That(leaderboard[0].Rank, Is.EqualTo(1));
            Assert.That(leaderboard[1].PlayerSlot, Is.EqualTo(2));
            Assert.That(leaderboard[1].Rank, Is.EqualTo(2));
            Assert.That(leaderboard[2].PlayerSlot, Is.EqualTo(0));
            Assert.That(leaderboard[3].PlayerSlot, Is.EqualTo(3));
            Assert.That(
                leaderboard.Select(entry =>
                    TagChaseRules.GetRewardForRank(entry.Rank)),
                Is.EqualTo(new[] { 3, 2, 1, 0 }));
        }
    }
}
