using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.WrongWay;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Client-side presentation for the server-authoritative WrongWay race.
    /// The network state owns progress and scoring while this component builds
    /// four visual runners, drives the local runner camera and renders only
    /// the next direction icon needed by this client.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WrongWayNetworkView : MonoBehaviour
    {
        public const float CameraFieldOfView = 52f;
        public const float StartPlatformDepth = 2.2f;
        public const float FinishPlatformDepth = 2f;
        public const float RunnerInterpolationSpeed = 14f;
        private const float LocalCameraFocusHeight = 0.85f;
        private const float LocalCameraLookAheadDistance = 0.65f;

        private static readonly Color[] FallbackPlayerColors =
        {
            new Color(0.18f, 0.62f, 1f),
            new Color(1f, 0.32f, 0.24f),
            new Color(0.25f, 0.86f, 0.42f),
            new Color(0.72f, 0.38f, 1f)
        };

        [SerializeField] private NetworkWrongWayState state;
        [SerializeField] private CinemachineCamera raceCamera;
        [SerializeField] private Transform runnerRoot;
        [SerializeField] private GameObject arenaPresentation;
        [SerializeField] private WrongWayHudBindings hud;
        [SerializeField] private GameObject progressVfxPrefab;
        [SerializeField] private GameObject finishVfxPrefab;

        private readonly RunnerView[] _runners =
            new RunnerView[WrongWayRules.PlayerCount];

        private GameplayCameraDirector _cameraDirector;
        private int _localSlot = -1;
        private bool _cameraConfigured;
        private bool _cameraRegistered;
        private Vector3 _cameraFocus;
        private bool _hasCameraFocus;
        private bool _hudContractErrorLogged;

        public GameObject ProgressVfxPrefab => progressVfxPrefab;
        public GameObject FinishVfxPrefab => finishVfxPrefab;

        public static float CourseLength =>
            WrongWayRules.StepCount * NetworkWrongWayState.StepDepth;

        public static float StepRise =>
            NetworkWrongWayState.StepHeight * 0.5f;

        public static float CourseHeight =>
            GetRunnerGroundHeight(WrongWayRules.StepCount);

        public static float GetLaneX(int playerSlot)
        {
            var clampedSlot = Mathf.Clamp(
                playerSlot,
                0,
                WrongWayRules.PlayerCount - 1);
            return NetworkWrongWayState.ArenaCenterX +
                   (clampedSlot - (WrongWayRules.PlayerCount - 1) * 0.5f) *
                   NetworkWrongWayState.LaneSpacing;
        }

        public static Vector3 GetRunnerWorldPosition(
            int playerSlot,
            int completedSteps)
        {
            var progress = Mathf.Clamp(
                completedSteps,
                0,
                WrongWayRules.StepCount);
            if (progress == 0)
            {
                return new Vector3(
                    GetLaneX(playerSlot),
                    0f,
                    -StartPlatformDepth * 0.5f);
            }

            return new Vector3(
                GetLaneX(playerSlot),
                GetRunnerGroundHeight(progress),
                (progress - 0.5f) * NetworkWrongWayState.StepDepth);
        }

        public static float GetRunnerGroundHeight(int completedSteps)
        {
            var progress = Mathf.Clamp(
                completedSteps,
                0,
                WrongWayRules.StepCount);
            return progress == 0
                ? 0f
                : (progress + 1) * StepRise;
        }

        public static Vector3 CalculateCameraFocus(int leadingProgress)
        {
            var progress = Mathf.Clamp(
                leadingProgress,
                0,
                WrongWayRules.StepCount);
            var runnerPosition = progress == 0
                ? new Vector3(
                    NetworkWrongWayState.ArenaCenterX,
                    0f,
                    -StartPlatformDepth * 0.5f)
                : new Vector3(
                    NetworkWrongWayState.ArenaCenterX,
                    GetRunnerGroundHeight(progress),
                    (progress - 0.5f) * NetworkWrongWayState.StepDepth);

            return CalculateLocalCameraFocus(runnerPosition);
        }

        private static Vector3 CalculateLocalCameraFocus(
            Vector3 runnerPosition)
        {
            return runnerPosition + new Vector3(
                0f,
                LocalCameraFocusHeight,
                LocalCameraLookAheadDistance);
        }

        public static Vector3 CalculateCameraPosition(Vector3 focus)
        {
            return focus + new Vector3(0f, 8.5f, -12.5f);
        }

        public static Quaternion CalculateCameraRotation(Vector3 focus)
        {
            var direction = focus - CalculateCameraPosition(focus);
            return Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        public void ConfigureVfx(
            GameObject progressPrefab,
            GameObject finishPrefab)
        {
            progressVfxPrefab = progressPrefab;
            finishVfxPrefab = finishPrefab;
        }

        private void Awake()
        {
            ResolveSceneReferences();
            ConfigureCamera();
            EnsurePresentation();
            SetWorldPresentationActive(false);
            SetHudActive(false);
        }

        private void OnEnable()
        {
            ResolveSceneReferences();
        }

        private void OnDisable()
        {
            SetWorldPresentationActive(false);
            SetHudActive(false);
            UnregisterCamera();
        }

        private void Update()
        {
            if (state == null)
            {
                state = GetComponent<NetworkWrongWayState>();
            }

            EnsurePresentation();

            var match = NetworkMatchState.Instance;
            var selected = match != null && match.IsWrongWayPhase;
            UpdateCameraRegistration(selected);

            var shouldShowWorld = selected &&
                                  state != null &&
                                  state.IsSpawned &&
                                  state.Phase != NetworkWrongWayPhase.Inactive;
            if (!shouldShowWorld)
            {
                SetWorldPresentationActive(false);
                SetHudActive(false);
                return;
            }

            SetWorldPresentationActive(true);
            ResolveLocalSlot(match);
            RefreshRunners(match);
            RefreshRaceCamera();

            var shouldShowDirection = match.IsWrongWayPlaying &&
                                      state.Phase ==
                                      NetworkWrongWayPhase.Running &&
                                      !state.IsPaused &&
                                      _localSlot >= 0 &&
                                      _localSlot < WrongWayRules.PlayerCount &&
                                      state.GetProgress(_localSlot) <
                                      WrongWayRules.StepCount;
            SetHudActive(shouldShowDirection);
            if (shouldShowDirection)
            {
                RefreshHud();
            }
        }

        private void SetHudActive(bool active)
        {
            if (hud != null &&
                hud.Canvas != null &&
                hud.Canvas.gameObject.activeSelf != active)
            {
                hud.Canvas.gameObject.SetActive(active);
            }
        }

        private void ResolveSceneReferences()
        {
            if (state == null)
            {
                state = GetComponent<NetworkWrongWayState>();
            }
            if (raceCamera == null)
            {
                raceCamera = GetComponentInChildren<CinemachineCamera>(true);
            }
            if (arenaPresentation == null)
            {
                var arena = FindDescendant(transform, "Arena Presentation");
                arenaPresentation = arena != null ? arena.gameObject : null;
            }
            if (runnerRoot == null)
            {
                runnerRoot = EnsureChild(transform, "Runtime Runners");
            }
        }

        private void EnsurePresentation()
        {
            ResolveSceneReferences();
            for (var slot = 0; slot < _runners.Length; slot++)
            {
                if (_runners[slot] == null)
                {
                    _runners[slot] = CreateRunner(slot);
                }
            }

            if ((hud == null || !hud.HasRequiredReferences) &&
                !_hudContractErrorLogged)
            {
                Debug.LogError(
                    "WrongWayNetworkView requires a connected " +
                    "WrongWayHud.prefab instance with complete bindings.",
                    this);
                _hudContractErrorLogged = true;
            }
        }

        private RunnerView CreateRunner(int slot)
        {
            var runnerObject = new GameObject(
                "WrongWay Runner " + (slot + 1));
            runnerObject.transform.SetParent(runnerRoot, false);
            runnerObject.transform.position =
                GetRunnerWorldPosition(slot, 0);
            runnerObject.transform.rotation = Quaternion.identity;

            var visual = runnerObject.AddComponent<PlayerAvatarVisual>();
            visual.EnsureBuilt();
            visual.SetBodyColor(FallbackPlayerColors[slot]);
            visual.SetDisplayName(GameText.F("Player {0}", slot + 1));
            visual.SetOwnerFirstPerson(false);
            visual.SetTopViewHighlight(false);

            var colliders = runnerObject.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }

            return new RunnerView(runnerObject.transform, visual);
        }

        private void ResolveLocalSlot(NetworkMatchState match)
        {
            var resolved = -1;
            for (var slot = 0; slot < _runners.Length; slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                if (avatar != null && avatar.IsOwner)
                {
                    resolved = slot;
                    break;
                }
            }

            _localSlot = resolved;
        }

        private void RefreshRunners(NetworkMatchState match)
        {
            for (var slot = 0; slot < _runners.Length; slot++)
            {
                var runner = _runners[slot];
                if (runner == null)
                {
                    continue;
                }

                var progress = state.GetProgress(slot);
                var target = GetRunnerWorldPosition(slot, progress);
                if (!runner.HasPosition ||
                    progress < runner.LastProgress ||
                    Vector3.SqrMagnitude(runner.Root.position - target) > 36f ||
                    state.Phase == NetworkWrongWayPhase.Countdown)
                {
                    runner.Root.position = target;
                    runner.HasPosition = true;
                }
                else
                {
                    runner.Root.position = Vector3.Lerp(
                        runner.Root.position,
                        target,
                        1f - Mathf.Exp(
                            -RunnerInterpolationSpeed *
                            Time.unscaledDeltaTime));
                }

                runner.Root.rotation = Quaternion.identity;
                var recovering = state.IsRecovering(slot);
                runner.Visual.SetEliminated(recovering);

                var avatar = match.GetAvatarForSlot(slot);
                if (avatar != null)
                {
                    var appearance = avatar.Appearance;
                    runner.Visual.SetBodyColor(appearance.BodyColor);
                    runner.Visual.ApplyAppearance(
                        appearance.EyeId,
                        appearance.MouthId,
                        appearance.HatId,
                        appearance.ExpressionId);
                    runner.Visual.SetDisplayName(
                        string.IsNullOrWhiteSpace(avatar.DisplayName)
                            ? GameText.F("Player {0}", slot + 1)
                            : avatar.DisplayName);
                }
                else
                {
                    runner.Visual.SetBodyColor(FallbackPlayerColors[slot]);
                    runner.Visual.SetDisplayName(
                        GameText.F("Player {0}", slot + 1));
                }

                RefreshRunnerVfx(runner, progress, recovering);
                runner.LastProgress = progress;
            }
        }

        private void RefreshRunnerVfx(
            RunnerView runner,
            int progress,
            bool recovering)
        {
            var finished = progress >= WrongWayRules.StepCount;
            if (!runner.HasVfxBaseline)
            {
                runner.HasVfxBaseline = true;
                runner.WasRecovering = recovering;
                runner.WasFinished = finished;
                return;
            }

            var scale = 0f;
            var height = 0.35f;
            var prefab = progressVfxPrefab;
            if (finished && !runner.WasFinished)
            {
                scale = 1.1f;
                height = 0.9f;
                prefab = finishVfxPrefab;
            }
            else if (recovering && !runner.WasRecovering)
            {
                scale = 0.8f;
                height = 0.65f;
            }
            else if (progress > runner.LastProgress)
            {
                scale = 0.5f;
            }

            if (scale > 0f && prefab != null)
            {
                OneShotVfxPool.Play(
                    prefab,
                    runner.Root.position + Vector3.up * height,
                    Quaternion.identity,
                    scale);
            }

            runner.WasRecovering = recovering;
            runner.WasFinished = finished;
        }

        private void ConfigureCamera()
        {
            if (raceCamera == null)
            {
                return;
            }

            var lens = raceCamera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            lens.FieldOfView = CameraFieldOfView;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 150f;
            raceCamera.Lens = lens;

            var focus = CalculateCameraFocus(0);
            raceCamera.ForceCameraPosition(
                CalculateCameraPosition(focus),
                CalculateCameraRotation(focus));
            raceCamera.Priority = 0;
            _cameraConfigured = true;
        }

        private void UpdateCameraRegistration(bool selected)
        {
            if (_cameraDirector == null)
            {
                _cameraDirector =
                    FindAnyObjectByType<GameplayCameraDirector>();
            }

            if (selected)
            {
                if (!_cameraConfigured)
                {
                    ConfigureCamera();
                }

                if (!_cameraRegistered &&
                    _cameraDirector != null &&
                    raceCamera != null)
                {
                    _cameraDirector.SetMinigameCamera(raceCamera);
                    _cameraRegistered = true;
                }
            }
            else
            {
                UnregisterCamera();
            }
        }

        private void UnregisterCamera()
        {
            if (_cameraRegistered &&
                _cameraDirector != null &&
                raceCamera != null)
            {
                _cameraDirector.ClearMinigameCamera(raceCamera);
            }

            if (raceCamera != null)
            {
                raceCamera.Priority = 0;
            }
            _cameraRegistered = false;
        }

        private void RefreshRaceCamera()
        {
            if (raceCamera == null)
            {
                return;
            }

            var hasLocalRunner = _localSlot >= 0 &&
                                 _localSlot < WrongWayRules.PlayerCount &&
                                 _runners[_localSlot]?.Root != null;
            var targetFocus = hasLocalRunner
                ? CalculateLocalCameraFocus(
                    _runners[_localSlot].Root.position)
                : CalculateCameraFocus(0);
            if (!_hasCameraFocus ||
                Vector3.SqrMagnitude(_cameraFocus - targetFocus) > 100f ||
                state.Phase == NetworkWrongWayPhase.Countdown)
            {
                _cameraFocus = targetFocus;
                _hasCameraFocus = true;
            }
            else
            {
                _cameraFocus = Vector3.Lerp(
                    _cameraFocus,
                    targetFocus,
                    1f - Mathf.Exp(
                        -5f * Time.unscaledDeltaTime));
            }

            raceCamera.ForceCameraPosition(
                CalculateCameraPosition(_cameraFocus),
                CalculateCameraRotation(_cameraFocus));
        }

        private void RefreshHud()
        {
            if (hud == null || !hud.HasRequiredReferences)
            {
                return;
            }

            hud.DirectionIcon.sprite = hud.GetDirectionIcon(
                state.GetCurrentDirection(_localSlot));
        }

        private void SetWorldPresentationActive(bool active)
        {
            if (arenaPresentation != null &&
                arenaPresentation.activeSelf != active)
            {
                arenaPresentation.SetActive(active);
            }

            if (runnerRoot != null &&
                runnerRoot.gameObject.activeSelf != active)
            {
                runnerRoot.gameObject.SetActive(active);
            }

            if (!active)
            {
                if (hud != null &&
                    hud.Canvas != null &&
                    hud.Canvas.gameObject.activeSelf)
                {
                    hud.Canvas.gameObject.SetActive(false);
                }

                _hasCameraFocus = false;
                for (var slot = 0; slot < _runners.Length; slot++)
                {
                    _runners[slot]?.Visual.SetEliminated(false);
                    if (_runners[slot] != null)
                    {
                        _runners[slot].HasVfxBaseline = false;
                    }
                }
            }
        }

        private static Transform EnsureChild(
            Transform parent,
            string childName)
        {
            var existing = parent.Find(childName);
            if (existing != null)
            {
                return existing;
            }

            var child = new GameObject(childName).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static Transform FindDescendant(
            Transform root,
            string childName)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == childName)
            {
                return root;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindDescendant(
                    root.GetChild(index),
                    childName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private sealed class RunnerView
        {
            public RunnerView(
                Transform root,
                PlayerAvatarVisual visual)
            {
                Root = root;
                Visual = visual;
                LastProgress = -1;
            }

            public Transform Root { get; }
            public PlayerAvatarVisual Visual { get; }
            public bool HasPosition { get; set; }
            public int LastProgress { get; set; }
            public bool HasVfxBaseline { get; set; }
            public bool WasRecovering { get; set; }
            public bool WasFinished { get; set; }
        }
    }
}
