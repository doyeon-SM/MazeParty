using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Consumes the arena's claim exactly once when presentation ownership is
    /// handed back to the board camera flow.
    /// </summary>
    internal sealed class ArenaCombatPresentationOwnership
    {
        private bool _claimed;

        public void Claim()
        {
            _claimed = true;
        }

        public bool TryRelease()
        {
            if (!_claimed)
            {
                return false;
            }

            _claimed = false;
            return true;
        }
    }

    /// <summary>
    /// Presents the existing network avatars in the arena. The owner gets an
    /// eye-level camera while alive; elimination hands the camera to an arena
    /// spectator view without spawning a duplicate player representation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArenaCombatNetworkView : MonoBehaviour
    {
        public const float FirstPersonFieldOfView = 70f;
        public const float SpectatorFieldOfView = 58f;
        public const float SpectatorHeight = 12f;
        public const float SpectatorFocusHeight = 0.8f;
        public const float ArenaCenterX = 1620f;
        public const float ArenaHalfWidth = 9f;
        public const float ArenaHalfDepth = 9f;

        [SerializeField] private NetworkArenaCombatState state;
        [SerializeField] private CinemachineCamera firstPersonCamera;
        [SerializeField] private CinemachineCamera spectatorCamera;
        [SerializeField] private GameObject arenaPresentation;
        [SerializeField] private Transform[] spawnMarkers = new Transform[4];
        [SerializeField] private GameObject hitSparkVfxPrefab;
        [SerializeField] private ArenaCombatAimView aimView;
        [SerializeField] private ArenaCombatHealthBarView healthBarPrefab;

        private GameplayCameraDirector _cameraDirector;
        private CinemachineCamera _registeredCamera;
        private NetworkPlayerAvatar _localAvatar;
        private readonly ArenaCombatPresentationOwnership
            _ownerPresentationOwnership =
                new ArenaCombatPresentationOwnership();
        private PlayerAvatarVisual _ownedOwnerPresentation;
        private bool _worldVisible;
        private bool _visibilityInitialized;
        private readonly ArenaCombatHealthBarView[] _healthBars =
            new ArenaCombatHealthBarView[4];

        public NetworkArenaCombatState State => state;
        public CinemachineCamera FirstPersonCamera => firstPersonCamera;
        public CinemachineCamera SpectatorCamera => spectatorCamera;
        public GameObject ArenaPresentation => arenaPresentation;
        public GameObject HitSparkVfxPrefab => hitSparkVfxPrefab;
        public ArenaCombatAimView AimView => aimView;
        public ArenaCombatHealthBarView HealthBarPrefab => healthBarPrefab;

        public static Vector3 SharedSpectatorCenter =>
            new Vector3(ArenaCenterX, 0f, 0f);

        public static Vector3 SharedSpectatorCameraPosition =>
            CalculateSpectatorCameraPosition(SharedSpectatorCenter);

        public static Quaternion SharedSpectatorCameraRotation =>
            CalculateSpectatorCameraRotation(SharedSpectatorCenter);

        public static Vector3 CalculateSpectatorCameraPosition(
            Vector3 center)
        {
            var focus = center + Vector3.up * SpectatorFocusHeight;
            return SharedCameraFraming.CalculatePosition(
                focus,
                SpectatorHeight - SpectatorFocusHeight);
        }

        public static Quaternion CalculateSpectatorCameraRotation(
            Vector3 center)
        {
            return SharedCameraFraming.Rotation;
        }

        public Transform GetSpawnMarker(int slot) =>
            slot >= 0 && slot < spawnMarkers.Length
                ? spawnMarkers[slot]
                : null;

        public void Configure(
            NetworkArenaCombatState networkState,
            CinemachineCamera ownerFirstPersonCamera,
            CinemachineCamera arenaSpectatorCamera,
            GameObject arena,
            Transform[] spawns)
        {
            state = networkState;
            firstPersonCamera = ownerFirstPersonCamera;
            spectatorCamera = arenaSpectatorCamera;
            arenaPresentation = arena;
            spawnMarkers = spawns;
            ConfigureCameras();
        }

        public void ConfigureVfx(GameObject hitSparkPrefab)
        {
            hitSparkVfxPrefab = hitSparkPrefab;
        }

        public void ConfigureAim(ArenaCombatAimView arenaAimView)
        {
            aimView = arenaAimView;
            SetAimVisible(false);
        }

        public void ConfigureHealthBar(ArenaCombatHealthBarView prefab)
        {
            healthBarPrefab = prefab;
        }


        private void Awake()
        {
            state ??= GetComponent<NetworkArenaCombatState>();
            ConfigureCameras();
            SetAimVisible(false);
        }

        private void OnDisable()
        {
            SetHealthBarsVisible(false);
            SetAimVisible(false);
            ReleaseOwnerPresentation();
            _worldVisible = false;
            _visibilityInitialized = false;
            UnregisterCamera();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            state ??= GetComponent<NetworkArenaCombatState>();
            var match = NetworkMatchState.Instance;
            var showWorld = state != null && state.IsSpawned &&
                match != null && match.IsArenaCombatPhase &&
                match.FlowState != BoardFlowState.MinigameLoading;
            SetWorldPresentationActive(showWorld);
            if (showWorld) ResolveLocalAvatar(match);
            // The selected minigame ID survives into the ceremony/next turn.
            // HP belongs only to the countdown and the active fight, not its result hold.
            var showHealthBars = showWorld && match.IsArenaCombatPlaying &&
                state.IsMatchActive &&
                (state.Phase == NetworkArenaCombatPhase.Countdown ||
                 state.Phase == NetworkArenaCombatPhase.Playing);
            RefreshHealthBars(match, showHealthBars);
            if (!showWorld)
            {
                SetAimVisible(false);
                // Additively loaded minigame scenes remain enabled between
                // turns. Release once instead of overwriting the board's local
                // first-person visibility on every dormant frame.
                ReleaseOwnerPresentation();
                UnregisterCamera();
                return;
            }

            var localSlot = _localAvatar != null
                ? _localAvatar.AssignedSlot
                : -1;
            var isAlive = localSlot >= 0 && localSlot < 4 &&
                          !state.IsEliminated(localSlot);

            var playing = match.FlowState ==
                          BoardFlowState.MinigamePlaying;
            var useFirstPerson = isAlive && playing;
            var showAim = useFirstPerson &&
                          state.Phase == NetworkArenaCombatPhase.Playing &&
                          !state.IsPaused;
            SetAimVisible(showAim);

            ClaimOwnerPresentation(useFirstPerson);
            RegisterCamera(useFirstPerson
                ? firstPersonCamera
                : spectatorCamera);
            if (useFirstPerson)
            {
                RefreshFirstPersonCamera();
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                RefreshStaticSpectatorCamera();
                Cursor.lockState = CursorLockMode.Confined;
                Cursor.visible = true;
            }

            // The common menu and the pause release button need a free pointer.
            LocalInputGate.ApplyPointerOverride();
        }

        private void RefreshHealthBars(NetworkMatchState match,
            bool showWorld)
        {
            for (var slot = 0; slot < _healthBars.Length; slot++)
            {
                var avatar = showWorld && match != null
                    ? match.GetAvatarForSlot(slot)
                    : null;
                var visual = avatar != null ? avatar.AvatarVisual : null;
                var bar = _healthBars[slot];
                if (visual != null && bar == null && healthBarPrefab != null)
                {
                    // A nameplate can be hidden or animated independently of combat HP.
                    bar = Instantiate(healthBarPrefab, transform, false);
                    _healthBars[slot] = bar;
                }
                if (bar == null)
                {
                    continue;
                }
                bar.SetTarget(visual, _cameraDirector != null ? _cameraDirector.OutputCamera : null);
                var visible = showWorld && visual != null && avatar != _localAvatar &&
                              !state.IsEliminated(slot);
                if (bar.gameObject.activeSelf != visible)
                {
                    bar.gameObject.SetActive(visible);
                }
                if (visible)
                {
                    bar.SetHealth(avatar.CombatHealth,
                        BoardCombatRules.TemporaryHealth);
                }
            }
        }

        private void SetHealthBarsVisible(bool visible)
        {
            foreach (var bar in _healthBars)
            {
                if (bar != null)
                {
                    bar.gameObject.SetActive(visible);
                }
            }
        }

        private void LateUpdate()
        {
            if (_registeredCamera == firstPersonCamera)
            {
                RefreshFirstPersonCamera();
            }
        }

        private void ResolveLocalAvatar(NetworkMatchState match)
        {
            if (_localAvatar != null && _localAvatar.IsSpawned &&
                _localAvatar.IsOwner)
            {
                return;
            }

            _localAvatar = null;
            for (var slot = 0; slot < 4; slot++)
            {
                var candidate = match.GetAvatarForSlot(slot);
                if (candidate != null && candidate.IsOwner)
                {
                    _localAvatar = candidate;
                    return;
                }
            }
        }

        private void ClaimOwnerPresentation(bool firstPerson)
        {
            var visual = _localAvatar != null
                ? _localAvatar.AvatarVisual
                : null;
            if (visual == null)
            {
                ReleaseOwnerPresentation();
                return;
            }

            if (_ownedOwnerPresentation != visual)
            {
                ReleaseOwnerPresentation();
                _ownedOwnerPresentation = visual;
            }

            _ownerPresentationOwnership.Claim();
            visual.SetOwnerFirstPerson(firstPerson);
        }

        private void ReleaseOwnerPresentation()
        {
            var visual = _ownedOwnerPresentation;
            _ownedOwnerPresentation = null;
            if (!_ownerPresentationOwnership.TryRelease())
            {
                return;
            }

            if (visual != null)
            {
                visual.SetOwnerFirstPerson(false);
            }
        }

        private void ConfigureCameras()
        {
            ConfigureCamera(firstPersonCamera, FirstPersonFieldOfView);
            ConfigureCamera(spectatorCamera, SpectatorFieldOfView);
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
            lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            lens.FieldOfView = fieldOfView;
            lens.NearClipPlane = 0.08f;
            lens.FarClipPlane = 120f;
            camera.Lens = lens;
        }

        private void RefreshFirstPersonCamera()
        {
            var eye = _localAvatar != null ? _localAvatar.EyePivot : null;
            if (firstPersonCamera == null || eye == null)
            {
                return;
            }

            firstPersonCamera.ForceCameraPosition(
                eye.position,
                eye.rotation);
        }

        private void RefreshStaticSpectatorCamera()
        {
            // The scene-authored spectator camera is fixed. Do not overwrite
            // manually tuned framing when ownership switches.
        }

        public void PresentHitSpark(Vector3 point, Vector3 normal)
        {
            if (hitSparkVfxPrefab == null) return;
            var outward = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
            OneShotVfxPool.Play(hitSparkVfxPrefab, point + outward * 0.025f,
                Quaternion.LookRotation(outward), 0.7f);
        }

        private void RegisterCamera(CinemachineCamera desired)
        {
            _cameraDirector ??=
                FindAnyObjectByType<GameplayCameraDirector>();
            if (_registeredCamera == desired)
            {
                return;
            }

            UnregisterCamera();
            _registeredCamera = desired;
            if (_cameraDirector != null && desired != null)
            {
                _cameraDirector.SetMinigameCamera(desired);
            }
        }

        private void UnregisterCamera()
        {
            if (_cameraDirector != null && _registeredCamera != null)
            {
                _cameraDirector.ClearMinigameCamera(_registeredCamera);
            }
            _registeredCamera = null;
        }

        private void SetWorldPresentationActive(bool visible)
        {
            if (_visibilityInitialized && _worldVisible == visible)
            {
                return;
            }

            _worldVisible = visible;
            _visibilityInitialized = true;
            if (arenaPresentation != null)
            {
                arenaPresentation.SetActive(visible);
            }
        }

        private void SetAimVisible(bool visible)
        {
            if (aimView != null)
            {
                aimView.SetVisible(visible);
            }
        }
    }
}
