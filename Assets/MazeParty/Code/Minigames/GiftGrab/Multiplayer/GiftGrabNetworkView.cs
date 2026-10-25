using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.GiftGrab;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Client presentation for the server-authoritative Gift Grab snapshot.
    /// The scene and prefabs own every visible element; this component only
    /// positions them and updates their serialized bindings.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GiftGrabNetworkView : MonoBehaviour
    {
        public const int PlayerCount = 4;
        public const float PlayerPresentationHeight = 1.05f;
        public const float GiftPresentationHeight = 0.55f;
        public static readonly Vector3 CarriedGiftLocalOffset =
            new Vector3(0f, 0.15f, 0.7f);
        public const float SharedCameraOrthographicSize = 11.5f;

        private const float ActionVfxSeconds = 0.38f;

        private static readonly Color[] FallbackPlayerColors =
        {
            new Color(0.16f, 0.48f, 0.95f),
            new Color(0.92f, 0.2f, 0.16f),
            new Color(0.18f, 0.78f, 0.32f),
            new Color(0.7f, 0.26f, 0.9f)
        };

        [SerializeField] private NetworkGiftGrabState state;
        [SerializeField] private BouncingBallsHudBindings giftHud;
        public BouncingBallsHudBindings GiftHud => giftHud;
        public void ConfigureHud(BouncingBallsHudBindings hud) => giftHud = hud;
        [SerializeField] private CinemachineCamera sharedCamera;
        [SerializeField] private Transform playerRoot;
        [SerializeField] private Transform giftRoot;
        [SerializeField] private Transform[] playerAnchors =
            new Transform[PlayerCount];
        [SerializeField] private Transform[] baseAnchors =
            new Transform[PlayerCount];
        [SerializeField] private Transform pushVfxAnchor;
        [SerializeField] private Transform throwVfxAnchor;
        [SerializeField] private Transform dropVfxAnchor;
        [SerializeField] private GameObject arenaPresentation;
        [SerializeField] private AudioSource cueAudioSource;
        [SerializeField] private GameObject actionBurstVfxPrefab;

        private readonly PlayerView[] _players = new PlayerView[PlayerCount];
        private GiftView[] _gifts = System.Array.Empty<GiftView>();
        private float[] _spawnFallRemaining = System.Array.Empty<float>();
        private GiftGrabGiftState[] _previousGiftStates =
            System.Array.Empty<GiftGrabGiftState>();
        private MaterialPropertyBlock _baseColorProperties;
        private GameplayCameraDirector _cameraDirector;
        private bool _cameraConfigured;
        private bool _worldVisible;
        private bool _worldVisibilityInitialized;
        private NetworkGiftGrabState _subscribedVfxState;
        private float _pushVfxRemaining;
        private float _throwVfxRemaining;
        private float _dropVfxRemaining;

        public NetworkGiftGrabState State => state;
        public GameObject ArenaPresentation => arenaPresentation;
        public Transform PlayerRoot => playerRoot;
        public Transform GiftRoot => giftRoot;
        public Transform[] PlayerAnchors => playerAnchors;
        public Transform[] BaseAnchors => baseAnchors;
        public GameObject ActionBurstVfxPrefab => actionBurstVfxPrefab;

        public static Vector3 SharedCameraPosition =>
            MinigameCameraFraming.CalculateSharedPosition(
                NetworkGiftGrabState.ArenaCenterX,
                26f);

        public static Quaternion SharedCameraRotation =>
            MinigameCameraFraming.SharedRotation;

        public void Configure(
            NetworkGiftGrabState networkState,
            CinemachineCamera camera,
            Transform runtimePlayers,
            Transform runtimeGifts,
            Transform[] fixedPlayerAnchors,
            Transform[] fixedBaseAnchors,
            Transform pushEffect,
            Transform throwEffect,
            Transform dropEffect,
            GameObject arena,
            AudioSource audioSource)
        {
            state = networkState;
            EnsureActionVfxSubscription();
            sharedCamera = camera;
            playerRoot = runtimePlayers;
            giftRoot = runtimeGifts;
            playerAnchors = fixedPlayerAnchors;
            baseAnchors = fixedBaseAnchors;
            pushVfxAnchor = pushEffect;
            throwVfxAnchor = throwEffect;
            dropVfxAnchor = dropEffect;
            arenaPresentation = arena;
            cueAudioSource = audioSource;
            _cameraConfigured = false;
            ConfigureCamera();
            CacheGiftViews();
            if (Application.isPlaying)
            {
                EnsurePlayers();
            }
        }

        public void ConfigureVfx(GameObject actionBurstPrefab)
        {
            actionBurstVfxPrefab = actionBurstPrefab;
        }

        private void Awake()
        {
            state ??= GetComponent<NetworkGiftGrabState>();
            EnsureActionVfxSubscription();
            ConfigureCamera();
            CacheGiftViews();
            EnsurePlayers();
            SetActionVfxActive(false);
            SetWorldPresentationActive(false);
        }

        private void OnEnable()
        {
            state ??= GetComponent<NetworkGiftGrabState>();
            EnsureActionVfxSubscription();
        }

        private void OnDisable()
        {
            UnsubscribeFromActionVfxEvents();
            RestoreGiftPresentations();
            SetWorldPresentationActive(false);
            UnregisterCamera();
        }

        private void Update()
        {
            state ??= GetComponent<NetworkGiftGrabState>();
            EnsureActionVfxSubscription();
            EnsurePlayers();
            CacheGiftViews();

            var match = NetworkMatchState.Instance;
            var selected = match != null && match.IsGiftGrabPhase;
            if (selected)
            {
                RegisterCamera();
            }
            else
            {
                UnregisterCamera();
            }

            var showWorld = state != null && state.IsSpawned && selected &&
                            (match.FlowState == BoardFlowState.MinigamePlaying ||
                             match.FlowState == BoardFlowState.MinigameResult);
            if (!showWorld)
            {
                SetWorldPresentationActive(false);
                return;
            }

            SetWorldPresentationActive(true);
            // MinigameLocalPlayerHighlight owns the brief shared countdown
            // marker; this view does not keep a persistent local-player mark.
            RefreshPlayers(match);
            RefreshGiftHud(match);
            RefreshGifts(match);
            RefreshBaseColors(match);
            RefreshActionVfx();
        }

        private void RefreshGiftHud(NetworkMatchState match)
        {
            if (giftHud == null || !giftHud.HasRequiredReferences) return;
            for (var slot = 0; slot < PlayerCount; slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                var name = avatar != null && !string.IsNullOrWhiteSpace(avatar.DisplayName)
                    ? avatar.DisplayName : GameText.F("Player {0}", slot + 1);
                giftHud.SetPlayer(slot, name.Replace("<", "＜").Replace(">", "＞"),
                    state.GetDepositedGiftCount(slot),
                    avatar != null ? avatar.Appearance.BodyColor : FallbackPlayerColors[slot]);
            }
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
            lens.FarClipPlane = 80f;
            sharedCamera.Lens = lens;
            sharedCamera.ForceCameraPosition(
                SharedCameraPosition,
                SharedCameraRotation);
            sharedCamera.Priority = 0;
            _cameraConfigured = true;
        }

        private void RegisterCamera()
        {
            _cameraDirector ??= FindAnyObjectByType<GameplayCameraDirector>();
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

        private void EnsurePlayers()
        {
            if (playerRoot == null || playerAnchors == null)
            {
                return;
            }

            for (var slot = 0; slot < PlayerCount; slot++)
            {
                if (_players[slot] != null ||
                    slot >= playerAnchors.Length ||
                    playerAnchors[slot] == null)
                {
                    continue;
                }

                var root = new GameObject("Gift Grab Player " + (slot + 1));
                root.transform.SetParent(playerRoot, false);
                root.transform.position = playerAnchors[slot].position;
                var visual = root.AddComponent<PlayerAvatarVisual>();
                visual.EnsureBuilt();
                visual.SetOwnerFirstPerson(false);
                visual.SetBodyColor(FallbackPlayerColors[slot]);
                visual.SetDisplayName(GameText.F("Player {0}", slot + 1));
                visual.SetNameplateVisible(true);
                DisableGeneratedHitColliders(root);
                _players[slot] = new PlayerView(root.transform, visual);
            }
        }

        private void CacheGiftViews()
        {
            if (giftRoot == null)
            {
                return;
            }

            if (_gifts.Length != giftRoot.childCount)
            {
                _gifts = new GiftView[giftRoot.childCount];
                _spawnFallRemaining = new float[giftRoot.childCount];
                _previousGiftStates = new GiftGrabGiftState[giftRoot.childCount];
            }
            for (var index = 0; index < _gifts.Length; index++)
            {
                if (_gifts[index] != null)
                {
                    continue;
                }
                var anchor = giftRoot.GetChild(index);
                var visual = anchor.Find("Gift Visual");
                if (visual != null)
                {
                    _gifts[index] = new GiftView(anchor, visual);
                }
            }
        }

        private void RefreshPlayers(NetworkMatchState match)
        {
            for (var slot = 0; slot < PlayerCount; slot++)
            {
                var player = _players[slot];
                if (player == null)
                {
                    continue;
                }

                var position = state.GetPlayerPosition(slot);
                player.Root.position = new Vector3(
                    position.x,
                    PlayerPresentationHeight,
                    position.y);
                var facing = state.GetPlayerFacing(slot);
                if (facing.sqrMagnitude > 0.001f)
                {
                    player.Root.rotation = Quaternion.LookRotation(
                        new Vector3(facing.x, 0f, facing.y),
                        Vector3.up);
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

        private void RefreshGifts(NetworkMatchState match)
        {
            for (var giftId = 0; giftId < _gifts.Length; giftId++)
            {
                var gift = _gifts[giftId];
                if (gift == null)
                {
                    continue;
                }
                var giftState = state.GetGiftState(giftId);
                if (!state.IsGiftActive(giftId))
                {
                    gift.RestoreAuthoredPresentation();
                    gift.Root.gameObject.SetActive(false);
                    _previousGiftStates[giftId] = giftState;
                    continue;
                }

                if (_previousGiftStates[giftId] ==
                        GiftGrabGiftState.Unspawned &&
                    giftId >= GiftGrabRules.InitialGiftCount)
                {
                    _spawnFallRemaining[giftId] =
                        (float)GiftGrabRules.SpawnFallSeconds;
                }
                _previousGiftStates[giftId] = giftState;

                gift.Variants?.Present(match.CurrentMinigameSeed, state.RoundNumber, giftId);
                gift.Root.gameObject.SetActive(true);
                var carrier = state.GetGiftCarrierSlot(giftId);
                var storedOwner = state.GetGiftStoredOwnerSlot(giftId);
                if (storedOwner >= 0 && storedOwner < PlayerCount)
                {
                    gift.RestoreAuthoredPresentation();
                    // Stored gifts remain on the exact server-authored grid so
                    // the visible steal target and authoritative contact point
                    // can never diverge. Merely lying over a base does not use
                    // this branch and therefore never looks deposited.
                    var position = state.GetGiftPosition(giftId);
                    gift.Root.position = new Vector3(
                        position.x,
                        GiftPresentationHeight,
                        position.y);
                    gift.Root.rotation = Quaternion.identity;
                }
                else if (carrier >= 0 && carrier < PlayerCount &&
                         _players[carrier] != null)
                {
                    gift.AttachVisualTo(_players[carrier].Root);
                }
                else
                {
                    gift.RestoreAuthoredPresentation();
                    var position = state.GetGiftPosition(giftId);
                    var falling = _spawnFallRemaining[giftId];
                    var height = GiftPresentationHeight +
                        5f * (falling /
                              (float)GiftGrabRules.SpawnFallSeconds);
                    gift.Root.position = new Vector3(
                        position.x,
                        height,
                        position.y);
                    gift.Root.rotation = Quaternion.identity;
                    _spawnFallRemaining[giftId] = Mathf.Max(0f,
                        falling - Time.unscaledDeltaTime);
                    var lockRemaining =
                        state.GetGiftPickupLockRemaining(giftId);
                    gift.Visual.gameObject.SetActive(
                        GiftGrabRules.IsDropPickupBlinkVisible(lockRemaining));
                }

                // The authoritative enum remains useful to artists/debuggers
                // even though carrier/owner are the placement source of truth.
                gift.LastStateName = state.GetGiftState(giftId).ToString();
            }
        }

        private void RefreshBaseColors(NetworkMatchState match)
        {
            _baseColorProperties ??= new MaterialPropertyBlock();
            for (var slot = 0; slot < PlayerCount; slot++)
            {
                if (baseAnchors == null || slot >= baseAnchors.Length ||
                    baseAnchors[slot] == null)
                {
                    continue;
                }
                var avatar = match.GetAvatarForSlot(slot);
                var color = avatar != null
                    ? avatar.Appearance.BodyColor
                    : FallbackPlayerColors[slot];
                foreach (var renderer in baseAnchors[slot]
                             .GetComponentsInChildren<Renderer>(true))
                {
                    renderer.GetPropertyBlock(_baseColorProperties);
                    _baseColorProperties.SetColor("_BaseColor", color);
                    _baseColorProperties.SetColor("_Color", color);
                    renderer.SetPropertyBlock(_baseColorProperties);
                }
            }
        }

        private void RefreshActionVfx()
        {
            _pushVfxRemaining = TickVfx(pushVfxAnchor, _pushVfxRemaining);
            _throwVfxRemaining = TickVfx(throwVfxAnchor, _throwVfxRemaining);
            _dropVfxRemaining = TickVfx(dropVfxAnchor, _dropVfxRemaining);

            for (var slot = 0; slot < PlayerCount; slot++)
            {
                _players[slot]?.Visual.SetKnockedDown(
                    state.Phase == NetworkGiftGrabPhase.Running && state.GetPlayerStunRemaining(slot) > 0d);
            }
        }

        private void HandleActionPresentationRequested(
            GiftGrabNetworkActionType actionType,
            int actorSlot,
            int targetSlot,
            Vector2 actionPosition)
        {
            if (!_worldVisible)
            {
                return;
            }

            if (actionType == GiftGrabNetworkActionType.PushCastStarted)
            {
                GetPlayerVisual(actorSlot)?.TriggerPush();
                return;
            }

            switch (actionType)
            {
                case GiftGrabNetworkActionType.Push:
                    GetPlayerVisual(targetSlot)?.TriggerHit();
                    PlaceVfx(pushVfxAnchor, actionPosition);
                    _pushVfxRemaining = ActionVfxSeconds;
                    break;
                case GiftGrabNetworkActionType.ThrownHit:
                    GetPlayerVisual(targetSlot)?.TriggerHit();
                    PlaceVfx(pushVfxAnchor, actionPosition);
                    _pushVfxRemaining = ActionVfxSeconds;
                    break;
                case GiftGrabNetworkActionType.Throw:
                    PlaceVfx(throwVfxAnchor, actionPosition);
                    _throwVfxRemaining = ActionVfxSeconds;
                    break;
                case GiftGrabNetworkActionType.Drop:
                case GiftGrabNetworkActionType.BoundsReturn:
                case GiftGrabNetworkActionType.Destroyed:
                    PlaceVfx(dropVfxAnchor, actionPosition);
                    _dropVfxRemaining = ActionVfxSeconds;
                    break;
            }

            if (actionBurstVfxPrefab != null &&
                actionType != GiftGrabNetworkActionType.None)
            {
                OneShotVfxPool.Play(
                    actionBurstVfxPrefab,
                    new Vector3(
                        actionPosition.x,
                        0.62f,
                        actionPosition.y),
                    Quaternion.identity,
                    0.72f);
            }
            string soundKey = null;
            switch (actionType)
            {
                case GiftGrabNetworkActionType.Pickup: soundKey = SoundKeys.MinigameGiftPickup; break;
                case GiftGrabNetworkActionType.Deposit: soundKey = SoundKeys.MinigameGiftDeposit; break;
                case GiftGrabNetworkActionType.Throw: soundKey = SoundKeys.MinigameGiftThrow; break;
                case GiftGrabNetworkActionType.Push:
                case GiftGrabNetworkActionType.ThrownHit: soundKey = SoundKeys.MinigameGiftHit; break;
            }
            if (soundKey != null)
                GameSound.PlayAt(soundKey, new Vector3(actionPosition.x, 0.62f, actionPosition.y));
        }

        private PlayerAvatarVisual GetPlayerVisual(int slot)
        {
            return slot >= 0 && slot < _players.Length
                ? _players[slot]?.Visual
                : null;
        }

        private void EnsureActionVfxSubscription()
        {
            if (_subscribedVfxState == state)
            {
                return;
            }
            UnsubscribeFromActionVfxEvents();
            _subscribedVfxState = state;
            if (_subscribedVfxState != null)
            {
                _subscribedVfxState.ActionPresentationRequested +=
                    HandleActionPresentationRequested;
            }
        }

        private void UnsubscribeFromActionVfxEvents()
        {
            if (_subscribedVfxState != null)
            {
                _subscribedVfxState.ActionPresentationRequested -=
                    HandleActionPresentationRequested;
                _subscribedVfxState = null;
            }
        }

        private static void PlaceVfx(Transform effect, Vector2 position)
        {
            if (effect == null)
            {
                return;
            }
            effect.position = new Vector3(position.x, 0.42f, position.y);
            effect.gameObject.SetActive(true);
        }

        private static float TickVfx(Transform effect, float remaining)
        {
            if (effect == null)
            {
                return 0f;
            }
            remaining = Mathf.Max(0f, remaining - Time.unscaledDeltaTime);
            effect.gameObject.SetActive(remaining > 0f);
            if (remaining > 0f)
            {
                var pulse = 1f + Mathf.Sin(Time.unscaledTime * 30f) * 0.2f;
                effect.localScale = Vector3.one * pulse;
            }
            return remaining;
        }

        private void SetWorldPresentationActive(bool active)
        {
            if (giftHud != null) giftHud.RootCanvas.gameObject.SetActive(active);
            if (_worldVisibilityInitialized && _worldVisible == active)
            {
                return;
            }
            _worldVisibilityInitialized = true;
            _worldVisible = active;
            if (!active)
            {
                _pushVfxRemaining = 0f;
                _throwVfxRemaining = 0f;
                _dropVfxRemaining = 0f;
                SetActionVfxActive(false);
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

        private void SetActionVfxActive(bool active)
        {
            pushVfxAnchor?.gameObject.SetActive(active);
            throwVfxAnchor?.gameObject.SetActive(active);
            dropVfxAnchor?.gameObject.SetActive(active);
        }

        private static void DisableGeneratedHitColliders(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
        }

        private void RestoreGiftPresentations()
        {
            for (var index = 0; index < _gifts.Length; index++)
            {
                _gifts[index]?.RestoreAuthoredPresentation();
            }
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == name)
            {
                return root;
            }
            for (var index = 0; index < root.childCount; index++)
            {
                var result = FindDescendant(root.GetChild(index), name);
                if (result != null)
                {
                    return result;
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

        private sealed class GiftView
        {
            public GiftView(Transform root, Transform visual)
            {
                Root = root;
                Visual = visual;
                Variants = visual.GetComponent<GiftVisualVariants>();
                AuthoredRootLocalScale = root.localScale;
                _authoredVisualParent = visual.parent;
                _authoredVisualLocalPosition = visual.localPosition;
                _authoredVisualLocalRotation = visual.localRotation;
                _authoredVisualLocalScale = visual.localScale;
                _authoredVisualWorldScale = visual.lossyScale;
                _authoredVisualActive = visual.gameObject.activeSelf;
            }

            public Transform Root { get; }
            public Transform Visual { get; }
            public GiftVisualVariants Variants { get; }
            public Vector3 AuthoredRootLocalScale { get; }
            public string LastStateName { get; set; }

            private readonly Transform _authoredVisualParent;
            private readonly Vector3 _authoredVisualLocalPosition;
            private readonly Quaternion _authoredVisualLocalRotation;
            private readonly Vector3 _authoredVisualLocalScale;
            private readonly Vector3 _authoredVisualWorldScale;
            private readonly bool _authoredVisualActive;

            public void RestoreAuthoredPresentation()
            {
                Root.localScale = AuthoredRootLocalScale;
                if (Visual.parent != _authoredVisualParent)
                {
                    Visual.SetParent(_authoredVisualParent, false);
                }
                Visual.localPosition = _authoredVisualLocalPosition;
                Visual.localRotation = _authoredVisualLocalRotation;
                Visual.localScale = _authoredVisualLocalScale;
                Visual.gameObject.SetActive(_authoredVisualActive);
            }

            public void AttachVisualTo(Transform playerRoot)
            {
                if (playerRoot == null)
                {
                    return;
                }

                Root.localScale = AuthoredRootLocalScale;
                if (Visual.parent != playerRoot)
                {
                    Visual.SetParent(playerRoot, false);
                }
                Visual.localPosition = CarriedGiftLocalOffset;
                Visual.localRotation = Quaternion.identity;
                Visual.gameObject.SetActive(true);
                SetWorldScale(Visual, _authoredVisualWorldScale);
            }

            private static void SetWorldScale(
                Transform target,
                Vector3 worldScale)
            {
                var parentScale = target.parent != null
                    ? target.parent.lossyScale
                    : Vector3.one;
                target.localScale = new Vector3(
                    DivideScale(worldScale.x, parentScale.x),
                    DivideScale(worldScale.y, parentScale.y),
                    DivideScale(worldScale.z, parentScale.z));
            }

            private static float DivideScale(float value, float scale)
            {
                return Mathf.Abs(scale) > 0.00001f
                    ? value / scale
                    : value;
            }
        }
    }
}
