using UnityEngine;

namespace MazeParty.Gameplay
{
    public static class PlayerGroundingRules
    {
        public const float Gravity = -24f;
        public const float GroundedVerticalVelocity = -2f;

        public static float AdvanceVerticalVelocity(
            float currentVelocity,
            bool isGrounded,
            float deltaTime)
        {
            if (isGrounded && currentVelocity < 0f)
            {
                return GroundedVerticalVelocity;
            }

            return currentVelocity + Gravity * Mathf.Max(0f, deltaTime);
        }
    }
}
