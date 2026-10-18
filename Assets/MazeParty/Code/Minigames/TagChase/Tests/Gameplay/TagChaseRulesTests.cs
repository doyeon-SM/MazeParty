using System.Linq;
using MazeParty.Gameplay.Minigames.TagChase;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class TagChaseRulesTests
    {
        [Test]
        public void FirstPersonMovementUsesViewYawAndSharedFiveMeterSpeed()
        {
            var facingNorth =
                TagChaseRules.CalculateFirstPersonMovement(
                    Vector2.up,
                    0f,
                    1f);
            var facingEast =
                TagChaseRules.CalculateFirstPersonMovement(
                    Vector2.up,
                    90f,
                    1f);

            Assert.That(facingNorth.x, Is.Zero.Within(0.0001f));
            Assert.That(
                facingNorth.y,
                Is.EqualTo(5f).Within(0.0001f));
            Assert.That(
                facingEast.x,
                Is.EqualTo(5f).Within(0.0001f));
            Assert.That(facingEast.y, Is.Zero.Within(0.0001f));
        }

        [Test]
        public void RunnerMovementUsesFixedMapAxesAndIsSlowerThanTagger()
        {
            var runner = TagChaseRules.CalculateRunnerMovement(
                Vector2.up, 1f);
            var tagger = TagChaseRules.CalculateFirstPersonMovement(
                Vector2.up, 0f, 1f);
            Assert.That(runner.x, Is.Zero.Within(0.0001f));
            Assert.That(runner.y, Is.EqualTo(2.75f).Within(0.0001f));
            Assert.That(tagger.y, Is.GreaterThan(runner.y));
        }

        [Test]
        public void EveryActiveUncaughtSlotUsesFirstPersonControls()
        {
            for (var slot = 0; slot < TagChaseRules.PlayerCount; slot++)
            {
                Assert.That(
                    TagChaseRules.CanUseFirstPersonControls(
                        slot,
                        true,
                        false),
                    Is.True,
                    "P" + (slot + 1));
            }

            Assert.That(
                TagChaseRules.CanUseFirstPersonControls(0, true, true),
                Is.False);
            Assert.That(
                TagChaseRules.CanUseFirstPersonControls(0, false, false),
                Is.False);
            Assert.That(
                TagChaseRules.CanUseFirstPersonControls(-1, true, false),
                Is.False);
        }

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
        public void FinalLeaderboard_UsesScoreAndSharedRanks()
        {
            var leaderboard =
                TagChaseRules.BuildFinalLeaderboard(
                    new[] { 5, 7, 7, 2 });

            Assert.That(leaderboard[0].PlayerSlot, Is.EqualTo(1));
            Assert.That(leaderboard[0].Rank, Is.EqualTo(1));
            Assert.That(leaderboard[1].PlayerSlot, Is.EqualTo(2));
            Assert.That(leaderboard[1].Rank, Is.EqualTo(1));
            Assert.That(leaderboard[2].PlayerSlot, Is.EqualTo(0));
            Assert.That(leaderboard[3].PlayerSlot, Is.EqualTo(3));
            Assert.That(
                leaderboard.Select(entry => entry.TotalPoints),
                Is.EqualTo(new[] { 7, 7, 5, 2 }));
        }
    }
}
