using MazeParty.Gameplay.Minigames.TerritoryPaint;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class TerritoryPaintRulesTests
    {
        [Test]
        public void PaintStroke_CreatesContinuousCircularTrail()
        {
            var surface = new TerritoryPaintSurface(
                TerritoryPaintRules.SurfaceResolution,
                0f,
                9f);

            surface.PaintStroke(
                0,
                new Vector2(-7f, 0f),
                new Vector2(7f, 0f),
                0.55f);

            var centerRow = surface.Resolution / 2;
            for (var x = 11;
                 x < surface.Resolution - 11;
                 x++)
            {
                Assert.That(
                    surface.GetOwnerAt(x, centerRow),
                    Is.EqualTo(0),
                    "The swept brush left a gap at x=" + x + ".");
            }
        }

        [Test]
        public void PaintStroke_OverpaintTransfersOwnedArea()
        {
            var surface = new TerritoryPaintSurface(64, 0f, 8f);
            var start = new Vector2(-4f, -1f);
            var end = new Vector2(4f, 1f);

            surface.PaintStroke(0, start, end, 1.1f);
            var originalOwned =
                surface.GetOwnedCellCount(0);
            Assert.That(originalOwned, Is.GreaterThan(0));

            surface.PaintStroke(1, start, end, 1.1f);

            Assert.That(
                surface.GetOwnedCellCount(0),
                Is.EqualTo(0));
            Assert.That(
                surface.GetOwnedCellCount(1),
                Is.EqualTo(originalOwned));
        }

        [Test]
        public void FullArena_NormalizesExactlyToOneThousand()
        {
            var surface = new TerritoryPaintSurface(32, 0f, 8f);

            surface.PaintStroke(
                2,
                Vector2.zero,
                Vector2.zero,
                20f);

            Assert.That(
                surface.GetOwnedCellCount(2),
                Is.EqualTo(surface.CellCount));
            Assert.That(
                surface.GetNormalizedScore(2),
                Is.EqualTo(TerritoryPaintRules.TotalScore));
        }

        [Test]
        public void Leaderboard_UsesAreaThenStableSlotTieBreak()
        {
            var leaderboard =
                TerritoryPaintScoring.BuildLeaderboard(
                    new[] { 100, 220, 220, 40 },
                    1000);

            Assert.That(leaderboard[0].PlayerSlot, Is.EqualTo(1));
            Assert.That(leaderboard[0].Rank, Is.EqualTo(1));
            Assert.That(leaderboard[0].Score, Is.EqualTo(220));
            Assert.That(leaderboard[1].PlayerSlot, Is.EqualTo(2));
            Assert.That(leaderboard[1].Rank, Is.EqualTo(2));
            Assert.That(leaderboard[2].PlayerSlot, Is.EqualTo(0));
            Assert.That(leaderboard[3].PlayerSlot, Is.EqualTo(3));
        }
    }
}
