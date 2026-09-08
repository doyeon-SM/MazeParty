using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public static class FootstepRules
    {
        public const float WalkSpeedMultiplier = 0.55f;
        public const float StandardAudibleRadius = 24f;
        public const float QuietWalkAudibleRadius = 6f;
        public const float StandardStepDistance = 1.8f;
        public const float QuietWalkStepDistance = 1.35f;
        public const float MovementInputThreshold = 0.01f;

        public static float SpeedMultiplier(bool quietWalking)
        {
            return quietWalking ? WalkSpeedMultiplier : 1f;
        }

        public static float AudibleRadius(bool quietWalking)
        {
            return quietWalking ? QuietWalkAudibleRadius : StandardAudibleRadius;
        }

        public static float StepDistance(bool quietWalking)
        {
            return quietWalking ? QuietWalkStepDistance : StandardStepDistance;
        }

        public static bool HasMovementIntent(Vector2 input)
        {
            return input.sqrMagnitude >= MovementInputThreshold * MovementInputThreshold;
        }
    }

    /// <summary>
    /// Distance-based cadence shared by network and editor-only player motors.
    /// A locomotion-mode change starts a fresh stride so switching modes cannot
    /// manufacture an immediate extra footstep.
    /// </summary>
    public sealed class FootstepCadenceTracker
    {
        private float _distanceSinceStep;
        private bool _hasMode;
        private bool _quietWalking;

        public float DistanceSinceStep => _distanceSinceStep;

        public int RecordMovement(float planarDistance, bool quietWalking)
        {
            if (float.IsNaN(planarDistance) || float.IsInfinity(planarDistance))
            {
                throw new ArgumentOutOfRangeException(nameof(planarDistance));
            }
            if (planarDistance <= 0f)
            {
                return 0;
            }

            if (!_hasMode || _quietWalking != quietWalking)
            {
                _hasMode = true;
                _quietWalking = quietWalking;
                _distanceSinceStep = 0f;
            }

            _distanceSinceStep += planarDistance;
            var stride = FootstepRules.StepDistance(quietWalking);
            var emitted = Mathf.FloorToInt(_distanceSinceStep / stride);
            if (emitted > 0)
            {
                _distanceSinceStep -= emitted * stride;
            }
            return emitted;
        }

        public void Reset()
        {
            _distanceSinceStep = 0f;
            _hasMode = false;
            _quietWalking = false;
        }
    }
}
