using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay.Minigames.BombPassing
{
    /// <summary>
    /// Baked XZ collision footprints for the authored Bomb Passing arena.
    /// Server movement reads these instead of scene PhysX colliders so every
    /// peer observes the same deterministic result.
    /// </summary>
    public static class BombPassingArenaCollisionLayout
    {
        private static readonly Rect[] BlockingRectValues =
        {
            new Rect(4.838404f, -3.185744f, 0.8000001f, 0.8000001f),
            new Rect(2.853352f, 1.514581f, 3f, 0.4f),
            new Rect(-0.661596f, -5.185744f, 0.8000001f, 0.8000001f),
            new Rect(-6.911596f, -5.185744f, 0.8000001f, 0.8000001f),
            new Rect(5.051107f, -6.288907f, 0.4f, 3f),
            new Rect(-7.161596f, 1.314256f, 0.8000001f, 0.8000001f),
            new Rect(2.338404f, 7.564256f, 0.8000001f, 0.8000001f),
            new Rect(-6.396648f, 1.514581f, 3f, 0.4f),
            new Rect(5.588404f, 1.314256f, 0.8000001f, 0.8000001f),
            new Rect(-0.146648f, 1.514581f, 3f, 0.4f),
            new Rect(-3.596648f, -1.285419f, 0.4f, 3f),
            new Rect(-3.146648f, -4.985419f, 3f, 0.4f),
            new Rect(-3.911596f, 1.314256f, 0.8000001f, 0.8000001f),
            new Rect(4.838404f, -6.685744f, 0.8000001f, 0.8000001f),
            new Rect(2.653351f, 4.714581f, 0.4f, 3f),
            new Rect(2.338404f, 4.314256f, 0.8000001f, 0.8000001f),
            new Rect(-0.911596f, 1.314256f, 0.8000001f, 0.8000001f),
            new Rect(2.088404f, -5.185744f, 0.8000001f, 0.8000001f),
            new Rect(-3.911596f, -1.685744f, 0.8000001f, 0.8000001f),
            new Rect(-0.146648f, 4.514581f, 3f, 0.4f),
            new Rect(-0.661596f, 4.314256f, 0.8000001f, 0.8000001f),
            new Rect(-6.146648f, -4.985419f, 3f, 0.4f),
            new Rect(2.353352f, -4.985419f, 3f, 0.4f)
        };

        public static IReadOnlyList<Rect> BlockingRects =>
            BlockingRectValues;
        public static int BlockingRectCount => BlockingRectValues.Length;

        public static bool BlocksCircle(
            double centerX,
            double centerZ,
            double radius)
        {
            var safeRadius = System.Math.Max(0d, radius);
            var radiusSquared = safeRadius * safeRadius;
            for (var index = 0; index < BlockingRectValues.Length; index++)
            {
                var rect = BlockingRectValues[index];
                var closestX = System.Math.Max(
                    rect.xMin,
                    System.Math.Min(rect.xMax, centerX));
                var closestZ = System.Math.Max(
                    rect.yMin,
                    System.Math.Min(rect.yMax, centerZ));
                var deltaX = centerX - closestX;
                var deltaZ = centerZ - closestZ;
                if (deltaX * deltaX + deltaZ * deltaZ <= radiusSquared)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool BlocksSegment(
            double startX,
            double startZ,
            double endX,
            double endZ)
        {
            return BlocksSegment(
                startX,
                startZ,
                endX,
                endZ,
                0d);
        }

        public static bool BlocksSegment(
            double startX,
            double startZ,
            double endX,
            double endZ,
            double clearance)
        {
            var safeClearance = System.Math.Max(0d, clearance);
            for (var index = 0; index < BlockingRectValues.Length; index++)
            {
                if (IntersectsRect(
                        startX,
                        startZ,
                        endX,
                        endZ,
                        BlockingRectValues[index],
                        safeClearance))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IntersectsRect(
            double startX,
            double startZ,
            double endX,
            double endZ,
            Rect rect,
            double clearance)
        {
            if (IntersectsRectBounds(
                    startX,
                    startZ,
                    endX,
                    endZ,
                    rect.xMin,
                    rect.xMax,
                    rect.yMin,
                    rect.yMax))
            {
                return true;
            }

            if (clearance <= 0d)
            {
                return false;
            }

            var clearanceSquared = clearance * clearance;
            if (PointRectDistanceSquared(startX, startZ, rect) <=
                    clearanceSquared ||
                PointRectDistanceSquared(endX, endZ, rect) <=
                    clearanceSquared)
            {
                return true;
            }

            return PointSegmentDistanceSquared(
                       rect.xMin,
                       rect.yMin,
                       startX,
                       startZ,
                       endX,
                       endZ) <= clearanceSquared ||
                   PointSegmentDistanceSquared(
                       rect.xMin,
                       rect.yMax,
                       startX,
                       startZ,
                       endX,
                       endZ) <= clearanceSquared ||
                   PointSegmentDistanceSquared(
                       rect.xMax,
                       rect.yMin,
                       startX,
                       startZ,
                       endX,
                       endZ) <= clearanceSquared ||
                   PointSegmentDistanceSquared(
                       rect.xMax,
                       rect.yMax,
                       startX,
                       startZ,
                       endX,
                       endZ) <= clearanceSquared;
        }

        private static bool IntersectsRectBounds(
            double startX,
            double startZ,
            double endX,
            double endZ,
            double minimumX,
            double maximumX,
            double minimumZ,
            double maximumZ)
        {
            var minimumFraction = 0d;
            var maximumFraction = 1d;
            return ClipAxis(
                       startX,
                       endX - startX,
                       minimumX,
                       maximumX,
                       ref minimumFraction,
                       ref maximumFraction) &&
                   ClipAxis(
                       startZ,
                       endZ - startZ,
                       minimumZ,
                       maximumZ,
                       ref minimumFraction,
                       ref maximumFraction);
        }

        private static double PointRectDistanceSquared(
            double pointX,
            double pointZ,
            Rect rect)
        {
            var closestX = System.Math.Max(
                rect.xMin,
                System.Math.Min(rect.xMax, pointX));
            var closestZ = System.Math.Max(
                rect.yMin,
                System.Math.Min(rect.yMax, pointZ));
            var deltaX = pointX - closestX;
            var deltaZ = pointZ - closestZ;
            return deltaX * deltaX + deltaZ * deltaZ;
        }

        private static double PointSegmentDistanceSquared(
            double pointX,
            double pointZ,
            double startX,
            double startZ,
            double endX,
            double endZ)
        {
            var segmentX = endX - startX;
            var segmentZ = endZ - startZ;
            var lengthSquared =
                segmentX * segmentX + segmentZ * segmentZ;
            if (lengthSquared <= 0.000000000000000001d)
            {
                var startDeltaX = pointX - startX;
                var startDeltaZ = pointZ - startZ;
                return startDeltaX * startDeltaX +
                    startDeltaZ * startDeltaZ;
            }

            var fraction =
                ((pointX - startX) * segmentX +
                 (pointZ - startZ) * segmentZ) /
                lengthSquared;
            fraction = System.Math.Max(0d, System.Math.Min(1d, fraction));
            var closestX = startX + segmentX * fraction;
            var closestZ = startZ + segmentZ * fraction;
            var deltaX = pointX - closestX;
            var deltaZ = pointZ - closestZ;
            return deltaX * deltaX + deltaZ * deltaZ;
        }

        private static bool ClipAxis(
            double start,
            double delta,
            double minimum,
            double maximum,
            ref double minimumFraction,
            ref double maximumFraction)
        {
            const double epsilon = 0.000000001d;
            if (System.Math.Abs(delta) <= epsilon)
            {
                return start >= minimum && start <= maximum;
            }

            var first = (minimum - start) / delta;
            var second = (maximum - start) / delta;
            if (first > second)
            {
                var swap = first;
                first = second;
                second = swap;
            }

            minimumFraction = System.Math.Max(minimumFraction, first);
            maximumFraction = System.Math.Min(maximumFraction, second);
            return minimumFraction <= maximumFraction + epsilon;
        }
    }
}
