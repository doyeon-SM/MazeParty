using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Applies a deterministic desktop frame cap in standalone players. VSync is
    /// disabled first because it otherwise overrides Application.targetFrameRate.
    /// </summary>
    public static class BuildFrameRateLimiter
    {
        public const int TargetFrameRate = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyOnPlayerStartup()
        {
#if !UNITY_EDITOR
            Apply();
#endif
        }

        public static void Apply()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TargetFrameRate;
        }
    }
}
