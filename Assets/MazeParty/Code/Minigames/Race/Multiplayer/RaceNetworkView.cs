using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.Race;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Shared fixed-camera presentation for the four production race lanes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaceNetworkView : MonoBehaviour
    {
        public const float PlayerPresentationHeight = 1.18f;
        public const float SharedCameraOrthographicSize = 23f;

        private const float PlayerInterpolationSpeed = 20f;

        private static readonly Color32[] FallbackPlayerColors =
        {
            new Color32(45, 122, 242, 255),
            new Color32(235, 57, 48, 255),
            new Color32(46, 199, 82, 255),
            new Color32(177, 68, 232, 255)
        };

        [SerializeField] private NetworkRaceState state;
        [SerializeField] private CinemachineCamera sharedCamera;
        [SerializeField] private Transform playerRoot;
        [SerializeField] private GameObject arenaPresentation;

        private readonly PlayerView[] _players =
            new PlayerView[RaceRules.PlayerCount];
        private GameplayCameraDirector _cameraDirector;
        private bool _cameraRegistered;
        private int _localSlot = -1;
        private bool _visibilityInitialized;
        private bool _worldVisible;

        public static Vector3 SharedCameraPosition =>
            new Vector3(NetworkRaceState.TrackCenterX, 45f, 0f);

        public static Quaternion SharedCameraRotation =>
            Quaternion.Euler(90f, 0f, 0f);

        public void Configure(
            NetworkRaceState networkState,
            CinemachineCamera raceCamera,
            Transform players,
            GameObject arena)
        {
            state = networkState;
            sharedCamera = raceCamera;
            playerRoot = players;
            arenaPresentation = arena;
            ConfigureCamera();
            if (Application.isPlaying)
            {
                EnsurePlayers();
            }
        }

        private void Awake()
        {
            state ??= GetComponent<NetworkRaceState>();
            ConfigureCamera();
            EnsurePlayers();
            SetWorldPresentationActive(false);
        }

        private void OnDisable()
        {
            SetWorldPresentationActive(false);
            UnregisterCamera();
        }

        private void Update()
        {
            state ??= GetComponent<NetworkRaceState>();
            EnsurePlayers();
            var match = NetworkMatchState.Instance;
            var selected = match != null && match.IsRacePhase;
            var shouldShowWorld =
                state != null && state.IsSpawned && selected &&
                (match.FlowState == BoardFlowState.MinigamePlaying ||
                 match.FlowState == BoardFlowState.SkippedResult);
            if (!shouldShowWorld)
            {
                SetWorldPresentationActive(false);
                UnregisterCamera();
                return;
            }

            SetWorldPresentationActive(true);
            ResolveLocalSlot(match);
            RefreshPlayers(match);
            RegisterCamera();
        }

        private void ConfigureCamera()
        {
            if (sharedCamera == null)
            {
                return;
            }
            sharedCamera.Priority = 0;
            var lens = sharedCamera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize = SharedCameraOrthographicSize;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 100f;
            sharedCamera.Lens = lens;
            sharedCamera.ForceCameraPosition(
                SharedCameraPosition,
                SharedCameraRotation);
        }

        private void EnsurePlayers()
        {
            if (!Application.isPlaying || playerRoot == null)
            {
                return;
            }
            for (var slot = 0; slot < _players.Length; slot++)
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
                new GameObject("Race Player " + (slot + 1));
            playerObject.transform.SetParent(playerRoot, false);
            var visual = playerObject.AddComponent<PlayerAvatarVisual>();
            visual.EnsureBuilt();
            visual.SetBodyColor(FallbackPlayerColors[slot]);
            visual.SetDisplayName("PLAYER " + (slot + 1));
            visual.SetOwnerFirstPerson(false);
            visual.SetTopViewHighlight(false);
            visual.SetEliminated(false);
            DisableGeneratedHitColliders(playerObject);
            return new PlayerView(playerObject.transform, visual);
        }

        private void ResolveLocalSlot(NetworkMatchState match)
        {
            var resolved = -1;
            for (var slot = 0; slot < RaceRules.PlayerCount; slot++)
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

        private void RefreshPlayers(NetworkMatchState match)
        {
            for (var slot = 0; slot < _players.Length; slot++)
            {
                var player = _players[slot];
                if (player == null)
                {
                    continue;
                }
                var target = new Vector3(
                    NetworkRaceState.GetLaneX(slot),
                    PlayerPresentationHeight,
                    NetworkRaceState.ProgressToWorldZ(
                        state.GetProgress(slot)));
                if (!player.HasPosition ||
                    state.Phase == NetworkRacePhase.Countdown)
                {
                    player.Root.position = target;
                    player.HasPosition = true;
                }
                else
                {
                    player.Root.position = Vector3.Lerp(
                        player.Root.position,
                        target,
                        1f - Mathf.Exp(
                            -PlayerInterpolationSpeed *
                            Time.unscaledDeltaTime));
                }
                player.Root.rotation = Quaternion.identity;

                var avatar = match.GetAvatarForSlot(slot);
                if (avatar != null)
                {
                    var appearance = avatar.Appearance;
                    player.Visual.SetBodyColor(appearance.BodyColor);
                    player.Visual.ApplyAppearance(
                        appearance.EyeId,
                        appearance.MouthId,
                        appearance.HatId);
                    player.Visual.SetDisplayName(
                        string.IsNullOrWhiteSpace(avatar.DisplayName)
                            ? "PLAYER " + (slot + 1)
                            : avatar.DisplayName);
                }
                player.Visual.SetOwnerFirstPerson(false);
                player.Visual.SetEliminated(false);
                player.Visual.SetTopViewHighlight(slot == _localSlot);
            }
        }

        private void RegisterCamera()
        {
            if (_cameraRegistered || sharedCamera == null)
            {
                return;
            }
            _cameraDirector ??= FindAnyObjectByType<GameplayCameraDirector>();
            if (_cameraDirector != null)
            {
                _cameraDirector.SetMinigameCamera(sharedCamera);
                _cameraRegistered = true;
            }
        }

        private void UnregisterCamera()
        {
            if (!_cameraRegistered)
            {
                return;
            }
            if (_cameraDirector != null && sharedCamera != null)
            {
                _cameraDirector.ClearMinigameCamera(sharedCamera);
            }
            _cameraRegistered = false;
        }

        private void SetWorldPresentationActive(bool active)
        {
            if (_visibilityInitialized && _worldVisible == active)
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

        private static void DisableGeneratedHitColliders(GameObject root)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }
        }

        private sealed class PlayerView
        {
            public PlayerView(Transform root, PlayerAvatarVisual visual)
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
