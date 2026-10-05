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
        public const float SpectatorBackOffset = 18f;
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

        public static Vector3 CalculateSpectatorCameraPosition(
            Vector3 center) =>
            center + Vector3.up * SpectatorHeight +
            Vector3.back * SpectatorBackOffset;

        public static Quaternion CalculateSpectatorCameraRotation(
            Vector3 center)
        {
            var position = CalculateSpectatorCameraPosition(center);
            return Quaternion.LookRotation(
                center + Vector3.up * SpectatorFocusHeight - position,
                Vector3.up);
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
                RefreshSpectatorCamera(match, showingCountdown);
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

        private void RefreshSpectatorCamera(
            NetworkMatchState match,
            bool showingCountdown)
        {
            if (spectatorCamera == null)
            {
                return;
            }

            // The pre-start overview deliberately includes the owner so the
            // shared two-second white outline remains visible. After a death,
            // follow the living group without leaking hidden health values.
            var center = new Vector3(ArenaCenterX, 0f, 0f);
            if (!showingCountdown)
            {
                var sum = Vector3.zero;
                var count = 0;
                for (var slot = 0; slot < 4; slot++)
                {
                    if (state.IsEliminated(slot))
                    {
                        continue;
                    }

                    var avatar = match.GetAvatarForSlot(slot);
                    if (avatar == null)
                    {
                        continue;
                    }

                    sum += avatar.transform.position;
                    count++;
                }

                if (count > 0)
                {
                    center = sum / count;
                    center.y = 0f;
                }
            }

            center.x = Mathf.Clamp(
                center.x,
                ArenaCenterX - 3f,
                ArenaCenterX + 3f);
            center.z = Mathf.Clamp(center.z, -3f, 3f);
            var cameraPosition = CalculateSpectatorCameraPosition(center);
            spectatorCamera.ForceCameraPosition(
                cameraPosition,
                CalculateSpectatorCameraRotation(center));
        }

        private void RefreshStaticSpectatorCamera()
        {
            if (spectatorCamera == null)
            {
                return;
            }

            var center = new Vector3(ArenaCenterX, 0f, 0f);
            var cameraPosition = CalculateSpectatorCameraPosition(center);
            spectatorCamera.ForceCameraPosition(
                cameraPosition,
                CalculateSpectatorCameraRotation(center));
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
