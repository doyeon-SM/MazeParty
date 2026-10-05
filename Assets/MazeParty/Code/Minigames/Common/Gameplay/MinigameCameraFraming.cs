using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Shared framing used by fixed orthographic minigame cameras. The camera
    /// stays centered on the arena while revealing a small amount of depth.
    /// </summary>
    public static class MinigameCameraFraming
    {
        public const float TiltFromTopDownDegrees =
            SharedCameraFraming.TiltFromTopDownDegrees;

        public static Quaternion SharedRotation => SharedCameraFraming.Rotation;

        public static Vector3 CalculateSharedPosition(
            float centerX,
            float height,
            float centerZ = 0f)
        {
            return SharedCameraFraming.CalculatePosition(
                new Vector3(centerX, 0f, centerZ),
                height);
        }
    }
}
