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
        public void IncrementalOwnership_PreservesOverpaintingAndClearsNewRounds()
        {
            foreach (var resolution in new[] { 9, 96 })
            {
                var surface = new TerritoryPaintSurface(resolution, 0f, 9f);
                var count = (surface.CellCount + 63) / 64;
                var painted = new ulong[count];
                var low = new ulong[count];
                var high = new ulong[count];
                void ReplicateAndVerify()
                {
                    var seen = new System.Collections.Generic.HashSet<int>();
                    while (surface.TryConsumeChangedWord(out var word, out var p,
                               out var l, out var h))
                    {
                        Assert.That(seen.Add(word), Is.True);
                        painted[word] = p; low[word] = l; high[word] = h;
                    }
                    for (var cell = 0; cell < surface.CellCount; cell++)
                    {
                        var word = cell / 64;
                        var mask = 1UL << (cell % 64);
                        var owner = (painted[word] & mask) == 0
                            ? TerritoryPaintRules.UnpaintedOwner
                            : (byte)(((low[word] & mask) != 0 ? 1 : 0) |
                                     ((high[word] & mask) != 0 ? 2 : 0));
                        Assert.That(owner, Is.EqualTo(surface.GetOwnerAtIndex(cell)));
                    }
                }
                ReplicateAndVerify();
                for (var slot = 0; slot < 4; slot++)
                {
                    // Cover the same trail in every owner encoding before and
                    // after a flush, including changes to both owner bit planes.
                    surface.PaintStroke(slot, new Vector2(-7f, 0f), new Vector2(7f, 0f), 1.1f);
                    ReplicateAndVerify();
                    surface.PaintStroke(slot, new Vector2(-7f, 0f), new Vector2(7f, 0f), 1.1f);
                    Assert.That(surface.TryConsumeChangedWord(out _, out _, out _, out _), Is.False);
                }
                surface.PaintStroke(0, Vector2.zero, Vector2.one, 2f);
                surface.PaintStroke(2, Vector2.zero, Vector2.one, 2f);
                ReplicateAndVerify();
                surface = new TerritoryPaintSurface(resolution, 0f, 9f);
                ReplicateAndVerify();
                Assert.That(painted, Is.All.Zero);
            }
        }

        [Test]
        public void Leaderboard_UsesAreaAndSharedRanks()
        {
            var leaderboard =
                TerritoryPaintScoring.BuildLeaderboard(
                    new[] { 100, 220, 220, 40 },
                    1000);

            Assert.That(leaderboard[0].PlayerSlot, Is.EqualTo(1));
            Assert.That(leaderboard[0].Rank, Is.EqualTo(1));
            Assert.That(leaderboard[0].Score, Is.EqualTo(220));
            Assert.That(leaderboard[1].PlayerSlot, Is.EqualTo(2));
            Assert.That(leaderboard[1].Rank, Is.EqualTo(1));
            Assert.That(leaderboard[2].PlayerSlot, Is.EqualTo(0));
            Assert.That(leaderboard[3].PlayerSlot, Is.EqualTo(3));
        }
    }
}
