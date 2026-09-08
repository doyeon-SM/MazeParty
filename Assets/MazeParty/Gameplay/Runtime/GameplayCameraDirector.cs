using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Gameplay
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class GameplayCameraDirector : MonoBehaviour, IGameplayCameraService
    {
        private const int LivePriority = 100;
        private const int StandbyPriority = 0;
        private const string RuntimeOverheadCameraName = "CM_PlayerOverhead (Runtime)";

        [Header("Output")]
        [SerializeField] private Camera outputCamera;

        [Header("Gameplay Cameras")]
        [SerializeField] private CinemachineCamera firstPersonCamera;
        [SerializeField] private CinemachineCamera boardTopDownCamera;
        [SerializeField] private CinemachineCamera minigameCamera;
        [SerializeField] private CinemachineCamera playerOverheadCamera;
        [SerializeField] private Transform localPlayerEye;
        [SerializeField] private GameplayMode activeMode = GameplayMode.BoardTopDown;

        [Header("Board Framing")]
        [SerializeField] private BoardCameraFramingAnchor boardFramingAnchor;
        [SerializeField] private Vector3 fallbackBoardCenter;
        [SerializeField] private BoardCameraFramingSettings fallbackBoardFraming =
            BoardCameraFramingSettings.Default;

        [Header("Transition Timing")]
        [SerializeField, Min(0f)] private float boardToOverheadDuration = 0.35f;
        [SerializeField, Min(0f)] private float overheadToFirstPersonDuration = 0.65f;
        [SerializeField, Min(0f)] private float directTransitionDuration = 0.4f;

        [Header("Player Overhead")]
        [SerializeField, Min(0.1f)] private float playerOverheadHeight = 8f;
        [SerializeField, Range(10f, 120f)] private float playerOverheadFieldOfView = 50f;

        private CinemachineBrain _brain;
        private Coroutine _transitionCoroutine;
        private CinemachineBlendDefinition _savedDefaultBlend;
        private bool _savedIgnoreTimeScale;
        private bool _brainSettingsCaptured;
        private bool _isTransitioning;
        private bool _hasCompletedMode;
        private bool _uiPointerVisible;
        private GameplayMode _completedMode;
        private GameplayMode _transitionFrom;
        private GameplayMode _transitionTarget;

        public event Action<GameplayMode, GameplayMode> TransitionStarted;
        public event Action<GameplayMode, GameplayMode> TransitionCompleted;

        public GameplayMode ActiveMode => activeMode;
        public GameplayMode CompletedMode => _hasCompletedMode ? _completedMode : activeMode;
        public Camera OutputCamera => outputCamera;
        public Transform LocalPlayerEye => ResolveLocalPlayerEye();
        public BoardCameraFramingAnchor BoardFramingAnchor => boardFramingAnchor;
        public bool IsTransitioning => _isTransitioning;

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

        public void SwitchTo(GameplayMode mode)
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
                RunTransition(previous, mode));
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

            if (!_isTransitioning)
                return;

            if (UsesPlayerBridge(_transitionFrom, _transitionTarget))
            {
                PrepareFirstPersonPose();
                PreparePlayerOverheadPose();
            }
        }

        private void OnDisable()
        {
            CancelTransition();
            _completedMode = activeMode;
            _hasCompletedMode = true;
            ApplyPriorities(activeMode);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private IEnumerator RunTransition(
            GameplayMode previous,
            GameplayMode target)
        {
            _transitionFrom = previous;
            _transitionTarget = target;
            _isTransitioning = true;
            CaptureBrainSettings();
            TransitionStarted?.Invoke(previous, target);

            if (previous == GameplayMode.BoardTopDown
                && target == GameplayMode.FirstPerson)
            {
                PreparePlayerOverheadPose();
                yield return BlendTo(
                    playerOverheadCamera,
                    boardToOverheadDuration);

                PrepareFirstPersonPose();
                yield return BlendTo(
                    firstPersonCamera,
                    overheadToFirstPersonDuration);
            }
            else if (previous == GameplayMode.FirstPerson
                && target == GameplayMode.BoardTopDown)
            {
                PreparePlayerOverheadPose();
                yield return BlendTo(
                    playerOverheadCamera,
                    overheadToFirstPersonDuration);

                PrepareBoardPose();
                yield return BlendTo(
                    boardTopDownCamera,
                    boardToOverheadDuration);
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
            TransitionCompleted?.Invoke(previous, target);
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
                || playerOverheadCamera != null;
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

            if (playerOverheadCamera != null || !Application.isPlaying)
                return;

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
                case GameplayMode.Minigame:
                    return minigameCamera;
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
            RestoreBrainSettings();
        }

        private void ApplyCursorPolicy()
        {
            var pointerVisible =
                _uiPointerVisible
                || activeMode != GameplayMode.FirstPerson;
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
            fallbackBoardFraming =
                fallbackBoardFraming.Sanitized();
        }
    }
}
