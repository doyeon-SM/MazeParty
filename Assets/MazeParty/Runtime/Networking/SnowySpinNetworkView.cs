using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.SnowySpin;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Shared-camera, world-only presentation of the server's rolling balls.
    /// Authored scene spheres are moved and recolored; no Canvas is generated.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnowySpinNetworkView : MonoBehaviour
    {
        public const float ArenaCenterX = 1500f;
        public const float SharedCameraOrthographicSize = 11f;
        public const float BallPresentationHeight = 0.65f;
        public const float PlayerPresentationHeight =
            BallPresentationHeight;

        private const float InterpolationSpeed = 20f;
        private const float FallDurationSeconds = 0.65f;
        private static readonly int BaseColorProperty =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty =
            Shader.PropertyToID("_Color");
        private static readonly Color[] FallbackColors =
        {
            new Color(0.18f, 0.58f, 1f),
            new Color(1f, 0.32f, 0.24f),
            new Color(0.25f, 0.86f, 0.48f),
            new Color(0.82f, 0.35f, 1f)
        };

        [SerializeField] private NetworkSnowySpinState state;
        [SerializeField] private CinemachineCamera sharedCamera;
        [SerializeField] private GameObject arenaPresentation;
        [SerializeField] private Transform[] playerBalls =
            new Transform[SnowySpinRules.PlayerCount];
        [SerializeField] private Renderer[] ballRenderers =
            new Renderer[SnowySpinRules.PlayerCount];

        private readonly float[] _fallStartedAt =
            new float[SnowySpinRules.PlayerCount];
        private readonly bool[] _wasEliminated =
            new bool[SnowySpinRules.PlayerCount];
        private GameplayCameraDirector _cameraDirector;
        private MaterialPropertyBlock _colorBlock;
        private bool _cameraRegistered;
        private bool _visibilityInitialized;
        private bool _worldVisible;
        private bool _hadVisibleFrame;
        private int _lastRoundNumber;

        public static Vector3 SharedCameraPosition =>
            new Vector3(ArenaCenterX, 23f, 0f);

        public static Quaternion SharedCameraRotation =>
            Quaternion.Euler(90f, 0f, 0f);

        public GameObject ArenaPresentation => arenaPresentation;
        public CinemachineCamera SharedCamera => sharedCamera;

        public Transform GetPlayerBall(int slot)
        {
            return SnowySpinRules.IsValidPlayerSlot(slot) &&
                playerBalls != null && slot < playerBalls.Length
                ? playerBalls[slot]
                : null;
        }

        public Transform GetBallTransform(int slot)
        {
            return GetPlayerBall(slot);
        }

        public Renderer GetBallRenderer(int slot)
        {
            return SnowySpinRules.IsValidPlayerSlot(slot) &&
                ballRenderers != null && slot < ballRenderers.Length
                ? ballRenderers[slot]
                : null;
        }

        public void Configure(
            NetworkSnowySpinState networkState,
            CinemachineCamera camera,
            GameObject arena,
            Transform[] balls,
            Renderer[] renderers)
        {
            state = networkState;
            sharedCamera = camera;
            arenaPresentation = arena;
            playerBalls = balls;
            ballRenderers = renderers;
            ConfigureCamera();
        }

        private void Awake()
        {
            state ??= GetComponent<NetworkSnowySpinState>();
            _colorBlock = new MaterialPropertyBlock();
            ConfigureCamera();
            SetWorldPresentationActive(false);
        }

        private void OnDisable()
        {
            SetWorldPresentationActive(false);
            UnregisterCamera();
        }

        private void Update()
        {
            state ??= GetComponent<NetworkSnowySpinState>();
            var match = NetworkMatchState.Instance;
            var selected = match != null && match.IsSnowySpinPhase;
            var shouldShowWorld = state != null && state.IsSpawned &&
                selected &&
                (match.FlowState == BoardFlowState.MinigamePlaying ||
                 match.FlowState == BoardFlowState.SkippedResult);
            if (!shouldShowWorld)
            {
                SetWorldPresentationActive(false);
                UnregisterCamera();
                return;
            }

            SetWorldPresentationActive(true);
            RefreshPlayers(match);
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

        private void RefreshPlayers(NetworkMatchState match)
        {
            if (arenaPresentation == null || playerBalls == null)
            {
                return;
            }

            if (_lastRoundNumber != state.RoundNumber)
            {
                _lastRoundNumber = state.RoundNumber;
                _hadVisibleFrame = false;
                for (var slot = 0; slot < _wasEliminated.Length;
                     slot++)
                {
                    _wasEliminated[slot] = false;
                    _fallStartedAt[slot] = 0f;
                }
            }

            for (var slot = 0; slot < SnowySpinRules.PlayerCount;
                 slot++)
            {
                if (slot >= playerBalls.Length ||
                    playerBalls[slot] == null)
                {
                    continue;
                }

                var ball = playerBalls[slot];
                var eliminated = state.IsEliminated(slot);
                if (eliminated && !_wasEliminated[slot])
                {
                    _fallStartedAt[slot] = _hadVisibleFrame
                        ? Time.unscaledTime
                        : Time.unscaledTime - FallDurationSeconds;
                }
                _wasEliminated[slot] = eliminated;

                var fallProgress = eliminated
                    ? Mathf.Clamp01((Time.unscaledTime -
                        _fallStartedAt[slot]) / FallDurationSeconds)
                    : 0f;
                var visible = !eliminated || fallProgress < 1f;
                if (ball.gameObject.activeSelf != visible)
                {
                    ball.gameObject.SetActive(visible);
                }
                if (!visible)
                {
                    continue;
                }

                var position = state.GetPlayerPosition(slot);
                var target = arenaPresentation.transform.TransformPoint(
                    new Vector3(
                        position.x,
                        BallPresentationHeight -
                            2.5f * fallProgress * fallProgress,
                        position.y));
                var previous = ball.position;
                ball.position = !_hadVisibleFrame ||
                    (previous - target).sqrMagnitude > 36f
                    ? target
                    : Vector3.Lerp(
                        previous,
                        target,
                        1f - Mathf.Exp(
                            -InterpolationSpeed *
                            Time.unscaledDeltaTime));

                var planarTravel = ball.position - previous;
                planarTravel.y = 0f;
                var distance = planarTravel.magnitude;
                if (_hadVisibleFrame && distance > 0.0001f)
                {
                    var axis = Vector3.Cross(
                        Vector3.up,
                        planarTravel / distance);
                    ball.rotation = Quaternion.AngleAxis(
                        distance / BallPresentationHeight *
                            Mathf.Rad2Deg,
                        axis) * ball.rotation;
                }
                ball.localScale = Vector3.one *
                    (BallPresentationHeight * 2f) *
                    Mathf.Lerp(1f, 0.15f, fallProgress);

                var avatar = match.GetAvatarForSlot(slot);
                var color = avatar != null
                    ? avatar.Appearance.BodyColor
                    : FallbackColors[slot];
                ApplyBallColor(slot, color);
            }
        }

        private void ApplyBallColor(int slot, Color color)
        {
            if (ballRenderers == null ||
                slot >= ballRenderers.Length ||
                ballRenderers[slot] == null)
            {
                return;
            }

            _colorBlock ??= new MaterialPropertyBlock();
            _colorBlock.Clear();
            _colorBlock.SetColor(BaseColorProperty, color);
            _colorBlock.SetColor(ColorProperty, color);
            ballRenderers[slot].SetPropertyBlock(_colorBlock);
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
    }
}
