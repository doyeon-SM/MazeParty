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
        public const float BallPresentationScale = 1.3f;
        public const float MaximumBallScaleMultiplier = 1.35f;
        public const float PlayerPresentationHeight =
            BallPresentationHeight;

        private const float InterpolationSpeed = 20f;
        private const float SpeedSmoothing = 10f;
        private const float FallDurationSeconds = 0.65f;
        private static readonly int BaseColorProperty =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty =
            Shader.PropertyToID("_Color");
        private static readonly int EmissionColorProperty =
            Shader.PropertyToID("_EmissionColor");
        private static readonly int GlowColorProperty =
            Shader.PropertyToID("_GlowColor");
        private static readonly int DepthGlowColorProperty =
            Shader.PropertyToID("_DepthGlowColor");
        private static readonly int ColorRampBlendProperty =
            Shader.PropertyToID("_ColorRampBlend");


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
        [SerializeField] private GameObject fallImpactVfxPrefab;
        [SerializeField] private GameObject collisionExplosionVfxPrefab;


        private readonly float[] _fallStartedAt =
            new float[SnowySpinRules.PlayerCount];
        private readonly bool[] _wasEliminated =
            new bool[SnowySpinRules.PlayerCount];
        private readonly float[] _smoothedSpeeds =
            new float[SnowySpinRules.PlayerCount];
        private readonly float[] _ballScaleMultipliers =
            new float[SnowySpinRules.PlayerCount];
        private NetworkSnowySpinState _subscribedCollisionState;
        private GameplayCameraDirector _cameraDirector;
        private MaterialPropertyBlock _colorBlock;
        private bool _cameraRegistered;
        private bool _visibilityInitialized;
        private bool _worldVisible;
        private bool _hadVisibleFrame;
        private int _lastRoundNumber;

        public static Vector3 SharedCameraPosition =>
            MinigameCameraFraming.CalculateSharedPosition(
                ArenaCenterX,
                23f);

        public static Quaternion SharedCameraRotation =>
            MinigameCameraFraming.SharedRotation;

        public GameObject ArenaPresentation => arenaPresentation;
        public CinemachineCamera SharedCamera => sharedCamera;
        public GameObject FallImpactVfxPrefab => fallImpactVfxPrefab;
        public GameObject CollisionExplosionVfxPrefab =>
            collisionExplosionVfxPrefab;


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

        public void ConfigureVfx(GameObject fallImpactPrefab)
        {
            fallImpactVfxPrefab = fallImpactPrefab;
        }

        public void ConfigureCollisionVfx(
            GameObject collisionExplosionPrefab)
        {
            collisionExplosionVfxPrefab = collisionExplosionPrefab;
        }


        private void Awake()
        {
            state ??= GetComponent<NetworkSnowySpinState>();
            _colorBlock = new MaterialPropertyBlock();
            ResetBallPresentationState();
            ConfigureCamera();
            SetWorldPresentationActive(false);
            OneShotVfxPool.Prewarm(collisionExplosionVfxPrefab, 4);
        }

        private void OnDisable()
        {
            UnbindCollisionPresentation();
            SetWorldPresentationActive(false);
            UnregisterCamera();
        }

        private void Update()
        {
            state ??= GetComponent<NetworkSnowySpinState>();
            BindCollisionPresentation();
            var match = NetworkMatchState.Instance;
            var selected = match != null && match.IsSnowySpinPhase;
            var shouldShowWorld = state != null && state.IsSpawned &&
                selected &&
                (match.FlowState == BoardFlowState.MinigamePlaying ||
                 match.FlowState == BoardFlowState.MinigameResult);
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
                ResetBallPresentationState();
            }

            var deltaTime = Mathf.Max(0f, Time.unscaledDeltaTime);
            for (var slot = 0; slot < SnowySpinRules.PlayerCount;
                 slot++)
            {
                if (slot >= playerBalls.Length ||
                    playerBalls[slot] == null)
                {
                    continue;
                }

                var ball = playerBalls[slot];
                var position = state.GetPlayerPosition(slot);
                var eliminated = state.IsEliminated(slot);
                var wasEliminated = _wasEliminated[slot];
                var beganFalling = eliminated && !wasEliminated;
                if (beganFalling)
                {
                    _fallStartedAt[slot] = _hadVisibleFrame
                        ? Time.unscaledTime
                        : Time.unscaledTime - FallDurationSeconds;
                    if (_hadVisibleFrame && fallImpactVfxPrefab != null)
                    {
                        var impactPosition = arenaPresentation.transform
                            .TransformPoint(new Vector3(
                                position.x,
                                BallPresentationHeight * 0.5f,
                                position.y));
                        OneShotVfxPool.Play(
                            fallImpactVfxPrefab,
                            impactPosition,
                            Quaternion.identity,
                            0.8f);
                    }
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

                var scaleMultiplier = Mathf.Max(
                    1f, _ballScaleMultipliers[slot]);
                var fallOffset =
                    2.5f * fallProgress * fallProgress;
                var target = arenaPresentation.transform.TransformPoint(
                    new Vector3(
                        position.x,
                        BallPresentationHeight * scaleMultiplier -
                            fallOffset,
                        position.y));
                var previous = ball.position;
                var teleported = !_hadVisibleFrame ||
                    (previous - target).sqrMagnitude > 36f;
                ball.position = teleported
                    ? target
                    : Vector3.Lerp(
                        previous,
                        target,
                        1f - Mathf.Exp(
                            -InterpolationSpeed *
                            deltaTime));

                var planarTravel = ball.position - previous;
                planarTravel.y = 0f;
                var distance = planarTravel.magnitude;
                var canSampleSpeed = _hadVisibleFrame &&
                    !teleported && deltaTime > 0.000001f &&
                    (!eliminated || beganFalling);
                if (canSampleSpeed)
                {
                    var sampledSpeed = Mathf.Min(
                        distance / deltaTime,
                        (float)SnowySpinRules.MaximumSpeed);
                    _smoothedSpeeds[slot] = Mathf.Lerp(
                        _smoothedSpeeds[slot],
                        sampledSpeed,
                        1f - Mathf.Exp(
                            -SpeedSmoothing * deltaTime));
                    _ballScaleMultipliers[slot] =
                        CalculateBallScaleMultiplier(
                            _smoothedSpeeds[slot]);
                }
                else if (!eliminated && teleported)
                {
                    _smoothedSpeeds[slot] = 0f;
                    _ballScaleMultipliers[slot] = 1f;
                }

                scaleMultiplier = Mathf.Max(
                    1f, _ballScaleMultipliers[slot]);
                var groundedTarget =
                    arenaPresentation.transform.TransformPoint(
                        new Vector3(
                            position.x,
                            BallPresentationHeight * scaleMultiplier -
                                fallOffset,
                            position.y));
                var groundedPosition = ball.position;
                groundedPosition.y = groundedTarget.y;
                ball.position = groundedPosition;

                if (_hadVisibleFrame && !teleported &&
                    distance > 0.0001f)
                {
                    var axis = Vector3.Cross(
                        Vector3.up,
                        planarTravel / distance);
                    ball.rotation = Quaternion.AngleAxis(
                        distance /
                            (BallPresentationHeight * scaleMultiplier) *
                            Mathf.Rad2Deg,
                        axis) * ball.rotation;
                }
                ball.localScale = Vector3.one *
                    BallPresentationScale *
                    scaleMultiplier *
                    Mathf.Lerp(1f, 0.15f, fallProgress);

                var avatar = match.GetAvatarForSlot(slot);
                var color = avatar != null
                    ? avatar.Appearance.BodyColor
                    : FallbackColors[slot];
                ApplyBallColor(slot, color);
            }
        }

        private void BindCollisionPresentation()
        {
            if (_subscribedCollisionState == state)
            {
                return;
            }

            UnbindCollisionPresentation();
            if (state == null)
            {
                return;
            }

            _subscribedCollisionState = state;
            _subscribedCollisionState.CollisionPresentationRequested +=
                HandleNetworkCollisionPresentation;
        }

        private void UnbindCollisionPresentation()
        {
            if (_subscribedCollisionState == null)
            {
                return;
            }

            _subscribedCollisionState.CollisionPresentationRequested -=
                HandleNetworkCollisionPresentation;
            _subscribedCollisionState = null;
        }

        private void HandleNetworkCollisionPresentation(
            int firstSlot,
            int secondSlot,
            Vector2 localCenter,
            float impactSpeed,
            float firstPostSpeed,
            float secondPostSpeed)
        {
            if (!_worldVisible)
            {
                return;
            }

            PlayCollisionPresentation(
                firstSlot,
                secondSlot,
                localCenter,
                impactSpeed,
                firstPostSpeed,
                secondPostSpeed);
        }

        public void PlayCollisionPresentation(
            int firstSlot,
            int secondSlot,
            Vector2 localCenter,
            float impactSpeed,
            float firstPostSpeed,
            float secondPostSpeed)
        {
            ApplyAuthoritativeCollisionSpeed(firstSlot, firstPostSpeed);
            ApplyAuthoritativeCollisionSpeed(secondSlot, secondPostSpeed);

            if (arenaPresentation == null ||
                collisionExplosionVfxPrefab == null)
            {
                return;
            }

            var normalizedImpact = Mathf.Clamp01(
                impactSpeed /
                ((float)SnowySpinRules.MaximumSpeed * 2f));
            var worldCenter = arenaPresentation.transform.TransformPoint(
                new Vector3(localCenter.x, 0f, localCenter.y));
            var centerHeight = 0f;
            var centerCount = 0;
            foreach (var slot in new[] { firstSlot, secondSlot })
            {
                if (SnowySpinRules.IsValidPlayerSlot(slot) &&
                    playerBalls != null && slot < playerBalls.Length &&
                    playerBalls[slot] != null)
                {
                    centerHeight += playerBalls[slot].position.y;
                    centerCount++;
                }
            }
            worldCenter.y = centerCount > 0
                ? centerHeight / centerCount
                : arenaPresentation.transform.TransformPoint(
                    new Vector3(
                        localCenter.x,
                        BallPresentationHeight,
                        localCenter.y)).y;

            var effect = OneShotVfxPool.Play(
                collisionExplosionVfxPrefab,
                worldCenter,
                Quaternion.identity,
                Mathf.Lerp(0.8f, 1.2f, normalizedImpact));
            ApplySnowExplosionWhite(effect);
        }

        private void ApplyAuthoritativeCollisionSpeed(
            int slot,
            float speed)
        {
            if (!SnowySpinRules.IsValidPlayerSlot(slot))
            {
                return;
            }

            var clampedSpeed = Mathf.Clamp(
                speed,
                0f,
                (float)SnowySpinRules.MaximumSpeed);
            _smoothedSpeeds[slot] = clampedSpeed;
            _ballScaleMultipliers[slot] =
                CalculateBallScaleMultiplier(clampedSpeed);
        }

        private static void ApplySnowExplosionWhite(
            PooledOneShotVfx effect)
        {
            if (effect == null)
            {
                return;
            }

            foreach (var particles in effect.ParticleSystems)
            {
                if (particles == null)
                {
                    continue;
                }

                var main = particles.main;
                main.startColor = CreateWhiteGradient(
                    main.startColor);

                var colorOverLifetime = particles.colorOverLifetime;
                if (colorOverLifetime.enabled)
                {
                    colorOverLifetime.color = CreateWhiteGradient(
                        colorOverLifetime.color);
                }

                var colorBySpeed = particles.colorBySpeed;
                if (colorBySpeed.enabled)
                {
                    colorBySpeed.color = CreateWhiteGradient(
                        colorBySpeed.color);
                }
            }

            var block = new MaterialPropertyBlock();
            foreach (var renderer in effect.GetComponentsInChildren<
                         Renderer>(true))
            {
                renderer.GetPropertyBlock(block);
                block.SetColor(BaseColorProperty, Color.white);
                block.SetColor(ColorProperty, Color.white);
                block.SetColor(EmissionColorProperty, Color.white);
                block.SetColor(GlowColorProperty, Color.white);
                block.SetColor(DepthGlowColorProperty, Color.white);
                block.SetFloat(ColorRampBlendProperty, 0f);

                renderer.SetPropertyBlock(block);
                block.Clear();
            }

            foreach (var flash in effect.FlashLights)
            {
                if (flash != null)
                {
                    flash.color = Color.white;
                }
            }
        }

        private static ParticleSystem.MinMaxGradient CreateWhiteGradient(
            ParticleSystem.MinMaxGradient source)
        {
            ParticleSystem.MinMaxGradient result;
            switch (source.mode)
            {
                case ParticleSystemGradientMode.TwoColors:
                    result = new ParticleSystem.MinMaxGradient(
                        WithWhiteRgb(source.colorMin),
                        WithWhiteRgb(source.colorMax));
                    break;
                case ParticleSystemGradientMode.Gradient:
                case ParticleSystemGradientMode.RandomColor:
                    result = new ParticleSystem.MinMaxGradient(
                        CreateWhiteGradient(source.gradient));
                    break;
                case ParticleSystemGradientMode.TwoGradients:
                    result = new ParticleSystem.MinMaxGradient(
                        CreateWhiteGradient(source.gradientMin),
                        CreateWhiteGradient(source.gradientMax));
                    break;
                default:
                    result = new ParticleSystem.MinMaxGradient(
                        WithWhiteRgb(source.color));
                    break;
            }

            result.mode = source.mode;
            return result;
        }

        private static Gradient CreateWhiteGradient(Gradient source)
        {
            if (source == null)
            {
                return new Gradient();
            }

            var sourceColorKeys = source.colorKeys;
            var colorKeys =
                new GradientColorKey[sourceColorKeys.Length];
            for (var index = 0; index < colorKeys.Length; index++)
            {
                colorKeys[index] = new GradientColorKey(
                    Color.white,
                    sourceColorKeys[index].time);
            }

            var result = new Gradient
            {
                mode = source.mode
            };
            result.SetKeys(colorKeys, source.alphaKeys);
            return result;
        }

        private static Color WithWhiteRgb(Color source)
        {
            return new Color(1f, 1f, 1f, source.a);
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


        public static float CalculateBallScaleMultiplier(
            float speed)
        {
            var normalizedSpeed = Mathf.Clamp01(
                Mathf.Max(0f, speed) /
                (float)SnowySpinRules.MaximumSpeed);
            return Mathf.Lerp(1f, MaximumBallScaleMultiplier,
                normalizedSpeed);
        }

        private void ResetBallPresentationState()
        {
            for (var slot = 0; slot < SnowySpinRules.PlayerCount;
                slot++)
            {
                _fallStartedAt[slot] = 0f;
                _wasEliminated[slot] = false;
                _smoothedSpeeds[slot] = 0f;
                _ballScaleMultipliers[slot] = 1f;
            }
        }


        private void OnEnable()
        {
            BindCollisionPresentation();
        }
}
}
