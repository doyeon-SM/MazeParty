using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.StableFooting;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Client-only presentation for replicated Stable Footing state. The
    /// additive scene owns a single fixed Cinemachine camera, giving every
    /// client the same view of the shared arena.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StableFootingNetworkView : MonoBehaviour
    {
        public const float SharedCameraHeight = 24f;
        public const float SharedCameraOrthographicSize = 11.5f;
        public const float RunnerPresentationHeight = 1.18f;

        private const float RunnerInterpolationSpeed = 16f;
        private const float TileInterpolationSpeed = 7f;
        private const float DroppedTileOffset = 5f;
        private const float EliminatedRunnerOffset = 4f;

        private static readonly Color[] FallbackPlayerColors =
        {
            new Color(0.16f, 0.48f, 0.95f),
            new Color(0.92f, 0.2f, 0.16f),
            new Color(0.18f, 0.78f, 0.32f),
            new Color(0.7f, 0.26f, 0.9f)
        };

        [SerializeField] private NetworkStableFootingState state;
        [SerializeField] private CinemachineCamera sharedCamera;
        [SerializeField] private Transform runnerRoot;
        [SerializeField] private Transform tileRoot;
        [SerializeField] private GameObject arenaPresentation;
        [SerializeField] private Renderer safeSymbolCrossRenderer;
        [SerializeField] private Renderer safeSymbolCircleRenderer;
        [SerializeField] private Renderer safeSymbolSquareRenderer;
        [SerializeField] private AudioSource cueAudioSource;
        [SerializeField] private StableFootingHudBindings hud;

        private readonly RunnerView[] _runners =
            new RunnerView[StableFootingRules.PlayerCount];
        private readonly TileView[] _tiles =
            new TileView[StableFootingRules.TileCount];

        private GameplayCameraDirector _cameraDirector;
        private bool _cameraConfigured;
        private bool _tileViewsCached;
        private bool _hudDefaultsCaptured;
        private bool _worldVisible;
        private bool _worldVisibilityInitialized;
        private string _defaultInstructionText = string.Empty;
        private int _localSlot = -1;
        private int _lastCueCycle = -1;
        private StableFootingCyclePhase _lastCuePhase =
            (StableFootingCyclePhase)byte.MaxValue;
        private uint _lastPushRevision;

        public static Quaternion SharedCameraRotation =>
            Quaternion.Euler(90f, 0f, 0f);

        public static Vector3 SharedCameraPosition =>
            new Vector3(
                NetworkStableFootingState.ArenaCenterX,
                SharedCameraHeight,
                0f);

        public void Configure(
            NetworkStableFootingState networkState,
            CinemachineCamera camera,
            Transform runners,
            Transform tiles,
            GameObject arena,
            Renderer cross,
            Renderer circle,
            Renderer square,
            AudioSource audioSource,
            StableFootingHudBindings hudBindings)
        {
            state = networkState;
            sharedCamera = camera;
            runnerRoot = runners;
            tileRoot = tiles;
            arenaPresentation = arena;
            safeSymbolCrossRenderer = cross;
            safeSymbolCircleRenderer = circle;
            safeSymbolSquareRenderer = square;
            cueAudioSource = audioSource;
            hud = hudBindings;
            _cameraConfigured = false;
            _tileViewsCached = false;
            _hudDefaultsCaptured = false;
            ResolveSceneReferences();
            ConfigureCamera();
            CacheTileViews();
            if (Application.isPlaying)
            {
                EnsureRunners();
            }
        }

        private void Awake()
        {
            ResolveSceneReferences();
            ConfigureCamera();
            CacheTileViews();
            EnsureRunners();
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
            state ??= GetComponent<NetworkStableFootingState>();
            CacheTileViews();
            EnsureRunners();

            var match = NetworkMatchState.Instance;
            var selected = match != null && match.IsStableFootingPhase;
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
            RefreshPushPresentation();
            RefreshTiles();
            RefreshSafeSymbolDisplay();
            RefreshCue();
            RefreshHud(match);
        }

        private void ResolveSceneReferences()
        {
            state ??= GetComponent<NetworkStableFootingState>();
            if (!_hudDefaultsCaptured &&
                hud != null && hud.InstructionText != null)
            {
                _defaultInstructionText = hud.InstructionText.text;
                _hudDefaultsCaptured = true;
            }
        }

        private void ConfigureCamera()
        {
            if (sharedCamera == null)
            {
                return;
            }

            var lens = sharedCamera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize = SharedCameraOrthographicSize;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 150f;
            sharedCamera.Lens = lens;
            sharedCamera.ForceCameraPosition(
                SharedCameraPosition,
                SharedCameraRotation);
            sharedCamera.Priority = 0;
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
            if (_cameraDirector != null && sharedCamera != null)
            {
                _cameraDirector.SetMinigameCamera(sharedCamera);
            }
        }

        private void UnregisterCamera()
        {
            if (_cameraDirector != null && sharedCamera != null)
            {
                _cameraDirector.ClearMinigameCamera(sharedCamera);
            }
            if (sharedCamera != null)
            {
                sharedCamera.Priority = 0;
            }
        }

        private void CacheTileViews()
        {
            if (_tileViewsCached || tileRoot == null)
            {
                return;
            }

            var foundCount = 0;
            for (var tileIndex = 0;
                 tileIndex < StableFootingRules.TileCount;
                 tileIndex++)
            {
                var anchor = FindDescendant(
                    tileRoot,
                    "Tile Anchor " + tileIndex.ToString("00"));
                if (anchor == null)
                {
                    continue;
                }

                _tiles[tileIndex] = new TileView(
                    anchor,
                    FindDescendant(anchor, "Cross Mark"),
                    FindDescendant(anchor, "Circle Mark"),
                    FindDescendant(anchor, "Square Mark"));
                foundCount++;
            }

            _tileViewsCached =
                foundCount == StableFootingRules.TileCount;
        }

        private void EnsureRunners()
        {
            if (runnerRoot == null)
            {
                return;
            }

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
            DisableGeneratedHitColliders(runnerObject);

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

                var eliminated = state.IsEliminated(slot);
                var target = state.GetRunnerPosition(slot) +
                             Vector3.up * RunnerPresentationHeight;
                if (eliminated)
                {
                    target.y -= EliminatedRunnerOffset;
                }

                if (!runner.HasPosition ||
                    state.Phase == NetworkStableFootingPhase.Countdown ||
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
                runner.Visual.SetEliminated(eliminated);
            }
        }

        private void RefreshPushPresentation()
        {
            if (state.PushRevision == _lastPushRevision)
            {
                return;
            }

            _lastPushRevision = state.PushRevision;
            var pusherSlot = state.LastPusherSlot;
            if (pusherSlot >= 0 && pusherSlot < _runners.Length)
            {
                _runners[pusherSlot]?.Visual.TriggerPunch();
            }
        }

        private void RefreshTiles()
        {
            var snap = state.Phase ==
                       NetworkStableFootingPhase.Countdown;
            for (var tileIndex = 0;
                 tileIndex < _tiles.Length;
                 tileIndex++)
            {
                var tile = _tiles[tileIndex];
                if (tile == null)
                {
                    continue;
                }

                tile.SetSymbol(state.GetTileSymbol(tileIndex));
                tile.SetActive(
                    state.IsTileActive(tileIndex),
                    snap,
                    Time.unscaledDeltaTime);
            }
        }

        private void RefreshSafeSymbolDisplay()
        {
            var visible = state.Phase ==
                          NetworkStableFootingPhase.Running;
            SetRendererGroupActive(
                safeSymbolCrossRenderer,
                visible && state.SafeSymbol ==
                    StableFootingSymbol.Cross);
            SetRendererGroupActive(
                safeSymbolCircleRenderer,
                visible && state.SafeSymbol ==
                    StableFootingSymbol.Circle);
            SetRendererGroupActive(
                safeSymbolSquareRenderer,
                visible && state.SafeSymbol ==
                    StableFootingSymbol.Square);
        }

        private void RefreshCue()
        {
            if (_lastCueCycle == state.CycleNumber &&
                _lastCuePhase == state.CyclePhase)
            {
                return;
            }

            _lastCueCycle = state.CycleNumber;
            _lastCuePhase = state.CyclePhase;
            if (state.Phase == NetworkStableFootingPhase.Running &&
                cueAudioSource != null &&
                cueAudioSource.clip != null)
            {
                cueAudioSource.Play();
            }
        }

        private void RefreshHud(NetworkMatchState match)
        {
            if (hud == null || !hud.HasRequiredReferences)
            {
                return;
            }

            var reconnectPaused = match.IsReconnectPaused;
            hud.PausePanel.SetActive(reconnectPaused);
            hud.ControlsPanel.SetActive(
                !reconnectPaused &&
                state.Phase == NetworkStableFootingPhase.Running);
            hud.ResultPanel.SetActive(
                state.Phase == NetworkStableFootingPhase.RoundResult ||
                state.Phase == NetworkStableFootingPhase.Complete);

            hud.RoundText.text = "ROUND " +
                Mathf.Clamp(
                    state.RoundNumber,
                    1,
                    MinigameCatalog.GetRoundCount(
                        ScheduledMinigameId.StableFooting)) +
                " / " + MinigameCatalog.GetRoundCount(
                    ScheduledMinigameId.StableFooting);
            hud.TimerText.text = reconnectPaused
                ? MinigameDisplayFormatter.FormatClock(
                    match.ReconnectRemaining)
                : MinigameDisplayFormatter.FormatClock(state.Remaining);
            hud.PhaseText.text = reconnectPaused
                ? "PLAYER DISCONNECTED · MATCH PAUSED"
                : BuildPhaseLabel();
            hud.InstructionText.text = reconnectPaused
                ? "Waiting up to 60 seconds for the player to reconnect."
                : BuildInstructionLabel();

            for (var slot = 0;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                var displayName = avatar != null &&
                                  !string.IsNullOrWhiteSpace(
                                      avatar.DisplayName)
                    ? avatar.DisplayName
                    : "PLAYER " + (slot + 1);
                var rank = ResolveDisplayedRank(slot);
                var eliminated = state.IsEliminated(slot);
                hud.PlayerRows[slot].text =
                    (slot == _localSlot ? "> " : string.Empty) +
                    MinigameDisplayFormatter.ToOrdinal(rank) +
                    "  " + displayName + "\n" +
                    (eliminated
                        ? "OUT  ·  FALL " +
                          state.GetEliminationOrder(slot)
                        : "STANDING") +
                    "\n+" + state.GetRoundPoints(slot) +
                    "  ·  TOTAL " + state.GetScore(slot) +
                    (state.GetFinalRank(slot) > 0
                        ? "\nFINAL " +
                          MinigameDisplayFormatter.ToOrdinal(
                              state.GetFinalRank(slot)) +
                          "  ·  GOLD +" +
                          StableFootingRules.GetPointsForRank(
                              state.GetFinalRank(slot))
                        : string.Empty);
                hud.PlayerRows[slot].color = avatar != null
                    ? avatar.Appearance.BodyColor
                    : hud.GetDefaultPlayerRowColor(slot);
            }
        }

        private string BuildPhaseLabel()
        {
            switch (state.Phase)
            {
                case NetworkStableFootingPhase.Countdown:
                    return "GET READY";
                case NetworkStableFootingPhase.RoundResult:
                    return "ROUND RESULTS";
                case NetworkStableFootingPhase.Complete:
                    return "FINAL RESULTS";
                case NetworkStableFootingPhase.Running:
                    switch (state.CyclePhase)
                    {
                        case StableFootingCyclePhase.ShuffleReveal:
                            return "SYMBOL SHUFFLE";
                        case StableFootingCyclePhase.Move:
                            return "MOVE TO " +
                                   SymbolLabel(state.SafeSymbol);
                        case StableFootingCyclePhase.Drop:
                            return "FLOOR DROPPING";
                        case StableFootingCyclePhase.Restore:
                            return "STABILIZING";
                        default:
                            return "HOLD ON";
                    }
                default:
                    return "STABLE FOOTING";
            }
        }

        private string BuildInstructionLabel()
        {
            if (state.Phase != NetworkStableFootingPhase.Running)
            {
                return string.IsNullOrWhiteSpace(_defaultInstructionText)
                    ? "WASD MOVE  ·  LEFT CLICK PUSH"
                    : _defaultInstructionText;
            }

            switch (state.CyclePhase)
            {
                case StableFootingCyclePhase.ShuffleReveal:
                    return "TARGET: " +
                           SymbolLabel(state.SafeSymbol) +
                           "  ·  WATCH THE TILE SYMBOLS";
                case StableFootingCyclePhase.Move:
                    return "STAND ON " +
                           SymbolLabel(state.SafeSymbol) +
                           "  ·  LEFT CLICK PUSH";
                case StableFootingCyclePhase.Drop:
                    return "UNSAFE TILES ARE FALLING";
                default:
                    return "GET READY FOR THE NEXT SYMBOL";
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
            if (state.Phase == NetworkStableFootingPhase.RoundResult &&
                roundRank > 0)
            {
                return roundRank;
            }

            var rank = 1;
            for (var other = 0;
                 other < StableFootingRules.PlayerCount;
                 other++)
            {
                if (other != slot &&
                    CompareLiveStanding(other, slot) < 0)
                {
                    rank++;
                }
            }
            return rank;
        }

        private int CompareLiveStanding(int leftSlot, int rightSlot)
        {
            var leftOut = state.IsEliminated(leftSlot);
            var rightOut = state.IsEliminated(rightSlot);
            if (leftOut != rightOut)
            {
                return leftOut ? 1 : -1;
            }
            if (leftOut)
            {
                var elimination = state.GetEliminationOrder(rightSlot)
                    .CompareTo(state.GetEliminationOrder(leftSlot));
                if (elimination != 0)
                {
                    return elimination;
                }
            }
            return leftSlot.CompareTo(rightSlot);
        }

        private void SetWorldPresentationActive(bool active)
        {
            if (_worldVisibilityInitialized && _worldVisible == active)
            {
                return;
            }
            _worldVisible = active;
            _worldVisibilityInitialized = true;

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
                _lastCueCycle = -1;
                _lastCuePhase =
                    (StableFootingCyclePhase)byte.MaxValue;
                cueAudioSource?.Stop();
            }
            else if (state != null)
            {
                _lastPushRevision = state.PushRevision;
            }
        }

        private void SetHudActive(bool active)
        {
            if (hud != null && hud.gameObject.activeSelf != active)
            {
                hud.gameObject.SetActive(active);
            }
        }

        private static void SetRendererGroupActive(
            Renderer renderer,
            bool active)
        {
            if (renderer == null)
            {
                return;
            }

            var target = renderer.gameObject;
            var parent = renderer.transform.parent;
            if (parent != null &&
                parent.name.EndsWith("Mark", StringComparison.Ordinal))
            {
                target = parent.gameObject;
            }
            if (target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }

        private static string SymbolLabel(StableFootingSymbol symbol)
        {
            switch (symbol)
            {
                case StableFootingSymbol.Circle: return "CIRCLE";
                case StableFootingSymbol.Square: return "SQUARE";
                default: return "CROSS";
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

        private static Transform FindDescendant(
            Transform root,
            string name)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == name)
            {
                return root;
            }
            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindDescendant(root.GetChild(index), name);
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
            }

            public Transform Root { get; }
            public PlayerAvatarVisual Visual { get; }
            public bool HasPosition { get; set; }
        }

        private sealed class TileView
        {
            private readonly Transform _root;
            private readonly GameObject _cross;
            private readonly GameObject _circle;
            private readonly GameObject _square;
            private readonly Vector3 _baseLocalPosition;
            private StableFootingSymbol _displayedSymbol =
                (StableFootingSymbol)byte.MaxValue;

            public TileView(
                Transform root,
                Transform cross,
                Transform circle,
                Transform square)
            {
                _root = root;
                _cross = cross != null ? cross.gameObject : null;
                _circle = circle != null ? circle.gameObject : null;
                _square = square != null ? square.gameObject : null;
                _baseLocalPosition = root.localPosition;
            }

            public void SetSymbol(StableFootingSymbol symbol)
            {
                if (_displayedSymbol == symbol)
                {
                    return;
                }
                _displayedSymbol = symbol;
                SetActive(_cross, symbol == StableFootingSymbol.Cross);
                SetActive(_circle, symbol == StableFootingSymbol.Circle);
                SetActive(_square, symbol == StableFootingSymbol.Square);
            }

            public void SetActive(
                bool active,
                bool snap,
                float deltaTime)
            {
                var target = _baseLocalPosition;
                if (!active)
                {
                    target.y -= DroppedTileOffset;
                }
                _root.localPosition = snap
                    ? target
                    : Vector3.Lerp(
                        _root.localPosition,
                        target,
                        1f - Mathf.Exp(
                            -TileInterpolationSpeed * deltaTime));
            }

            private static void SetActive(
                GameObject target,
                bool active)
            {
                if (target != null && target.activeSelf != active)
                {
                    target.SetActive(active);
                }
            }
        }
    }
}
