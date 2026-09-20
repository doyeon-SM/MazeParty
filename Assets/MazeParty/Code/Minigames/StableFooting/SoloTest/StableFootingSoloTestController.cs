using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.StableFooting;
using MazeParty.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Dev.MinigameSoloTest
{
    /// <summary>
    /// Offline Stable Footing harness injected by the minigame solo launcher.
    /// It reuses the generated production arena and production gameplay rules
    /// without starting NGO or Unity Gaming Services.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class StableFootingSoloTestController : MonoBehaviour
    {
        private const float RunnerHeight =
            StableFootingNetworkView.RunnerPresentationHeight;
        private const float DroppedTileY = -5.5f;
        private const float TileAnimationSpeed = 10f;

        private static readonly Color[] PlayerColors =
        {
            new Color(0.16f, 0.48f, 0.95f),
            new Color(0.92f, 0.2f, 0.16f),
            new Color(0.18f, 0.78f, 0.32f),
            new Color(0.7f, 0.26f, 0.9f)
        };

        private StableFootingSoloSession _session;
        private Transform _runtimeRoot;
        private Transform _tileRoot;
        private Transform _playerAnchorRoot;
        private readonly Transform[] _tileAnchors =
            new Transform[StableFootingRules.TileCount];
        private readonly GameObject[] _crossMarks =
            new GameObject[StableFootingRules.TileCount];
        private readonly GameObject[] _circleMarks =
            new GameObject[StableFootingRules.TileCount];
        private readonly GameObject[] _squareMarks =
            new GameObject[StableFootingRules.TileCount];
        private readonly Transform[] _runners =
            new Transform[StableFootingRules.PlayerCount];
        private readonly PlayerAvatarVisual[] _runnerVisuals =
            new PlayerAvatarVisual[StableFootingRules.PlayerCount];
        private readonly ulong[] _seenEliminationOrders =
            new ulong[StableFootingRules.PlayerCount];

        private GameObject _safeCross;
        private GameObject _safeCircle;
        private GameObject _safeSquare;
        private Camera _runtimeCamera;
        private Vector3 _facingDirection = Vector3.forward;
        private float _nextPushAt;
        private string _feedback = string.Empty;
        private MinigameSoloFeedbackStyle _feedbackStyle =
            MinigameSoloFeedbackStyle.Neutral;
        private float _feedbackUntil = float.NegativeInfinity;
        private MinigameSoloHudView _hud;
        private bool _initialized;
        private NetworkStableFootingState _productionState;
        private StableFootingNetworkView _productionView;
        private GameObject _productionRunnerRoot;
        private GameObject _productionHud;
        private GameObject _arenaPresentation;
        private bool _productionStateWasEnabled;
        private bool _productionViewWasEnabled;
        private bool _productionRunnerRootWasActive;
        private bool _productionHudWasActive;
        private bool _arenaPresentationWasActive;
        private bool _productionStateCaptured;

        public bool IsInitialized => _initialized;
        public StableFootingSoloSession Session => _session;
        public Camera RuntimeCamera => _runtimeCamera;
        public Transform LocalRunner => _runners[
            StableFootingSoloSession.LocalPlayerSlot];

        public void ConfigureHud(MinigameSoloHudView hud)
        {
            _hud = hud;
        }

        public void Begin(int seed)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "The Stable Footing solo harness is already initialized.");
            }
            if (_hud == null || !_hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "The Stable Footing solo harness requires a valid " +
                    "MinigameSoloHud prefab instance.");
            }

            _hud.BindActions(RestartRound, StartNextSeed, StopSoloTest);
            DisableProductionPresentation();
            ResolveArenaContract();
            CreateRuntimeRoot();
            CreateRunners();
            CreateRuntimeCamera();

            _session = new StableFootingSoloSession();
            _session.Begin(seed);
            ResetRoundPresentation();
            _initialized = true;
            UpdateHud();

            Debug.Log(
                "[Minigame Solo Test] Stable Footing started with seed " +
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

            if (_session.Phase == StableFootingSoloPhase.Running)
            {
                SimulatePlayers();
            }

            var previousPhase = _session.Phase;
            var previousRound = _session.RoundNumber;
            _session.Tick(Time.unscaledDeltaTime);
            ApplySessionTransition(previousPhase, previousRound);
            RefreshArenaPresentation();
            RefreshRunnerPresentation();
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
            var state = FindAnyObjectByType<NetworkStableFootingState>(
                FindObjectsInactive.Include);
            if (state == null)
            {
                throw new InvalidOperationException(
                    "The active scene does not contain " +
                    "NetworkStableFootingState.");
            }

            _productionState = state;
            _productionView =
                state.GetComponent<StableFootingNetworkView>();
            _productionRunnerRoot =
                FindNamedTransform("Runtime Runners")?.gameObject;
            _productionHud =
                FindNamedTransform("StableFootingHud")?.gameObject;
            _arenaPresentation =
                FindNamedTransform("Arena Presentation")?.gameObject;
            _productionStateWasEnabled = state.enabled;
            _productionViewWasEnabled =
                _productionView != null && _productionView.enabled;
            _productionRunnerRootWasActive =
                _productionRunnerRoot != null &&
                _productionRunnerRoot.activeSelf;
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
            _productionRunnerRoot?.SetActive(false);
            _productionHud?.SetActive(false);
        }

        private void ResolveArenaContract()
        {
            if (_arenaPresentation == null)
            {
                throw new InvalidOperationException(
                    "Stable Footing scene contract is missing Arena " +
                    "Presentation.");
            }
            _arenaPresentation.SetActive(true);

            _tileRoot = FindNamedTransform("Tile Anchors");
            _playerAnchorRoot = FindNamedTransform("Player Anchors");
            var safeDisplay = FindNamedTransform("Safe Symbol Display");
            if (_tileRoot == null ||
                _tileRoot.childCount != StableFootingRules.TileCount ||
                _playerAnchorRoot == null ||
                _playerAnchorRoot.childCount !=
                    StableFootingRules.PlayerCount ||
                safeDisplay == null)
            {
                throw new InvalidOperationException(
                    "Stable Footing scene contract is missing its tile, " +
                    "player or safe-symbol anchors.");
            }

            for (var index = 0;
                 index < StableFootingRules.TileCount;
                 index++)
            {
                var anchor = _tileRoot.Find(
                    "Tile Anchor " + index.ToString("00"));
                if (anchor == null)
                {
                    throw new InvalidOperationException(
                        "Stable Footing is missing Tile Anchor " +
                        index.ToString("00") + ".");
                }
                _tileAnchors[index] = anchor;
                _crossMarks[index] = RequireChild(anchor, "Cross Mark");
                _circleMarks[index] = RequireChild(anchor, "Circle Mark");
                _squareMarks[index] = RequireChild(anchor, "Square Mark");
            }

            _safeCross = RequireChild(safeDisplay, "Cross Mark");
            _safeCircle = RequireChild(safeDisplay, "Circle Mark");
            _safeSquare = RequireChild(safeDisplay, "Square Mark");
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
            if (_productionRunnerRoot != null)
            {
                _productionRunnerRoot.SetActive(
                    _productionRunnerRootWasActive);
            }
            if (_productionHud != null)
            {
                _productionHud.SetActive(_productionHudWasActive);
            }
            if (_arenaPresentation != null)
            {
                _arenaPresentation.SetActive(
                    _arenaPresentationWasActive);
            }

            _productionStateCaptured = false;
        }

        private void CreateRuntimeRoot()
        {
            _runtimeRoot = new GameObject("[Solo Test] Runtime").transform;
            _runtimeRoot.SetParent(transform, false);
        }

        private void CreateRunners()
        {
            for (var slot = 0;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                var runner = new GameObject(
                    slot == StableFootingSoloSession.LocalPlayerSlot
                        ? "Solo Runner"
                        : "Practice Runner " + (slot + 1));
                runner.transform.SetParent(_runtimeRoot, false);
                _runners[slot] = runner.transform;

                var visual = runner.AddComponent<PlayerAvatarVisual>();
                visual.EnsureBuilt();
                visual.SetOwnerFirstPerson(false);
                visual.SetBodyColor(PlayerColors[slot]);
                visual.SetDisplayName(
                    slot == StableFootingSoloSession.LocalPlayerSlot
                        ? "SOLO DEV"
                        : "PRACTICE " + (slot + 1));
                visual.SetTopViewHighlight(
                    slot == StableFootingSoloSession.LocalPlayerSlot);
                _runnerVisuals[slot] = visual;

                var colliders = runner.GetComponentsInChildren<Collider>(true);
                for (var index = 0; index < colliders.Length; index++)
                {
                    colliders[index].enabled = false;
                }
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
                StableFootingNetworkView.SharedCameraPosition,
                StableFootingNetworkView.SharedCameraRotation);

            _runtimeCamera = cameraObject.GetComponent<Camera>();
            _runtimeCamera.orthographic = true;
            _runtimeCamera.orthographicSize =
                StableFootingNetworkView.SharedCameraOrthographicSize;
            _runtimeCamera.nearClipPlane = 0.1f;
            _runtimeCamera.farClipPlane = 160f;
            _runtimeCamera.clearFlags = CameraClearFlags.SolidColor;
            _runtimeCamera.backgroundColor =
                new Color(0.018f, 0.026f, 0.045f, 1f);
        }

        private void SimulatePlayers()
        {
            var localPlayer = _session.LocalPlayer;
            if (localPlayer == null || !localPlayer.IsAlive)
            {
                return;
            }

            if (_session.CurrentCyclePhase == StableFootingCyclePhase.Move)
            {
                var input = ReadMovementInput();
                if (input.sqrMagnitude > 0.0001f)
                {
                    input = Vector2.ClampMagnitude(input, 1f);
                    _facingDirection = new Vector3(
                        input.x,
                        0f,
                        input.y).normalized;
                    var position = LocalRunner.position +
                                   _facingDirection *
                                   NetworkStableFootingState.MovementSpeed *
                                   Time.unscaledDeltaTime;
                    position.y = RunnerHeight;
                    LocalRunner.position = position;
                    LocalRunner.rotation = Quaternion.LookRotation(
                        _facingDirection,
                        Vector3.up);
                }

                var mouse = Mouse.current;
                if (mouse != null &&
                    mouse.leftButton.wasPressedThisFrame)
                {
                    TryPushNearestRunner();
                }
            }

            UpdatePlayerTileOccupancy();
        }

        private void TryPushNearestRunner()
        {
            if (Time.unscaledTime < _nextPushAt)
            {
                return;
            }
            _nextPushAt = Time.unscaledTime +
                          (float)StableFootingRules.PushCooldownSeconds;

            var origin = LocalRunner.position;
            var bestSlot = -1;
            var bestDistance = float.PositiveInfinity;
            var pushRange = NetworkStableFootingState.TileSize *
                            NetworkStableFootingState.PushRangeInTiles;
            for (var slot = 1;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                if (!_session.RoundState.GetPlayer(slot).IsAlive)
                {
                    continue;
                }
                var offset = _runners[slot].position - origin;
                offset.y = 0f;
                var distance = offset.magnitude;
                if (distance > pushRange ||
                    distance >= bestDistance ||
                    Vector3.Dot(
                        offset.normalized,
                        _facingDirection) <
                    NetworkStableFootingState.PushConeDot)
                {
                    continue;
                }
                bestSlot = slot;
                bestDistance = distance;
            }

            if (bestSlot < 0)
            {
                ShowFeedback(
                    "PUSH MISSED",
                    MinigameSoloFeedbackStyle.Warning,
                    0.8f);
                return;
            }

            var distanceMeters =
                NetworkStableFootingState.TileSize *
                StableFootingRules.PushDistanceInTiles;
            var pushedFrom = _runners[bestSlot].position;
            var pushedPosition = pushedFrom +
                                 _facingDirection * distanceMeters;
            var cycle = _session.CurrentCycle;
            if (cycle != null)
            {
                ulong activeTileMask = 0UL;
                for (var tileIndex = 0;
                     tileIndex < StableFootingRules.TileCount;
                     tileIndex++)
                {
                    if (cycle.IsTileActive(tileIndex))
                    {
                        activeTileMask |= 1UL << tileIndex;
                    }
                }

                if (NetworkStableFootingState.TryFindFirstUnsupportedPoint(
                        pushedFrom,
                        pushedPosition,
                        activeTileMask,
                        out var unsupportedPoint))
                {
                    pushedPosition = unsupportedPoint;
                }
            }
            pushedPosition.y = RunnerHeight;
            _runners[bestSlot].position = pushedPosition;
            _runners[bestSlot].rotation = Quaternion.LookRotation(
                _facingDirection,
                Vector3.up);
            UpdatePlayerTileOccupancy();
            ShowFeedback(
                "PUSHED PRACTICE " + (bestSlot + 1),
                MinigameSoloFeedbackStyle.Success,
                1f);
        }

        private void UpdatePlayerTileOccupancy()
        {
            for (var slot = 0;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                if (_runners[slot] != null)
                {
                    _session.SetPlayerTile(
                        slot,
                        WorldToTileIndex(_runners[slot].position));
                }
            }
        }

        private void ApplySessionTransition(
            StableFootingSoloPhase previousPhase,
            int previousRound)
        {
            if (previousPhase == _session.Phase &&
                previousRound == _session.RoundNumber)
            {
                return;
            }

            if (_session.Phase == StableFootingSoloPhase.Countdown)
            {
                ResetRoundPresentation();
                Debug.Log(
                    "[Minigame Solo Test] Stable Footing round " +
                    _session.RoundNumber + " ready.");
            }
            else if (_session.Phase == StableFootingSoloPhase.RoundResult)
            {
                var standing = _session
                    .GetRoundResult(_session.RoundNumber)
                    .GetStandingForSlot(
                        StableFootingSoloSession.LocalPlayerSlot);
                Debug.Log(
                    "[Minigame Solo Test] Stable Footing round " +
                    _session.RoundNumber + " result: rank " +
                    standing.Rank + ".");
            }
            else if (_session.Phase == StableFootingSoloPhase.Complete)
            {
                Debug.Log(
                    "[Minigame Solo Test] Stable Footing complete.");
            }
        }

        private void ResetRoundPresentation()
        {
            Array.Clear(
                _seenEliminationOrders,
                0,
                _seenEliminationOrders.Length);
            _facingDirection = Vector3.forward;
            _nextPushAt = 0f;
            _feedback = string.Empty;
            _feedbackStyle = MinigameSoloFeedbackStyle.Neutral;
            _feedbackUntil = float.NegativeInfinity;

            for (var index = 0;
                 index < _tileAnchors.Length;
                 index++)
            {
                var position = _tileAnchors[index].localPosition;
                position.y = 0f;
                _tileAnchors[index].localPosition = position;
            }
            for (var slot = 0;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                _runners[slot].gameObject.SetActive(true);
                _runners[slot].SetPositionAndRotation(
                    _playerAnchorRoot.GetChild(slot).position,
                    Quaternion.identity);
                _session.SetPlayerTile(
                    slot,
                    WorldToTileIndex(_runners[slot].position));
            }

            RefreshArenaPresentation(true);
            RefreshRunnerPresentation(true);
        }

        private void RefreshArenaPresentation(bool immediate = false)
        {
            if (_session?.RoundState == null)
            {
                return;
            }

            var cycle = _session.PresentationCycle;
            if (cycle == null &&
                _session.RoundState.Schedule.Cycles.Count > 0)
            {
                cycle = _session.RoundState.Schedule.Cycles[0];
            }
            if (cycle == null)
            {
                return;
            }

            var phase = _session.PresentationCyclePhase;
            for (var index = 0;
                 index < StableFootingRules.TileCount;
                 index++)
            {
                var tileIsActive = cycle.IsTileActive(index);
                if (tileIsActive)
                {
                    var symbol = cycle.GetSymbolForTile(index);
                    SetActive(_crossMarks[index],
                        symbol == StableFootingSymbol.Cross);
                    SetActive(_circleMarks[index],
                        symbol == StableFootingSymbol.Circle);
                    SetActive(_squareMarks[index],
                        symbol == StableFootingSymbol.Square);
                }
                else
                {
                    SetActive(_crossMarks[index], false);
                    SetActive(_circleMarks[index], false);
                    SetActive(_squareMarks[index], false);
                }

                var shouldDrop = !tileIsActive ||
                    (phase == StableFootingCyclePhase.Drop &&
                     !cycle.IsTileSafe(index)) ||
                    (phase == StableFootingCyclePhase.Restore &&
                     cycle.WillBePermanentlyRemoved(index));
                var targetY = shouldDrop ? DroppedTileY : 0f;
                var local = _tileAnchors[index].localPosition;
                local.y = immediate
                    ? targetY
                    : Mathf.Lerp(
                        local.y,
                        targetY,
                        1f - Mathf.Exp(
                            -TileAnimationSpeed *
                            Time.unscaledDeltaTime));
                _tileAnchors[index].localPosition = local;
            }

            var revealSafeSymbol =
                _session.Phase == StableFootingSoloPhase.Running;
            SetActive(
                _safeCross,
                revealSafeSymbol &&
                cycle.SafeSymbol == StableFootingSymbol.Cross);
            SetActive(
                _safeCircle,
                revealSafeSymbol &&
                cycle.SafeSymbol == StableFootingSymbol.Circle);
            SetActive(
                _safeSquare,
                revealSafeSymbol &&
                cycle.SafeSymbol == StableFootingSymbol.Square);
        }

        private void RefreshRunnerPresentation(bool immediate = false)
        {
            if (_session?.RoundState == null)
            {
                return;
            }

            for (var slot = 0;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                var player = _session.RoundState.GetPlayer(slot);
                var runner = _runners[slot];
                var position = runner.position;
                var targetY = player.IsEliminated
                    ? DroppedTileY - 1f
                    : RunnerHeight;
                position.y = immediate
                    ? targetY
                    : Mathf.Lerp(
                        position.y,
                        targetY,
                        1f - Mathf.Exp(
                            -TileAnimationSpeed *
                            Time.unscaledDeltaTime));
                runner.position = position;

                if (player.IsEliminated &&
                    player.EliminationOrder != 0UL &&
                    _seenEliminationOrders[slot] !=
                        player.EliminationOrder)
                {
                    _seenEliminationOrders[slot] =
                        player.EliminationOrder;
                    if (slot == StableFootingSoloSession.LocalPlayerSlot)
                    {
                        ShowFeedback(
                            "UNSTABLE TILE · OUT",
                            MinigameSoloFeedbackStyle.Error,
                            2f);
                    }
                }
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

        private static Vector2 ReadMovementInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return Vector2.zero;
            }
            return new Vector2(
                (keyboard.dKey.isPressed ? 1f : 0f) -
                (keyboard.aKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed ? 1f : 0f) -
                (keyboard.sKey.isPressed ? 1f : 0f));
        }

        private void UpdateHud()
        {
            if (_hud == null || _session == null)
            {
                return;
            }

            var cycle = _session.CurrentCycle;
            var feature = cycle == null
                ? "SYMBOLS SHUFFLING"
                : GetCyclePhaseLabel(_session.CurrentCyclePhase) +
                  "  ·  SAFE " +
                  cycle.SafeSymbol.ToString().ToUpperInvariant() +
                  "  ·  CYCLE " + cycle.CycleNumber;
            var local = _session.LocalPlayer;
            var localState = local == null
                ? "READY"
                : local.IsAlive ? "ALIVE" : "OUT";

            if (_session.Phase == StableFootingSoloPhase.RoundResult)
            {
                var standing = _session
                    .GetRoundResult(_session.RoundNumber)
                    .GetStandingForSlot(
                        StableFootingSoloSession.LocalPlayerSlot);
                feature = ToOrdinal(standing.Rank) +
                          "  ·  +" + standing.Points + " PT";
            }
            else if (_session.Phase == StableFootingSoloPhase.Complete)
            {
                feature = GetFinalStandingLabel();
            }

            var feedbackVisible = Time.unscaledTime < _feedbackUntil;
            _hud.SetContent(
                "DEVELOPER SOLO TEST  /  STABLE FOOTING",
                "ROUND " + _session.RoundNumber + " / " +
                StableFootingRules.RoundCount + "  ·  " +
                GetSoloPhaseLabel() + "  ·  " +
                FormatClock(_session.RemainingSeconds),
                "STATE " + localState + "  ·  TILE " +
                (_session.GetPlayerTile(
                    StableFootingSoloSession.LocalPlayerSlot) + 1) +
                " / " + StableFootingRules.TileCount +
                "  ·  SEED " + _session.Seed,
                feature,
                "WASD move  |  Left click pushes 1.5 tiles  |  " +
                "R restart  |  N next seed  |  Esc stop",
                feedbackVisible ? _feedback : string.Empty,
                feedbackVisible
                    ? _feedbackStyle
                    : MinigameSoloFeedbackStyle.Neutral);
        }

        private string GetSoloPhaseLabel()
        {
            switch (_session.Phase)
            {
                case StableFootingSoloPhase.Countdown:
                    return "START IN";
                case StableFootingSoloPhase.Running:
                    return "RUNNING";
                case StableFootingSoloPhase.RoundResult:
                    return "ROUND RESULT";
                case StableFootingSoloPhase.Complete:
                    return "COMPLETE";
                default:
                    return _session.Phase.ToString().ToUpperInvariant();
            }
        }

        private static string GetCyclePhaseLabel(
            StableFootingCyclePhase phase)
        {
            switch (phase)
            {
                case StableFootingCyclePhase.ShuffleReveal:
                    return "SHUFFLING";
                case StableFootingCyclePhase.Move:
                    return "MOVE";
                case StableFootingCyclePhase.Drop:
                    return "UNSAFE TILES DROP";
                case StableFootingCyclePhase.Restore:
                    return "RESTORING";
                default:
                    return "ROUND COMPLETE";
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
                    StableFootingSoloSession.LocalPlayerSlot)
                {
                    return "MATCH " + ToOrdinal(leaderboard[index].Rank) +
                           "  ·  " +
                           leaderboard[index].TotalPoints + " PT";
                }
            }
            return "MATCH COMPLETE";
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

        private static int WorldToTileIndex(Vector3 position)
        {
            if (NetworkStableFootingState.TryGetTileIndex(
                    position,
                    out var tileIndex))
            {
                return tileIndex;
            }
            return -1;
        }

        private static GameObject RequireChild(
            Transform parent,
            string childName)
        {
            var child = parent.Find(childName);
            if (child == null)
            {
                throw new InvalidOperationException(
                    parent.name + " is missing child '" +
                    childName + "'.");
            }
            return child.gameObject;
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
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
