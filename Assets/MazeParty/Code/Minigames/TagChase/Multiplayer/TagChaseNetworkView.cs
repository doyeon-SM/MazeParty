using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.TagChase;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Client presentation for Tag Chase. Runners share one deterministic
    /// group camera while the current tagger receives an owner-only
    /// first-person view.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TagChaseNetworkView : MonoBehaviour
    {
        public const float PlayerPresentationHeight = 1.18f;
        public const float FirstPersonEyeHeight = 1.62f;
        public const float SharedFieldOfView = 48f;
        public const float TaggerFieldOfView = 68f;

        private const float PlayerInterpolationSpeed = 18f;

        private static readonly Color32[] FallbackPlayerColors =
        {
            new Color32(45, 122, 242, 255),
            new Color32(235, 57, 48, 255),
            new Color32(46, 199, 82, 255),
            new Color32(177, 68, 232, 255)
        };

        [SerializeField] private NetworkTagChaseState state;
        [SerializeField] private CinemachineCamera sharedRunnerCamera;
        [SerializeField] private CinemachineCamera taggerCamera;
        [SerializeField] private Transform playerRoot;
        [SerializeField] private GameObject arenaPresentation;
        [SerializeField] private GameObject hitSparkVfxPrefab;
        [SerializeField] private GameObject taggerAuraPrefab;

        private readonly PlayerView[] _players =
            new PlayerView[TagChaseRules.PlayerCount];

        private GameplayCameraDirector _cameraDirector;
        private MinigameCommonHudView _commonHud;
        private CinemachineCamera _registeredCamera;
        private int _localSlot = -1;
        private bool _worldVisible;
        private bool _visibilityInitialized;
        private bool _caughtMaskBaselineInitialized;
        private byte _lastCaughtMask;
        private NetworkTagChaseState _subscribedActionState;

        public GameObject HitSparkVfxPrefab => hitSparkVfxPrefab;
        public GameObject TaggerAuraPrefab => taggerAuraPrefab;

        public void ConfigureVfx(
            GameObject hitSparkPrefab,
            GameObject auraPrefab)
        {
            hitSparkVfxPrefab = hitSparkPrefab;
            taggerAuraPrefab = auraPrefab;
        }

        public static Vector3 InitialSharedCameraPosition =>
            new Vector3(
                NetworkTagChaseState.ArenaCenterX,
                11f,
                -13f);

        public static Quaternion InitialSharedCameraRotation =>
            Quaternion.LookRotation(
                new Vector3(
                    NetworkTagChaseState.ArenaCenterX,
                    0.8f,
                    0f) - InitialSharedCameraPosition,
                Vector3.up);

        public void Configure(
            NetworkTagChaseState networkState,
            CinemachineCamera runnerCamera,
            CinemachineCamera firstPersonCamera,
            Transform players,
            GameObject arena)
        {
            state = networkState;
            EnsureActionPresentationSubscription();
            sharedRunnerCamera = runnerCamera;
            taggerCamera = firstPersonCamera;
            playerRoot = players;
            arenaPresentation = arena;
            ConfigureCameras();
            if (Application.isPlaying)
            {
                EnsurePlayers();
            }
        }

        private void Awake()
        {
            state ??= GetComponent<NetworkTagChaseState>();
            EnsureActionPresentationSubscription();
            ConfigureCameras();
            EnsurePlayers();
            SetWorldPresentationActive(false);
        }

        private void OnEnable()
        {
            state ??= GetComponent<NetworkTagChaseState>();
            EnsureActionPresentationSubscription();
        }

        private void OnDisable()
        {
            UnsubscribeFromActionPresentation();
            SetWorldPresentationActive(false);
            SetTaggerAimVisible(false);
            UnregisterCamera();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            state ??= GetComponent<NetworkTagChaseState>();
            EnsureActionPresentationSubscription();
            EnsurePlayers();

            var match = NetworkMatchState.Instance;
            var selected =
                match != null && match.IsTagChasePhase;
            var shouldShowWorld =
                state != null &&
                state.IsSpawned &&
                selected &&
                (match.FlowState ==
                    BoardFlowState.MinigamePlaying ||
                 match.FlowState ==
                    BoardFlowState.MinigameResult);
            if (!shouldShowWorld)
            {
                SetWorldPresentationActive(false);
                SetTaggerAimVisible(false);
                UnregisterCamera();
                return;
            }

            SetWorldPresentationActive(true);
            ResolveLocalSlot(match);
            RefreshPlayers(match);
            RefreshCatchVfx();
            RefreshCamera(match);
        }

        private void ConfigureCameras()
        {
            ConfigureCamera(
                sharedRunnerCamera,
                SharedFieldOfView);
            ConfigureCamera(
                taggerCamera,
                TaggerFieldOfView);
            if (sharedRunnerCamera != null)
            {
                sharedRunnerCamera.ForceCameraPosition(
                    InitialSharedCameraPosition,
                    InitialSharedCameraRotation);
            }
        }

        private static void ConfigureCamera(
            CinemachineCamera camera,
            float fieldOfView)
        {
            if (camera == null)
            {
                return;
            }

            camera.Priority = 0;
            var lens = camera.Lens;
            lens.ModeOverride =
                LensSettings.OverrideModes.Perspective;
            lens.FieldOfView = fieldOfView;
            lens.NearClipPlane = 0.08f;
            lens.FarClipPlane = 100f;
            camera.Lens = lens;
        }

        private void ResolveLocalSlot(NetworkMatchState match)
        {
            var resolved = -1;
            for (var slot = 0;
                 slot < TagChaseRules.PlayerCount;
                 slot++)
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

        private void EnsurePlayers()
        {
            if (!Application.isPlaying || playerRoot == null)
            {
                return;
            }

            for (var slot = 0;
                 slot < _players.Length;
                 slot++)
            {
                if (_players[slot] == null)
                {
                    _players[slot] = CreatePlayer(slot);
                }
            }
        }

        private PlayerView CreatePlayer(int slot)
        {
            var playerObject =
                new GameObject("Tag Chase Player " + (slot + 1));
            playerObject.transform.SetParent(playerRoot, false);
            var visual =
                playerObject.AddComponent<PlayerAvatarVisual>();
            visual.EnsureBuilt();
            visual.SetBodyColor(FallbackPlayerColors[slot]);
            visual.SetDisplayName(GameText.F("PLAYER {0}", slot + 1));
            visual.SetOwnerFirstPerson(false);
            visual.SetTopViewHighlight(false);
            visual.SetEliminated(false);
            DisableGeneratedHitColliders(playerObject);
            GameObject aura = null;
            if (taggerAuraPrefab != null)
            {
                aura = Instantiate(
                    taggerAuraPrefab,
                    playerObject.transform,
                    false);
                aura.name = "Tagger Aura";
                aura.transform.localPosition =
                    Vector3.down * PlayerPresentationHeight;
                aura.SetActive(false);
            }
            return new PlayerView(
                playerObject.transform,
                visual,
                aura);
        }

        private void RefreshPlayers(NetworkMatchState match)
        {
            var localIsTagger =
                state.IsTagger(_localSlot);
            var showingStartCountdown =
                match.IsMinigameStartCountdown;
            for (var slot = 0;
                 slot < _players.Length;
                 slot++)
            {
                var player = _players[slot];
                if (player == null)
                {
                    continue;
                }

                var position =
                    state.GetPlayerPosition(slot);
                var target =
                    new Vector3(
                        position.x,
                        PlayerPresentationHeight,
                        position.y);
                if (!player.HasPosition ||
                    state.Phase ==
                    NetworkTagChasePhase.Countdown ||
                    Vector3.SqrMagnitude(
                        player.Root.position - target) > 64f)
                {
                    player.Root.position = target;
                    player.HasPosition = true;
                }
                else
                {
                    player.Root.position =
                        Vector3.Lerp(
                            player.Root.position,
                            target,
                            1f - Mathf.Exp(
                                -PlayerInterpolationSpeed *
                                Time.unscaledDeltaTime));
                }

                var facing =
                    state.GetPlayerFacing(slot);
                if (facing.sqrMagnitude > 0.0001f)
                {
                    player.Root.rotation =
                        Quaternion.LookRotation(
                            new Vector3(
                                facing.x,
                                0f,
                                facing.y),
                            Vector3.up);
                }

                var avatar =
                    match.GetAvatarForSlot(slot);
                if (avatar != null)
                {
                    var appearance = avatar.Appearance;
                    player.Visual.SetBodyColor(
                        appearance.BodyColor);
                    player.Visual.ApplyAppearance(
                        appearance.EyeId,
                        appearance.MouthId,
                        appearance.HatId,
                        appearance.ExpressionId);
                    player.Visual.SetDisplayName(
                        string.IsNullOrWhiteSpace(
                            avatar.DisplayName)
                            ? GameText.F("PLAYER {0}", slot + 1)
                            : avatar.DisplayName);
                }

                player.Visual.SetEliminated(
                    state.IsCaught(slot));
                player.Visual.SetOwnerFirstPerson(
                    localIsTagger &&
                    !showingStartCountdown &&
                    slot == _localSlot);
                var hideAuraFromFirstPersonOwner =
                    localIsTagger &&
                    !showingStartCountdown &&
                    slot == _localSlot;
                player.SetTaggerAura(
                    state.IsTagger(slot) &&
                    !hideAuraFromFirstPersonOwner,
                    PresentationAccessibility.FlashIntensityScale);
            }
        }

        private void RefreshCatchVfx()
        {
            var caughtMask = state.CaughtMask;
            if (!_caughtMaskBaselineInitialized)
            {
                _caughtMaskBaselineInitialized = true;
                _lastCaughtMask = caughtMask;
                return;
            }

            var newlyCaught = (byte)(caughtMask & ~_lastCaughtMask);
            _lastCaughtMask = caughtMask;
            for (var slot = 0; slot < _players.Length; slot++)
            {
                if ((newlyCaught & (1 << slot)) == 0)
                {
                    continue;
                }

                var player = _players[slot];
                player?.Visual.TriggerHit();
                if (player != null && hitSparkVfxPrefab != null)
                {
                    OneShotVfxPool.Play(
                        hitSparkVfxPrefab,
                        player.Root.position + Vector3.up * 0.7f,
                        Quaternion.identity,
                        0.75f);
                }
            }
        }

        private void RefreshCamera(NetworkMatchState match)
        {
            var localIsTagger =
                state.IsTagger(_localSlot);
            // The tagger must see their own player and countdown outline
            // before play starts. Switch back to the original owner-only
            // first-person camera as soon as the shared countdown ends.
            var showingStartCountdown =
                match.IsMinigameStartCountdown;
            var useTaggerCamera =
                localIsTagger && !showingStartCountdown;
            var desiredCamera =
                useTaggerCamera
                    ? taggerCamera
                    : sharedRunnerCamera;
            RegisterCamera(desiredCamera);

            if (useTaggerCamera)
            {
                RefreshTaggerCamera(match);
                Cursor.lockState =
                    CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                RefreshSharedRunnerCamera();
                Cursor.lockState =
                    CursorLockMode.Confined;
                Cursor.visible = true;
            }

            SetTaggerAimVisible(
                useTaggerCamera &&
                state.Phase == NetworkTagChasePhase.Running &&
                !state.IsPaused);

            // The common menu and the pause release button need a free pointer.
            LocalInputGate.ApplyPointerOverride();
        }

        private void RefreshTaggerCamera(
            NetworkMatchState match)
        {
            if (taggerCamera == null ||
                !TagChaseRules.IsValidPlayerSlot(_localSlot))
            {
                return;
            }

            var position =
                state.GetPlayerPosition(_localSlot);
            var avatar =
                match.GetAvatarForSlot(_localSlot);
            var facing =
                state.GetPlayerFacing(_localSlot);
            var fallbackYaw =
                Mathf.Atan2(facing.x, facing.y) *
                Mathf.Rad2Deg;
            var yaw =
                avatar != null
                    ? avatar.LocalLookYaw
                    : fallbackYaw;
            var pitch =
                avatar != null
                    ? Mathf.Clamp(
                        avatar.LocalLookPitch,
                        -75f,
                        75f)
                    : 0f;
            taggerCamera.ForceCameraPosition(
                new Vector3(
                    position.x,
                    FirstPersonEyeHeight,
                    position.y),
                Quaternion.Euler(pitch, yaw, 0f));
        }

        private void RefreshSharedRunnerCamera()
        {
            if (sharedRunnerCamera == null)
            {
                return;
            }
            sharedRunnerCamera.ForceCameraPosition(
                InitialSharedCameraPosition,
                InitialSharedCameraRotation);
        }

        private void SetTaggerAimVisible(bool visible)
        {
            _commonHud ??= FindAnyObjectByType<MinigameCommonHudView>(
                FindObjectsInactive.Include);
            _commonHud?.SetTaggerAimVisible(visible);
        }

        private void RegisterCamera(
            CinemachineCamera desired)
        {
            _cameraDirector ??=
                FindAnyObjectByType<GameplayCameraDirector>();
            if (_registeredCamera == desired)
            {
                return;
            }

            if (_cameraDirector != null &&
                _registeredCamera != null)
            {
                _cameraDirector.ClearMinigameCamera(
                    _registeredCamera);
            }

            _registeredCamera = desired;
            if (_cameraDirector != null &&
                _registeredCamera != null)
            {
                _cameraDirector.SetMinigameCamera(
                    _registeredCamera);
            }
        }

        private void UnregisterCamera()
        {
            if (_cameraDirector != null &&
                _registeredCamera != null)
            {
                _cameraDirector.ClearMinigameCamera(
                    _registeredCamera);
            }
            _registeredCamera = null;
        }

        private void HandleAttackPresentationRequested(int slot)
        {
            if (!_worldVisible || slot < 0 || slot >= _players.Length)
            {
                return;
            }

            _players[slot]?.Visual.TriggerPunch();
        }

        private void EnsureActionPresentationSubscription()
        {
            if (_subscribedActionState == state)
            {
                return;
            }

            UnsubscribeFromActionPresentation();
            _subscribedActionState = state;
            if (_subscribedActionState != null)
            {
                _subscribedActionState.AttackPresentationRequested +=
                    HandleAttackPresentationRequested;
            }
        }

        private void UnsubscribeFromActionPresentation()
        {
            if (_subscribedActionState == null)
            {
                return;
            }

            _subscribedActionState.AttackPresentationRequested -=
                HandleAttackPresentationRequested;
            _subscribedActionState = null;
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
            if (playerRoot != null)
            {
                playerRoot.gameObject.SetActive(active);
            }
            if (!active)
            {
                _caughtMaskBaselineInitialized = false;
                _lastCaughtMask = 0;
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

        private sealed class PlayerView
        {
            public PlayerView(
                Transform root,
                PlayerAvatarVisual visual,
                GameObject aura)
            {
                Root = root;
                Visual = visual;
                Aura = aura;
                if (aura != null)
                {
                    AuraParticles = aura.GetComponentsInChildren<
                        ParticleSystem>(true);
                    AuraEmissionRates = new float[AuraParticles.Length];
                    for (var index = 0;
                         index < AuraParticles.Length;
                         index++)
                    {
                        AuraEmissionRates[index] = AuraParticles[index]
                            .emission.rateOverTimeMultiplier;
                    }
                    AuraLights = aura.GetComponentsInChildren<Light>(true);
                    AuraLightIntensities = new float[AuraLights.Length];
                    for (var index = 0;
                         index < AuraLights.Length;
                         index++)
                    {
                        AuraLightIntensities[index] =
                            AuraLights[index].intensity;
                    }
                }
            }

            public Transform Root { get; }
            public PlayerAvatarVisual Visual { get; }
            public GameObject Aura { get; }
            private ParticleSystem[] AuraParticles { get; }
            private float[] AuraEmissionRates { get; }
            private Light[] AuraLights { get; }
            private float[] AuraLightIntensities { get; }
            public bool HasPosition { get; set; }

            public void SetTaggerAura(bool active, float intensityScale)
            {
                if (Aura == null)
                {
                    return;
                }
                if (Aura.activeSelf != active)
                {
                    Aura.SetActive(active);
                }
                if (!active)
                {
                    return;
                }

                for (var index = 0;
                     index < AuraParticles.Length;
                     index++)
                {
                    var emission = AuraParticles[index].emission;
                    emission.rateOverTimeMultiplier =
                        AuraEmissionRates[index] * intensityScale;
                }
                for (var index = 0; index < AuraLights.Length; index++)
                {
                    AuraLights[index].intensity =
                        AuraLightIntensities[index] * intensityScale;
                }
            }
        }
    }
}
