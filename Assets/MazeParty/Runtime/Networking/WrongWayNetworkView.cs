using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.WrongWay;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Client-side presentation for the server-authoritative WrongWay race.
    /// The network state owns progress and scoring while this component builds
    /// four visual runners, drives the race camera and renders the local HUD.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WrongWayNetworkView : MonoBehaviour
    {
        public const float CameraFieldOfView = 52f;
        public const float StartPlatformDepth = 2.2f;
        public const float FinishPlatformDepth = 2f;
        public const float RunnerInterpolationSpeed = 14f;

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

        private readonly RunnerView[] _runners =
            new RunnerView[WrongWayRules.PlayerCount];

        private GameplayCameraDirector _cameraDirector;
        private int _localSlot = -1;
        private bool _cameraConfigured;
        private bool _cameraRegistered;
        private Vector3 _cameraFocus;
        private bool _hasCameraFocus;
        private bool _hudContractErrorLogged;

        public static float CourseLength =>
            WrongWayRules.StepCount * NetworkWrongWayState.StepDepth;

        public static float CourseHeight =>
            WrongWayRules.StepCount * NetworkWrongWayState.StepHeight;

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
                progress * NetworkWrongWayState.StepHeight,
                (progress - 0.5f) * NetworkWrongWayState.StepDepth);
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
                    progress * NetworkWrongWayState.StepHeight,
                    (progress - 0.5f) * NetworkWrongWayState.StepDepth);

            return runnerPosition + new Vector3(0f, 1.35f, 1.6f);
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
            var shouldShowHud = shouldShowWorld &&
                                match.IsWrongWayPlaying;

            SetHudActive(shouldShowHud);

            if (!shouldShowWorld)
            {
                SetWorldPresentationActive(false);
                return;
            }

            SetWorldPresentationActive(true);
            ResolveLocalSlot(match);
            RefreshRunners(match);
            RefreshRaceCamera();
            RefreshHud(match);
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
            visual.SetDisplayName("Player " + (slot + 1));
            visual.SetOwnerFirstPerson(false);

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

            if (resolved == _localSlot)
            {
                return;
            }

            _localSlot = resolved;
            for (var slot = 0; slot < _runners.Length; slot++)
            {
                _runners[slot]?.Visual.SetTopViewHighlight(
                    slot == _localSlot);
            }
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
                runner.LastProgress = progress;
                runner.Visual.SetEliminated(state.IsRecovering(slot));

                var avatar = match.GetAvatarForSlot(slot);
                if (avatar != null)
                {
                    var appearance = avatar.Appearance;
                    runner.Visual.SetBodyColor(appearance.BodyColor);
                    runner.Visual.ApplyAppearance(
                        appearance.EyeId,
                        appearance.MouthId,
                        appearance.HatId);
                    runner.Visual.SetDisplayName(avatar.DisplayName);
                }
                else
                {
                    runner.Visual.SetBodyColor(FallbackPlayerColors[slot]);
                    runner.Visual.SetDisplayName("Player " + (slot + 1));
                }
            }
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

            var leadingProgress = 0;
            for (var slot = 0; slot < WrongWayRules.PlayerCount; slot++)
            {
                leadingProgress = Mathf.Max(
                    leadingProgress,
                    state.GetProgress(slot));
            }

            var targetFocus = CalculateCameraFocus(leadingProgress);
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

        private void RefreshHud(NetworkMatchState match)
        {
            if (hud == null || !hud.HasRequiredReferences)
            {
                return;
            }

            hud.PhaseText.text = BuildPhaseLabel();
            hud.PromptText.text = BuildLocalPrompt();
            var progressRows = hud.ProgressRows;

            for (var slot = 0; slot < progressRows.Length; slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                var displayName = avatar != null &&
                                  !string.IsNullOrWhiteSpace(
                                      avatar.DisplayName)
                    ? avatar.DisplayName
                    : "Player " + (slot + 1);
                var rank = ResolveDisplayedRank(slot);
                var prefix = slot == _localSlot ? ">  " : "   ";
                progressRows[slot].text =
                    prefix +
                    MinigameDisplayFormatter.ToOrdinal(rank) +
                    "   " +
                    displayName +
                    "   STEP " +
                    state.GetProgress(slot) +
                    " / " +
                    WrongWayRules.StepCount +
                    "   SCORE " +
                    state.GetScore(slot);

                progressRows[slot].color = avatar != null
                    ? avatar.Appearance.BodyColor
                    : hud.GetDefaultProgressRowColor(slot);
            }
        }

        private string BuildPhaseLabel()
        {
            var totalRounds =
                MinigameCatalog.GetRoundCount(
                    ScheduledMinigameId.WrongWay);
            var round = Mathf.Clamp(
                state.RoundNumber,
                1,
                totalRounds);
            var pauseSuffix = state.IsPaused ? "  ·  PAUSED" : string.Empty;

            switch (state.Phase)
            {
                case NetworkWrongWayPhase.Countdown:
                    return "WRONG WAY  ·  ROUND " +
                           round +
                           " / " +
                           totalRounds +
                           "  ·  START IN " +
                           Mathf.CeilToInt((float)state.Remaining) +
                           pauseSuffix;
                case NetworkWrongWayPhase.Running:
                    return "WRONG WAY  ·  ROUND " +
                           round +
                           " / " +
                           totalRounds +
                           "  ·  " +
                           state.Remaining.ToString("0.0") +
                           "s" +
                           pauseSuffix;
                case NetworkWrongWayPhase.RoundResult:
                    return "ROUND " +
                           round +
                           " RESULTS  ·  " +
                           state.Remaining.ToString("0.0") +
                           "s" +
                           pauseSuffix;
                case NetworkWrongWayPhase.Complete:
                    return "WRONG WAY  ·  FINAL RESULTS";
                default:
                    return "WRONG WAY";
            }
        }

        private string BuildLocalPrompt()
        {
            if (_localSlot < 0 ||
                _localSlot >= WrongWayRules.PlayerCount)
            {
                return "WAITING FOR LOCAL PLAYER";
            }

            if (state.IsPaused)
            {
                return "PAUSED";
            }

            switch (state.Phase)
            {
                case NetworkWrongWayPhase.Countdown:
                    return Mathf.Max(
                        1,
                        Mathf.CeilToInt((float)state.Remaining)).ToString();
                case NetworkWrongWayPhase.Running:
                    if (state.IsRecovering(_localSlot))
                    {
                        return "WRONG!  GET UP...";
                    }

                    if (state.GetProgress(_localSlot) >=
                        WrongWayRules.StepCount)
                    {
                        return "FINISH!";
                    }

                    return DirectionLabel(
                        state.GetCurrentDirection(_localSlot));
                case NetworkWrongWayPhase.RoundResult:
                    return "ROUND " +
                           MinigameDisplayFormatter.ToOrdinal(
                               state.GetRoundRank(_localSlot)) +
                           "  ·  +" +
                           state.GetRoundPoints(_localSlot) +
                           " POINTS";
                case NetworkWrongWayPhase.Complete:
                    return "FINAL " +
                           MinigameDisplayFormatter.ToOrdinal(
                               state.GetFinalRank(_localSlot));
                default:
                    return "GET READY";
            }
        }

        private int ResolveDisplayedRank(int slot)
        {
            var finalRank = state.GetFinalRank(slot);
            if (finalRank > 0)
            {
                return finalRank;
            }

            var roundRank = state.GetRoundRank(slot);
            if (state.Phase == NetworkWrongWayPhase.RoundResult &&
                roundRank > 0)
            {
                return roundRank;
            }

            var progress = state.GetProgress(slot);
            var rank = 1;
            for (var other = 0;
                 other < WrongWayRules.PlayerCount;
                 other++)
            {
                if (other == slot)
                {
                    continue;
                }

                var otherProgress = state.GetProgress(other);
                if (otherProgress > progress ||
                    (otherProgress == progress && other < slot))
                {
                    rank++;
                }
            }

            return rank;
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
                }
            }
        }

        private static string DirectionLabel(WrongWayDirection direction)
        {
            switch (direction)
            {
                case WrongWayDirection.Up:
                    return "W    ↑";
                case WrongWayDirection.Down:
                    return "S    ↓";
                case WrongWayDirection.Left:
                    return "A    ←";
                case WrongWayDirection.Right:
                    return "D    →";
                default:
                    return "?";
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
        }
    }
}
