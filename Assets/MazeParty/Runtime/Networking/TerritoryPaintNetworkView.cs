using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.TerritoryPaint;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Client presentation for the replicated paint surface and four logical
    /// runners. Every client registers the same fixed top-down camera.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerritoryPaintNetworkView : MonoBehaviour
    {
        public const float SharedCameraHeight = 25f;
        public const float SharedCameraOrthographicSize = 10.7f;
        public const float RunnerPresentationHeight = 1.18f;

        private const float RunnerInterpolationSpeed = 18f;

        private static readonly Color32 UnpaintedColor =
            new Color32(58, 65, 73, 255);
        private static readonly Color32[] FallbackPlayerColors =
        {
            new Color32(45, 122, 242, 255),
            new Color32(235, 57, 48, 255),
            new Color32(46, 199, 82, 255),
            new Color32(177, 68, 232, 255)
        };

        [SerializeField] private NetworkTerritoryPaintState state;
        [SerializeField] private CinemachineCamera sharedCamera;
        [SerializeField] private Transform runnerRoot;
        [SerializeField] private GameObject arenaPresentation;
        [SerializeField] private Renderer paintSurfaceRenderer;
        [SerializeField] private TerritoryPaintHudBindings hud;

        private readonly RunnerView[] _runners =
            new RunnerView[TerritoryPaintRules.PlayerCount];
        private readonly Color32[] _pixels =
            new Color32[
                TerritoryPaintRules.SurfaceResolution *
                TerritoryPaintRules.SurfaceResolution];
        private readonly Color32[] _resolvedPlayerColors =
            (Color32[])FallbackPlayerColors.Clone();

        private GameplayCameraDirector _cameraDirector;
        private Texture2D _paintTexture;
        private Material _surfaceMaterial;
        private uint _lastPaintRevision = uint.MaxValue;
        private int _localSlot = -1;
        private bool _cameraConfigured;
        private bool _worldVisible;
        private bool _visibilityInitialized;
        private bool _paintColorsDirty = true;

        public static Quaternion SharedCameraRotation =>
            Quaternion.Euler(90f, 0f, 0f);

        public static Vector3 SharedCameraPosition =>
            new Vector3(
                NetworkTerritoryPaintState.ArenaCenterX,
                SharedCameraHeight,
                0f);

        public void Configure(
            NetworkTerritoryPaintState networkState,
            CinemachineCamera camera,
            Transform runners,
            GameObject arena,
            Renderer surfaceRenderer,
            TerritoryPaintHudBindings hudBindings)
        {
            state = networkState;
            sharedCamera = camera;
            runnerRoot = runners;
            arenaPresentation = arena;
            paintSurfaceRenderer = surfaceRenderer;
            hud = hudBindings;
            _cameraConfigured = false;
            ConfigureCamera();
            if (Application.isPlaying)
            {
                EnsurePaintTexture();
                EnsureRunners();
            }
        }

        private void Awake()
        {
            state ??= GetComponent<NetworkTerritoryPaintState>();
            ConfigureCamera();
            EnsurePaintTexture();
            EnsureRunners();
            SetWorldPresentationActive(false);
            SetHudActive(false);
        }

        private void OnDisable()
        {
            SetWorldPresentationActive(false);
            SetHudActive(false);
            UnregisterCamera();
        }

        private void OnDestroy()
        {
            if (_paintTexture != null)
            {
                Destroy(_paintTexture);
            }
            if (_surfaceMaterial != null)
            {
                Destroy(_surfaceMaterial);
            }
        }

        private void Update()
        {
            state ??= GetComponent<NetworkTerritoryPaintState>();
            EnsurePaintTexture();
            EnsureRunners();

            var match = NetworkMatchState.Instance;
            var selected =
                match != null && match.IsTerritoryPaintPhase;
            if (selected)
            {
                RegisterCamera();
            }
            else
            {
                UnregisterCamera();
            }

            var shouldShowWorld =
                state != null && state.IsSpawned &&
                match != null && selected &&
                (match.FlowState == BoardFlowState.MinigamePlaying ||
                 match.FlowState == BoardFlowState.SkippedResult);
            var shouldShowHud =
                shouldShowWorld &&
                match.FlowState == BoardFlowState.MinigamePlaying;
            SetHudActive(shouldShowHud);
            if (!shouldShowWorld)
            {
                SetWorldPresentationActive(false);
                return;
            }

            SetWorldPresentationActive(true);
            ResolveLocalSlot(match);
            RefreshRunners(match);
            RefreshPaintSurface();
            RefreshHud(match);
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
            lens.FarClipPlane = 80f;
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
        }

        private void EnsurePaintTexture()
        {
            if (!Application.isPlaying ||
                _paintTexture != null ||
                paintSurfaceRenderer == null)
            {
                return;
            }

            _paintTexture = new Texture2D(
                TerritoryPaintRules.SurfaceResolution,
                TerritoryPaintRules.SurfaceResolution,
                TextureFormat.RGBA32,
                false)
            {
                name = "Territory Paint Runtime Surface",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            for (var index = 0; index < _pixels.Length; index++)
            {
                _pixels[index] = UnpaintedColor;
            }
            _paintTexture.SetPixels32(_pixels);
            _paintTexture.Apply(false, false);

            if (paintSurfaceRenderer.sharedMaterial != null)
            {
                _surfaceMaterial =
                    new Material(paintSurfaceRenderer.sharedMaterial)
                    {
                        name = "Territory Paint Runtime Material"
                    };
                _surfaceMaterial.mainTexture = _paintTexture;
                if (_surfaceMaterial.HasProperty("_BaseMap"))
                {
                    _surfaceMaterial.SetTexture(
                        "_BaseMap",
                        _paintTexture);
                }
                paintSurfaceRenderer.sharedMaterial = _surfaceMaterial;
            }
        }

        private void EnsureRunners()
        {
            if (!Application.isPlaying || runnerRoot == null)
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
            var runnerObject =
                new GameObject("Runner " + (slot + 1));
            runnerObject.transform.SetParent(runnerRoot, false);
            var visual =
                runnerObject.AddComponent<PlayerAvatarVisual>();
            visual.EnsureBuilt();
            visual.SetOwnerFirstPerson(false);
            visual.SetBodyColor(FallbackPlayerColors[slot]);
            visual.SetDisplayName("PLAYER " + (slot + 1));
            visual.SetEliminated(false);
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

            if (_localSlot == resolved)
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

                var position = state.GetPlayerPosition(slot);
                var target = new Vector3(
                    position.x,
                    RunnerPresentationHeight,
                    position.y);
                if (!runner.HasPosition ||
                    state.Phase ==
                    NetworkTerritoryPaintPhase.Countdown ||
                    Vector3.SqrMagnitude(
                        runner.Root.position - target) > 64f)
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

                var facing = state.GetPlayerFacing(slot);
                if (facing.sqrMagnitude > 0.0001f)
                {
                    runner.Root.rotation = Quaternion.LookRotation(
                        new Vector3(facing.x, 0f, facing.y),
                        Vector3.up);
                }

                var avatar = match.GetAvatarForSlot(slot);
                if (avatar != null)
                {
                    var appearance = avatar.Appearance;
                    var bodyColor = (Color32)appearance.BodyColor;
                    if (!_resolvedPlayerColors[slot].Equals(bodyColor))
                    {
                        _resolvedPlayerColors[slot] = bodyColor;
                        _paintColorsDirty = true;
                    }
                    runner.Visual.SetBodyColor(appearance.BodyColor);
                    runner.Visual.ApplyAppearance(
                        appearance.EyeId,
                        appearance.MouthId,
                        appearance.HatId);
                    runner.Visual.SetDisplayName(
                        string.IsNullOrWhiteSpace(avatar.DisplayName)
                            ? "PLAYER " + (slot + 1)
                            : avatar.DisplayName);
                }
            }
        }

        private void RefreshPaintSurface()
        {
            if (_paintTexture == null ||
                (state.PaintRevision == _lastPaintRevision &&
                 !_paintColorsDirty))
            {
                return;
            }

            _lastPaintRevision = state.PaintRevision;
            _paintColorsDirty = false;
            var resolution =
                TerritoryPaintRules.SurfaceResolution;
            for (var y = 0; y < resolution; y++)
            {
                for (var x = 0; x < resolution; x++)
                {
                    var owner = state.GetPaintOwner(x, y);
                    _pixels[y * resolution + x] =
                        owner < _resolvedPlayerColors.Length
                            ? _resolvedPlayerColors[owner]
                            : UnpaintedColor;
                }
            }

            _paintTexture.SetPixels32(_pixels);
            _paintTexture.Apply(false, false);
        }

        private void RefreshHud(NetworkMatchState match)
        {
            if (hud == null || !hud.HasRequiredReferences)
            {
                return;
            }

            hud.TimerDial.SetTime(
                match.IsReconnectPaused
                    ? match.ReconnectRemaining
                    : state.Remaining,
                match.IsReconnectPaused
                    ? NetworkMatchState.ReconnectGraceSeconds
                    : GetTimerDuration(state.Phase));
            for (var slot = 0;
                 slot < TerritoryPaintRules.PlayerCount;
                 slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                var displayName =
                    avatar != null &&
                    !string.IsNullOrWhiteSpace(avatar.DisplayName)
                        ? avatar.DisplayName
                        : "PLAYER " + (slot + 1);
                hud.PlayerRows[slot].text =
                    (slot == _localSlot ? "> " : string.Empty) +
                    displayName + "   " +
                    state.GetScore(slot);
                hud.PlayerRows[slot].color =
                    avatar != null
                        ? avatar.Appearance.BodyColor
                        : hud.GetDefaultPlayerRowColor(slot);
            }
        }

        private static double GetTimerDuration(
            NetworkTerritoryPaintPhase phase)
        {
            switch (phase)
            {
                case NetworkTerritoryPaintPhase.Countdown:
                    return NetworkTerritoryPaintState.CountdownSeconds;
                case NetworkTerritoryPaintPhase.Running:
                    return TerritoryPaintRules.RoundSeconds;
                case NetworkTerritoryPaintPhase.RoundResult:
                    return NetworkTerritoryPaintState.ResultSeconds;
                default:
                    return 1d;
            }
        }


        private void SetWorldPresentationActive(bool active)
        {
            if (_visibilityInitialized &&
                _worldVisible == active)
            {
                return;
            }

            _visibilityInitialized = true;
            _worldVisible = active;
            if (arenaPresentation != null)
            {
                arenaPresentation.SetActive(active);
            }
            if (runnerRoot != null)
            {
                runnerRoot.gameObject.SetActive(active);
            }
        }

        private void SetHudActive(bool active)
        {
            if (hud != null && hud.RootCanvas != null &&
                hud.RootCanvas.gameObject.activeSelf != active)
            {
                hud.RootCanvas.gameObject.SetActive(active);
            }
        }

        private static void DisableGeneratedHitColliders(
            GameObject root)
        {
            var colliders =
                root.GetComponentsInChildren<Collider>(true);
            for (var index = 0;
                 index < colliders.Length;
                 index++)
            {
                colliders[index].enabled = false;
            }
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
    }
}
