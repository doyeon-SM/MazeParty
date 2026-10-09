using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.TerritoryPaint;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Client presentation for the replicated paint surface and four logical
    /// runners. Every client registers the same fixed angled camera.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerritoryPaintNetworkView : MonoBehaviour
    {
        public const float SharedCameraHeight = 25f;
        public const float SharedCameraOrthographicSize = 10.7f;
        public const float RunnerPresentationHeight = 1.18f;

        private const float RunnerInterpolationSpeed = 18f;
        private const float PaintSplashCooldownSeconds = 0.14f;

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
        [SerializeField] private GameObject paintSplashVfxPrefab;

        private readonly RunnerView[] _runners =
            new RunnerView[TerritoryPaintRules.PlayerCount];
        private readonly Color32[] _pixels =
            new Color32[
                TerritoryPaintRules.SurfaceResolution *
                TerritoryPaintRules.SurfaceResolution];
        private readonly Color32[] _resolvedPlayerColors =
            (Color32[])FallbackPlayerColors.Clone();
        private readonly byte[] _paintOwners =
            new byte[
                TerritoryPaintRules.SurfaceResolution *
                TerritoryPaintRules.SurfaceResolution];
        private readonly int[] _paintChangeCounts =
            new int[TerritoryPaintRules.PlayerCount];

        private GameplayCameraDirector _cameraDirector;
        private Texture2D _paintTexture;
        private Material _surfaceMaterial;
        private uint _lastPaintRevision = uint.MaxValue;
        private int _localSlot = -1;
        private bool _cameraConfigured;
        private bool _worldVisible;
        private bool _visibilityInitialized;
        private bool _paintColorsDirty = true;
        private float _nextPaintSplashAt;

        public GameObject PaintSplashVfxPrefab => paintSplashVfxPrefab;

        public static Quaternion SharedCameraRotation =>
            MinigameCameraFraming.SharedRotation;

        public static Vector3 SharedCameraPosition =>
            MinigameCameraFraming.CalculateSharedPosition(
                NetworkTerritoryPaintState.ArenaCenterX,
                SharedCameraHeight);

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

        public void ConfigureVfx(GameObject paintSplashPrefab)
        {
            paintSplashVfxPrefab = paintSplashPrefab;
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
                 match.FlowState == BoardFlowState.MinigameResult);
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
                _paintOwners[index] =
                    TerritoryPaintRules.UnpaintedOwner;
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
            visual.SetDisplayName(GameText.F("PLAYER {0}", slot + 1));
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
        }

        private void RefreshRunners(NetworkMatchState match)
        {
            for (var slot = 0; slot < _runners.Length; slot++)
            {
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
                }

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
                            ? GameText.F("PLAYER {0}", slot + 1)
                            : avatar.DisplayName);
                }
            }
        }

        internal static int GetPaintTextureIndex(
            int cellX,
            int cellY,
            int resolution)
        {
            if (resolution < 1)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(resolution));
            }
            if (cellX < 0 || cellX >= resolution)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(cellX));
            }
            if (cellY < 0 || cellY >= resolution)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(cellY));
            }

            // Unity's built-in Plane maps +X/+Z toward UV zero on both
            // axes. Mirror logical cells into that UV layout so the paint
            // stays beneath the authoritative world-space runner.
            var textureX = resolution - 1 - cellX;
            var textureY = resolution - 1 - cellY;
            return textureY * resolution + textureX;
        }

        private void RefreshPaintSurface()
        {
            if (_paintTexture == null)
            {
                return;
            }

            var revision = state.PaintRevision;
            var revisionChanged = revision != _lastPaintRevision;
            if (!revisionChanged && !_paintColorsDirty)
            {
                return;
            }

            if (revisionChanged)
            {
                for (var slot = 0;
                     slot < _paintChangeCounts.Length;
                     slot++)
                {
                    _paintChangeCounts[slot] = 0;
                }
            }
            var canPlaySplash = revisionChanged &&
                                _lastPaintRevision != uint.MaxValue;
            _paintColorsDirty = false;
            var resolution =
                TerritoryPaintRules.SurfaceResolution;
            for (var y = 0; y < resolution; y++)
            {
                for (var x = 0; x < resolution; x++)
                {
                    var owner = state.GetPaintOwner(x, y);
                    var index = GetPaintTextureIndex(x, y, resolution);
                    if (canPlaySplash &&
                        owner != _paintOwners[index] &&
                        owner < _paintChangeCounts.Length)
                    {
                        _paintChangeCounts[owner]++;
                    }
                    _paintOwners[index] = owner;
                    _pixels[index] =
                        owner < _resolvedPlayerColors.Length
                            ? _resolvedPlayerColors[owner]
                            : UnpaintedColor;
                }
            }

            _paintTexture.SetPixels32(_pixels);
            _paintTexture.Apply(false, false);
            if (revisionChanged)
            {
                _lastPaintRevision = revision;
            }
            if (canPlaySplash)
            {
                RefreshPaintSplashVfx();
            }
        }

        private void RefreshPaintSplashVfx()
        {
            var painterSlot = -1;
            var largestChangeCount = 0;
            for (var slot = 0; slot < _paintChangeCounts.Length; slot++)
            {
                if (_paintChangeCounts[slot] > largestChangeCount)
                {
                    largestChangeCount = _paintChangeCounts[slot];
                    painterSlot = slot;
                }
            }

            if (painterSlot < 0 ||
                paintSplashVfxPrefab == null ||
                Time.unscaledTime < _nextPaintSplashAt)
            {
                return;
            }

            var runner = _runners[painterSlot];
            if (runner == null || !runner.HasPosition)
            {
                return;
            }

            OneShotVfxPool.Play(
                paintSplashVfxPrefab,
                runner.Root.position + Vector3.up * 0.15f,
                Quaternion.identity,
                0.55f);
            _nextPaintSplashAt =
                Time.unscaledTime + PaintSplashCooldownSeconds;
        }

        private void RefreshHud(NetworkMatchState match)
        {
            if (hud == null || !hud.HasRequiredReferences)
            {
                return;
            }

            for (var slot = 0;
                 slot < TerritoryPaintRules.PlayerCount;
                 slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                hud.PlayerRows[slot].text =
                    (slot == _localSlot ? "> " : string.Empty) +
                    GameText.F(
                        "P{0}  {1}",
                        slot + 1,
                        state.GetScore(slot));
                hud.PlayerRows[slot].color =
                    avatar != null
                        ? avatar.Appearance.BodyColor
                        : hud.GetDefaultPlayerRowColor(slot);
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
            if (!active)
            {
                _lastPaintRevision = uint.MaxValue;
                _nextPaintSplashAt = 0f;
            }
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
