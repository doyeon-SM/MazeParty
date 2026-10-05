using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Shared framing used by fixed orthographic minigame cameras. The camera
    /// stays centered on the arena while revealing a small amount of depth.
    /// </summary>
    public static class MinigameCameraFraming
    {
        public const float TiltFromTopDownDegrees = 35f;

        public static Quaternion SharedRotation => Quaternion.Euler(
            90f - TiltFromTopDownDegrees,
            0f,
            0f);

        public static Vector3 CalculateSharedPosition(
            float centerX,
            float height,
            float centerZ = 0f)
        {
            var backwardOffset = Mathf.Tan(
                TiltFromTopDownDegrees * Mathf.Deg2Rad) * height;
            return new Vector3(
                centerX,
                height,
                centerZ - backwardOffset);
        }
    }
}
