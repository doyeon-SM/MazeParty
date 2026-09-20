using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.CliffBarrage;
using MazeParty.Gameplay.Minigames.RedLightGreenLight;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Scene-authored, shared-camera presentation of the server-owned cliff match.
    /// The five shells and two laser rigs are moved and toggled, never replaced.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CliffBarrageNetworkView : MonoBehaviour
    {
        public const float ArenaCenterX = 1740f;
        public const float ArenaHalfExtent = 8f;
        public const float SharedCameraOrthographicSize = 11.5f;
        public const float PlayerPresentationHeight = 1.18f;
        public const float ProjectilePresentationHeight = 0.85f;

        private const float InterpolationSpeed = 22f;
        private const float FallDurationSeconds = 0.6f;
        private const float LaserWarningWidth = 0.14f;
        private const float LaserFiringWidth = 0.65f;
        private static readonly Color[] FallbackPlayerColors =
        {
            new Color(0.16f, 0.48f, 0.95f),
            new Color(0.92f, 0.2f, 0.16f),
            new Color(0.18f, 0.78f, 0.32f),
            new Color(0.7f, 0.26f, 0.9f)
        };

        [SerializeField] private NetworkCliffBarrageState state;
        [SerializeField] private CinemachineCamera sharedCamera;
        [SerializeField] private GameObject arenaPresentation;
        [SerializeField] private Transform playerRoot;
        [SerializeField] private Transform[] projectiles =
            new Transform[NetworkCliffBarrageState.ProjectilePoolSize];
        [SerializeField] private GameObject[] laserRoots =
            new GameObject[NetworkCliffBarrageState.LaserPoolSize];
        [SerializeField] private Transform[] warningBeams =
            new Transform[NetworkCliffBarrageState.LaserPoolSize];
        [SerializeField] private Transform[] firingBeams =
            new Transform[NetworkCliffBarrageState.LaserPoolSize];

        private readonly PlayerView[] _players = new PlayerView[4];
        private readonly bool[] _wasEliminated = new bool[4];
        private readonly float[] _fallStartedAt = new float[4];
        private GameplayCameraDirector _cameraDirector;
        private bool _cameraRegistered;
        private bool _worldVisible;
        private bool _visibilityInitialized;
        private bool _hadVisibleFrame;
        private int _lastRoundNumber;
        private int _localSlot = -1;

        public static Vector3 SharedCameraPosition =>
            new Vector3(ArenaCenterX, 23f, 0f);

        public static Quaternion SharedCameraRotation =>
            Quaternion.Euler(90f, 0f, 0f);

        public GameObject ArenaPresentation => arenaPresentation;
        public CinemachineCamera SharedCamera => sharedCamera;
        public Transform PlayerRoot => playerRoot;
        public int AuthoredProjectileCount => projectiles?.Length ?? 0;
        public int AuthoredLaserCount => laserRoots?.Length ?? 0;

        public Transform GetPlayerTransform(int slot) =>
            slot >= 0 && slot < _players.Length
                ? _players[slot]?.Root
                : null;

        public Transform GetProjectileTransform(int index) =>
            projectiles != null && index >= 0 && index < projectiles.Length
                ? projectiles[index]
                : null;

        public GameObject GetLaserRoot(int index) =>
            laserRoots != null && index >= 0 && index < laserRoots.Length
                ? laserRoots[index]
                : null;

        public Transform GetWarningBeam(int index) =>
            warningBeams != null && index >= 0 &&
            index < warningBeams.Length
                ? warningBeams[index]
                : null;

        public Transform GetFiringBeam(int index) =>
            firingBeams != null && index >= 0 &&
            index < firingBeams.Length
                ? firingBeams[index]
                : null;

        public void Configure(
            NetworkCliffBarrageState networkState,
            CinemachineCamera camera,
            GameObject arena,
            Transform players,
            Transform[] pooledProjectiles,
            GameObject[] pooledLaserRoots,
            Transform[] telegraphs,
            Transform[] beams)
        {
            state = networkState;
            sharedCamera = camera;
            arenaPresentation = arena;
            playerRoot = players;
            projectiles = pooledProjectiles;
            laserRoots = pooledLaserRoots;
            warningBeams = telegraphs;
            firingBeams = beams;
            ConfigureCamera();
            if (Application.isPlaying)
            {
                EnsurePlayers();
            }
        }

        private void Awake()
        {
            state ??= GetComponent<NetworkCliffBarrageState>();
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
            state ??= GetComponent<NetworkCliffBarrageState>();
            EnsurePlayers();
            var match = NetworkMatchState.Instance;
            var shouldShowWorld = state != null && state.IsSpawned &&
                match != null && match.IsCliffBarragePhase &&
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
            RefreshProjectiles();
            RefreshLasers();
            RegisterCamera();
            _hadVisibleFrame = true;
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
            lens.FarClipPlane = 100f;
            sharedCamera.Lens = lens;
            sharedCamera.ForceCameraPosition(
                SharedCameraPosition,
                SharedCameraRotation);
            sharedCamera.Priority = 0;
        }

        private void EnsurePlayers()
        {
            if (!Application.isPlaying || playerRoot == null)
            {
                return;
            }

            for (var slot = 0; slot < _players.Length; slot++)
            {
                if (_players[slot] != null)
                {
                    continue;
                }

                var playerObject = new GameObject(
                    "Cliff Barrage Player " + (slot + 1));
                playerObject.transform.SetParent(playerRoot, false);
                var visual = playerObject.AddComponent<PlayerAvatarVisual>();
                visual.EnsureBuilt();
                visual.SetBodyColor(FallbackPlayerColors[slot]);
                visual.SetDisplayName("PLAYER " + (slot + 1));
                visual.SetOwnerFirstPerson(false);
                visual.SetTopViewHighlight(false);
                visual.SetEliminated(false);
                var torsoPose = playerObject.AddComponent<
                    RedLightGreenLightPlayerPresentation>();
                DisableGeneratedHitColliders(playerObject);
                _players[slot] = new PlayerView(
                    playerObject.transform, visual, torsoPose);
            }
        }

        private void ResolveLocalSlot(NetworkMatchState match)
        {
            _localSlot = -1;
            for (var slot = 0; slot < _players.Length; slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                if (avatar != null && avatar.IsOwner)
                {
                    _localSlot = slot;
                    return;
                }
            }
        }

        private void RefreshPlayers(NetworkMatchState match)
        {
            if (arenaPresentation == null)
            {
                return;
            }

            if (_lastRoundNumber != state.RoundNumber)
            {
                _lastRoundNumber = state.RoundNumber;
                _hadVisibleFrame = false;
                for (var slot = 0; slot < _players.Length; slot++)
                {
                    _wasEliminated[slot] = false;
                    _fallStartedAt[slot] = 0f;
                }
            }

            var arena = arenaPresentation.transform;
            for (var slot = 0; slot < _players.Length; slot++)
            {
                var player = _players[slot];
                if (player == null)
                {
                    continue;
                }

                var eliminated = state.IsEliminated(slot);
                if (eliminated && !_wasEliminated[slot])
                {
                    _fallStartedAt[slot] = _hadVisibleFrame
                        ? Time.unscaledTime
                        : Time.unscaledTime - FallDurationSeconds;
                }
                _wasEliminated[slot] = eliminated;
                var fall = eliminated
                    ? Mathf.Clamp01((Time.unscaledTime -
                        _fallStartedAt[slot]) / FallDurationSeconds)
                    : 0f;
                var visible = !eliminated || fall < 1f;
                if (player.Root.gameObject.activeSelf != visible)
                {
                    player.Root.gameObject.SetActive(visible);
                }
                if (!visible)
                {
                    continue;
                }

                var position = state.GetPlayerPosition(slot);
                var target = arena.TransformPoint(new Vector3(
                    position.x,
                    PlayerPresentationHeight - 5f * fall * fall,
                    position.y));
                player.Root.position = !_hadVisibleFrame ||
                    (player.Root.position - target).sqrMagnitude > 36f
                    ? target
                    : Vector3.Lerp(
                        player.Root.position,
                        target,
                        1f - Mathf.Exp(-InterpolationSpeed *
                            Time.unscaledDeltaTime));
                var facing = state.GetPlayerFacing(slot);
                if (facing.sqrMagnitude > 0.01f)
                {
                    player.Root.rotation = Quaternion.LookRotation(
                        new Vector3(facing.x, 0f, facing.y),
                        Vector3.up);
                }

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
                player.Visual.SetTopViewHighlight(slot == _localSlot);
                player.TorsoPose.ApplyState(
                    state.GetHitCount(slot), eliminated);
                player.Root.localScale = Vector3.one *
                    Mathf.Lerp(1f, 0.35f, fall);
            }
        }

        private void RefreshProjectiles()
        {
            if (projectiles == null || arenaPresentation == null)
            {
                return;
            }

            for (var index = 0; index < projectiles.Length; index++)
            {
                var shell = projectiles[index];
                if (shell == null)
                {
                    continue;
                }
                var snapshot = state.GetProjectile(index);
                if (shell.gameObject.activeSelf != snapshot.Active)
                {
                    shell.gameObject.SetActive(snapshot.Active);
                }
                if (!snapshot.Active)
                {
                    continue;
                }
                var position = snapshot.Position;
                shell.localPosition = new Vector3(
                    position.x, ProjectilePresentationHeight,
                    position.y);
                shell.Rotate(Vector3.up,
                    240f * Time.unscaledDeltaTime,
                    Space.Self);
            }
        }

        private void RefreshLasers()
        {
            if (laserRoots == null || warningBeams == null ||
                firingBeams == null)
            {
                return;
            }

            for (var index = 0; index < laserRoots.Length; index++)
            {
                var root = laserRoots[index];
                if (root == null || index >= warningBeams.Length ||
                    index >= firingBeams.Length)
                {
                    continue;
                }
                var snapshot = state.GetLaser(index);
                var warning = snapshot.Phase ==
                    CliffBarrageLaserPhase.Warning;
                var firing = snapshot.Phase ==
                    CliffBarrageLaserPhase.Firing;
                if (root.activeSelf != (warning || firing))
                {
                    root.SetActive(warning || firing);
                }
                if (!warning && !firing)
                {
                    continue;
                }

                var delta = snapshot.End - snapshot.Start;
                var length = Mathf.Max(0.01f, delta.magnitude);
                var direction = new Vector3(delta.x, 0f, delta.y);
                root.transform.localPosition = new Vector3(
                    (snapshot.Start.x + snapshot.End.x) * 0.5f,
                    0f,
                    (snapshot.Start.y + snapshot.End.y) * 0.5f);
                root.transform.localRotation =
                    direction.sqrMagnitude > 0.0001f
                        ? Quaternion.LookRotation(direction, Vector3.up)
                        : Quaternion.identity;
                SetBeam(warningBeams[index], warning,
                    LaserWarningWidth, 0.08f, length);
                SetBeam(firingBeams[index], firing,
                    LaserFiringWidth, 1.5f, length);
            }
        }

        private static void SetBeam(
            Transform beam, bool active, float width,
            float height, float length)
        {
            if (beam == null)
            {
                return;
            }
            if (beam.gameObject.activeSelf != active)
            {
                beam.gameObject.SetActive(active);
            }
            beam.localPosition = new Vector3(0f, height * 0.5f, 0f);
            beam.localRotation = Quaternion.identity;
            beam.localScale = new Vector3(width, height, length);
        }

        private void RegisterCamera()
        {
            if (_cameraRegistered || sharedCamera == null)
            {
                return;
            }
            _cameraDirector ??=
                FindAnyObjectByType<GameplayCameraDirector>();
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
            if (sharedCamera != null)
            {
                sharedCamera.Priority = 0;
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
            if (!active)
            {
                _hadVisibleFrame = false;
                _lastRoundNumber = 0;
            }
            if (arenaPresentation != null)
            {
                arenaPresentation.SetActive(active);
            }
        }

        private static void DisableGeneratedHitColliders(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
        }

        private sealed class PlayerView
        {
            public PlayerView(
                Transform root,
                PlayerAvatarVisual visual,
                RedLightGreenLightPlayerPresentation torsoPose)
            {
                Root = root;
                Visual = visual;
                TorsoPose = torsoPose;
            }

            public Transform Root { get; }
            public PlayerAvatarVisual Visual { get; }
            public RedLightGreenLightPlayerPresentation TorsoPose { get; }
        }
    }
}
