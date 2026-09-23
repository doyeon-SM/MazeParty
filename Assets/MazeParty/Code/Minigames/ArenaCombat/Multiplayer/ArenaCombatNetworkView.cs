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
        public const float ArenaCenterX = 1620f;
        public const float ArenaHalfWidth = 9f;
        public const float ArenaHalfDepth = 9f;

        [SerializeField] private NetworkArenaCombatState state;
        [SerializeField] private CinemachineCamera firstPersonCamera;
        [SerializeField] private CinemachineCamera spectatorCamera;
        [SerializeField] private GameObject arenaPresentation;
        [SerializeField] private Transform[] spawnMarkers = new Transform[4];

        private GameplayCameraDirector _cameraDirector;
        private CinemachineCamera _registeredCamera;
        private NetworkPlayerAvatar _localAvatar;
        private bool _worldVisible;
        private bool _visibilityInitialized;

        public NetworkArenaCombatState State => state;
        public CinemachineCamera FirstPersonCamera => firstPersonCamera;
        public CinemachineCamera SpectatorCamera => spectatorCamera;
        public GameObject ArenaPresentation => arenaPresentation;

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

        private void Awake()
        {
            state ??= GetComponent<NetworkArenaCombatState>();
            ConfigureCameras();
        }

        private void OnDisable()
        {
            if (_localAvatar != null && _localAvatar.AvatarVisual != null)
                _localAvatar.AvatarVisual.SetOwnerFirstPerson(false);
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
            var cameraPosition = center + new Vector3(0f, 12f, -14f);
            spectatorCamera.ForceCameraPosition(
                cameraPosition,
                Quaternion.LookRotation(
                    center + Vector3.up * 0.8f - cameraPosition,
                    Vector3.up));
        }

        private void RefreshStaticSpectatorCamera()
        {
            if (spectatorCamera == null)
            {
                return;
            }

            var cameraPosition =
                new Vector3(ArenaCenterX, 12f, -14f);
            var focus = new Vector3(ArenaCenterX, 0.8f, 0f);
            spectatorCamera.ForceCameraPosition(
                cameraPosition,
                Quaternion.LookRotation(
                    focus - cameraPosition,
                    Vector3.up));
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
    }
}
