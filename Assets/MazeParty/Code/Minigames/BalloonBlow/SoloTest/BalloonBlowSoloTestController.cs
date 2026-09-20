using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.BalloonBlow;
using MazeParty.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Dev.MinigameSoloTest
{
    /// <summary>
    /// Offline Balloon Blow harness injected by the shared minigame launcher.
    /// It reuses the generated production arena, balloons, label prefabs and
    /// production pure rules without starting NGO or online services.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class BalloonBlowSoloTestController : MonoBehaviour
    {
        private const float BalloonMinimumScale = 0.22f;
        private const float BalloonMaximumScale = 1.7f;
        private const float BalloonScaleSpeed = 12f;

        private static readonly Color[] PlayerColors =
        {
            new Color(0.16f, 0.48f, 0.95f),
            new Color(0.92f, 0.2f, 0.16f),
            new Color(0.18f, 0.78f, 0.32f),
            new Color(0.7f, 0.26f, 0.9f)
        };

        private readonly Transform[] _playerAnchors =
            new Transform[BalloonBlowRules.PlayerCount];
        private readonly Transform[] _balloonAnchors =
            new Transform[BalloonBlowRules.PlayerCount];
        private readonly Transform[] _balloonBodies =
            new Transform[BalloonBlowRules.PlayerCount];
        private readonly Transform[] _balloonKnots =
            new Transform[BalloonBlowRules.PlayerCount];
        private readonly BalloonBlowStationLabel[] _stationLabels =
            new BalloonBlowStationLabel[BalloonBlowRules.PlayerCount];
        private readonly Transform[] _players =
            new Transform[BalloonBlowRules.PlayerCount];
        private readonly PlayerAvatarVisual[] _playerVisuals =
            new PlayerAvatarVisual[BalloonBlowRules.PlayerCount];

        private BalloonBlowSoloSession _session;
        private Transform _runtimeRoot;
        private Camera _runtimeCamera;
        private MinigameSoloHudView _hud;
        private bool _initialized;
        private string _feedback = string.Empty;
        private MinigameSoloFeedbackStyle _feedbackStyle =
            MinigameSoloFeedbackStyle.Neutral;
        private float _feedbackUntil = float.NegativeInfinity;
        private BalloonBlowPlayerPhase _previousLocalPhase;
        private bool _previousLocalPopped;

        private NetworkBalloonBlowState _productionState;
        private BalloonBlowNetworkView _productionView;
        private GameObject _productionPlayerRoot;
        private GameObject _productionHud;
        private GameObject _arenaPresentation;
        private bool _productionStateWasEnabled;
        private bool _productionViewWasEnabled;
        private bool _productionPlayerRootWasActive;
        private bool _productionHudWasActive;
        private bool _arenaPresentationWasActive;
        private bool _productionStateCaptured;

        public bool IsInitialized => _initialized;
        public BalloonBlowSoloSession Session => _session;
        public Camera RuntimeCamera => _runtimeCamera;
        public Transform LocalPlayer =>
            _players[BalloonBlowSoloSession.LocalPlayerSlot];

        public void ConfigureHud(MinigameSoloHudView hud)
        {
            _hud = hud;
        }

        public void Begin(int seed)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "The Balloon Blow solo harness is already initialized.");
            }
            if (_hud == null || !_hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Balloon Blow solo requires a valid MinigameSoloHud " +
                    "prefab instance.");
            }

            _hud.BindActions(RestartRound, StartNextSeed, StopSoloTest);
            DisableProductionPresentation();
            ResolveArenaContract();
            CreateRuntimeRoot();
            CreatePlayers();
            CreateRuntimeCamera();

            _session = new BalloonBlowSoloSession();
            _session.Begin(seed);
            ResetRoundPresentation();
            _initialized = true;
            UpdateHud();

            Debug.Log(
                "[Minigame Solo Test] Balloon Blow started with seed " +
                seed + ". No network session was created.");
        }

        private void Update()
        {
            if (!_initialized || _session == null)
            {
                return;
            }
            if (HandleKeyboardShortcuts())
            {
                return;
            }

            if (_session.Phase == BalloonBlowSoloPhase.Running)
            {
                var mouse = Mouse.current;
                _session.SetLocalInflateHeld(
                    mouse != null && mouse.leftButton.isPressed);
            }

            var previousPhase = _session.Phase;
            var previousRound = _session.RoundNumber;
            _session.Tick(Time.unscaledDeltaTime);
            ApplySessionTransition(previousPhase, previousRound);
            RefreshPresentation();
            DetectLocalFeedback();
            UpdateHud();
        }

        private void OnDestroy()
        {
            if (_hud != null)
            {
                _hud.BindActions(null, null, null);
            }
            RestoreProductionPresentation();
        }

        public void RestartRound()
        {
            if (_session == null)
            {
                return;
            }
            _session.RestartCurrentRound();
            ResetRoundPresentation();
            UpdateHud();
        }

        public void StartNewSeed(int seed)
        {
            if (_session == null)
            {
                return;
            }
            _session.Begin(seed);
            ResetRoundPresentation();
            UpdateHud();
        }

        private void DisableProductionPresentation()
        {
            var state = FindAnyObjectByType<NetworkBalloonBlowState>(
                FindObjectsInactive.Include);
            if (state == null)
            {
                throw new InvalidOperationException(
                    "The active scene does not contain " +
                    "NetworkBalloonBlowState.");
            }

            _productionState = state;
            _productionView = state.GetComponent<BalloonBlowNetworkView>();
            _productionPlayerRoot =
                FindNamedTransform("Runtime Players")?.gameObject;
            _productionHud =
                FindNamedTransform("BalloonBlowHud")?.gameObject;
            _arenaPresentation =
                FindNamedTransform("Arena Presentation")?.gameObject;
            _productionStateWasEnabled = state.enabled;
            _productionViewWasEnabled =
                _productionView != null && _productionView.enabled;
            _productionPlayerRootWasActive =
                _productionPlayerRoot != null &&
                _productionPlayerRoot.activeSelf;
            _productionHudWasActive =
                _productionHud != null && _productionHud.activeSelf;
            _arenaPresentationWasActive =
                _arenaPresentation != null &&
                _arenaPresentation.activeSelf;
            _productionStateCaptured = true;

            state.enabled = false;
            if (_productionView != null)
            {
                _productionView.enabled = false;
            }
            _productionPlayerRoot?.SetActive(false);
            _productionHud?.SetActive(false);
        }

        private void ResolveArenaContract()
        {
            if (_arenaPresentation == null)
            {
                throw new InvalidOperationException(
                    "Balloon Blow scene is missing Arena Presentation.");
            }
            _arenaPresentation.SetActive(true);

            var playerRoot = FindNamedTransform("Player Anchors");
            var balloonRoot = FindNamedTransform("Balloon Anchors");
            var labelRoot = FindNamedTransform("Station Label Anchors");
            if (playerRoot == null ||
                balloonRoot == null ||
                labelRoot == null ||
                playerRoot.childCount != BalloonBlowRules.PlayerCount ||
                balloonRoot.childCount != BalloonBlowRules.PlayerCount ||
                labelRoot.childCount != BalloonBlowRules.PlayerCount)
            {
                throw new InvalidOperationException(
                    "Balloon Blow scene is missing its four fixed player, " +
                    "balloon or prefab label anchors.");
            }

            for (var slot = 0;
                 slot < BalloonBlowRules.PlayerCount;
                 slot++)
            {
                _playerAnchors[slot] = RequireChild(
                    playerRoot,
                    "Player Anchor " + (slot + 1));
                _balloonAnchors[slot] = RequireChild(
                    balloonRoot,
                    "Balloon Anchor " + (slot + 1));
                _balloonBodies[slot] = RequireChild(
                    _balloonAnchors[slot],
                    "Balloon Body");
                _balloonKnots[slot] = RequireChild(
                    _balloonAnchors[slot],
                    "Balloon Knot");

                var labelTransform = RequireChild(
                    labelRoot,
                    "Station Label " + (slot + 1));
                _stationLabels[slot] = labelTransform.GetComponent<
                    BalloonBlowStationLabel>();
                if (_stationLabels[slot] == null ||
                    !_stationLabels[slot].HasRequiredReferences)
                {
                    throw new InvalidOperationException(
                        "Balloon Blow station label " + (slot + 1) +
                        " has invalid prefab bindings.");
                }
            }
        }

        private void RestoreProductionPresentation()
        {
            if (!_productionStateCaptured)
            {
                return;
            }
            if (_productionState != null)
            {
                _productionState.enabled = _productionStateWasEnabled;
            }
            if (_productionView != null)
            {
                _productionView.enabled = _productionViewWasEnabled;
            }
            _productionPlayerRoot?.SetActive(_productionPlayerRootWasActive);
            _productionHud?.SetActive(_productionHudWasActive);
            _arenaPresentation?.SetActive(_arenaPresentationWasActive);
            _productionStateCaptured = false;
        }

        private void CreateRuntimeRoot()
        {
            _runtimeRoot = new GameObject("[Solo Test] Runtime").transform;
            _runtimeRoot.SetParent(transform, false);
        }

        private void CreatePlayers()
        {
            for (var slot = 0;
                 slot < BalloonBlowRules.PlayerCount;
                 slot++)
            {
                var playerObject = new GameObject(
                    slot == BalloonBlowSoloSession.LocalPlayerSlot
                        ? "Solo Balloon Player"
                        : "Practice Balloon Player " + (slot + 1));
                playerObject.transform.SetParent(_runtimeRoot, false);
                playerObject.transform.SetPositionAndRotation(
                    _playerAnchors[slot].position,
                    _playerAnchors[slot].rotation);
                _players[slot] = playerObject.transform;

                var visual = playerObject.AddComponent<PlayerAvatarVisual>();
                visual.EnsureBuilt();
                visual.SetOwnerFirstPerson(false);
                visual.SetBodyColor(PlayerColors[slot]);
                visual.SetDisplayName(
                    slot == BalloonBlowSoloSession.LocalPlayerSlot
                        ? "SOLO DEV"
                        : "PRACTICE " + (slot + 1));
                visual.SetTopViewHighlight(
                    slot == BalloonBlowSoloSession.LocalPlayerSlot);
                DisableBuiltInNameplate(playerObject.transform);
                DisableGeneratedHitColliders(playerObject);
                _playerVisuals[slot] = visual;
            }
        }

        private void CreateRuntimeCamera()
        {
            var cameras = FindObjectsByType<Camera>(
                FindObjectsInactive.Include);
            for (var index = 0; index < cameras.Length; index++)
            {
                cameras[index].enabled = false;
            }
            var listeners = FindObjectsByType<AudioListener>(
                FindObjectsInactive.Include);
            for (var index = 0; index < listeners.Length; index++)
            {
                listeners[index].enabled = false;
            }

            var cameraObject = new GameObject(
                "Solo Output Camera",
                typeof(Camera),
                typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(_runtimeRoot, false);
            cameraObject.transform.SetPositionAndRotation(
                BalloonBlowNetworkView.SharedCameraPosition,
                BalloonBlowNetworkView.SharedCameraRotation);
            _runtimeCamera = cameraObject.GetComponent<Camera>();
            _runtimeCamera.orthographic = true;
            _runtimeCamera.orthographicSize =
                BalloonBlowNetworkView.SharedCameraOrthographicSize;
            _runtimeCamera.nearClipPlane = 0.1f;
            _runtimeCamera.farClipPlane = 100f;
            _runtimeCamera.clearFlags = CameraClearFlags.SolidColor;
            _runtimeCamera.backgroundColor =
                new Color(0.018f, 0.014f, 0.04f, 1f);
        }

        private void ResetRoundPresentation()
        {
            _feedback = string.Empty;
            _feedbackStyle = MinigameSoloFeedbackStyle.Neutral;
            _feedbackUntil = float.NegativeInfinity;
            _previousLocalPhase = BalloonBlowPlayerPhase.Ready;
            _previousLocalPopped = false;
            for (var slot = 0;
                 slot < BalloonBlowRules.PlayerCount;
                 slot++)
            {
                _players[slot].SetPositionAndRotation(
                    _playerAnchors[slot].position,
                    _playerAnchors[slot].rotation);
                _balloonAnchors[slot].localScale =
                    Vector3.one * BalloonMinimumScale;
                _balloonBodies[slot].gameObject.SetActive(true);
                _balloonKnots[slot].gameObject.SetActive(true);
                _stationLabels[slot].SetContent(
                    GetPlayerName(slot),
                    0f,
                    false,
                    slot == BalloonBlowSoloSession.LocalPlayerSlot,
                    PlayerColors[slot]);
            }
        }

        private void RefreshPresentation()
        {
            for (var slot = 0;
                 slot < BalloonBlowRules.PlayerCount;
                 slot++)
            {
                var player = _session.GetPlayer(slot);
                var popped = player.IsPopped;
                var normalized = Mathf.Clamp01(
                    player.ProgressPercent /
                    BalloonBlowRules.MaxProgressPercent);
                var targetScale = Mathf.Lerp(
                    BalloonMinimumScale,
                    BalloonMaximumScale,
                    normalized);
                var currentScale = _balloonAnchors[slot].localScale.x;
                var scale = Mathf.Lerp(
                    currentScale,
                    targetScale,
                    1f - Mathf.Exp(
                        -BalloonScaleSpeed * Time.unscaledDeltaTime));
                _balloonAnchors[slot].localScale = Vector3.one * scale;
                _balloonBodies[slot].gameObject.SetActive(!popped);
                _balloonKnots[slot].gameObject.SetActive(!popped);

                _stationLabels[slot].SetContent(
                    GetPlayerName(slot),
                    player.ProgressPercent,
                    popped,
                    slot == BalloonBlowSoloSession.LocalPlayerSlot,
                    PlayerColors[slot]);
                _stationLabels[slot].transform.position =
                    _playerAnchors[slot].position +
                    Vector3.up * 3.2f;
                _stationLabels[slot].FaceCamera(_runtimeCamera);
                _playerVisuals[slot].SetCrouching(
                    player.Phase == BalloonBlowPlayerPhase.Inflating);
            }
        }

        private void DetectLocalFeedback()
        {
            var local = _session.LocalPlayer;
            if (local == null)
            {
                return;
            }
            if (local.IsPopped && !_previousLocalPopped)
            {
                ShowFeedback(
                    "POP!  " + ToOrdinal((int)local.PopOrder),
                    MinigameSoloFeedbackStyle.Success,
                    2f);
            }
            else if (local.Phase != _previousLocalPhase)
            {
                if (local.Phase == BalloonBlowPlayerPhase.Cooldown)
                {
                    ShowFeedback(
                        local.RequiresReleaseToRearm
                            ? "OVERHELD · 1.5s REST · RELEASE TO REARM"
                            : "REST · 1.0s COOLDOWN",
                        local.RequiresReleaseToRearm
                            ? MinigameSoloFeedbackStyle.Warning
                            : MinigameSoloFeedbackStyle.Neutral,
                        1.5f);
                }
                else if (local.Phase ==
                         BalloonBlowPlayerPhase.AwaitingRelease)
                {
                    ShowFeedback(
                        "RELEASE LEFT CLICK",
                        MinigameSoloFeedbackStyle.Warning,
                        1f);
                }
            }

            _previousLocalPopped = local.IsPopped;
            _previousLocalPhase = local.Phase;
        }

        private void ApplySessionTransition(
            BalloonBlowSoloPhase previousPhase,
            int previousRound)
        {
            if (previousPhase == _session.Phase &&
                previousRound == _session.RoundNumber)
            {
                return;
            }

            if (_session.Phase == BalloonBlowSoloPhase.Countdown)
            {
                ResetRoundPresentation();
                Debug.Log(
                    "[Minigame Solo Test] Balloon Blow round " +
                    _session.RoundNumber + " ready.");
            }
            else if (_session.Phase == BalloonBlowSoloPhase.RoundResult)
            {
                var standing = _session
                    .GetRoundResult(_session.RoundNumber)
                    .GetStandingForSlot(
                        BalloonBlowSoloSession.LocalPlayerSlot);
                Debug.Log(
                    "[Minigame Solo Test] Balloon Blow round " +
                    _session.RoundNumber + " result: rank " +
                    standing.Rank + ".");
            }
            else if (_session.Phase == BalloonBlowSoloPhase.Complete)
            {
                Debug.Log(
                    "[Minigame Solo Test] Balloon Blow complete.");
            }
        }

        private bool HandleKeyboardShortcuts()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                StopSoloTest();
                return true;
            }
            if (keyboard.rKey.wasPressedThisFrame)
            {
                RestartRound();
                return true;
            }
            if (keyboard.nKey.wasPressedThisFrame)
            {
                StartNewSeed(unchecked(_session.Seed + 1));
                return true;
            }
            return false;
        }

        private void UpdateHud()
        {
            if (_hud == null || _session == null)
            {
                return;
            }

            var local = _session.LocalPlayer;
            var feature = GetFeatureLabel(local);
            if (_session.Phase == BalloonBlowSoloPhase.RoundResult)
            {
                var standing = _session
                    .GetRoundResult(_session.RoundNumber)
                    .GetStandingForSlot(
                        BalloonBlowSoloSession.LocalPlayerSlot);
                feature = ToOrdinal(standing.Rank) +
                          " · +" + standing.Points + " PT";
            }
            else if (_session.Phase == BalloonBlowSoloPhase.Complete)
            {
                feature = GetFinalStandingLabel();
            }

            var progress = local != null ? local.ProgressPercent : 0f;
            var localPhase = local != null
                ? local.Phase.ToString().ToUpperInvariant()
                : "READY";
            var feedbackVisible = Time.unscaledTime < _feedbackUntil;
            _hud.SetContent(
                "DEVELOPER SOLO TEST  /  BALLOON BLOW",
                "ROUND " + _session.RoundNumber + " / " +
                BalloonBlowRules.RoundCount + "  ·  " +
                GetSoloPhaseLabel() + "  ·  " +
                FormatClock(_session.RemainingSeconds),
                "BALLOON " + Mathf.RoundToInt(progress) + "%  ·  " +
                localPhase + "  ·  SEED " + _session.Seed,
                feature,
                "Hold LMB inflate  |  Release rest  |  R restart  |  " +
                "N next seed  |  Esc stop",
                feedbackVisible ? _feedback : string.Empty,
                feedbackVisible
                    ? _feedbackStyle
                    : MinigameSoloFeedbackStyle.Neutral);
        }

        private string GetFeatureLabel(BalloonBlowPlayerRoundState local)
        {
            if (_session.Phase == BalloonBlowSoloPhase.Countdown)
            {
                return "GET READY";
            }
            if (local == null)
            {
                return "HOLD LEFT CLICK";
            }

            switch (local.Phase)
            {
                case BalloonBlowPlayerPhase.Inflating:
                    return "INFLATING · RELEASE BEFORE 2.0 SECONDS";
                case BalloonBlowPlayerPhase.Cooldown:
                    return "RESTING " +
                           local.CooldownRemainingSeconds.ToString("0.0") +
                           "s · BALLOON SHRINKS 3%/s";
                case BalloonBlowPlayerPhase.AwaitingRelease:
                    return "RELEASE LEFT CLICK TO REARM";
                case BalloonBlowPlayerPhase.Popped:
                    return "POPPED " + ToOrdinal((int)local.PopOrder);
                default:
                    return "HOLD LEFT CLICK TO INFLATE";
            }
        }

        private string GetSoloPhaseLabel()
        {
            switch (_session.Phase)
            {
                case BalloonBlowSoloPhase.Countdown:
                    return "START IN";
                case BalloonBlowSoloPhase.Running:
                    return "RUNNING";
                case BalloonBlowSoloPhase.RoundResult:
                    return "ROUND RESULT";
                case BalloonBlowSoloPhase.Complete:
                    return "COMPLETE";
                default:
                    return _session.Phase.ToString().ToUpperInvariant();
            }
        }

        private string GetFinalStandingLabel()
        {
            var leaderboard = _session.Leaderboard;
            for (var index = 0;
                 leaderboard != null && index < leaderboard.Count;
                 index++)
            {
                if (leaderboard[index].PlayerSlot ==
                    BalloonBlowSoloSession.LocalPlayerSlot)
                {
                    return "MATCH " + ToOrdinal(leaderboard[index].Rank) +
                           " · " + leaderboard[index].TotalPoints + " PT";
                }
            }
            return "MATCH COMPLETE";
        }

        private static string GetPlayerName(int slot)
        {
            return slot == BalloonBlowSoloSession.LocalPlayerSlot
                ? "SOLO DEV"
                : "PRACTICE " + (slot + 1);
        }

        private void ShowFeedback(
            string message,
            MinigameSoloFeedbackStyle style,
            float seconds)
        {
            _feedback = message;
            _feedbackStyle = style;
            _feedbackUntil = Time.unscaledTime + seconds;
        }

        private static Transform RequireChild(
            Transform parent,
            string childName)
        {
            var child = parent.Find(childName);
            if (child == null)
            {
                throw new InvalidOperationException(
                    parent.name + " is missing child '" + childName + "'.");
            }
            return child;
        }

        private static Transform FindNamedTransform(string objectName)
        {
            var transforms = FindObjectsByType<Transform>(
                FindObjectsInactive.Include);
            for (var index = 0; index < transforms.Length; index++)
            {
                if (transforms[index] != null &&
                    transforms[index].name == objectName)
                {
                    return transforms[index];
                }
            }
            return null;
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
                var found = FindDescendant(root.GetChild(index), childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static void DisableBuiltInNameplate(Transform root)
        {
            var nameplate = FindDescendant(root, "NameplateAnchor");
            if (nameplate != null)
            {
                nameplate.gameObject.SetActive(false);
            }
        }

        private static void DisableGeneratedHitColliders(GameObject root)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }
        }

        private static string ToOrdinal(int rank)
        {
            switch (rank)
            {
                case 1: return "1ST";
                case 2: return "2ND";
                case 3: return "3RD";
                case 4: return "4TH";
                default: return "--";
            }
        }

        private static string FormatClock(double seconds)
        {
            var whole = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            return (whole / 60).ToString("00") + ":" +
                   (whole % 60).ToString("00");
        }

        private void StartNextSeed()
        {
            if (_session != null)
            {
                StartNewSeed(unchecked(_session.Seed + 1));
            }
        }

        private static void StopSoloTest()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
