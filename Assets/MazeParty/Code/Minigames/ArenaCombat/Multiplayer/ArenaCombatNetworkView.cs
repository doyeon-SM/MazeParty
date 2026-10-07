using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
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

        private GameplayCameraDirector _cameraDirector;
        private CinemachineCamera _registeredCamera;
        private NetworkPlayerAvatar _localAvatar;
        private bool _worldVisible;
        private bool _visibilityInitialized;
        private readonly PresentationEventRevisionGate[] _hitVfxGates =
            new PresentationEventRevisionGate[4];

        public NetworkArenaCombatState State => state;
        public CinemachineCamera FirstPersonCamera => firstPersonCamera;
        public CinemachineCamera SpectatorCamera => spectatorCamera;
        public GameObject ArenaPresentation => arenaPresentation;
        public GameObject HitSparkVfxPrefab => hitSparkVfxPrefab;

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

        private void Awake()
        {
            state ??= GetComponent<NetworkArenaCombatState>();
            ConfigureCameras();
        }

        private void OnDisable()
        {
            if (_localAvatar != null && _localAvatar.AvatarVisual != null)
                _localAvatar.AvatarVisual.SetOwnerFirstPerson(false);
            ResetHitVfxGates();
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
            var selected =
                match != null &&
                match.CurrentMinigame == ScheduledMinigameId.ArenaCombat;
            var showWorld = state != null && state.IsSpawned && selected;
            SetWorldPresentationActive(showWorld);
            if (!showWorld)
            {
                if (_localAvatar != null && _localAvatar.AvatarVisual != null)
                _localAvatar.AvatarVisual.SetOwnerFirstPerson(false);
                UnregisterCamera();
                return;
            }

            ResolveLocalAvatar(match);
            RefreshHitVfx(match);
            var localSlot = _localAvatar != null
                ? _localAvatar.AssignedSlot
                : -1;
            var isAlive = localSlot >= 0 && localSlot < 4 &&
                          !state.IsEliminated(localSlot);
            var showingCountdown = match.IsMinigameStartCountdown ||
                                   state.Phase == NetworkArenaCombatPhase.Countdown;
            var playing = match.FlowState ==
                          BoardFlowState.MinigamePlaying;
            var useFirstPerson = isAlive && playing && !showingCountdown;

            _localAvatar?.AvatarVisual?.SetOwnerFirstPerson(useFirstPerson);
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

        private void ConfigureCameras()
        {
            ConfigureCamera(firstPersonCamera, FirstPersonFieldOfView);
            ConfigureCamera(spectatorCamera, SpectatorFieldOfView);
            RefreshStaticSpectatorCamera();
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
            if (spectatorCamera == null)
            {
                return;
            }

            spectatorCamera.ForceCameraPosition(
                SharedSpectatorCameraPosition,
                SharedSpectatorCameraRotation);
        }

        private void RefreshHitVfx(NetworkMatchState match)
        {
            for (var slot = 0; slot < _hitVfxGates.Length; slot++)
            {
                if (!_hitVfxGates[slot].Observe(
                        (uint)state.GetHitSequence(slot)))
                {
                    continue;
                }

                var avatar = match != null
                    ? match.GetAvatarForSlot(slot)
                    : null;
                if (avatar != null && hitSparkVfxPrefab != null)
                {
                    OneShotVfxPool.Play(
                        hitSparkVfxPrefab,
                        avatar.transform.position + Vector3.up,
                        Quaternion.identity,
                        0.7f);
                }
            }
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
            if (!visible)
            {
                ResetHitVfxGates();
            }
        }

        private void ResetHitVfxGates()
        {
            for (var slot = 0; slot < _hitVfxGates.Length; slot++)
            {
                _hitVfxGates[slot].Reset();
            }
        }
    }
}
