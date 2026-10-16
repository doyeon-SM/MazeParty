using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Gameplay
{
    // Evaluate after the local avatar has restored its owner-only eye rotation.
    // NetworkTransform and the server motor may update the body earlier in the
    // frame, but the rendered camera must always use the final local look pose.
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class GameplayCameraDirector : MonoBehaviour, IGameplayCameraService
    {
        private const int LivePriority = 100;
        private const int StandbyPriority = 0;
        private const string RuntimeOverheadCameraName = "CM_PlayerOverhead (Runtime)";
        private const string RuntimePlayerWideCameraName = "CM_PlayerWide (Runtime)";
        private const string RuntimeCombatSpectatorCameraName =
            "CM_CombatSpectator (Runtime)";
        private const string RuntimeBoardResourceEventCameraName =
            "CM_BoardResourceEvent (Runtime)";

        [Header("Output")]
        [SerializeField] private Camera outputCamera;

        [Header("Gameplay Cameras")]
        [SerializeField] private CinemachineCamera firstPersonCamera;
        [SerializeField] private CinemachineCamera boardTopDownCamera;
        [SerializeField] private CinemachineCamera minigameCamera;
        [SerializeField] private CinemachineCamera playerOverheadCamera;
        [SerializeField] private CinemachineCamera playerWideCamera;
        [SerializeField] private CinemachineCamera combatSpectatorCamera;
        [SerializeField] private CinemachineCamera boardResourceEventCamera;
        [SerializeField] private Transform localPlayerEye;
        [SerializeField] private GameplayMode activeMode = GameplayMode.BoardTopDown;

        [Header("Board Framing")]
        [SerializeField] private BoardCameraFramingAnchor boardFramingAnchor;
        [SerializeField] private Vector3 fallbackBoardCenter;
        [SerializeField] private BoardCameraFramingSettings fallbackBoardFraming =
            BoardCameraFramingSettings.Default;

        [Header("Transition Timing")]
        [SerializeField] private bool smoothCameraTransitions;
        [SerializeField, Min(0f)] private float boardToOverheadDuration = 0.35f;
        [SerializeField, Min(0f)] private float boardRecenterDuration = 0.3f;
        [SerializeField, Min(0f)] private float overheadToFirstPersonDuration = 0.65f;
        [SerializeField, Min(0f)] private float directTransitionDuration = 0.4f;

        [SerializeField, Min(30f)] private float boardRotationDegreesPerSecond = 120f;

        [Header("Player Overhead")]
        [SerializeField, Min(0.1f)] private float playerOverheadHeight = 8f;
        [SerializeField, Range(10f, 120f)] private float playerOverheadFieldOfView = 50f;

        [Header("Combat Spectator")]
        [SerializeField, Min(1f)] private float combatSpectatorHeight = 10f;
        [SerializeField, Range(10f, 120f)] private float combatSpectatorFieldOfView = 55f;

        [Header("Board Resource Event")]
        [SerializeField, Min(0.5f)] private float boardResourceEventDistance = 3.4f;
        [SerializeField] private float boardResourceEventHeightOffset = 0.15f;
        [SerializeField, Range(10f, 120f)]
        private float boardResourceEventFieldOfView = 60f;

        private CinemachineBrain _brain;
        private Coroutine _transitionCoroutine;
        private CinemachineBlendDefinition _savedDefaultBlend;
        private bool _savedIgnoreTimeScale;
        private bool _brainSettingsCaptured;
        private bool _isTransitioning;
        private bool _drivingBoardPose;
        private Vector3 _boardTransitionPosition;
        private Quaternion _boardTransitionRotation;
        private float _boardTransitionFieldOfView;
        private bool _hasCompletedMode;
        private bool _uiPointerVisible;
        private GameplayMode _completedMode;
        private GameplayMode _transitionFrom;
        private GameplayMode _transitionTarget;
        private Vector3 _combatSpectatorFocus;
        private Transform _boardResourceEventFacing;
        private Transform _boardResourceEventHead;
        private float _boardResourceEventFocusHeight;

        public event Action<GameplayMode, GameplayMode> TransitionStarted;
        public event Action<GameplayMode, GameplayMode> TransitionCompleted;

        public GameplayMode ActiveMode => activeMode;
        public GameplayMode CompletedMode => _hasCompletedMode ? _completedMode : activeMode;
        public Camera OutputCamera => outputCamera;
        public Transform LocalPlayerEye => ResolveLocalPlayerEye();
        public BoardCameraFramingAnchor BoardFramingAnchor => boardFramingAnchor;
        public bool IsTransitioning => _isTransitioning;
        public bool SmoothCameraTransitions => smoothCameraTransitions;
        // Reserve the same phases for every look direction. Smoothstep's peak
        // angular speed is 1.5x its average, including the worst 180-degree turn.
        private float BoardRotationDuration =>
            Mathf.Max(directTransitionDuration, 270f / Mathf.Max(30f, boardRotationDegreesPerSecond));
        private float BoardZoomDuration =>
            Mathf.Max(boardToOverheadDuration + overheadToFirstPersonDuration,
                270f / Mathf.Max(30f, boardRotationDegreesPerSecond));
        public double BoardTransitionBudgetSeconds => !smoothCameraTransitions ? 0.25d :
            BoardRotationDuration + BoardZoomDuration + boardRecenterDuration + 0.25d;

        public void Configure(
            Camera cameraOutput,
            CinemachineCamera firstPerson,
            CinemachineCamera boardTopDown,
            CinemachineCamera minigame)
        {
            outputCamera = cameraOutput;
            firstPersonCamera = firstPerson;
            boardTopDownCamera = boardTopDown;
            minigameCamera = minigame;
            _brain = null;

            if (Application.isPlaying)
            {
                EnsureRuntimeCameraSystem();
                PrepareModePose(activeMode);
            }

            ApplyPriorities(activeMode);
        }

        public void SetLocalPlayerEye(Transform eye)
        {
            localPlayerEye = eye;

            if (firstPersonCamera != null && eye != null)
                firstPersonCamera.Follow = eye;

            if (Application.isPlaying)
            {
                PrepareFirstPersonPose();
                PreparePlayerOverheadPose();
                PreparePlayerWidePose();
            }
        }

        public void SetBoardFramingAnchor(BoardCameraFramingAnchor anchor)
        {
            boardFramingAnchor = anchor;
            RefreshBoardFraming();
        }

        public void SetFallbackBoardFraming(
            Vector3 center,
            BoardCameraFramingSettings settings)
        {
            fallbackBoardCenter = center;
            fallbackBoardFraming = settings.Sanitized();
            RefreshBoardFraming();
        }

        public void RefreshBoardFraming()
        {
            if (Application.isPlaying)
                PrepareBoardPose();
        }

        public void SetCombatSpectatorFocus(Vector3 worldCenter)
        {
            _combatSpectatorFocus = worldCenter;
            if (Application.isPlaying)
            {
                EnsureRuntimeCameraSystem();
                PrepareCombatSpectatorPose();
            }
        }

        public void SetBoardResourceEventFocus(
            Transform facing,
            Transform head,
            float focusHeight)
        {
            _boardResourceEventFacing = facing;
            _boardResourceEventHead = head;
            _boardResourceEventFocusHeight = Mathf.Max(0f, focusHeight);
            if (Application.isPlaying)
            {
                EnsureRuntimeCameraSystem();
                PrepareBoardResourceEventPose();
            }
        }

        public void SetMinigameCamera(CinemachineCamera camera)
        {
            if (minigameCamera == camera)
            {
                return;
            }

            SetPriority(minigameCamera, StandbyPriority);
            minigameCamera = camera;
            ApplyPriorities(activeMode);
        }

        public void ClearMinigameCamera(CinemachineCamera expectedCamera)
        {
            if (minigameCamera != expectedCamera)
            {
                return;
            }

            SetPriority(minigameCamera, StandbyPriority);
            minigameCamera = null;
        }

        public void SwitchTo(GameplayMode mode) => SwitchTo(mode, null);

        public void SwitchTo(GameplayMode mode, Func<double> boardTransitionElapsed)
        {
            if (!Application.isPlaying)
            {
                activeMode = mode;
                _completedMode = mode;
                _hasCompletedMode = true;
                ApplyPriorities(mode);
                ApplyCursorPolicy();
                return;
            }

            EnsureRuntimeCameraSystem();

            if (!smoothCameraTransitions)
            {
                SnapTo(mode);
                return;
            }

            if (!isActiveAndEnabled)
            {
                SnapTo(mode);
                return;
            }

            if (_isTransitioning)
            {
                // A newer local state is authoritative. Avoid queuing stale camera motion.
                SnapTo(mode);
                return;
            }

            var previous = CompletedMode;
            activeMode = mode;
            ApplyCursorPolicy();

            if (previous == mode)
            {
                PrepareModePose(mode);
                ApplyPriorities(mode);
                return;
            }

            if (!CanTransition(previous, mode))
            {
                SnapTo(mode);
                return;
            }

            _transitionCoroutine = StartCoroutine(
                RunTransition(previous, mode, boardTransitionElapsed));
        }

        public void SnapTo(GameplayMode mode)
        {
            SnapTo(mode, null);
        }

        public void SnapTo(GameplayMode mode, Transform eye)
        {
            var previous = CompletedMode;
            CancelTransition();

            if (eye != null)
                SetLocalPlayerEye(eye);

            activeMode = mode;
            EnsureRuntimeCameraSystem();
            PrepareModePose(mode);
            ApplyPriorities(mode);

            if (_brain != null)
                _brain.ResetState();

            _completedMode = mode;
            _hasCompletedMode = true;
            ApplyCursorPolicy();
            TransitionCompleted?.Invoke(previous, mode);
        }

        public void SetUiPointerVisible(bool visible)
        {
            _uiPointerVisible = visible;
            ApplyCursorPolicy();
        }

        private void Awake()
        {
            _completedMode = activeMode;
            _hasCompletedMode = true;

            if (Application.isPlaying)
                EnsureRuntimeCameraSystem();
        }

        private void OnEnable()
        {
            _completedMode = activeMode;
            _hasCompletedMode = true;

            if (Application.isPlaying)
            {
                EnsureRuntimeCameraSystem();
                PrepareModePose(activeMode);
            }

            ApplyPriorities(activeMode);
            LocalInputGate.Changed += ApplyCursorPolicy;
            ApplyCursorPolicy();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
                return;

            // The first-person virtual camera has no procedural body component.
            // Keep its raw pose attached to the moving eye after a blend completes.
            if (!_isTransitioning && activeMode == GameplayMode.FirstPerson)
                PrepareFirstPersonPose();
            else if (!_isTransitioning && activeMode == GameplayMode.CombatSpectator)
                PrepareCombatSpectatorPose();
            else if (!_isTransitioning && activeMode == GameplayMode.BoardResourceEvent)
                PrepareBoardResourceEventPose();

            if (!_isTransitioning)
                return;

            if (_drivingBoardPose)
                ApplyBoardTransitionPose();
        }

        private void OnDisable()
        {
            LocalInputGate.Changed -= ApplyCursorPolicy;
            CancelTransition();
            _completedMode = activeMode;
            _hasCompletedMode = true;
            ApplyPriorities(activeMode);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private IEnumerator RunTransition(
            GameplayMode previous,
            GameplayMode target,
            Func<double> boardTransitionElapsed)
        {
            _transitionFrom = previous;
            _transitionTarget = target;
            _isTransitioning = true;
            CaptureBrainSettings();
            TransitionStarted?.Invoke(previous, target);

            if (UsesPlayerBridge(previous, target))
            {
                ApplyCursorPolicy();
                yield return RunBoardTransition(target, boardTransitionElapsed);
            }
            else
            {
                PrepareModePose(target);
                yield return BlendTo(
                    GetModeCamera(target),
                    directTransitionDuration);
            }

            _transitionCoroutine = null;
            _isTransitioning = false;
            _completedMode = target;
            _hasCompletedMode = true;
            RestoreBrainSettings();
            ApplyPriorities(target);
            ApplyCursorPolicy();
            TransitionCompleted?.Invoke(previous, target);
        }

        private IEnumerator RunBoardTransition(GameplayMode target, Func<double> synchronizedElapsed)
        {
            var eye = ResolveLocalPlayerEye();
            if (eye == null || outputCamera == null) yield break;
            PrepareBoardPose();
            var toFirstPerson = target == GameplayMode.FirstPerson;
            var sourcePosition = outputCamera.transform.position;
            var sourceRotation = outputCamera.transform.rotation;
            var sourceFov = outputCamera.fieldOfView;
            var sourcePitch = Mathf.Asin(Mathf.Clamp(
                -(sourceRotation * Vector3.forward).y, -1f, 1f)) * Mathf.Rad2Deg;
            var sourceYaw = GetCameraHeading(sourceRotation);
            // Capture the direction once. Cursor confinement prevents mouse look
            // from moving this target while the camera performs its own motion.
            var targetYaw = toFirstPerson ? GetCameraHeading(eye.rotation) : 0f;
            var targetPitch = toFirstPerson
                ? Mathf.DeltaAngle(0f, eye.eulerAngles.x) : 90f;
            _boardTransitionPosition = sourcePosition;
            _boardTransitionRotation = sourceRotation;
            _boardTransitionFieldOfView = sourceFov;
            _drivingBoardPose = true;
            SetBrainBlend(0f);
            SetLiveCamera(playerOverheadCamera);
            ApplyBoardTransitionPose();
            _brain?.ResetState();

            var localStartedAt = Time.unscaledTimeAsDouble;
            Func<double> elapsed = synchronizedElapsed ??
                (() => Time.unscaledTimeAsDouble - localStartedAt);
            var phaseStartedAt = 0d;
            yield return MoveBoardCamera(sourcePosition, sourcePosition,
                sourcePitch, sourcePitch, sourceYaw, targetYaw,
                sourceFov, sourceFov, BoardRotationDuration, elapsed, phaseStartedAt);
            phaseStartedAt += BoardRotationDuration;

            PreparePlayerWidePose();
            var widePosition = playerWideCamera.transform.position;
            var boardFov = boardTopDownCamera.Lens.FieldOfView;
            if (toFirstPerson)
            {
                // Recenter with heading already aligned, then tilt only in pitch
                // while approaching the eye. No quaternion roll around the head.
                yield return MoveBoardCamera(sourcePosition, widePosition,
                    sourcePitch, sourcePitch, targetYaw, targetYaw,
                    sourceFov, boardFov, boardRecenterDuration, elapsed, phaseStartedAt);
                phaseStartedAt += boardRecenterDuration;
            }

            var zoomStart = toFirstPerson ? widePosition : sourcePosition;
            var zoomEnd = toFirstPerson ? eye.position : widePosition;
            var zoomFov = toFirstPerson ? firstPersonCamera.Lens.FieldOfView : boardFov;
            yield return MoveBoardCamera(zoomStart, zoomEnd,
                sourcePitch, targetPitch, targetYaw, targetYaw,
                toFirstPerson ? boardFov : sourceFov, zoomFov,
                BoardZoomDuration, elapsed, phaseStartedAt, toFirstPerson ? eye : null);
            phaseStartedAt += BoardZoomDuration;

            if (!toFirstPerson)
            {
                yield return MoveBoardCamera(widePosition, boardTopDownCamera.transform.position,
                    90f, 90f, 0f, 0f, boardFov, boardFov, boardRecenterDuration, elapsed, phaseStartedAt);
            }
            PrepareModePose(target);
            SetLiveCamera(GetModeCamera(target));
            _brain?.ResetState();
            _drivingBoardPose = false;
        }

        private IEnumerator MoveBoardCamera(Vector3 from, Vector3 to,
            float fromPitch, float toPitch, float fromYaw, float toYaw,
            float fromFov, float toFov, float duration,
            Func<double> elapsed, double phaseStartedAt, Transform followEnd = null)
        {
            float progress;
            do
            {
                var now = elapsed();
                progress = duration > 0f && !double.IsNaN(now)
                    ? Mathf.Clamp01((float)((now - phaseStartedAt) / duration)) : 1f;
                var eased = progress * progress * (3f - 2f * progress);
                _boardTransitionPosition = Vector3.Lerp(from,
                    followEnd != null ? followEnd.position : to, eased);
                _boardTransitionRotation = Quaternion.Euler(
                    Mathf.Lerp(fromPitch, toPitch, eased),
                    Mathf.LerpAngle(fromYaw, toYaw, eased), 0f);
                _boardTransitionFieldOfView = Mathf.Lerp(fromFov, toFov, eased);
                ApplyBoardTransitionPose();
                if (progress < 1f) yield return null;
            } while (progress < 1f);
        }

        private void ApplyBoardTransitionPose()
        {
            var lens = playerOverheadCamera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            lens.FieldOfView = _boardTransitionFieldOfView;
            playerOverheadCamera.Lens = lens;
            playerOverheadCamera.ForceCameraPosition(
                _boardTransitionPosition, _boardTransitionRotation);
            // The brain can evaluate earlier in LateUpdate; keep the render pose
            // in the same frame as the controlled virtual camera.
            outputCamera.transform.SetPositionAndRotation(
                _boardTransitionPosition, _boardTransitionRotation);
            outputCamera.fieldOfView = _boardTransitionFieldOfView;
        }

        private static float GetCameraHeading(Quaternion rotation)
        {
            var forward = rotation * Vector3.forward;
            if (forward.x * forward.x + forward.z * forward.z < 0.0001f)
                forward = rotation * Vector3.up;
            return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        private IEnumerator BlendTo(
            CinemachineCamera incoming,
            float duration)
        {
            SetBrainBlend(duration);
            SetLiveCamera(incoming);

            if (duration <= 0f)
            {
                yield return null;
                yield break;
            }

            var finishTime = Time.unscaledTime + duration;
            while (Time.unscaledTime < finishTime)
                yield return null;

            // Let CinemachineBrain evaluate the last frame of the blend.
            yield return null;
        }

        private bool CanTransition(
            GameplayMode previous,
            GameplayMode target)
        {
            if (GetModeCamera(previous) == null
                || GetModeCamera(target) == null)
            {
                return false;
            }

            return !UsesPlayerBridge(previous, target)
                || (playerOverheadCamera != null && playerWideCamera != null);
        }

        private static bool UsesPlayerBridge(
            GameplayMode previous,
            GameplayMode target)
        {
            return (previous == GameplayMode.BoardTopDown
                    && target == GameplayMode.FirstPerson)
                || (previous == GameplayMode.FirstPerson
                    && target == GameplayMode.BoardTopDown);
        }

        private void EnsureRuntimeCameraSystem()
        {
            if (_brain == null && outputCamera != null)
            {
                _brain = outputCamera.GetComponent<CinemachineBrain>();
                if (_brain == null && Application.isPlaying)
                    _brain = outputCamera.gameObject.AddComponent<CinemachineBrain>();
            }

            if (_brain != null && !smoothCameraTransitions)
            {
                _brain.DefaultBlend = new CinemachineBlendDefinition(
                    CinemachineBlendDefinition.Styles.Cut,
                    0f);
            }

            if (!Application.isPlaying)
                return;

            if (playerOverheadCamera == null)
            {
                var overheadObject = new GameObject(RuntimeOverheadCameraName)
                {
                    hideFlags = HideFlags.DontSave
                };
                overheadObject.transform.SetParent(transform, false);
                playerOverheadCamera =
                    overheadObject.AddComponent<CinemachineCamera>();
                playerOverheadCamera.Priority = StandbyPriority;
                PreparePlayerOverheadPose();
            }

            if (playerWideCamera == null)
            {
                var wideObject = new GameObject(RuntimePlayerWideCameraName)
                {
                    hideFlags = HideFlags.DontSave
                };
                wideObject.transform.SetParent(transform, false);
                playerWideCamera = wideObject.AddComponent<CinemachineCamera>();
                playerWideCamera.Priority = StandbyPriority;
                PreparePlayerWidePose();
            }

            if (combatSpectatorCamera == null)
            {
                var spectatorObject = new GameObject(RuntimeCombatSpectatorCameraName)
                {
                    hideFlags = HideFlags.DontSave
                };
                spectatorObject.transform.SetParent(transform, false);
                combatSpectatorCamera =
                    spectatorObject.AddComponent<CinemachineCamera>();
                combatSpectatorCamera.Priority = StandbyPriority;
                PrepareCombatSpectatorPose();
            }

            if (boardResourceEventCamera == null)
            {
                var eventCameraObject = new GameObject(
                    RuntimeBoardResourceEventCameraName)
                {
                    hideFlags = HideFlags.DontSave
                };
                eventCameraObject.transform.SetParent(transform, false);
                boardResourceEventCamera =
                    eventCameraObject.AddComponent<CinemachineCamera>();
                boardResourceEventCamera.Priority = StandbyPriority;
                PrepareBoardResourceEventPose();
            }
        }

        private void PrepareModePose(GameplayMode mode)
        {
            switch (mode)
            {
                case GameplayMode.FirstPerson:
                    PrepareFirstPersonPose();
                    break;
                case GameplayMode.BoardTopDown:
                    PrepareBoardPose();
                    break;
                case GameplayMode.CombatSpectator:
                    PrepareCombatSpectatorPose();
                    break;
                case GameplayMode.BoardResourceEvent:
                    PrepareBoardResourceEventPose();
                    break;
            }
        }

        private void PrepareFirstPersonPose()
        {
            var eye = ResolveLocalPlayerEye();
            if (firstPersonCamera == null
                || eye == null
                || eye == firstPersonCamera.transform)
            {
                return;
            }

            firstPersonCamera.ForceCameraPosition(
                eye.position,
                eye.rotation);

            // CinemachineBrain commonly evaluates earlier in LateUpdate. Apply
            // the same final pose to the render camera so a corrected network
            // body transform cannot leak through for one frame as visible shake.
            if (!_isTransitioning && activeMode == GameplayMode.FirstPerson &&
                outputCamera != null)
            {
                outputCamera.transform.SetPositionAndRotation(
                    eye.position,
                    eye.rotation);
            }
        }

        private void PreparePlayerOverheadPose()
        {
            var eye = ResolveLocalPlayerEye();
            if (playerOverheadCamera == null || eye == null)
                return;

            var lens = playerOverheadCamera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            lens.FieldOfView = Mathf.Clamp(
                playerOverheadFieldOfView,
                10f,
                120f);
            playerOverheadCamera.Lens = lens;

            var rotation = Quaternion.Euler(
                90f,
                eye.eulerAngles.y,
                0f);
            playerOverheadCamera.ForceCameraPosition(
                eye.position + Vector3.up * Mathf.Max(
                    0.1f,
                    playerOverheadHeight),
                rotation);
        }

        private void PreparePlayerWidePose()
        {
            var eye = ResolveLocalPlayerEye();
            if (playerWideCamera == null || eye == null)
                return;

            var pose = boardFramingAnchor != null
                ? boardFramingAnchor.Evaluate(GetOutputAspect())
                : fallbackBoardFraming.Sanitized().Evaluate(
                    fallbackBoardCenter, GetOutputAspect());
            var lens = playerWideCamera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            lens.FieldOfView = pose.FieldOfView;
            playerWideCamera.Lens = lens;
            playerWideCamera.ForceCameraPosition(
                eye.position + Vector3.up * pose.Height,
                pose.Rotation);
        }

        private void PrepareBoardPose()
        {
            if (boardTopDownCamera == null)
                return;

            var pose = boardFramingAnchor != null
                ? boardFramingAnchor.Evaluate(GetOutputAspect())
                : fallbackBoardFraming.Sanitized().Evaluate(
                    fallbackBoardCenter,
                    GetOutputAspect());

            var lens = boardTopDownCamera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            lens.FieldOfView = pose.FieldOfView;
            boardTopDownCamera.Lens = lens;
            boardTopDownCamera.ForceCameraPosition(
                pose.Position,
                pose.Rotation);
        }

        private void PrepareCombatSpectatorPose()
        {
            if (combatSpectatorCamera == null)
                return;

            var lens = combatSpectatorCamera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            lens.FieldOfView = Mathf.Clamp(combatSpectatorFieldOfView, 10f, 120f);
            combatSpectatorCamera.Lens = lens;

            var position = SharedCameraFraming.CalculatePosition(
                _combatSpectatorFocus,
                Mathf.Max(1f, combatSpectatorHeight));
            combatSpectatorCamera.ForceCameraPosition(
                position,
                SharedCameraFraming.Rotation);
        }

        private void PrepareBoardResourceEventPose()
        {
            if (boardResourceEventCamera == null ||
                _boardResourceEventFacing == null)
            {
                return;
            }

            CalculateBoardResourceEventPose(_boardResourceEventFacing, _boardResourceEventHead,
                _boardResourceEventFocusHeight, out var position, out var rotation, out var fieldOfView);
            var lens = boardResourceEventCamera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            lens.FieldOfView = fieldOfView;
            boardResourceEventCamera.Lens = lens;
            boardResourceEventCamera.ForceCameraPosition(position, rotation);
        }

        public void CalculateBoardResourceEventPose(Transform facing, Transform head, float focusHeight,
            out Vector3 position, out Quaternion rotation, out float fieldOfView)
        {
            var focus = head != null
                ? head.position
                : facing.position +
                  Vector3.up * PlayerAvatarVisual.StandingEyeHeight;
            focus += Vector3.up * focusHeight;
            var forward = Vector3.ProjectOnPlane(
                facing.forward,
                Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            position = focus +
                           forward.normalized * Mathf.Max(
                               0.5f,
                               boardResourceEventDistance) +
                           Vector3.up * boardResourceEventHeightOffset;
            var direction = focus - position;
            rotation = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : Quaternion.identity;
            fieldOfView = Mathf.Clamp(boardResourceEventFieldOfView, 10f, 120f);
        }

        private float GetOutputAspect()
        {
            if (outputCamera != null && outputCamera.aspect > 0.1f)
                return outputCamera.aspect;

            return Screen.height > 0
                ? Mathf.Max(0.1f, (float)Screen.width / Screen.height)
                : 16f / 9f;
        }

        private Transform ResolveLocalPlayerEye()
        {
            if (localPlayerEye != null)
                return localPlayerEye;

            if (firstPersonCamera == null)
                return null;

            return firstPersonCamera.Follow != null
                ? firstPersonCamera.Follow
                : firstPersonCamera.transform;
        }

        private CinemachineCamera GetModeCamera(GameplayMode mode)
        {
            switch (mode)
            {
                case GameplayMode.FirstPerson:
                    return firstPersonCamera;
                case GameplayMode.BoardTopDown:
                    return boardTopDownCamera;
                case GameplayMode.CombatSpectator:
                    return combatSpectatorCamera;
                case GameplayMode.Minigame:
                    return minigameCamera;
                case GameplayMode.BoardResourceEvent:
                    return boardResourceEventCamera;
                default:
                    return null;
            }
        }

        private void SetLiveCamera(CinemachineCamera incoming)
        {
            SetPriority(firstPersonCamera, StandbyPriority);
            SetPriority(boardTopDownCamera, StandbyPriority);
            SetPriority(minigameCamera, StandbyPriority);
            SetPriority(playerOverheadCamera, StandbyPriority);
            SetPriority(playerWideCamera, StandbyPriority);
            SetPriority(combatSpectatorCamera, StandbyPriority);
            SetPriority(boardResourceEventCamera, StandbyPriority);
            SetPriority(incoming, LivePriority);
        }

        private void ApplyPriorities(GameplayMode mode)
        {
            SetLiveCamera(GetModeCamera(mode));
        }

        private static void SetPriority(
            CinemachineCamera camera,
            int priority)
        {
            if (camera != null)
                camera.Priority = priority;
        }

        private void CaptureBrainSettings()
        {
            if (_brain == null || _brainSettingsCaptured)
                return;

            _savedDefaultBlend = _brain.DefaultBlend;
            _savedIgnoreTimeScale = _brain.IgnoreTimeScale;
            _brain.IgnoreTimeScale = true;
            _brainSettingsCaptured = true;
        }

        private void SetBrainBlend(float duration)
        {
            if (_brain == null)
                return;

            var safeDuration = Mathf.Max(0f, duration);
            var style = safeDuration <= 0f
                ? CinemachineBlendDefinition.Styles.Cut
                : CinemachineBlendDefinition.Styles.EaseInOut;
            _brain.DefaultBlend = new CinemachineBlendDefinition(
                style,
                safeDuration);
        }

        private void RestoreBrainSettings()
        {
            if (_brain == null || !_brainSettingsCaptured)
                return;

            _brain.DefaultBlend = _savedDefaultBlend;
            _brain.IgnoreTimeScale = _savedIgnoreTimeScale;
            _brainSettingsCaptured = false;
        }

        private void CancelTransition()
        {
            if (_transitionCoroutine != null)
                StopCoroutine(_transitionCoroutine);

            _transitionCoroutine = null;
            _isTransitioning = false;
            _drivingBoardPose = false;
            RestoreBrainSettings();
        }

        private void ApplyCursorPolicy()
        {
            // The common menu and the pause release button keep the pointer free.
            if (Application.isPlaying && LocalInputGate.ApplyPointerOverride())
            {
                return;
            }

            var pointerVisible =
                _uiPointerVisible
                || activeMode != GameplayMode.FirstPerson
                || (_isTransitioning && UsesPlayerBridge(_transitionFrom, _transitionTarget));
            Cursor.lockState = pointerVisible
                ? CursorLockMode.Confined
                : CursorLockMode.Locked;
            Cursor.visible = pointerVisible;
        }

        private void OnValidate()
        {
            boardToOverheadDuration = Mathf.Max(
                0f,
                boardToOverheadDuration);
            boardRecenterDuration = Mathf.Max(0f, boardRecenterDuration);
            overheadToFirstPersonDuration = Mathf.Max(
                0f,
                overheadToFirstPersonDuration);
            directTransitionDuration = Mathf.Max(
                0f,
                directTransitionDuration);
            playerOverheadHeight = Mathf.Max(
                0.1f,
                playerOverheadHeight);
            playerOverheadFieldOfView = Mathf.Clamp(
                playerOverheadFieldOfView,
                10f,
                120f);
            combatSpectatorHeight = Mathf.Max(1f, combatSpectatorHeight);
            combatSpectatorFieldOfView = Mathf.Clamp(
                combatSpectatorFieldOfView,
                10f,
                120f);
            boardResourceEventDistance = Mathf.Max(
                0.5f,
                boardResourceEventDistance);
            boardResourceEventFieldOfView = Mathf.Clamp(
                boardResourceEventFieldOfView,
                10f,
                120f);
            fallbackBoardFraming =
                fallbackBoardFraming.Sanitized();
        }
    }
}
