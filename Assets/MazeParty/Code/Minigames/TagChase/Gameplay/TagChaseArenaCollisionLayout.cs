using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay.Minigames.TagChase
{
    /// <summary>
    /// Baked XZ collision footprints for the authored Tag Chase maze.
    /// The server simulation reads this layout instead of relying on PhysX,
    /// keeping movement deterministic while matching the visible walls.
    /// </summary>
    public static class TagChaseArenaCollisionLayout
    {
        private static readonly Rect[] BlockingRectValues =
        {
            new Rect(1050.03992f, 4.48f, 0.8000001f, 0.8000001f),
            new Rect(1050.71558f, 4.619236f, 3f, 0.4f),
            new Rect(1050.78992f, -4.13f, 0.8000001f, 0.8000001f),
            new Rect(1050.78992f, -7.63f, 0.8000001f, 0.8000001f),
            new Rect(1051.01563f, -6.930765f, 0.400000423f, 3.00000048f),
            new Rect(1051.46558f, -3.88076472f, 3f, 0.4f),
            new Rect(1053.03992f, 1.150656f, 0.8000001f, 0.8000001f),
            new Rect(1053.26563f, 1.81923532f, 0.400000423f, 3.00000048f),
            new Rect(1053.46558f, 4.619236f, 3f, 0.4f),
            new Rect(1054.28992f, -4.13f, 0.8000001f, 0.8000001f),
            new Rect(1056.53992f, 4.48f, 0.8000001f, 0.8000001f),
            new Rect(1057.03992f, -7.34934473f, 0.8000001f, 0.8000001f),
            new Rect(1057.26563f, -10.1807652f, 0.400000423f, 3.00000048f),
            new Rect(1058.53992f, -1.38000011f, 0.8000001f, 0.8000001f),
            new Rect(1058.96558f, -1.13076425f, 3f, 0.4f),
            new Rect(1061.28992f, 6.37f, 0.8000001f, 0.8000001f),
            new Rect(1061.28992f, -6.63f, 0.8000001f, 0.8000001f),
            new Rect(1061.96558f, -1.13076425f, 3f, 0.4f),
            new Rect(1061.96558f, 6.369236f, 3f, 0.4f),
            new Rect(1061.96558f, -6.380764f, 3f, 0.4f),
            new Rect(1064.53992f, 2.37f, 0.8000001f, 0.8000001f),
            new Rect(1064.76563f, -0.18076396f, 0.400000423f, 3.00000048f),
            new Rect(1064.76563f, -3.18076444f, 0.400000423f, 3.00000048f),
            new Rect(1064.76563f, -6.180765f, 0.400000423f, 3.00000048f),
            new Rect(1064.96558f, 6.369236f, 3f, 0.4f),
            new Rect(1064.96558f, -6.380764f, 3f, 0.4f),
            new Rect(1067.78992f, 6.37f, 0.8000001f, 0.8000001f),
            new Rect(1067.78992f, -6.63f, 0.8000001f, 0.8000001f),
            new Rect(1068.53992f, -0.7899999f, 0.8000001f, 0.8000001f),
            new Rect(1068.96558f, -0.6307635f, 3f, 0.4f)
        };

        public static IReadOnlyList<Rect> BlockingRects =>
            BlockingRectValues;
        public static int BlockingRectCount => BlockingRectValues.Length;

        public static bool BlocksCircle(Vector2 center, float radius)
        {
            var safeRadius = Mathf.Max(0f, radius);
            var radiusSquared = safeRadius * safeRadius;
            for (var index = 0; index < BlockingRectValues.Length; index++)
            {
                var rect = BlockingRectValues[index];
                var closest = new Vector2(
                    Mathf.Clamp(center.x, rect.xMin, rect.xMax),
                    Mathf.Clamp(center.y, rect.yMin, rect.yMax));
                if ((center - closest).sqrMagnitude <= radiusSquared)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
