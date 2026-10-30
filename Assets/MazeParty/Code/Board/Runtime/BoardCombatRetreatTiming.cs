using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>Shared clock for the aftermath, including the final top view.</summary>
    public static class BoardCombatRetreatTiming
    {
        public const double Duration = 3d;
        public const double MotionStart = 0.4d;
        public const double MotionDuration = 1.2d;
        public const double CameraDelay = 0.2d;
        public const double StandUpAt = MotionStart + MotionDuration + CameraDelay;
        public const double TopViewAt = 2.35d;

        public static float MotionProgress(double elapsed)
        {
            var progress = Mathf.Clamp01((float)((elapsed - MotionStart) / MotionDuration));
            return progress * progress * (3f - 2f * progress);
        }
    }
}
