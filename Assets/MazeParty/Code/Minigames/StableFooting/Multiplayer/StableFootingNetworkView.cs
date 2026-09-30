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
        [SerializeField] private GameObject interactionVfxPrefab;

        private readonly RunnerView[] _runners =
            new RunnerView[StableFootingRules.PlayerCount];
        private readonly TileView[] _tiles =
            new TileView[StableFootingRules.TileCount];

        private GameplayCameraDirector _cameraDirector;
        private bool _cameraConfigured;
        private bool _tileViewsCached;
        private bool _worldVisible;
        private bool _worldVisibilityInitialized;
        private int _lastCueCycle = -1;
        private StableFootingCyclePhase _lastCuePhase =
            (StableFootingCyclePhase)byte.MaxValue;
        private bool _cueBaselineInitialized;
        private NetworkStableFootingState _subscribedVfxState;

        public static Quaternion SharedCameraRotation =>
            Quaternion.Euler(90f, 0f, 0f);

        public static Vector3 SharedCameraPosition =>
            new Vector3(
                NetworkStableFootingState.ArenaCenterX,
                SharedCameraHeight,
                0f);

        public GameObject InteractionVfxPrefab => interactionVfxPrefab;

        public void ConfigureVfx(GameObject interactionPrefab)
        {
            interactionVfxPrefab = interactionPrefab;
        }

        public void Configure(
            NetworkStableFootingState networkState,
            CinemachineCamera camera,
            Transform runners,
            Transform tiles,
            GameObject arena,
            Renderer cross,
            Renderer circle,
            Renderer square,
            AudioSource audioSource)
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
            _cameraConfigured = false;
            _tileViewsCached = false;
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
            EnsurePushVfxSubscription();
            ConfigureCamera();
            CacheTileViews();
            EnsureRunners();
            SetWorldPresentationActive(false);
        }

        private void OnEnable()
        {
            ResolveSceneReferences();
            EnsurePushVfxSubscription();
        }

        private void OnDisable()
        {
            UnsubscribeFromPushVfxEvents();
            SetWorldPresentationActive(false);
            UnregisterCamera();
        }

        private void Update()
        {
            state ??= GetComponent<NetworkStableFootingState>();
            EnsurePushVfxSubscription();
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
                                       BoardFlowState.MinigameResult);
            if (!shouldShowWorld)
            {
                SetWorldPresentationActive(false);
                return;
            }

            SetWorldPresentationActive(true);
            // MinigameLocalPlayerHighlight owns the brief shared countdown
            // marker; this view does not keep a persistent local-player mark.
            RefreshRunners(match);
            RefreshTiles();
            RefreshSafeSymbolDisplay();
            RefreshCue();
        }

        private void ResolveSceneReferences()
        {
            state ??= GetComponent<NetworkStableFootingState>();
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
            visual.SetDisplayName(GameText.F("Player {0}", slot + 1));
            DisableGeneratedHitColliders(runnerObject);

            return new RunnerView(runnerObject.transform, visual);
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
                        appearance.HatId,
                        appearance.ExpressionId);
                    runner.Visual.SetDisplayName(
                        string.IsNullOrWhiteSpace(avatar.DisplayName)
                            ? GameText.F("Player {0}", slot + 1)
                            : avatar.DisplayName);
                }
                runner.Visual.SetEliminated(eliminated);
            }
        }

        private void HandlePushPresentationRequested(
            int pusherSlot,
            int targetSlot,
            Vector3 targetPosition)
        {
            if (!_worldVisible)
            {
                return;
            }

            if (pusherSlot >= 0 && pusherSlot < _runners.Length)
            {
                _runners[pusherSlot]?.Visual.TriggerPunch();
            }
            var target = targetSlot >= 0 && targetSlot < _runners.Length
                ? _runners[targetSlot]
                : null;
            if (target != null)
            {
                target.Visual.TriggerHit();
                if (interactionVfxPrefab != null)
                {
                    OneShotVfxPool.Play(
                        interactionVfxPrefab,
                        targetPosition + Vector3.up * 0.65f,
                        Quaternion.identity,
                        0.65f);
                }
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
            if (!_cueBaselineInitialized)
            {
                _cueBaselineInitialized = true;
                _lastCueCycle = state.CycleNumber;
                _lastCuePhase = state.CyclePhase;
                return;
            }
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
            if (state.Phase == NetworkStableFootingPhase.Running &&
                interactionVfxPrefab != null)
            {
                var renderer = state.SafeSymbol ==
                               StableFootingSymbol.Cross
                    ? safeSymbolCrossRenderer
                    : state.SafeSymbol == StableFootingSymbol.Circle
                        ? safeSymbolCircleRenderer
                        : safeSymbolSquareRenderer;
                if (renderer != null)
                {
                    OneShotVfxPool.Play(
                        interactionVfxPrefab,
                        renderer.bounds.center,
                        Quaternion.identity,
                        1.1f);
                }
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
                _cueBaselineInitialized = false;
                cueAudioSource?.Stop();
            }
        }

        private void EnsurePushVfxSubscription()
        {
            if (_subscribedVfxState == state)
            {
                return;
            }
            UnsubscribeFromPushVfxEvents();
            _subscribedVfxState = state;
            if (_subscribedVfxState != null)
            {
                _subscribedVfxState.PushPresentationRequested +=
                    HandlePushPresentationRequested;
            }
        }

        private void UnsubscribeFromPushVfxEvents()
        {
            if (_subscribedVfxState != null)
            {
                _subscribedVfxState.PushPresentationRequested -=
                    HandlePushPresentationRequested;
                _subscribedVfxState = null;
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
