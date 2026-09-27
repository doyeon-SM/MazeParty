using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Applies a deterministic desktop frame cap in standalone players. VSync is
    /// disabled first because it otherwise overrides Application.targetFrameRate.
    /// </summary>
    public static class BuildFrameRateLimiter
    {
        public const int DefaultTargetFrameRate = 60;

        public static void Apply(int targetFrameRate = DefaultTargetFrameRate)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate =
                targetFrameRate > 0 ? targetFrameRate : -1;
        }
    }
}
