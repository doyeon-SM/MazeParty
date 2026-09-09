using System;
using UnityEngine;

namespace MazeParty.Gameplay.Minigames.Minefield
{
    /// <summary>
    /// CharacterController motor for top-view play. Local input can be predicted,
    /// forwarded through LocalInputSampled, or disabled while a server drives
    /// SimulateAuthoritativeMovement directly.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(MinefieldPlayerActor))]
    public sealed class MinefieldPlayerMotor : MonoBehaviour
    {
        [SerializeField] private MinefieldInputAdapter input;
        [SerializeField] private MinefieldPlayerActor playerActor;
        [SerializeField] private MinefieldSonar sonar;
        [SerializeField] private Transform movementFrame;
        [SerializeField, Min(0f)] private float healthyMoveSpeed = 5f;
        [SerializeField, Min(0f)] private float walkingMoveSpeed =
            5f * FootstepRules.WalkSpeedMultiplier;
        [SerializeField, Min(0f)] private float acceleration = 28f;
        [SerializeField, Min(0f)] private float turnSpeed = 720f;
        [SerializeField] private float gravity = -24f;
        [SerializeField] private bool inputAuthority;
        [SerializeField] private bool localPrediction = true;
        [SerializeField] private bool movementEnabled = true;

        private CharacterController _controller;
        private MinefieldPlayerActor _subscribedActor;
        private Vector3 _planarVelocity;
        private float _verticalVelocity;

        public event Action<MinefieldPlayerMotor, MinefieldInputFrame> LocalInputSampled;

        public MinefieldPlayerActor PlayerActor => playerActor;
        public bool HasInputAuthority => inputAuthority;
        public bool LocalPrediction => localPrediction;
        public bool MovementEnabled => movementEnabled;
        public Vector2 LastMoveIntent { get; private set; }
        public Vector3 PlanarVelocity => _planarVelocity;
        public float PlanarSpeed => _planarVelocity.magnitude;
        public float HealthyMoveSpeed => healthyMoveSpeed;
        public float WalkingMoveSpeed => walkingMoveSpeed;
        public float CurrentMoveSpeed => GetMoveSpeed(
            playerActor != null
                ? playerActor.State
                : MinefieldPlayerState.Healthy);

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (input == null)
            {
                input = GetComponent<MinefieldInputAdapter>();
            }
            if (playerActor == null)
            {
                playerActor = GetComponent<MinefieldPlayerActor>();
            }
            if (sonar == null)
            {
                sonar = GetComponent<MinefieldSonar>();
            }
        }

        private void OnEnable()
        {
            SubscribeToActor(playerActor);
            input?.SetInputEnabled(inputAuthority);
        }

        private void OnDisable()
        {
            input?.SetInputEnabled(false);
            SubscribeToActor(null);
            LastMoveIntent = Vector2.zero;
            _planarVelocity = Vector3.zero;
        }

        private void Update()
        {
            if (!inputAuthority || input == null)
            {
                return;
            }

            var frame = input.ReadFrame();
            var acceptedMove = movementEnabled ? frame.Move : Vector2.zero;
            var acceptedFrame = new MinefieldInputFrame(
                frame.Sequence,
                acceptedMove,
                frame.SonarPressed);
            LocalInputSampled?.Invoke(this, acceptedFrame);

            if (localPrediction)
            {
                SimulateAuthoritativeMovement(acceptedMove, Time.deltaTime);
            }
            else
            {
                LastMoveIntent = acceptedMove;
            }

            if (frame.SonarPressed)
            {
                sonar?.TryRequestPulse(acceptedMove);
            }
        }

        public void Configure(
            MinefieldInputAdapter inputAdapter,
            MinefieldPlayerActor actor,
            MinefieldSonar sonarController,
            Transform inputMovementFrame = null)
        {
            input = inputAdapter;
            sonar = sonarController;
            movementFrame = inputMovementFrame;
            SubscribeToActor(actor);
            input?.SetInputEnabled(inputAuthority && isActiveAndEnabled);
        }

        public void ConfigureMovement(
            float fullSpeed,
            float crippledWalkingSpeed,
            float accelerationRate,
            float rotationSpeed)
        {
            healthyMoveSpeed = Mathf.Max(0f, fullSpeed);
            walkingMoveSpeed = Mathf.Max(0f, crippledWalkingSpeed);
            acceleration = Mathf.Max(0f, accelerationRate);
            turnSpeed = Mathf.Max(0f, rotationSpeed);
        }
        public void SetInputAuthority(bool hasAuthority)
        {
            inputAuthority = hasAuthority;
            input?.SetInputEnabled(hasAuthority && isActiveAndEnabled);
            if (!hasAuthority)
            {
                LastMoveIntent = Vector2.zero;
            }
        }

        public void SetLocalPrediction(bool enabled)
        {
            localPrediction = enabled;
        }

