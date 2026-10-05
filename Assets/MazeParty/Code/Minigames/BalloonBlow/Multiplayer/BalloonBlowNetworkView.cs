using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.BalloonBlow;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Client-only presentation for the server-authoritative Balloon Blow
    /// state. Every client registers the same fixed Cinemachine view, while
    /// only the owning player's station receives the top-view highlight.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BalloonBlowNetworkView : MonoBehaviour
    {
        public const float ArenaCenterX = 580f;
        public const float PlayerPresentationHeight = 1.05f;
        public const float SharedCameraOrthographicSize = 7.8f;

        private const float BalloonMinimumScale = 0.22f;
        private const float BalloonMaximumScale = 1.7f;
        private const float BalloonScaleSpeed = 12f;

        private static readonly Color[] FallbackPlayerColors =
        {
            new Color(0.16f, 0.48f, 0.95f),
            new Color(0.92f, 0.2f, 0.16f),
            new Color(0.18f, 0.78f, 0.32f),
            new Color(0.7f, 0.26f, 0.9f)
        };

        [SerializeField] private NetworkBalloonBlowState state;
        [SerializeField] private CinemachineCamera sharedCamera;
        [SerializeField] private Transform playerRoot;
        [SerializeField] private Transform[] playerAnchors =
            new Transform[BalloonBlowRules.PlayerCount];
        [SerializeField] private Transform[] balloonAnchors =
            new Transform[BalloonBlowRules.PlayerCount];
        [SerializeField] private GameObject arenaPresentation;
        [SerializeField] private AudioSource cueAudioSource;
        [SerializeField] private GameObject popBurstVfxPrefab;

        private readonly PlayerView[] _players =
            new PlayerView[BalloonBlowRules.PlayerCount];
        private readonly BalloonView[] _balloons =
            new BalloonView[BalloonBlowRules.PlayerCount];
        private readonly bool[] _wasPopped =
            new bool[BalloonBlowRules.PlayerCount];

        private GameplayCameraDirector _cameraDirector;
        private bool _cameraConfigured;
        private bool _worldVisible;
        private bool _worldVisibilityInitialized;
        private bool _popBaselineInitialized;

        public static Vector3 SharedCameraPosition =>
            new Vector3(ArenaCenterX, 11.5f, -16f);

        public static Quaternion SharedCameraRotation =>
            Quaternion.Euler(36f, 0f, 0f);

        public GameObject PopBurstVfxPrefab => popBurstVfxPrefab;

        public void Configure(
            NetworkBalloonBlowState networkState,
            CinemachineCamera camera,
            Transform runtimePlayers,
            Transform[] fixedPlayerAnchors,
            Transform[] fixedBalloonAnchors,
            GameObject arena,
            AudioSource audioSource)
        {
            state = networkState;
            sharedCamera = camera;
            playerRoot = runtimePlayers;
            playerAnchors = fixedPlayerAnchors;
            balloonAnchors = fixedBalloonAnchors;
            arenaPresentation = arena;
            cueAudioSource = audioSource;
            _cameraConfigured = false;
            ConfigureCamera();
            CacheBalloonViews();
            if (Application.isPlaying)
            {
                EnsurePlayers();
            }
        }

        public void ConfigureVfx(GameObject popBurstPrefab)
        {
            popBurstVfxPrefab = popBurstPrefab;
        }

        private void Awake()
        {
            state ??= GetComponent<NetworkBalloonBlowState>();
            ConfigureCamera();
            CacheBalloonViews();
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
            state ??= GetComponent<NetworkBalloonBlowState>();
            EnsurePlayers();
            CacheBalloonViews();

            var match = NetworkMatchState.Instance;
            var selected = match != null && match.IsBalloonBlowPhase;
            if (selected)
            {
                RegisterCamera();
            }
            else
            {
                UnregisterCamera();
            }

            var shouldShowWorld =
                state != null && state.IsSpawned && selected &&
                (match.FlowState == BoardFlowState.MinigamePlaying ||
                 match.FlowState == BoardFlowState.MinigameResult);
            if (!shouldShowWorld)
            {
                SetWorldPresentationActive(false);
                return;
            }

            SetWorldPresentationActive(true);
            // MinigameLocalPlayerHighlight owns the brief shared countdown
            // marker; this view does not keep a persistent local-player mark.
            RefreshPlayers(match);
            RefreshBalloons();
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
            if (sharedCamera != null)
            {
                sharedCamera.Priority = 0;
            }
        }

        private void CacheBalloonViews()
        {
            if (balloonAnchors == null)
            {
                return;
            }

            for (var slot = 0;
                 slot < _balloons.Length && slot < balloonAnchors.Length;
                 slot++)
            {
                if (_balloons[slot] != null ||
                    balloonAnchors[slot] == null)
                {
                    continue;
                }

                var body = balloonAnchors[slot].Find("Balloon Body");
                var knot = balloonAnchors[slot].Find("Balloon Knot");
                if (body != null && knot != null)
                {
                    _balloons[slot] = new BalloonView(
                        balloonAnchors[slot],
                        body,
                        knot);
                }
            }
        }

        private void EnsurePlayers()
        {
            if (playerRoot == null || playerAnchors == null)
            {
                return;
            }

            for (var slot = 0;
                 slot < _players.Length && slot < playerAnchors.Length;
                 slot++)
            {
                if (_players[slot] != null || playerAnchors[slot] == null)
                {
                    continue;
                }

                var playerObject = new GameObject(
                    "Balloon Player " + (slot + 1));
                playerObject.transform.SetParent(playerRoot, false);
                playerObject.transform.SetPositionAndRotation(
                    playerAnchors[slot].position,
                    playerAnchors[slot].rotation);

                var visual = playerObject.AddComponent<PlayerAvatarVisual>();
                visual.EnsureBuilt();
                visual.SetOwnerFirstPerson(false);
                visual.SetBodyColor(FallbackPlayerColors[slot]);
                visual.SetDisplayName(GameText.F("Player {0}", slot + 1));
                visual.SetNameplateVisible(true);
                DisableGeneratedHitColliders(playerObject);

                _players[slot] = new PlayerView(
                    playerObject.transform,
                    visual);
            }
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

                if (playerAnchors != null &&
                    slot < playerAnchors.Length &&
                    playerAnchors[slot] != null)
                {
                    player.Root.SetPositionAndRotation(
                        playerAnchors[slot].position,
                        playerAnchors[slot].rotation);
                }

                var avatar = match.GetAvatarForSlot(slot);
                var displayName =
                    avatar != null &&
                    !string.IsNullOrWhiteSpace(avatar.DisplayName)
                        ? avatar.DisplayName
                        : GameText.F("Player {0}", slot + 1);
                player.Visual.SetDisplayName(
                    displayName);
                if (avatar != null)
                {
                    var appearance = avatar.Appearance;
                    player.Visual.SetBodyColor(appearance.BodyColor);
                    player.Visual.ApplyAppearance(
                        appearance.EyeId,
                        appearance.MouthId,
                        appearance.HatId,
                        appearance.ExpressionId);
                }
                player.Visual.SetEliminated(false);
            }
        }

        private void RefreshBalloons()
        {
            for (var slot = 0; slot < _balloons.Length; slot++)
            {
                var popped = state.IsPlayerPopped(slot);
                var isNewPop = _popBaselineInitialized &&
                               popped && !_wasPopped[slot];
                _wasPopped[slot] = popped;
                if (isNewPop)
                {
                    PlayPopVfx(slot);
                    cueAudioSource?.Play();
                }

                var balloon = _balloons[slot];
                if (balloon == null)
                {
                    continue;
                }

                var progress = Mathf.Clamp(
                    state.GetPlayerProgress(slot),
                    0f,
                    BalloonBlowRules.MaxProgressPercent);

                var normalized = progress /
                                 BalloonBlowRules.MaxProgressPercent;
                var targetScale = Mathf.Lerp(
                    BalloonMinimumScale,
                    BalloonMaximumScale,
                    normalized);
                var player = slot < _players.Length
                    ? _players[slot]
                    : null;
                var mouth = player?.Visual?.Bindings?.MouthAnchor;
                var blowing = !popped && state.IsPlayerInflating(slot);
                player?.Visual?.SetMouthBlowing(blowing);
                if (mouth != null)
                {
                    balloon.FollowMouth(
                        mouth.position,
                        player.Root.rotation,
                        targetScale);
                }
                balloon.SetScale(targetScale, popped);
            }

            _popBaselineInitialized = true;
        }

        private void PlayPopVfx(int slot)
        {
            if (popBurstVfxPrefab == null || balloonAnchors == null ||
                slot < 0 || slot >= balloonAnchors.Length ||
                balloonAnchors[slot] == null)
            {
                return;
            }

            OneShotVfxPool.Play(
                popBurstVfxPrefab,
                _balloons[slot] != null
                    ? _balloons[slot].BodyPosition
                    : balloonAnchors[slot].position,
                Quaternion.identity,
                1.05f);
        }

        private void SetWorldPresentationActive(bool active)
        {
            if (_worldVisibilityInitialized &&
                _worldVisible == active)
            {
                return;
            }

            _worldVisibilityInitialized = true;
            _worldVisible = active;
            if (!active)
            {
                _popBaselineInitialized = false;
            }
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

        private static Transform FindDescendant(
            Transform root,
            string childName)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == childName)
            {
                return root;
            }
            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindDescendant(root.GetChild(index), childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
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
        }

        private sealed class BalloonView
        {
            private readonly Transform _root;
            private readonly Transform _body;
            private readonly Transform _knot;

            public BalloonView(
                Transform root,
                Transform body,
                Transform knot)
            {
                _root = root;
                _body = body;
                _knot = knot;
            }

            public Vector3 BodyPosition => _body.position;

            public void FollowMouth(
                Vector3 mouthPosition,
                Quaternion playerRotation,
                float scale)
            {
                var forward = playerRotation * Vector3.forward;
                _root.SetPositionAndRotation(
                    mouthPosition +
                    forward * (0.11f + 0.47f * scale) +
                    Vector3.up * 0.06f,
                    playerRotation);
            }

            public void SetScale(float scale, bool popped)
            {
                var visible = !popped;
                _body.gameObject.SetActive(visible);
                _knot.gameObject.SetActive(visible);
                if (!visible)
                {
                    return;
                }

                var current = _root.localScale.x;
                var smoothed = Mathf.Lerp(
                    current,
                    scale,
                    1f - Mathf.Exp(
                        -BalloonScaleSpeed * Time.unscaledDeltaTime));
                _root.localScale = Vector3.one * smoothed;
            }
        }
    }
}
