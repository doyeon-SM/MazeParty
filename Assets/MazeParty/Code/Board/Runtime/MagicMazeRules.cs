using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public static class MagicMazeRules
    {
        public const string MapId = "magic-maze";
        public const int IconChanges = 5;
        public const double IconInterval = .5d;
        public const double FinalIconAt = BoardCameraCloudTiming.Duration + IconChanges * IconInterval;
        public const double ExitCloudAt = FinalIconAt + 1d;
        public const double ShuffleDuration = ExitCloudAt + BoardCameraCloudTiming.Duration;
        public static bool ShufflesTiles(int turn, int total, bool requested = false) => turn > 0 && turn < total && (requested || turn % 3 == 0);
        public static bool ChangesWalls(int turn, int total) => turn > 0 && turn < total && turn % 5 == 0;
        public static bool ChangesMap(int turn, int total, bool requested = false) => ShufflesTiles(turn, total, requested) || ChangesWalls(turn, total);
        public static double Duration(int turn, int total, bool requested = false) => ShufflesTiles(turn, total, requested) ? ShuffleDuration : 0d;
        // Nearest integer, with half rounded up. The same count is kept for every generation.
        public static int InactiveWallCount(int count) => (count * 3 + 5) / 10;
        public static int PreviewStep(double elapsed) => elapsed < BoardCameraCloudTiming.Duration || elapsed >= FinalIconAt
            ? -1 : Math.Min(IconChanges - 1, (int)((elapsed - BoardCameraCloudTiming.Duration) / IconInterval));

        public static bool[] SelectWalls(int count, int seed, int generation)
        {
            if (count < 0 || generation < 0 || generation > 100) throw new ArgumentOutOfRangeException();
            var previous = new bool[count];
            for (var round = 0; round <= generation; round++)
            {
                var available = new List<int>();
                for (var i = 0; i < count; i++) if (!previous[i]) available.Add(i);
                Shuffle(available, unchecked(seed ^ (round * 486187739) ^ 0x2D32B917));
                previous = new bool[count];
                for (var i = 0; i < InactiveWallCount(count); i++) previous[available[i]] = true;
            }
            return previous;
        }

        public static Dictionary<Vector2Int, BoardLandingEffectType> ShuffleEffects(
            IReadOnlyDictionary<Vector2Int, BoardLandingEffectType> source, ISet<Vector2Int> fixedTiles, int seed)
        {
            var result = new Dictionary<Vector2Int, BoardLandingEffectType>();
            var coordinates = new List<Vector2Int>();
            foreach (var pair in source)
            {
                result.Add(pair.Key, pair.Value);
                if (fixedTiles == null || !fixedTiles.Contains(pair.Key)) coordinates.Add(pair.Key);
            }
            coordinates.Sort((a,b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var effects = new List<BoardLandingEffectType>();
            foreach (var coordinate in coordinates) effects.Add(source[coordinate]);
            Shuffle(effects, seed);
            for (var i = 0; i < coordinates.Count; i++) result[coordinates[i]] = effects[i];
            return result;
        }

        private static void Shuffle<T>(IList<T> values, int seed)
        {
            uint state = unchecked((uint)seed) ^ 0xA511E9B3u;
            if (state == 0) state = 0x6D2B79F5u;
            for (var i = values.Count - 1; i > 0; i--)
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                var j = (int)(state % (uint)(i + 1));
                var value = values[i]; values[i] = values[j]; values[j] = value;
            }
        }
    }
}
