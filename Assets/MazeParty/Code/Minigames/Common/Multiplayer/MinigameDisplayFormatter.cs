using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Shared formatting helpers for minigame UI text.
    /// </summary>
    public static class MinigameDisplayFormatter
    {
        public static string FormatClock(double seconds)
        {
            var safeSeconds = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            return (safeSeconds / 60).ToString("00") +
                   ":" + (safeSeconds % 60).ToString("00");
        }

        public static string ToOrdinal(int rank)
        {
            return rank switch
            {
                1 => "1ST",
                2 => "2ND",
                3 => "3RD",
                4 => "4TH",
                _ => "--"
            };
        }
    }
}
