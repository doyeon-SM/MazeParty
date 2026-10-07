using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.BombPassing;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// World-space presentation of the server-owned bomb match. All clients
    /// register the same authored camera and render the same replicated state.
    /// No Canvas UI is created for this minigame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BombPassingNetworkView : MonoBehaviour
    {
        public const float ArenaCenterX = 1380f;
        public const float SharedCameraOrthographicSize = 12f;
        public const float PlayerPresentationHeight = 1.18f;
        public const float GroundBombHeight = 0.75f;
        public const float CarriedBombHeight = 1.75f;

        private const float InterpolationSpeed = 20f;
        private const float ExplosionFlashSeconds = 0.35f;
        private static readonly int BaseColorProperty =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty =
            Shader.PropertyToID("_Color");
        private static readonly int EmissionColorProperty =
            Shader.PropertyToID("_EmissionColor");
        private static readonly Color BombColor =
            new Color(1f, 0.23f, 0.1f, 1f);
        private static readonly Color[] FallbackPlayerColors =
        {
            new Color(0.16f, 0.48f, 0.95f),
            new Color(0.92f, 0.2f, 0.16f),
            new Color(0.18f, 0.78f, 0.32f),
            new Color(0.7f, 0.26f, 0.9f)
        };

        [SerializeField] private NetworkBombPassingState state;
        [SerializeField] private CinemachineCamera sharedCamera;
        [SerializeField] private Transform playerRoot;
        [SerializeField] private GameObject arenaPresentation;
        [SerializeField] private Transform bombTransform;
        [SerializeField] private Renderer bombRenderer;
        [SerializeField] private Light bombLight;
        [SerializeField] private Light explosionFlashLight;
        [SerializeField] private GameObject explosionVfxPrefab;

        private readonly PlayerView[] _players =
            new PlayerView[BombPassingRules.PlayerCount];
        private GameplayCameraDirector _cameraDirector;
        private MaterialPropertyBlock _colorBlock;
        private float _baseLightIntensity = 1f;
        private float _explosionPeakIntensity = 4f;
        private float _explosionFlashUntil;
        private uint _lastExplosionSequence;
        private bool _cameraRegistered;
        private bool _visibilityInitialized;
        private bool _worldVisible;
        private bool _hadVisibleFrame;
        private int _localSlot = -1;

        public static Vector3 SharedCameraPosition =>
            MinigameCameraFraming.CalculateSharedPosition(
                ArenaCenterX,
                24f);

        public static Quaternion SharedCameraRotation =>
            MinigameCameraFraming.SharedRotation;

        public GameObject ArenaPresentation => arenaPresentation;
        public Transform PlayerRoot => playerRoot;
        public Transform BombTransform => bombTransform;
        public Renderer BombRenderer => bombRenderer;
        public Light BombLight => bombLight;
        public Light ExplosionFlashLight => explosionFlashLight;
        public GameObject ExplosionVfxPrefab => explosionVfxPrefab;

        public Transform GetPlayerTransform(int slot)
        {
            return slot >= 0 && slot < _players.Length
                ? _players[slot]?.Root
                : null;
        }

        public PlayerAvatarVisual GetPlayerVisual(int slot)
        {
            return slot >= 0 && slot < _players.Length
                ? _players[slot]?.Visual
                : null;
        }

        public void Configure(
            NetworkBombPassingState networkState,
            CinemachineCamera camera,
            Transform players,
            GameObject arena,
            Transform bomb,
            Renderer bombVisual,
            Light warningLight,
            Light explosionLight,
            GameObject explosionPrefab)
        {
            state = networkState;
            sharedCamera = camera;
            playerRoot = players;
            arenaPresentation = arena;
            bombTransform = bomb;
            bombRenderer = bombVisual;
            bombLight = warningLight;
            explosionFlashLight = explosionLight;
            explosionVfxPrefab = explosionPrefab;
            ConfigureCamera();
            if (Application.isPlaying)
            {
                EnsurePlayers();
            }
        }

        private void Awake()
        {
            state ??= GetComponent<NetworkBombPassingState>();
            _baseLightIntensity = bombLight != null
                ? Mathf.Max(0.01f, bombLight.intensity)
                : 1f;
            _explosionPeakIntensity = explosionFlashLight != null
                ? Mathf.Max(0.01f, explosionFlashLight.intensity)
                : 4f;
            _colorBlock = new MaterialPropertyBlock();
            OneShotVfxPool.Prewarm(explosionVfxPrefab, 2);
            ConfigureCamera();
            EnsurePlayers();
            if (explosionFlashLight == null)
            {
                Debug.LogError(
                    "[Bomb Passing] Authored explosion flash Light is " +
                    "missing. Rebuild the Bomb Passing scene; explosion " +
                    "flash presentation will remain disabled.",
                    this);
            }
            else
            {
                explosionFlashLight.enabled = false;
            }
            SetWorldPresentationActive(false);
        }

        private void OnDisable()
        {
            if (explosionFlashLight != null)
            {
                explosionFlashLight.enabled = false;
            }
            SetWorldPresentationActive(false);
            UnregisterCamera();
        }

        private void Update()
        {
            state ??= GetComponent<NetworkBombPassingState>();
            EnsurePlayers();
            var match = NetworkMatchState.Instance;
            var selected = match != null && match.IsBombPassingPhase;
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
            ResolveLocalSlot(match);
            RefreshPlayers(match);
            RefreshExplosionEvent();
            RefreshBomb();
            RefreshExplosionLight();
            RefreshFixedCamera();
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
            RefreshFixedCamera();
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

                var playerObject =
                    new GameObject("Bomb Passing Player " + (slot + 1));
                playerObject.transform.SetParent(playerRoot, false);
                var visual = playerObject.AddComponent<PlayerAvatarVisual>();
                visual.EnsureBuilt();
                visual.SetBodyColor(FallbackPlayerColors[slot]);
                visual.SetDisplayName(GameText.F("PLAYER {0}", slot + 1));
                visual.SetOwnerFirstPerson(false);
                visual.SetTopViewHighlight(false);
                visual.SetEliminated(false);
                DisableGeneratedHitColliders(playerObject);
                _players[slot] =
                    new PlayerView(playerObject.transform, visual);
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
                    break;
                }
            }
        }

        private void RefreshPlayers(NetworkMatchState match)
        {
            var arena = arenaPresentation != null
                ? arenaPresentation.transform
                : transform;
            for (var slot = 0; slot < _players.Length; slot++)
            {
                var player = _players[slot];
                if (player == null)
                {
                    continue;
                }

                var position = state.GetPlayerPosition(slot);
                var target = arena.TransformPoint(new Vector3(
                    position.x,
                    PlayerPresentationHeight,
                    position.y));
                player.Root.position = !_hadVisibleFrame ||
                    (player.Root.position - target).sqrMagnitude > 36f
                    ? target
                    : Vector3.Lerp(
                        player.Root.position,
                        target,
                        1f - Mathf.Exp(
                            -InterpolationSpeed * Time.unscaledDeltaTime));
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
                        appearance.HatId,
                        appearance.ExpressionId);
                    player.Visual.SetDisplayName(
                        string.IsNullOrWhiteSpace(avatar.DisplayName)
                            ? GameText.F("PLAYER {0}", slot + 1)
                            : avatar.DisplayName);
                }
                player.Visual.SetOwnerFirstPerson(false);
                player.Visual.SetCrouching(state.IsStunned(slot));
                player.Visual.SetEliminated(state.IsEliminated(slot));
            }
        }

        private void RefreshBomb()
        {
            if (bombTransform == null)
            {
                return;
            }

            var visible =
                state.Phase == NetworkBombPassingPhase.Playing;
            if (bombTransform.gameObject.activeSelf != visible)
            {
                bombTransform.gameObject.SetActive(visible);
            }
            if (!visible)
            {
                return;
            }

            var arena = arenaPresentation != null
                ? arenaPresentation.transform
                : transform;
            var position = state.BombPosition;
            var height = state.BombHolderSlot >= 0
                ? CarriedBombHeight
                : GroundBombHeight;
            var target = arena.TransformPoint(new Vector3(
                position.x,
                height,
                position.y));
            bombTransform.position = !_hadVisibleFrame ||
                (bombTransform.position - target).sqrMagnitude > 25f
                ? target
                : Vector3.Lerp(
                    bombTransform.position,
                    target,
                    1f - Mathf.Exp(
                        -InterpolationSpeed * Time.unscaledDeltaTime));
            bombTransform.Rotate(
                Vector3.up,
                100f * Time.unscaledDeltaTime,
                Space.World);
            RefreshBombLight();
        }

        private void RefreshBombLight()
        {
            if (bombLight == null)
            {
                return;
            }

            var fuse = Mathf.Max(0.01f, state.BombFuseSeconds);
            var ratio = Mathf.Clamp01(state.BombRemainingSeconds / fuse);
            var frequency = ResolveWarningFrequency(
                ratio,
                PresentationAccessibility.ReduceFlashes);
            var on = frequency <= 0f || state.IsPaused ||
                Mathf.Repeat(Time.unscaledTime * frequency, 1f) < 0.5f;
            bombLight.enabled = on;
            var warningScale =
                PresentationAccessibility.FlashIntensityScale;
            bombLight.intensity =
                on ? _baseLightIntensity * warningScale : 0f;
            if (bombRenderer != null)
            {
                _colorBlock ??= new MaterialPropertyBlock();
                _colorBlock.Clear();
                _colorBlock.SetColor(BaseColorProperty, BombColor);
                _colorBlock.SetColor(ColorProperty, BombColor);
                _colorBlock.SetColor(
                    EmissionColorProperty,
                    on
                        ? BombColor * (3f * warningScale)
                        : BombColor * 0.15f);
                bombRenderer.SetPropertyBlock(_colorBlock);
            }
        }

        private void RefreshExplosionEvent()
        {
            var sequence = state.ExplosionSequence;
            if (!_hadVisibleFrame)
            {
                // A late joiner should not replay an old explosion.
                _lastExplosionSequence = sequence;
                return;
            }
            if (sequence == _lastExplosionSequence)
            {
                return;
            }

            _lastExplosionSequence = sequence;
            var slot = state.LastExplodedSlot;
            var arena = arenaPresentation != null
                ? arenaPresentation.transform
                : transform;
            Vector3 explosionPosition;
            if (BombPassingRules.IsValidPlayerSlot(slot))
            {
                var position = state.GetPlayerPosition(slot);
                explosionPosition = arena.TransformPoint(new Vector3(
                    position.x,
                    1.2f,
                    position.y));
            }
            else
            {
                explosionPosition = bombTransform != null
                    ? bombTransform.position
                    : arena.position + Vector3.up;
            }

            OneShotVfxPool.Play(
                explosionVfxPrefab,
                explosionPosition,
                Quaternion.identity,
                1.35f);
            if (explosionFlashLight == null)
            {
                return;
            }

            explosionFlashLight.transform.position = explosionPosition;
            _explosionFlashUntil =
                Time.unscaledTime + ExplosionFlashSeconds;
            explosionFlashLight.enabled = true;
        }

        private void RefreshExplosionLight()
        {
            if (explosionFlashLight == null ||
                !explosionFlashLight.enabled)
            {
                return;
            }

            var remaining = _explosionFlashUntil - Time.unscaledTime;
            if (remaining <= 0f)
            {
                explosionFlashLight.enabled = false;
                return;
            }

            explosionFlashLight.intensity =
                ResolveFlashIntensity(
                    _explosionPeakIntensity,
                    remaining / ExplosionFlashSeconds);
        }

        internal static float ResolveWarningFrequency(
            float fuseRemainingRatio,
            bool reduceFlashes)
        {
            if (reduceFlashes)
            {
                return 0f;
            }

            var ratio = Mathf.Clamp01(fuseRemainingRatio);
            return ratio <= 0.1f
                ? 9f
                : ratio <= 0.2f
                    ? 5f
                    : ratio <= 0.5f
                        ? 2f
                        : 0f;
        }

        internal static float ResolveFlashIntensity(
            float peakIntensity,
            float normalizedRemaining)
        {
            return Mathf.Max(0f, peakIntensity) *
                   PresentationAccessibility.FlashIntensityScale *
                   Mathf.Clamp01(normalizedRemaining);
        }

        private void RefreshFixedCamera()
        {
            if (sharedCamera == null)
            {
                return;
            }

            sharedCamera.ForceCameraPosition(
                SharedCameraPosition,
                SharedCameraRotation);
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
            sharedCamera.Priority = 0;
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
                _explosionFlashUntil = 0f;
                if (explosionFlashLight != null)
                {
                    explosionFlashLight.enabled = false;
                }
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
    }
}
