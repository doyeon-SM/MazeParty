using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.RedLightGreenLight;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Client presentation for the replicated Red Light / Green Light race.
    /// The scene owns no output Camera; its Cinemachine camera is registered
    /// with the Board camera director while this minigame is selected.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RedLightGreenLightNetworkView : MonoBehaviour
    {
        public const float PlayerCameraHeight = 18f;
        public const float PlayerCameraOrthographicSize = 9f;
        public const float PlayerCameraTiltDegrees = 10f;

        private const float RunnerInterpolationSpeed = 16f;

        private static readonly Color[] FallbackPlayerColors =
        {
            new Color(0.16f, 0.48f, 0.95f),
            new Color(0.92f, 0.2f, 0.16f),
            new Color(0.18f, 0.78f, 0.32f),
            new Color(0.7f, 0.26f, 0.9f)
        };

        [SerializeField] private NetworkRedLightGreenLightState state;
        [SerializeField] private CinemachineCamera topDownCamera;
        [SerializeField] private Transform runnerRoot;
        [SerializeField] private GameObject arenaPresentation;
        [SerializeField] private Transform observerHead;
        [SerializeField] private Renderer greenSignalRenderer;
        [SerializeField] private Renderer redSignalRenderer;
        [SerializeField] private Light greenSignalLight;
        [SerializeField] private Light redSignalLight;
        [SerializeField] private AudioSource cueAudioSource;
        [SerializeField] private AudioClip greenCue;
        [SerializeField] private AudioClip turnWarningCue;
        [SerializeField] private AudioClip redCue;
        [SerializeField] private RedLightGreenLightHudBindings hud;

        private readonly RunnerView[] _runners =
            new RunnerView[RedLightGreenLightRules.PlayerCount];
        private MaterialPropertyBlock _signalProperties;

        private GameplayCameraDirector _cameraDirector;
        private bool _cameraConfigured;
        private bool _hudDefaultsCaptured;
        private string _defaultInstructionText = string.Empty;
        private int _localSlot = -1;
        private RedLightGreenLightSignalPhase _lastSignalPhase =
            (RedLightGreenLightSignalPhase)byte.MaxValue;

        public static Quaternion PlayerCameraRotation => Quaternion.Euler(
            90f - PlayerCameraTiltDegrees,
            0f,
            0f);

        public static Vector3 CalculatePlayerCameraPosition(Vector3 focus)
        {
            var backwardOffset = Mathf.Tan(
                PlayerCameraTiltDegrees * Mathf.Deg2Rad) *
                PlayerCameraHeight;
            return focus +
                   Vector3.up * PlayerCameraHeight +
                   Vector3.back * backwardOffset;
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
                state = GetComponent<NetworkRedLightGreenLightState>();
            }
            EnsurePresentation();

            var match = NetworkMatchState.Instance;
            var selected = match != null &&
                           match.IsRedLightGreenLightPhase;
            if (selected)
            {
                RegisterCamera();
            }
            else
            {
                UnregisterCamera();
            }

            var shouldShowWorld = state != null && state.IsSpawned &&
                                  match != null && selected &&
                                  (match.FlowState ==
                                       BoardFlowState.MinigamePlaying ||
                                   match.FlowState ==
                                       BoardFlowState.SkippedResult);
            var shouldShowHud = shouldShowWorld &&
                                match.FlowState ==
                                BoardFlowState.MinigamePlaying;
            SetHudActive(shouldShowHud);

            if (!shouldShowWorld)
            {
                SetWorldPresentationActive(false);
                return;
            }

            SetWorldPresentationActive(true);
            ResolveLocalSlot(match);
            RefreshRunners(match);
            RefreshPlayerCamera();
            RefreshSignalPresentation();
            RefreshHud(match);
        }

        private void ResolveSceneReferences()
        {
            state ??= GetComponent<NetworkRedLightGreenLightState>();
            topDownCamera ??=
                GetComponentInChildren<CinemachineCamera>(true);
            runnerRoot ??= EnsureChild(transform, "Runtime Runners");

            if (arenaPresentation == null)
            {
                var arena = FindDescendant(
                    transform,
                    "Arena Presentation");
                arenaPresentation = arena != null
                    ? arena.gameObject
                    : null;
            }
            observerHead ??= FindDescendant(transform, "Observer Head");
            greenSignalRenderer ??= FindRenderer(
                transform,
                "Green Signal");
            redSignalRenderer ??= FindRenderer(
                transform,
                "Red Signal");
            greenSignalLight ??= FindLight(
                transform,
                "Green Signal Light");
            redSignalLight ??= FindLight(
                transform,
                "Red Signal Light");
            cueAudioSource ??= GetComponentInChildren<AudioSource>(true);
            if (!_hudDefaultsCaptured &&
                hud != null &&
                hud.InstructionText != null)
            {
                _defaultInstructionText = hud.InstructionText.text;
                _hudDefaultsCaptured = true;
            }
        }

        private void ConfigureCamera()
        {
            if (topDownCamera == null)
            {
                return;
            }

            var lens = topDownCamera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize = PlayerCameraOrthographicSize;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 150f;
            topDownCamera.Lens = lens;
            topDownCamera.ForceCameraPosition(
                CalculatePlayerCameraPosition(new Vector3(
                    NetworkRedLightGreenLightState.ArenaCenterX,
                    0f,
                    0f)),
                PlayerCameraRotation);
            topDownCamera.Priority = 0;
            _cameraConfigured = true;
        }

        private void RegisterCamera()
        {
            _cameraDirector ??=
                FindAnyObjectByType<GameplayCameraDirector>();
            if (!_cameraConfigured)
            {
                ConfigureCamera();
            }
            if (_cameraDirector != null && topDownCamera != null)
            {
                _cameraDirector.SetMinigameCamera(topDownCamera);
            }
        }

        private void UnregisterCamera()
        {
            if (_cameraDirector != null && topDownCamera != null)
            {
                _cameraDirector.ClearMinigameCamera(topDownCamera);
            }
            if (topDownCamera != null)
            {
                topDownCamera.Priority = 0;
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
        }

        private RunnerView CreateRunner(int slot)
        {
            var runnerObject = new GameObject("Runner " + (slot + 1));
            runnerObject.transform.SetParent(runnerRoot, false);

            var visual = runnerObject.AddComponent<PlayerAvatarVisual>();
            visual.EnsureBuilt();
            visual.SetOwnerFirstPerson(false);
            visual.SetBodyColor(FallbackPlayerColors[slot]);
            visual.SetDisplayName("Player " + (slot + 1));

            var presentation = runnerObject.AddComponent<
                RedLightGreenLightPlayerPresentation>();
            presentation.ApplyState(0, false);
            DisableGeneratedHitColliders(runnerObject);

            return new RunnerView(
                runnerObject.transform,
                visual,
                presentation);
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
                var target = state.GetRunnerPosition(slot);
                if (!runner.HasPosition ||
                    state.Phase ==
                        NetworkRedLightGreenLightPhase.Countdown ||
                    Vector3.SqrMagnitude(runner.Root.position - target) >
                        64f)
                {
                    runner.Root.position = target;
                    runner.HasPosition = true;
                }
                else
                {
                    var previous = runner.Root.position;
                    runner.Root.position = Vector3.Lerp(
                        previous,
                        target,
                        1f - Mathf.Exp(
                            -RunnerInterpolationSpeed *
                            Time.unscaledDeltaTime));
                    var direction = runner.Root.position - previous;
                    direction.y = 0f;
                    if (direction.sqrMagnitude > 0.00001f)
                    {
                        runner.Root.rotation = Quaternion.LookRotation(
                            direction.normalized,
                            Vector3.up);
                    }
                }

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

                var playerState = state.GetPlayerState(slot);
                var violationCount = state.GetViolationCount(slot);
                if (playerState != runner.LastState ||
                    violationCount != runner.LastViolationCount)
                {
                    runner.Presentation.ApplyState(
                        violationCount,
                        playerState ==
                            RedLightGreenLightPlayerState.Eliminated);
                    runner.LastState = playerState;
                    runner.LastViolationCount = violationCount;
                }
            }
        }

        private void RefreshPlayerCamera()
        {
            if (topDownCamera == null || _localSlot < 0 ||
                _localSlot >= _runners.Length)
            {
                return;
            }

            var runner = _runners[_localSlot];
            if (runner?.Root == null)
            {
                return;
            }
            topDownCamera.ForceCameraPosition(
                CalculatePlayerCameraPosition(runner.Root.position),
                PlayerCameraRotation);
        }

        private void RefreshSignalPresentation()
        {
            var phase = state.Phase ==
                        NetworkRedLightGreenLightPhase.Running
                ? state.SignalPhase
                : RedLightGreenLightSignalPhase.Green;

            if (observerHead != null)
            {
                var yaw = 180f;
                if (state.Phase ==
                    NetworkRedLightGreenLightPhase.Running)
                {
                    switch (phase)
                    {
                        case RedLightGreenLightSignalPhase.TurnWarning:
                            var progress = 1f - Mathf.Clamp01(
                                (float)(state.SignalRemaining /
                                RedLightGreenLightRules
                                    .TurnWarningSeconds));
                            yaw = Mathf.LerpAngle(180f, 0f, progress);
                            break;
                        case RedLightGreenLightSignalPhase.Red:
                            yaw = 0f;
                            break;
                    }
                }
                observerHead.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }

            var greenColor = phase ==
                RedLightGreenLightSignalPhase.Green
                    ? new Color(0.12f, 1f, 0.25f)
                    : phase ==
                      RedLightGreenLightSignalPhase.TurnWarning
                        ? new Color(1f, 0.7f, 0.08f)
                        : new Color(0.04f, 0.1f, 0.05f);
            var redColor = phase ==
                RedLightGreenLightSignalPhase.Red
                    ? new Color(1f, 0.08f, 0.04f)
                    : phase ==
                      RedLightGreenLightSignalPhase.TurnWarning
                        ? new Color(1f, 0.7f, 0.08f)
                        : new Color(0.12f, 0.025f, 0.02f);
            SetRendererColor(greenSignalRenderer, greenColor);
            SetRendererColor(redSignalRenderer, redColor);

            if (greenSignalLight != null)
            {
                greenSignalLight.enabled =
                    phase != RedLightGreenLightSignalPhase.Red;
                greenSignalLight.color = greenColor;
                greenSignalLight.intensity =
                    phase == RedLightGreenLightSignalPhase.Green
                        ? 4f
                        : 2f;
            }
            if (redSignalLight != null)
            {
                redSignalLight.enabled =
                    phase != RedLightGreenLightSignalPhase.Green;
                redSignalLight.color = redColor;
                redSignalLight.intensity =
                    phase == RedLightGreenLightSignalPhase.Red
                        ? 4f
                        : 2f;
            }

            if (_lastSignalPhase == phase)
            {
                return;
            }
            _lastSignalPhase = phase;
            var clip = phase == RedLightGreenLightSignalPhase.Green
                ? greenCue
                : phase == RedLightGreenLightSignalPhase.TurnWarning
                    ? turnWarningCue
                    : redCue;
            if (cueAudioSource != null && clip != null)
            {
                cueAudioSource.PlayOneShot(clip);
            }
        }

        private void SetRendererColor(Renderer target, Color color)
        {
            if (target == null)
            {
                return;
            }
            // MaterialPropertyBlock creates a native Unity object internally.
            // Constructing it in a MonoBehaviour field initializer runs during
            // Unity's managed-object construction phase and is rejected by
            // Unity 6. Allocate it lazily once normal engine callbacks run.
            _signalProperties ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(_signalProperties);
            _signalProperties.SetColor("_BaseColor", color);
            _signalProperties.SetColor("_Color", color);
            target.SetPropertyBlock(_signalProperties);
            _signalProperties.Clear();
        }

        private void RefreshHud(NetworkMatchState match)
        {
            if (hud == null || !hud.HasRequiredReferences)
            {
                return;
            }

            var phaseText = hud.PhaseText;
            var instructionText = hud.InstructionText;
            var scoreRows = hud.PlayerRows;

            if (match.IsReconnectPaused)
            {
                phaseText.text =
                    "PLAYER DISCONNECTED  ·  MATCH PAUSED  ·  " +
                    MinigameDisplayFormatter.FormatClock(
                        match.ReconnectRemaining);
                hud.SetSignal(
                    "PAUSED",
                    RedLightGreenLightHudSignalStyle.Neutral);
                instructionText.text =
                    "Waiting up to 60 seconds for the player to reconnect.";
            }
            else
            {
                instructionText.text = _defaultInstructionText;
                phaseText.text = BuildPhaseLabel();
                BuildSignalLabel(out var label, out var signalStyle);
                hud.SetSignal(label, signalStyle);
            }

            for (var slot = 0; slot < scoreRows.Length; slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                var displayName = avatar != null &&
                                  !string.IsNullOrWhiteSpace(
                                      avatar.DisplayName)
                    ? avatar.DisplayName
                    : "PLAYER " + (slot + 1);
                var playerState = state.GetPlayerState(slot);
                var rank = ResolveDisplayedRank(slot);
                var stateLabel = PlayerStateLabel(playerState);
                scoreRows[slot].text =
                    (slot == _localSlot ? "> " : string.Empty) +
                    MinigameDisplayFormatter.ToOrdinal(rank) +
                    "  " + displayName + "\n" +
                    stateLabel + "  ·  " +
                    state.GetForwardProgress(slot).ToString("0.0") + "m\n" +
                    "+" + state.GetRoundPoints(slot) +
                    "  ·  TOTAL " + state.GetScore(slot) +
                    (state.GetFinalRank(slot) > 0
                        ? "\nFINAL " +
                          MinigameDisplayFormatter.ToOrdinal(
                              state.GetFinalRank(slot)) +
                          "  ·  GOLD +" +
                          RedLightGreenLightRules.GetPointsForRank(
                              state.GetFinalRank(slot))
                        : string.Empty);
                scoreRows[slot].color = avatar != null
                    ? avatar.Appearance.BodyColor
                    : hud.GetDefaultPlayerRowColor(slot);
            }
        }

        private string BuildPhaseLabel()
        {
            var totalRounds =
                MinigameCatalog.GetRoundCount(
                    ScheduledMinigameId.RedLightGreenLight);
            var round = Mathf.Clamp(
                state.RoundNumber,
                1,
                totalRounds);
            switch (state.Phase)
            {
                case NetworkRedLightGreenLightPhase.Countdown:
                    return "RED LIGHT, GREEN LIGHT  ·  ROUND " +
                           round + " / " + totalRounds + "  ·  START IN " +
                           Mathf.CeilToInt((float)state.Remaining);
                case NetworkRedLightGreenLightPhase.Running:
                    return "RED LIGHT, GREEN LIGHT  ·  ROUND " +
                           round + " / " + totalRounds + "  ·  " +
                           MinigameDisplayFormatter.FormatClock(
                               state.Remaining);
                case NetworkRedLightGreenLightPhase.RoundResult:
                    return "ROUND " + round +
                           " RESULTS  ·  NEXT IN " +
                           Mathf.CeilToInt((float)state.Remaining);
                case NetworkRedLightGreenLightPhase.Complete:
                    return "RED LIGHT, GREEN LIGHT  ·  FINAL RESULTS";
                default:
                    return "RED LIGHT, GREEN LIGHT";
            }
        }

        private void BuildSignalLabel(
            out string label,
            out RedLightGreenLightHudSignalStyle style)
        {
            if (state.Phase == NetworkRedLightGreenLightPhase.Countdown)
            {
                label = "GET READY";
                style = RedLightGreenLightHudSignalStyle.Neutral;
                return;
            }
            if (state.Phase != NetworkRedLightGreenLightPhase.Running)
            {
                label = state.Phase ==
                        NetworkRedLightGreenLightPhase.Complete
                    ? "FINAL RESULTS"
                    : "ROUND RESULTS";
                style = RedLightGreenLightHudSignalStyle.Neutral;
                return;
            }

            switch (state.SignalPhase)
            {
                case RedLightGreenLightSignalPhase.Green:
                    label = "GREEN LIGHT  ·  MOVE";
                    style = RedLightGreenLightHudSignalStyle.Green;
                    break;
                case RedLightGreenLightSignalPhase.TurnWarning:
                    label = "TURNING  ·  STOP!";
                    style = RedLightGreenLightHudSignalStyle.TurnWarning;
                    break;
                default:
                    label = "RED LIGHT  ·  FREEZE";
                    style = RedLightGreenLightHudSignalStyle.Red;
                    break;
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
            if (state.Phase ==
                    NetworkRedLightGreenLightPhase.RoundResult &&
                roundRank > 0)
            {
                return roundRank;
            }

            var playerState = state.GetPlayerState(slot);
            var rank = 1;
            for (var other = 0;
                 other < RedLightGreenLightRules.PlayerCount;
                 other++)
            {
                if (other == slot)
                {
                    continue;
                }
                var otherState = state.GetPlayerState(other);
                if (CompareLiveStanding(
                        other,
                        otherState,
                        slot,
                        playerState) < 0)
                {
                    rank++;
                }
            }
            return rank;
        }

        private int CompareLiveStanding(
            int leftSlot,
            RedLightGreenLightPlayerState leftState,
            int rightSlot,
            RedLightGreenLightPlayerState rightState)
        {
            var leftCategory =
                leftState == RedLightGreenLightPlayerState.Finished
                    ? 0
                    : leftState ==
                      RedLightGreenLightPlayerState.Eliminated
                        ? 2
                        : 1;
            var rightCategory =
                rightState == RedLightGreenLightPlayerState.Finished
                    ? 0
                    : rightState ==
                      RedLightGreenLightPlayerState.Eliminated
                        ? 2
                        : 1;
            var category = leftCategory.CompareTo(rightCategory);
            if (category != 0)
            {
                return category;
            }
            var progress = state.GetForwardProgress(rightSlot).CompareTo(
                state.GetForwardProgress(leftSlot));
            return progress != 0
                ? progress
                : leftSlot.CompareTo(rightSlot);
        }

        private static string PlayerStateLabel(
            RedLightGreenLightPlayerState playerState)
        {
            switch (playerState)
            {
                case RedLightGreenLightPlayerState.Warned:
                    return "WARNED · WALK SPEED";
                case RedLightGreenLightPlayerState.Eliminated:
                    return "OUT";
                case RedLightGreenLightPlayerState.Finished:
                    return "FINISHED";
                default:
                    return "RUNNING";
            }
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
                _lastSignalPhase =
                    (RedLightGreenLightSignalPhase)byte.MaxValue;
                cueAudioSource?.Stop();
            }
        }

        private void SetHudActive(bool active)
        {
            if (hud != null && hud.gameObject.activeSelf != active)
            {
                hud.gameObject.SetActive(active);
            }
        }

        private static void DisableGeneratedHitColliders(
            GameObject runner)
        {
            var colliders = runner.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
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

        private static Renderer FindRenderer(
            Transform root,
            string childName)
        {
            var child = FindDescendant(root, childName);
            return child != null ? child.GetComponent<Renderer>() : null;
        }

        private static Light FindLight(
            Transform root,
            string childName)
        {
            var child = FindDescendant(root, childName);
            return child != null ? child.GetComponent<Light>() : null;
        }

        private sealed class RunnerView
        {
            public RunnerView(
                Transform root,
                PlayerAvatarVisual visual,
                RedLightGreenLightPlayerPresentation presentation)
            {
                Root = root;
                Visual = visual;
                Presentation = presentation;
                LastState =
                    (RedLightGreenLightPlayerState)byte.MaxValue;
                LastViolationCount = -1;
            }

            public Transform Root { get; }
            public PlayerAvatarVisual Visual { get; }
            public RedLightGreenLightPlayerPresentation Presentation {
                get;
            }
            public bool HasPosition { get; set; }
            public RedLightGreenLightPlayerState LastState { get; set; }
            public int LastViolationCount { get; set; }
        }
    }
}