        public void SetMovementEnabled(bool enabled)
        {
            movementEnabled = enabled;
            if (!enabled)
            {
                LastMoveIntent = Vector2.zero;
            }
        }

        public void SetMovementFrame(Transform frame)
        {
            movementFrame = frame;
        }

        public void SimulateAuthoritativeMovement(Vector2 moveIntent, float deltaTime)
        {
            LastMoveIntent = movementEnabled
                ? Vector2.ClampMagnitude(moveIntent, 1f)
                : Vector2.zero;

            if (_controller == null)
            {
                _controller = GetComponent<CharacterController>();
            }

            if (playerActor != null && !playerActor.CanMove)
            {
                _planarVelocity = Vector3.zero;
                _verticalVelocity = 0f;
                return;
            }

            var step = Mathf.Max(0f, deltaTime);
            var desiredDirection = ResolveWorldDirection(LastMoveIntent);
            var targetVelocity = desiredDirection * CurrentMoveSpeed;
            _planarVelocity = Vector3.MoveTowards(
                _planarVelocity,
                targetVelocity,
                Mathf.Max(0f, acceleration) * step);

            if (desiredDirection.sqrMagnitude > 0.0001f && turnSpeed > 0f)
            {
                var targetRotation = Quaternion.LookRotation(
                    desiredDirection,
                    Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    turnSpeed * step);
            }

            if (_controller == null || !_controller.enabled)
            {
                return;
            }

            if (_controller.isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -2f;
            }
            else
            {
                _verticalVelocity += gravity * step;
            }

            _controller.Move(
                (_planarVelocity + Vector3.up * _verticalVelocity) * step);
        }

        public void ApplyObservedPlanarVelocity(Vector3 velocity)
        {
            velocity.y = 0f;
            _planarVelocity = velocity;
        }

        public bool IsStationary(float maximumPlanarSpeed)
        {
            return PlanarSpeed <= Mathf.Max(0f, maximumPlanarSpeed) &&
                   LastMoveIntent.sqrMagnitude <= 0.0001f;
        }

        public void TeleportAuthoritatively(Vector3 position, Quaternion rotation)
        {
            if (_controller == null)
            {
                _controller = GetComponent<CharacterController>();
            }

            var restoreController = _controller != null && _controller.enabled;
            if (restoreController)
            {
                _controller.enabled = false;
            }

            transform.SetPositionAndRotation(position, rotation);

            if (restoreController)
            {
                _controller.enabled = true;
            }

            LastMoveIntent = Vector2.zero;
            _planarVelocity = Vector3.zero;
            _verticalVelocity = 0f;
        }

        public float GetMoveSpeed(MinefieldPlayerState playerState)
        {
            if (playerState == MinefieldPlayerState.Crippled)
            {
                return walkingMoveSpeed;
            }

            return playerState == MinefieldPlayerState.Healthy
                ? healthyMoveSpeed
                : 0f;
        }

        private Vector3 ResolveWorldDirection(Vector2 moveIntent)
        {
            if (moveIntent.sqrMagnitude <= 0.0001f)
            {
                return Vector3.zero;
            }

            var right = Vector3.right;
            var forward = Vector3.forward;
            if (movementFrame != null)
            {
                right = Vector3.ProjectOnPlane(
                    movementFrame.right,
                    Vector3.up).normalized;
                forward = Vector3.ProjectOnPlane(
                    movementFrame.up,
                    Vector3.up).normalized;
                if (forward.sqrMagnitude <= 0.0001f)
                {
                    forward = Vector3.ProjectOnPlane(
                        movementFrame.forward,
                        Vector3.up).normalized;
                }
            }

            if (right.sqrMagnitude <= 0.0001f)
            {
                right = Vector3.right;
            }
            if (forward.sqrMagnitude <= 0.0001f)
            {
                forward = Vector3.forward;
            }

            var direction = right * moveIntent.x + forward * moveIntent.y;
            return direction.sqrMagnitude > 1f ? direction.normalized : direction;
        }

        private void SubscribeToActor(MinefieldPlayerActor actor)
        {
            if (_subscribedActor != null)
            {
                _subscribedActor.StateChanged -= HandleActorStateChanged;
            }

            playerActor = actor;
            _subscribedActor = actor;
            if (_subscribedActor != null && isActiveAndEnabled)
            {
                _subscribedActor.StateChanged += HandleActorStateChanged;
            }
        }

        private void HandleActorStateChanged(
            MinefieldPlayerActor actor,
            MinefieldActorSnapshot snapshot)
        {
            if (snapshot.State == MinefieldPlayerState.Healthy ||
                snapshot.State == MinefieldPlayerState.Crippled)
            {
                return;
            }

            LastMoveIntent = Vector2.zero;
            _planarVelocity = Vector3.zero;
            _verticalVelocity = 0f;
        }
    }
}
