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
        [SerializeField] private TagChaseHudBindings hud;

        private readonly PlayerView[] _players =
            new PlayerView[TagChaseRules.PlayerCount];

        private GameplayCameraDirector _cameraDirector;
        private CinemachineCamera _registeredCamera;
        private int _localSlot = -1;
        private bool _worldVisible;
        private bool _visibilityInitialized;

        public static Vector3 InitialSharedCameraPosition =>
            new Vector3(
                NetworkTagChaseState.ArenaCenterX,
                11f,
                -13f);

        public static Quaternion InitialSharedCameraRotation =>
            Quaternion.LookRotation(
                new Vector3(0f, 0.8f, 0f) -
                new Vector3(0f, 11f, -13f),
                Vector3.up);

        public void Configure(
            NetworkTagChaseState networkState,
            CinemachineCamera runnerCamera,
            CinemachineCamera firstPersonCamera,
            Transform players,
            GameObject arena,
            TagChaseHudBindings hudBindings)
        {
            state = networkState;
            sharedRunnerCamera = runnerCamera;
            taggerCamera = firstPersonCamera;
            playerRoot = players;
            arenaPresentation = arena;
            hud = hudBindings;
            ConfigureCameras();
            if (Application.isPlaying)
            {
                EnsurePlayers();
            }
        }

        private void Awake()
        {
            state ??= GetComponent<NetworkTagChaseState>();
            ConfigureCameras();
            EnsurePlayers();
            SetWorldPresentationActive(false);
            SetHudActive(false);
        }

        private void OnDisable()
        {
            SetWorldPresentationActive(false);
            SetHudActive(false);
            UnregisterCamera();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            state ??= GetComponent<NetworkTagChaseState>();
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
                    BoardFlowState.SkippedResult);
            var shouldShowHud =
                shouldShowWorld &&
                match.FlowState ==
                BoardFlowState.MinigamePlaying;
            SetHudActive(shouldShowHud);
            if (!shouldShowWorld)
            {
                SetWorldPresentationActive(false);
                UnregisterCamera();
                return;
            }

            SetWorldPresentationActive(true);
            ResolveLocalSlot(match);
            RefreshPlayers(match);
            RefreshCamera(match);
            RefreshHud(match);
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
            visual.SetDisplayName("PLAYER " + (slot + 1));
            visual.SetOwnerFirstPerson(false);
            visual.SetTopViewHighlight(false);
            visual.SetEliminated(false);
            DisableGeneratedHitColliders(playerObject);
            return new PlayerView(
                playerObject.transform,
                visual);
        }

        private void RefreshPlayers(NetworkMatchState match)
        {
            var localIsTagger =
                state.IsTagger(_localSlot);
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
                        appearance.HatId);
                    player.Visual.SetDisplayName(
                        string.IsNullOrWhiteSpace(
                            avatar.DisplayName)
                            ? "PLAYER " + (slot + 1)
                            : avatar.DisplayName);
                }

                player.Visual.SetEliminated(
                    state.IsCaught(slot));
                player.Visual.SetOwnerFirstPerson(
                    localIsTagger &&
                    slot == _localSlot);
                player.Visual.SetTopViewHighlight(
                    !localIsTagger &&
                    slot == _localSlot &&
                    !state.IsCaught(slot));
            }
        }

        private void RefreshCamera(NetworkMatchState match)
        {
            var localIsTagger =
                state.IsTagger(_localSlot);
            var desiredCamera =
                localIsTagger
                    ? taggerCamera
                    : sharedRunnerCamera;
            RegisterCamera(desiredCamera);

            if (localIsTagger)
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

            var count = 0;
            var center = Vector3.zero;
            for (var slot = 0;
                 slot < TagChaseRules.PlayerCount;
                 slot++)
            {
                if (state.IsTagger(slot) ||
                    state.IsCaught(slot))
                {
                    continue;
                }

                var position =
                    state.GetPlayerPosition(slot);
                center +=
                    new Vector3(position.x, 0f, position.y);
                count++;
            }

            if (count == 0)
            {
                center = new Vector3(
                    NetworkTagChaseState.ArenaCenterX,
                    0f,
                    0f);
            }
            else
            {
                center /= count;
            }

            var radius = 0f;
            for (var slot = 0;
                 slot < TagChaseRules.PlayerCount;
                 slot++)
            {
                if (state.IsTagger(slot) ||
                    state.IsCaught(slot))
                {
                    continue;
                }

                var position =
                    state.GetPlayerPosition(slot);
                radius = Mathf.Max(
                    radius,
                    Vector2.Distance(
                        new Vector2(center.x, center.z),
                        position));
            }

            var height = 6.5f + radius * 0.45f;
            var back = 8f + radius * 0.85f;
            var cameraPosition =
                center + new Vector3(0f, height, -back);
            var focus =
                center + Vector3.up * 0.8f;
            sharedRunnerCamera.ForceCameraPosition(
                cameraPosition,
                Quaternion.LookRotation(
                    focus - cameraPosition,
                    Vector3.up));
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

        private void RefreshHud(NetworkMatchState match)
        {
            if (hud == null ||
                !hud.HasRequiredReferences)
            {
                return;
            }

            var remaining = state.Remaining;
            var duration = GetPhaseDuration(state.Phase);
            if (match.IsReconnectPaused)
            {
                remaining = match.ReconnectRemaining;
                duration =
                    NetworkMatchState.ReconnectGraceSeconds;
            }

            hud.TimerDial.SetTime(
                remaining,
                duration);
        }

        private static double GetPhaseDuration(
            NetworkTagChasePhase phase)
        {
            switch (phase)
            {
                case NetworkTagChasePhase.Countdown:
                    return NetworkTagChaseState.CountdownSeconds;
                case NetworkTagChasePhase.Running:
                    return TagChaseRules.RoundSeconds;
                case NetworkTagChasePhase.RoundResult:
                    return NetworkTagChaseState.RoundResultSeconds;
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
            if (playerRoot != null)
            {
                playerRoot.gameObject.SetActive(active);
            }
        }

        private void SetHudActive(bool active)
        {
            if (hud != null &&
                hud.RootCanvas != null &&
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

        private sealed class PlayerView
        {
            public PlayerView(
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
