using UnityEngine;

namespace MazeParty.Gameplay
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class GameplayPlayerMotor : MonoBehaviour, IPushReceiver
    {
        [SerializeField, Min(0f)] private float moveSpeed = 5f;
        [SerializeField, Min(0f)] private float mouseSensitivity = 0.08f;
        [SerializeField, Min(0f)] private float externalVelocityDecay = 7f;
        [SerializeField, Min(0f)] private float contactPushStrength = 1.5f;
        [SerializeField] private float gravity = -24f;
        [SerializeField] private Transform lookPivot;

        private CharacterController _controller;
        private FootstepAudioEmitter _footstepEmitter;
        private PlayerAvatarVisual _avatarVisual;
        private readonly Collider[] _standingClearanceHits = new Collider[16];
        private readonly FootstepCadenceTracker _footstepCadence =
            new FootstepCadenceTracker();
        private Vector3 _externalVelocity;
        private float _verticalVelocity;
        private float _pitch;

        public Vector3 ExternalVelocity => _externalVelocity;
        public bool IsWalkingQuietly { get; private set; }
        public PlayerAvatarVisual AvatarVisual => _avatarVisual;
        public bool IsCrouching { get; private set; }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _footstepEmitter = GetComponent<FootstepAudioEmitter>();
            if (_footstepEmitter == null)
                _footstepEmitter = gameObject.AddComponent<FootstepAudioEmitter>();
            if (lookPivot == null)
                lookPivot = transform;
            _avatarVisual = GetComponent<PlayerAvatarVisual>();
            if (_avatarVisual == null)
                _avatarVisual = gameObject.AddComponent<PlayerAvatarVisual>();
            _avatarVisual.EnsureBuilt();
            _avatarVisual.ConfigureEyePivot(lookPivot);
            _avatarVisual.SetDisplayName("Local Player");
        }

        public void Configure(Transform pivot)
        {
            lookPivot = pivot != null ? pivot : transform;
        }

        public void Tick(Vector2 move, Vector2 look, bool allowDirectInput, bool allowLook)
        {
            Tick(move, look, false, allowDirectInput, allowLook);
        }

        public void Tick(
            Vector2 move,
            Vector2 look,
            bool quietWalkHeld,
            bool allowDirectInput,
            bool allowLook)
        {
            if (_controller == null)
                _controller = GetComponent<CharacterController>();

            var deltaTime = Time.deltaTime;
            UpdateCrouch(allowDirectInput && quietWalkHeld);
            if (allowDirectInput)
            {
                var localMove = new Vector3(move.x, 0f, move.y);
                if (localMove.sqrMagnitude > 1f)
                    localMove.Normalize();

                IsWalkingQuietly = IsCrouching &&
                                   FootstepRules.HasMovementIntent(move);
                var worldMove = transform.TransformDirection(localMove) *
                                (moveSpeed * FootstepRules.SpeedMultiplier(
                                    IsWalkingQuietly));
                var previousPosition = transform.position;
                MoveCharacter(worldMove, deltaTime);
                RecordFootstepDistance(
                    previousPosition,
                    transform.position,
                    FootstepRules.HasMovementIntent(move),
                    IsWalkingQuietly);
            }
            else
            {
                IsWalkingQuietly = false;
                MoveCharacter(Vector3.zero, deltaTime);
            }

            if (allowDirectInput && allowLook)
                ApplyLook(look);
        }

        public void ApplyPush(Vector3 impulse)
        {
            _externalVelocity += new Vector3(impulse.x, Mathf.Max(0f, impulse.y), impulse.z);
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            if (_controller == null)
                _controller = GetComponent<CharacterController>();

            _controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _controller.enabled = true;
            _externalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            _pitch = 0f;
            IsWalkingQuietly = false;
            IsCrouching = false;
            ApplyCrouchState(false);
            _avatarVisual?.SetCrouching(false);
            _footstepCadence.Reset();
            if (lookPivot != null)
                lookPivot.localRotation = Quaternion.identity;
        }

        private void MoveCharacter(Vector3 directVelocity, float deltaTime)
        {
            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;
            else
                _verticalVelocity += gravity * deltaTime;

            var velocity = directVelocity + _externalVelocity + Vector3.up * _verticalVelocity;
            _controller.Move(velocity * deltaTime);
            _externalVelocity = Vector3.MoveTowards(
                _externalVelocity,
                Vector3.zero,
                externalVelocityDecay * deltaTime);
        }

        private void UpdateCrouch(bool crouchHeld)
        {
            var crouching = crouchHeld || IsCrouching && !CanStand();
            if (IsCrouching != crouching)
            {
                IsCrouching = crouching;
                ApplyCrouchState(crouching);
            }
            _avatarVisual?.SetCrouching(crouching);
        }

        private void ApplyCrouchState(bool crouching)
        {
            _controller.height = crouching
                ? PlayerAvatarVisual.CrouchingControllerHeight
                : PlayerAvatarVisual.StandingControllerHeight;
            var center = _controller.center;
            center.y = crouching
                ? PlayerAvatarVisual.CrouchingControllerCenterY
                : PlayerAvatarVisual.StandingControllerCenterY;
            _controller.center = center;
            if (lookPivot != null && lookPivot != transform)
            {
                var position = lookPivot.localPosition;
                position.y = crouching
                    ? PlayerAvatarVisual.CrouchingEyeHeight
                    : PlayerAvatarVisual.StandingEyeHeight;
                lookPivot.localPosition = position;
            }
        }

        private bool CanStand()
        {
            var radius = Mathf.Max(0.01f, _controller.radius - 0.02f);
            var center = transform.TransformPoint(new Vector3(
                _controller.center.x,
                PlayerAvatarVisual.StandingControllerCenterY,
                _controller.center.z));
            var segment = Mathf.Max(
                0f,
                PlayerAvatarVisual.StandingControllerHeight * 0.5f - radius);
            var count = Physics.OverlapCapsuleNonAlloc(
                center + transform.up * segment,
                center - transform.up * segment,
                radius,
                _standingClearanceHits,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (var index = 0; index < count; index++)
            {
                var candidate = _standingClearanceHits[index];
                if (candidate != null &&
                    candidate.transform != transform &&
                    !candidate.transform.IsChildOf(transform))
                {
                    return false;
                }
            }
            return true;
        }

        private void ApplyLook(Vector2 look)
        {
            transform.Rotate(Vector3.up, look.x * mouseSensitivity, Space.World);
            _pitch = Mathf.Clamp(_pitch - look.y * mouseSensitivity, -85f, 85f);
            lookPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void RecordFootstepDistance(
            Vector3 previousPosition,
            Vector3 currentPosition,
            bool hasMovementIntent,
            bool quietWalking)
        {
            if (!hasMovementIntent || !_controller.isGrounded)
                return;

            var delta = currentPosition - previousPosition;
            delta.y = 0f;
            var stepCount = _footstepCadence.RecordMovement(
                delta.magnitude,
                quietWalking);
            for (var step = 0; step < stepCount; step++)
            {
                _footstepEmitter?.PresentFootstep(transform.position, quietWalking);
            }
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit == null || hit.gameObject == null || hit.moveDirection.y < -0.3f)
                return;

            var behaviours = hit.gameObject.GetComponentsInParent<MonoBehaviour>();
            for (var i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] == this || !(behaviours[i] is IPushReceiver receiver))
                    continue;

                var planarDirection = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
                if (planarDirection.sqrMagnitude > 0.001f)
                    receiver.ApplyPush(planarDirection.normalized * contactPushStrength);
                return;
            }
        }
    }
}
