using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
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
        public const float SharedCameraOrthographicSize = 11.5f;

        private const float ActionVfxSeconds = 0.38f;
        private const float LabelHeight = 2.9f;

        private static readonly Color[] FallbackPlayerColors =
        {
            new Color(0.16f, 0.48f, 0.95f),
            new Color(0.92f, 0.2f, 0.16f),
            new Color(0.18f, 0.78f, 0.32f),
            new Color(0.7f, 0.26f, 0.9f)
        };

        [SerializeField] private NetworkGiftGrabState state;
        [SerializeField] private CinemachineCamera sharedCamera;
        [SerializeField] private Transform playerRoot;
        [SerializeField] private Transform giftRoot;
        [SerializeField] private Transform[] playerAnchors =
            new Transform[PlayerCount];
        [SerializeField] private Transform[] baseAnchors =
            new Transform[PlayerCount];
        [SerializeField] private Transform[] depositedGiftAnchors =
            new Transform[PlayerCount];
        [SerializeField] private GiftGrabBaseLabel[] playerLabels =
            new GiftGrabBaseLabel[PlayerCount];
        [SerializeField] private GiftGrabBaseLabel[] baseLabels =
            new GiftGrabBaseLabel[PlayerCount];
        [SerializeField] private Transform pushVfxAnchor;
        [SerializeField] private Transform throwVfxAnchor;
        [SerializeField] private Transform dropVfxAnchor;
        [SerializeField] private Transform[] stunVfxAnchors =
            new Transform[PlayerCount];
        [SerializeField] private GameObject arenaPresentation;
        [SerializeField] private AudioSource cueAudioSource;
        [SerializeField] private GiftGrabHudBindings hud;

        private readonly PlayerView[] _players = new PlayerView[PlayerCount];
        private GiftView[] _gifts = System.Array.Empty<GiftView>();
        private GameplayCameraDirector _cameraDirector;
        private bool _cameraConfigured;
        private bool _worldVisible;
        private bool _worldVisibilityInitialized;
        private int _localSlot = -1;
        private ulong _lastActionRevision;
        private float _pushVfxRemaining;
        private float _throwVfxRemaining;
        private float _dropVfxRemaining;

        public NetworkGiftGrabState State => state;
        public GameObject ArenaPresentation => arenaPresentation;
        public Transform PlayerRoot => playerRoot;
        public Transform GiftRoot => giftRoot;
        public Transform[] PlayerAnchors => playerAnchors;
        public Transform[] BaseAnchors => baseAnchors;
        public Transform[] DepositedGiftAnchors => depositedGiftAnchors;
        public GiftGrabBaseLabel[] PlayerLabels => playerLabels;
        public GiftGrabBaseLabel[] BaseLabels => baseLabels;
        public GiftGrabHudBindings Hud => hud;

        public static Vector3 SharedCameraPosition =>
            new Vector3(NetworkGiftGrabState.ArenaCenterX, 26f, 0f);

        public static Quaternion SharedCameraRotation =>
            Quaternion.Euler(90f, 0f, 0f);

        public void Configure(
            NetworkGiftGrabState networkState,
            CinemachineCamera camera,
            Transform runtimePlayers,
            Transform runtimeGifts,
            Transform[] fixedPlayerAnchors,
            Transform[] fixedBaseAnchors,
            Transform[] fixedDepositedGiftAnchors,
            GiftGrabBaseLabel[] fixedPlayerLabels,
            GiftGrabBaseLabel[] fixedBaseLabels,
            Transform pushEffect,
            Transform throwEffect,
            Transform dropEffect,
            Transform[] stunEffects,
            GameObject arena,
            AudioSource audioSource,
            GiftGrabHudBindings hudBindings)
        {
            state = networkState;
            sharedCamera = camera;
            playerRoot = runtimePlayers;
            giftRoot = runtimeGifts;
            playerAnchors = fixedPlayerAnchors;
            baseAnchors = fixedBaseAnchors;
            depositedGiftAnchors = fixedDepositedGiftAnchors;
            playerLabels = fixedPlayerLabels;
            baseLabels = fixedBaseLabels;
            pushVfxAnchor = pushEffect;
            throwVfxAnchor = throwEffect;
            dropVfxAnchor = dropEffect;
            stunVfxAnchors = stunEffects;
            arenaPresentation = arena;
            cueAudioSource = audioSource;
            hud = hudBindings;
            _cameraConfigured = false;
            ConfigureCamera();
            CacheGiftViews();
            if (Application.isPlaying)
            {
                EnsurePlayers();
            }
        }

        private void Awake()
        {
            state ??= GetComponent<NetworkGiftGrabState>();
            ConfigureCamera();
            CacheGiftViews();
            EnsurePlayers();
            SetActionVfxActive(false);
            SetWorldPresentationActive(false);
            SetHudActive(false);
        }

        private void OnDisable()
        {
            SetWorldPresentationActive(false);
            SetHudActive(false);
            UnregisterCamera();
        }

        private void Update()
        {
            state ??= GetComponent<NetworkGiftGrabState>();
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
                             match.FlowState == BoardFlowState.SkippedResult);
            SetHudActive(showWorld &&
                         match.FlowState == BoardFlowState.MinigamePlaying);
            if (!showWorld)
            {
                SetWorldPresentationActive(false);
                return;
            }

            SetWorldPresentationActive(true);
            ResolveLocalSlot(match);
            RefreshPlayers(match);
            RefreshGifts();
            RefreshLabels(match);
            RefreshActionVfx();
            RefreshHud(match);
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
                visual.SetDisplayName("Player " + (slot + 1));
                DisableBuiltInNameplate(root.transform);
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

        private void ResolveLocalSlot(NetworkMatchState match)
        {
            var resolved = -1;
            for (var slot = 0; slot < PlayerCount; slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                if (avatar != null && avatar.IsOwner)
                {
                    resolved = slot;
                    break;
                }
            }
            if (_localSlot == resolved)
            {
                return;
            }

            _localSlot = resolved;
            for (var slot = 0; slot < PlayerCount; slot++)
            {
                _players[slot]?.Visual.SetTopViewHighlight(slot == _localSlot);
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
                if (avatar != null)
                {
                    var appearance = avatar.Appearance;
                    player.Visual.SetBodyColor(appearance.BodyColor);
                    player.Visual.ApplyAppearance(
                        appearance.EyeId,
                        appearance.MouthId,
                        appearance.HatId);
                }
                player.Visual.SetEliminated(false);
            }
        }

        private void RefreshGifts()
        {
            var available = Mathf.Min(
                _gifts.Length,
                Mathf.Max(0, state.GiftSpawnedCount));
            for (var giftId = 0; giftId < _gifts.Length; giftId++)
            {
                var gift = _gifts[giftId];
                if (gift == null)
                {
                    continue;
                }
                if (giftId >= available)
                {
                    gift.Root.gameObject.SetActive(false);
                    continue;
                }

                gift.Root.gameObject.SetActive(true);
                var carrier = state.GetGiftCarrierSlot(giftId);
                var storedOwner = state.GetGiftStoredOwnerSlot(giftId);
                if (storedOwner >= 0 && storedOwner < PlayerCount)
                {
                    // Stored gifts remain on the exact server-authored grid so
                    // the visible steal target and authoritative contact point
                    // can never diverge. Merely lying over a base does not use
                    // this branch and therefore never looks deposited.
                    var position = state.GetGiftPosition(giftId);
                    gift.Root.position = new Vector3(
                        position.x,
                        GiftPresentationHeight,
                        position.y);
                    gift.Root.rotation = Quaternion.Euler(
                        0f,
                        giftId * 29f,
                        0f);
                }
                else if (carrier >= 0 && carrier < PlayerCount &&
                         _players[carrier] != null)
                {
                    var facing = state.GetPlayerFacing(carrier);
                    if (facing.sqrMagnitude < 0.001f)
                    {
                        facing = Vector2.up;
                    }
                    facing.Normalize();
                    gift.Root.position = _players[carrier].Root.position +
                        new Vector3(facing.x * 0.65f, 1.25f,
                            facing.y * 0.65f);
                    gift.Root.rotation = Quaternion.Euler(0f,
                        Time.unscaledTime * 90f + giftId * 17f, 0f);
                }
                else
                {
                    var position = state.GetGiftPosition(giftId);
                    gift.Root.position = new Vector3(
                        position.x,
                        GiftPresentationHeight,
                        position.y);
                    gift.Root.rotation = Quaternion.Euler(
                        0f,
                        Time.unscaledTime * 45f + giftId * 23f,
                        0f);
                }

                // The authoritative enum remains useful to artists/debuggers
                // even though carrier/owner are the placement source of truth.
                gift.LastStateName = state.GetGiftState(giftId).ToString();
            }
        }

        private void RefreshLabels(NetworkMatchState match)
        {
            var outputCamera = Camera.main;
            for (var slot = 0; slot < PlayerCount; slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                var playerName = avatar != null
                    ? avatar.DisplayName
                    : "Player " + (slot + 1);
                var color = avatar != null
                    ? avatar.Appearance.BodyColor
                    : FallbackPlayerColors[slot];

                if (playerLabels != null && slot < playerLabels.Length &&
                    playerLabels[slot] != null && _players[slot] != null)
                {
                    var label = playerLabels[slot];
                    label.transform.position = _players[slot].Root.position +
                                               Vector3.up * LabelHeight;
                    var carried = state.GetCarriedGiftId(slot);
                    var stun = state.GetPlayerStunRemaining(slot);
                    label.SetContent(
                        playerName,
                        stun > 0d ? "STUN " + stun.ToString("0.0") + "s" :
                        carried >= 0 ? "CARRYING GIFT" : "",
                        slot == _localSlot,
                        color);
                    label.FaceCamera(outputCamera);
                }

                if (baseLabels != null && slot < baseLabels.Length &&
                    baseLabels[slot] != null)
                {
                    var label = baseLabels[slot];
                    label.SetContent(
                        playerName + " BASE",
                        state.GetStoredGiftCount(slot) + " GIFTS",
                        slot == _localSlot,
                        color);
                    label.FaceCamera(outputCamera);
                }
            }
        }

        private void RefreshActionVfx()
        {
            if (_lastActionRevision != state.ActionRevision)
            {
                _lastActionRevision = state.ActionRevision;
                TriggerActionVfx();
            }

            _pushVfxRemaining = TickVfx(pushVfxAnchor, _pushVfxRemaining);
            _throwVfxRemaining = TickVfx(throwVfxAnchor, _throwVfxRemaining);
            _dropVfxRemaining = TickVfx(dropVfxAnchor, _dropVfxRemaining);

            for (var slot = 0; slot < PlayerCount; slot++)
            {
                if (stunVfxAnchors == null ||
                    slot >= stunVfxAnchors.Length ||
                    stunVfxAnchors[slot] == null)
                {
                    continue;
                }
                var active = state.GetPlayerStunRemaining(slot) > 0d;
                stunVfxAnchors[slot].gameObject.SetActive(active);
                if (active && _players[slot] != null)
                {
                    stunVfxAnchors[slot].position =
                        _players[slot].Root.position + Vector3.up * 0.04f;
                    stunVfxAnchors[slot].Rotate(
                        Vector3.up,
                        150f * Time.unscaledDeltaTime,
                        Space.World);
                }
            }
        }

        private void TriggerActionVfx()
        {
            var actor = state.LastActionActorSlot;
            var target = state.LastActionTargetSlot;
            var giftId = state.LastActionGiftId;
            var actorPosition = actor >= 0 && actor < PlayerCount
                ? state.GetPlayerPosition(actor)
                : new Vector2(NetworkGiftGrabState.ArenaCenterX, 0f);
            var targetPosition = target >= 0 && target < PlayerCount
                ? state.GetPlayerPosition(target)
                : actorPosition;

            switch (state.LastActionType)
            {
                case GiftGrabNetworkActionType.Push:
                case GiftGrabNetworkActionType.ThrownHit:
                    PlaceVfx(pushVfxAnchor, targetPosition);
                    _pushVfxRemaining = ActionVfxSeconds;
                    break;
                case GiftGrabNetworkActionType.Throw:
                    PlaceVfx(throwVfxAnchor, actorPosition);
                    _throwVfxRemaining = ActionVfxSeconds;
                    break;
                case GiftGrabNetworkActionType.Drop:
                case GiftGrabNetworkActionType.BoundsReturn:
                    var dropPosition = giftId >= 0 &&
                                       giftId < state.GiftSpawnedCount
                        ? state.GetGiftPosition(giftId)
                        : targetPosition;
                    PlaceVfx(dropVfxAnchor, dropPosition);
                    _dropVfxRemaining = ActionVfxSeconds;
                    break;
            }
            cueAudioSource?.Play();
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

        private void RefreshHud(NetworkMatchState match)
        {
            if (hud == null || !hud.HasRequiredReferences)
            {
                return;
            }

            hud.PhaseText.text = GetPhaseLabel(state.Phase);
            hud.TimerText.text =
                MinigameDisplayFormatter.FormatClock(state.RemainingSeconds);
            hud.RoundText.text =
                "ROUND " + state.RoundNumber + " / " +
                MinigameCatalog.GetRoundCount(ScheduledMinigameId.GiftGrab);
            hud.InstructionText.text = GetLocalInstruction();
            hud.LocalStatusText.text = GetLocalStatus();
            hud.NeutralGiftText.text =
                "LOOSE GIFTS  " + CountLooseGifts();
            hud.PausePanel.SetActive(state.IsPaused);
            hud.ControlsPanel.SetActive(
                state.Phase == NetworkGiftGrabPhase.Countdown ||
                state.Phase == NetworkGiftGrabPhase.Running);
            var showResult =
                state.Phase == NetworkGiftGrabPhase.RoundResult ||
                state.Phase == NetworkGiftGrabPhase.Complete;
            hud.ResultPanel.SetActive(showResult);
            if (showResult)
            {
                hud.ResultText.text = GetResultLabel();
            }

            for (var slot = 0; slot < PlayerCount; slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                var playerName = avatar != null
                    ? avatar.DisplayName
                    : "PLAYER " + (slot + 1);
                hud.PlayerRows[slot].text =
                    playerName.ToUpperInvariant() + "  ·  " +
                    state.GetStoredGiftCount(slot) + " STORED" +
                    (state.GetCarriedGiftId(slot) >= 0 ? "  ·  CARRY" : "") +
                    (state.GetPlayerStunRemaining(slot) > 0d ? "  ·  STUN" : "");
                hud.PlayerRows[slot].color = slot == _localSlot
                    ? hud.LocalPlayerRowColor
                    : hud.GetDefaultPlayerRowColor(slot);
            }
        }

        private int CountLooseGifts()
        {
            var count = 0;
            for (var giftId = 0;
                 giftId < state.GiftSpawnedCount;
                 giftId++)
            {
                if (state.GetGiftState(giftId) == GiftGrabGiftState.Loose)
                {
                    count++;
                }
            }
            return count;
        }

        private string GetLocalInstruction()
        {
            if (_localSlot < 0)
            {
                return "WASD MOVE · LEFT CLICK ACTION";
            }
            if (state.GetPlayerStunRemaining(_localSlot) > 0d)
            {
                return "STUNNED · YOUR GIFT WAS DROPPED";
            }
            if (state.GetActionCooldownRemainingSeconds(_localSlot) > 0d)
            {
                return "ACTION RECOVERING · KEEP MOVING";
            }
            if (state.GetCarriedGiftId(_localSlot) >= 0)
            {
                return "RETURN TO YOUR BASE · LEFT CLICK THROWS";
            }
            return "GRAB LOOSE GIFTS · STEAL FROM RIVAL BASES · PUSH WITH CLICK";
        }

        private string GetLocalStatus()
        {
            if (_localSlot < 0)
            {
                return "SPECTATING";
            }
            var carried = state.GetCarriedGiftId(_localSlot);
            var stun = state.GetPlayerStunRemaining(_localSlot);
            var cooldown = state.GetActionCooldownRemainingSeconds(_localSlot);
            return "YOU  ·  " + state.GetStoredGiftCount(_localSlot) +
                   " STORED  ·  " +
                   (carried >= 0 ? "CARRYING #" + (carried + 1) : "HANDS FREE") +
                   "  ·  STUN " + stun.ToString("0.0") + "s" +
                   "  ·  ACTION " + cooldown.ToString("0.0") + "s";
        }

        private string GetResultLabel()
        {
            if (_localSlot < 0)
            {
                return state.Phase == NetworkGiftGrabPhase.Complete
                    ? "MATCH COMPLETE"
                    : "ROUND COMPLETE";
            }
            if (state.Phase == NetworkGiftGrabPhase.Complete)
            {
                return "MATCH " +
                       MinigameDisplayFormatter.ToOrdinal(
                           state.GetFinalRank(_localSlot)) +
                       "\n" + state.GetScore(_localSlot) + " POINTS · " +
                       state.GetTotalStoredGiftCount(_localSlot) + " GIFTS";
            }
            return "ROUND " +
                   MinigameDisplayFormatter.ToOrdinal(
                       state.GetRoundRank(_localSlot)) +
                   "\n+" + state.GetRoundPoints(_localSlot) + " POINTS · " +
                   state.GetStoredGiftCount(_localSlot) + " GIFTS";
        }

        private static string GetPhaseLabel(NetworkGiftGrabPhase phase)
        {
            switch (phase)
            {
                case NetworkGiftGrabPhase.Countdown:
                    return "GIFT GRAB · GET READY";
                case NetworkGiftGrabPhase.Running:
                    return "GIFT GRAB · COLLECT";
                case NetworkGiftGrabPhase.RoundResult:
                    return "GIFT GRAB · ROUND RESULT";
                case NetworkGiftGrabPhase.Complete:
                    return "GIFT GRAB · MATCH RESULT";
                default:
                    return "GIFT GRAB";
            }
        }

        private void SetWorldPresentationActive(bool active)
        {
            if (_worldVisibilityInitialized && _worldVisible == active)
            {
                return;
            }
            _worldVisibilityInitialized = true;
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
            if (hud != null && hud.gameObject.activeSelf != active)
            {
                hud.gameObject.SetActive(active);
            }
        }

        private void SetActionVfxActive(bool active)
        {
            pushVfxAnchor?.gameObject.SetActive(active);
            throwVfxAnchor?.gameObject.SetActive(active);
            dropVfxAnchor?.gameObject.SetActive(active);
            if (stunVfxAnchors == null)
            {
                return;
            }
            foreach (var effect in stunVfxAnchors)
            {
                effect?.gameObject.SetActive(active);
            }
        }

        private static void DisableGeneratedHitColliders(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
        }

        private static void DisableBuiltInNameplate(Transform root)
        {
            var nameplate = FindDescendant(root, "NameplateAnchor");
            if (nameplate != null)
            {
                nameplate.gameObject.SetActive(false);
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
            }

            public Transform Root { get; }
            public Transform Visual { get; }
            public string LastStateName { get; set; }
        }
    }
}
