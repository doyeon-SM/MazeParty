using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Shared lowered overview framing for fixed arena and spectator cameras.
    /// </summary>
    public static class SharedCameraFraming
    {
        public const float TiltFromTopDownDegrees = 35f;

        public static Quaternion Rotation => Quaternion.Euler(
            90f - TiltFromTopDownDegrees,
            0f,
            0f);

        public static Vector3 CalculatePosition(
            Vector3 focus,
            float heightAboveFocus)
        {
            var safeHeight = Mathf.Max(0.01f, heightAboveFocus);
            var backwardOffset = Mathf.Tan(
                TiltFromTopDownDegrees * Mathf.Deg2Rad) * safeHeight;
            return focus +
                   Vector3.up * safeHeight +
                   Vector3.back * backwardOffset;
        }
    }
}
