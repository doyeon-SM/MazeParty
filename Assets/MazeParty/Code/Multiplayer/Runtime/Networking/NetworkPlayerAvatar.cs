using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.ArenaCombat;
using MazeParty.Gameplay.Minigames.Race;
using MazeParty.Gameplay.Minigames.SequenceMemory;
using MazeParty.Gameplay.Minigames.WrongWay;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(CharacterController))]
    public sealed partial class NetworkPlayerAvatar : NetworkBehaviour, IDamageable, IPushReceiver
    {
        private const float BoardStartRecoveryHeight = 1f;
        private const float BoardStartAnchorHeightTolerance = 0.05f;
        private const float ControllerFootprintPadding = 0.02f;

        [SerializeField, Min(0.1f)] private float moveSpeed = 5f;
        [SerializeField, Min(0.01f)] private float lookSensitivity = 0.12f;
        [SerializeField] private Transform eyePivot;

        private readonly NetworkVariable<int> _slot = new NetworkVariable<int>(
            -1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _privateRoll = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _remainingMoves = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<Vector2Int> _turnRouteOrigin =
            new NetworkVariable<Vector2Int>(
                default,
                NetworkVariableReadPermission.Owner,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _hasTurnRouteOrigin =
            new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Owner,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _turnRouteStepOffset =
            new NetworkVariable<int>(
                0,
                NetworkVariableReadPermission.Owner,
                NetworkVariableWritePermission.Server);
        private readonly NetworkList<BoardRouteChoice> _boardRouteChoices =
            new NetworkList<BoardRouteChoice>(
                default,
                NetworkVariableReadPermission.Owner,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<byte> _choiceResolution = new NetworkVariable<byte>(
            (byte)ItemChoiceResolution.NotStarted,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _selectedItemSlot = new NetworkVariable<int>(
            -1,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<byte> _occupiedItemMask = new NetworkVariable<byte>(
            0,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<byte> _itemSlot0 = CreatePrivateItemSlot();
        private readonly NetworkVariable<byte> _itemSlot1 = CreatePrivateItemSlot();
        private readonly NetworkVariable<byte> _itemSlot2 = CreatePrivateItemSlot();
        private readonly NetworkVariable<bool> _boardReady = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _hasLogicalTile = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<Vector2Int> _logicalTileCoordinate =
            new NetworkVariable<Vector2Int>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _maxHealth = new NetworkVariable<int>(
            PlayerStatRules.DefaultMaxHealth,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _currentHealth = new NetworkVariable<int>(
            PlayerStatRules.DefaultMaxHealth,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _keyCount = new NetworkVariable<int>(
            PlayerStatRules.DefaultStartingKeys,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _gold = new NetworkVariable<int>(
            PlayerStatRules.DefaultStartingGold,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _minigameWins = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<byte> _actionState = new NetworkVariable<byte>(
            (byte)PlayerBoardActionState.Hidden,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _isQuietWalking = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _isCrouching = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<PlayerAppearanceState> _appearance =
            new NetworkVariable<PlayerAppearanceState>(
                PlayerAppearanceState.Default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> _displayName =
            new NetworkVariable<FixedString64Bytes>(
                new FixedString64Bytes("Player"),
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<byte> _combatState = new NetworkVariable<byte>(
            (byte)NetworkCombatState.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _combatHealth = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _pendingCombatProtection = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<double> _personalProtectionEndsAt =
            new NetworkVariable<double>(
                0d,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<double> _pausedPersonalProtectionRemaining =
            new NetworkVariable<double>(
                0d,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private double _boardDeathEndsAt;
        private double _pausedBoardDeathRemaining;

        private CharacterController _characterController;
        private FootstepAudioEmitter _footstepEmitter;
        private bool _knockOutSoundPresented;
        private BoardTopology _topology;
        private PlayerBoardBoundaryWalls _boundaryWalls;
        private PlayerAvatarVisual _avatarVisual;
        private bool _nextPunchUsesRightHand = true;
        private readonly BoardTraversalState _traversal = new BoardTraversalState();
        private readonly FootstepCadenceTracker _footstepCadence =
            new FootstepCadenceTracker();
        private Vector2 _serverInput;
        private Vector2 _lastSentInput;
        private Vector2 _lastSentMinefieldInput;
        private Vector2 _lastSentRedLightGreenLightInput;
        private Vector2 _lastSentStableFootingInput;
        private Vector2 _lastSentGiftGrabInput;
        private Vector2 _lastSentTagChaseInput;
        private Vector2 _lastSentTerritoryPaintInput;
        private Vector2 _lastSentBombPassingInput;
        private int _lastSentBombPassingRound = -1;
        private uint _lastSentBombPassingInputEpoch;
        private Vector2 _lastSentCliffBarrageInput;
        private int _lastSentCliffBarrageRound = -1;
        private uint _lastSentCliffBarrageInputEpoch;
        private Vector2 _lastSentSnowySpinInput;
        private int _lastSentSnowySpinRound = -1;
        private uint _lastSentSnowySpinInputEpoch;
        private float _lastSentBouncingShieldAxis;
        private int _lastSentBouncingShieldRound = -1;
        private uint _lastSentBouncingShieldInputEpoch;
        private bool _lastSentBalloonBlowHeld;
        private int _lastSentBalloonBlowRound = -1;
        private uint _lastSentBalloonBlowInputEpoch;
        private int _lastSentGiftGrabRound = -1;
        private uint _lastSentGiftGrabInputEpoch;
        private int _lastSentTagChaseRound = -1;
        private uint _lastSentTagChaseInputEpoch;
        private float _lastSentTagChaseYaw;
        private int _lastSentTerritoryPaintRound = -1;
        private uint _lastSentTerritoryPaintInputEpoch;
        private bool _serverQuietWalkHeld;
        private bool _lastSentQuietWalkHeld;
        private float _serverYaw;
        private float _serverPitch;
        private float _lastSentYaw;
        private float _lastSentPitch;
        private float _localYaw;
        private float _localPitch;
        private float _nextInputRefresh;
        private float _nextMinefieldInputRefresh;
        private float _nextRedLightGreenLightInputRefresh;
        private float _nextStableFootingInputRefresh;
        private float _nextGiftGrabInputRefresh;
        private float _nextTagChaseInputRefresh;
        private float _nextTerritoryPaintInputRefresh;
        private float _nextBombPassingInputRefresh;
        private float _nextCliffBarrageInputRefresh;
        private float _nextSnowySpinInputRefresh;
        private float _nextBalloonBlowInputRefresh;
        private float _nextBouncingShieldInputRefresh;
        private float _nextSlotResolveAttempt;
        private double _nextLocalPrimaryRepeatAt;
        private double _nextLobbyPunchAllowedAt;
        private bool _boardReadyRequestSent;
        private bool _boardPositionInitialized;
        private bool _lobbyPositionInitialized;
        private bool _restoredFromSnapshot;
        private int _configuredBoundarySlot = -1;
        private BoardTopology _configuredBoundaryTopology;
        private BoardTile _displayedBoundaryTile;
        private int _displayedBoundaryMoves = int.MinValue;
        private bool _boundaryWallsActive;
        private Vector3 _combatKnockbackVelocity;
        private float _verticalVelocity;
        private readonly Collider[] _standingClearanceHits = new Collider[16];
        private NetworkWorldDie _cachedLocalWorldDie;
        private int _cachedLocalWorldDieSlot = -1;
        private float _nextLocalWorldDieResolveAt;

        public float LocalLookYaw => _localYaw;
        public float LocalLookPitch => _localPitch;
        public int AssignedSlot => _slot.Value;
        public int LocalVisibleRoll
        {
            get
            {
                if (!IsOwner)
                {
                    return 0;
                }

                if (UsesDoubleDice) return _privateRoll.Value;
                var slot = _slot.Value;
                var die = ResolveLocalWorldDie(slot);
                var match = NetworkMatchState.Instance;
                return WorldDieHudPresentationPolicy.ResolveVisibleRoll(
                    true,
                    _privateRoll.Value,
                    slot,
                    die != null && die.IsSpawned,
                    die != null ? die.AssignedSlot : -1,
                    die != null ? die.Phase : WorldDiePhase.Hidden,
                    die != null ? die.PublicFace : 0,
                    match != null &&
                    match.FlowState != BoardFlowState.Action);
            }
        }
        public WorldDiePhase LocalWorldDiePhase
        {
            get
            {
                if (!IsOwner)
                {
                    return WorldDiePhase.Hidden;
                }

                var die = ResolveLocalWorldDie(_slot.Value);
                return die != null && die.IsSpawned
                    ? die.Phase
                    : WorldDiePhase.Hidden;
            }
        }
        public int LocalWorldDiePublicFace
        {
            get
            {
                if (!IsOwner)
                {
                    return 0;
                }

                var die = ResolveLocalWorldDie(_slot.Value);
                return die != null && die.IsSpawned
                    ? die.PublicFace
                    : 0;
            }
        }
        public int LocalRemainingMoves => IsOwner ? _remainingMoves.Value : 0;
        public int LocalTurnRoll => IsOwner ? _privateRoll.Value : 0;
        public Vector2Int LocalTurnRouteOrigin => IsOwner
            ? _turnRouteOrigin.Value
            : default;
        public bool HasLocalTurnRouteOrigin =>
            IsOwner && _hasTurnRouteOrigin.Value;
        public int LocalTurnRouteStepOffset => IsOwner
            ? _turnRouteStepOffset.Value
            : 0;
        public NetworkList<BoardRouteChoice> LocalBoardRouteChoices =>
            _boardRouteChoices;
        public int LocalSelectedItemSlot => IsOwner ? _selectedItemSlot.Value : -1;
        public byte LocalOccupiedItemMask => IsOwner ? _occupiedItemMask.Value : (byte)0;
        public ItemChoiceResolution LocalChoiceResolution => IsOwner
            ? (ItemChoiceResolution)_choiceResolution.Value
            : ItemChoiceResolution.NotStarted;
        public bool HasResolvedItemChoice =>
            (ItemChoiceResolution)_choiceResolution.Value != ItemChoiceResolution.Pending &&
            (ItemChoiceResolution)_choiceResolution.Value != ItemChoiceResolution.NotStarted;
        public bool HasRolled => _privateRoll.Value > 0;
        public bool IsBoardReady => _boardReady.Value;
        public Transform EyePivot => eyePivot;
        public bool HasLogicalBoardTile => _hasLogicalTile.Value;
        public Vector2Int LogicalBoardTileCoordinate => _logicalTileCoordinate.Value;
        public int MaxHealth => _maxHealth.Value;
        public int CurrentHealth => _currentHealth.Value;
        public int KeyCount => _keyCount.Value;
        public int Gold => _gold.Value;
        public int MinigameWins => _minigameWins.Value;
        public PlayerBoardActionState ActionState =>
            (PlayerBoardActionState)_actionState.Value;
        public bool IsQuietWalking => _isQuietWalking.Value;
        public bool IsCrouching => _isCrouching.Value;
        public PlayerAppearanceState Appearance => _appearance.Value;
        public string DisplayName => _displayName.Value.ToString();
        public PlayerAvatarVisual AvatarVisual => _avatarVisual;
        public NetworkCombatState CombatState => (NetworkCombatState)_combatState.Value;
        public int CombatHealth => _combatHealth.Value;
        public bool IsCombatParticipant => CombatState != NetworkCombatState.None;
        public bool IsCombatAlive => CombatState == NetworkCombatState.Active;
        public double PersonalItemProtectionRemaining
        {
            get
            {
                if (_pausedPersonalProtectionRemaining.Value > 0d)
                {
                    return _pausedPersonalProtectionRemaining.Value;
                }

                var now = NetworkManager != null && NetworkManager.IsListening
                    ? NetworkManager.ServerTime.Time
                    : Time.unscaledTimeAsDouble;
                return _personalProtectionEndsAt.Value > 0d
                    ? Math.Max(0d, _personalProtectionEndsAt.Value - now)
                    : 0d;
            }
        }
        public BoardTile CurrentBoardTileOnServer =>
            IsServer && _traversal.IsInitialized ? _traversal.CurrentTile : null;
        public bool IsBoardDeathInProgressOnServer =>
            IsServer && (_boardDeathEndsAt > 0d || _pausedBoardDeathRemaining > 0d);

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _footstepEmitter = GetComponent<FootstepAudioEmitter>();
            if (_footstepEmitter == null)
            {
                _footstepEmitter = gameObject.AddComponent<FootstepAudioEmitter>();
            }
            _boundaryWalls = GetComponent<PlayerBoardBoundaryWalls>();
            if (_boundaryWalls == null)
            {
                _boundaryWalls = gameObject.AddComponent<PlayerBoardBoundaryWalls>();
            }
            EnsureEyePivot();
            _avatarVisual = GetComponent<PlayerAvatarVisual>();
            if (_avatarVisual == null)
            {
                _avatarVisual = gameObject.AddComponent<PlayerAvatarVisual>();
            }
            _avatarVisual.EnsureBuilt();
            _avatarVisual.ConfigureEyePivot(eyePivot);
        }

        public override void OnNetworkSpawn()
        {
            _slot.OnValueChanged += OnSlotChanged;
            _combatState.OnValueChanged += OnCombatStateChanged;
            _currentHealth.OnValueChanged += OnBoardHealthChanged;
            _isCrouching.OnValueChanged += OnCrouchingChanged;
            _appearance.OnValueChanged += OnAppearanceChanged;
            _displayName.OnValueChanged += OnDisplayNameChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplySlotVisual(_slot.Value);
            ApplyAppearance(_appearance.Value);
            ApplyDisplayName(_displayName.Value);
            ApplyCrouchPresentation(_isCrouching.Value);
            ApplyCombatColliderState(CombatState);
            ApplyBoardDeathPresentation();
            _localYaw = transform.eulerAngles.y;
            _serverYaw = _localYaw;
            _serverPitch = 0f;

            if (IsServer)
            {
                TryAssignAuthoritativeSlotOnServer();
            }

            if (IsOwner && IsBoardLoaded())
            {
                _boardReadyRequestSent = false;
            }

            if (IsOwner)
            {
                var controller = OnlineSessionController.Instance;
                RequestLocalAppearance(controller != null
                    ? controller.LocalAppearance
                    : PlayerProfilePreferences.Load().Appearance);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkMatchState.Instance != null &&
                NetworkMatchState.Instance.GameplayEnabled)
            {
                NetworkMatchState.Instance.CaptureDisconnectedAvatarOnServer(this);
            }

            _slot.OnValueChanged -= OnSlotChanged;
            _combatState.OnValueChanged -= OnCombatStateChanged;
            _currentHealth.OnValueChanged -= OnBoardHealthChanged;
            _isCrouching.OnValueChanged -= OnCrouchingChanged;
            _appearance.OnValueChanged -= OnAppearanceChanged;
            _displayName.OnValueChanged -= OnDisplayNameChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            DisposeBoardItems();
            ClearLocalWorldDieCache();
            _traversal.Dispose();
        }

        public override void OnDestroy()
        {
            _traversal.Dispose();
            base.OnDestroy();
        }

        private void Update()
        {
            if (!IsSpawned)
            {
                return;
            }

            TickBoardItems();
            TickHandGestures();
            if (IsServer && _slot.Value < 0 && Time.unscaledTime >= _nextSlotResolveAttempt)
            {
                _nextSlotResolveAttempt = Time.unscaledTime + 0.25f;
                TryAssignAuthoritativeSlotOnServer();
            }

            if (!IsOwner)
            {
                return;
            }

            if (IsBoardLoaded() && !_boardReady.Value && !_boardReadyRequestSent)
            {
                _boardReadyRequestSent = true;
                NotifyBoardReadyRpc();
            }

            HandleLocalLook();
            var handlingMinigame = SubmitLocalCliffBarrageInput();
            if (!handlingMinigame)
            {
                handlingMinigame = SubmitLocalSnowySpinInput();
            }
            if (!handlingMinigame)
            {
                handlingMinigame = SubmitLocalBombPassingInput();
            }
            if (!handlingMinigame)
            {
                handlingMinigame = SubmitLocalBouncingShieldInput();
            }
            if (!handlingMinigame)
            {
                handlingMinigame = SubmitLocalSequenceMemoryInput();
            }
            if (!handlingMinigame)
            {
                handlingMinigame = SubmitLocalRaceInput();
            }
            if (!handlingMinigame)
            {
                handlingMinigame = SubmitLocalTagChaseInput();
            }
            if (!handlingMinigame)
            {
                handlingMinigame =
                    SubmitLocalTerritoryPaintInput();
            }
            if (!handlingMinigame)
            {
                handlingMinigame = SubmitLocalGiftGrabInput();
            }
            if (!handlingMinigame)
            {
                handlingMinigame = SubmitLocalBalloonBlowInput();
            }
            if (!handlingMinigame)
            {
                handlingMinigame = SubmitLocalStableFootingMovement();
            }
            if (!handlingMinigame)
            {
                handlingMinigame = SubmitLocalRedLightGreenLightMovement();
            }
            if (!handlingMinigame)
            {
                handlingMinigame = SubmitLocalWrongWayDirection();
            }
            if (!handlingMinigame)
            {
                handlingMinigame = SubmitLocalMinefieldMovement();
            }
            // Submit the latest first-person aim before a punch so the
            // server validates that punch against this frame's yaw/pitch.
            var arenaCombatInput = !handlingMinigame &&
                NetworkMatchState.Instance != null &&
                NetworkMatchState.Instance.IsArenaCombatPlaying;
            if (arenaCombatInput)
            {
                SubmitLocalMovement();
            }
            HandleLocalActionButtons();
            if (!handlingMinigame && !arenaCombatInput)
            {
                SubmitLocalMovement();
            }
            RefreshLocalBoundaryPresentation();
        }

        private void LateUpdate()
        {
            if (IsSpawned && IsOwner)
            {
                // The server-authoritative body yaw can arrive after input was
                // sampled. Rebuild the owner-only eye offset every frame so the
                // view keeps the yaw/pitch chosen by the player instead of
                // snapping back toward the replicated body rotation.
                ApplyLocalEyeRotation();
            }
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            if (BoardBoundaryActivationRules.IsLobbyPhase(
                    GetCurrentSessionPhase()))
            {
                HideBoundaryWalls();
                if (_slot.Value >= 0 && CanUseLobbyInput())
                {
                    SimulateLobbyMovementOnServer();
                }
                return;
            }

            if (_slot.Value < 0)
            {
                return;
            }

            if (CanUseLobbyInput())
            {
                HideBoundaryWalls();
                SimulateLobbyMovementOnServer();
                return;
            }

            if (!_boardReady.Value)
            {
                return;
            }

            var match = NetworkMatchState.Instance;
            var canMoveInAction = match != null && match.CanAcceptActionInput &&
                                  HasResolvedItemChoice && !IsSwapping && !IsBoardDeathInProgressOnServer;
            var canMoveInCombat = match != null && match.CanAvatarUseCombatInput(this);
            var canMoveInArenaCombat = canMoveInCombat && match.IsArenaCombatPlaying;
            if (!canMoveInAction && !canMoveInCombat)
            {
                StopServerInputOnServer();
                _combatKnockbackVelocity = Vector3.zero;
                if (match != null &&
                    !match.IsGlobalSimulationPaused &&
                    IsBoardLoaded())
                {
                    MoveControllerWithGrounding(
                        Vector3.zero,
                        Time.fixedDeltaTime);
                }
                return;
            }

            if (!canMoveInArenaCombat)
            {
                EnsureTraversalInitialized();
            }
            if (!canMoveInArenaCombat && !_traversal.IsInitialized)
            {
                StopServerInputOnServer();
                MoveControllerWithGrounding(
                    Vector3.zero,
                    Time.fixedDeltaTime);
                return;
            }

            UpdateCrouchStateOnServer(_serverQuietWalkHeld);
            transform.rotation = Quaternion.Euler(0f, _serverYaw, 0f);
            if (eyePivot != null)
            {
                eyePivot.localRotation = Quaternion.Euler(_serverPitch, 0f, 0f);
            }
            var localMovement = new Vector3(_serverInput.x, 0f, _serverInput.y);
            var worldMovement = transform.TransformDirection(localMovement);
            worldMovement.y = 0f;
            if (worldMovement.sqrMagnitude > 1f)
            {
                worldMovement.Normalize();
            }

            var hasMovementIntent = FootstepRules.HasMovementIntent(_serverInput);
            var quietWalking = hasMovementIntent && _isCrouching.Value;
            if (_isQuietWalking.Value != quietWalking)
            {
                _isQuietWalking.Value = quietWalking;
            }

            var velocity = worldMovement *
                           (moveSpeed * FootstepRules.SpeedMultiplier(quietWalking));
            if (_combatKnockbackVelocity.sqrMagnitude > 0.0001f)
            {
                velocity += _combatKnockbackVelocity;
                _combatKnockbackVelocity = Vector3.MoveTowards(
                    _combatKnockbackVelocity,
                    Vector3.zero,
                    8f * Time.fixedDeltaTime);
            }

            var previousPosition = transform.position;
            MoveControllerWithGrounding(velocity, Time.fixedDeltaTime);
            RecordNetworkFootsteps(
                previousPosition,
                transform.position,
                hasMovementIntent,
                quietWalking);

            if (canMoveInArenaCombat)
            {
                ClampControllerInsideArenaCombat();
            }
            else if (canMoveInCombat)
            {
                ClampControllerInsideTile(_traversal.CurrentTile);
            }
            else
            {
                ResolveBoardTraversalAfterMovement();
            }
        }

        public void ChooseItem(int slotIndex)
        {
            if (IsOwner)
            {
                ResolveItemChoiceRpc(slotIndex, false);
            }
        }

        public void ChooseNoItem()
        {
            if (IsOwner)
            {
                ResolveItemChoiceRpc(-1, true);
            }
        }

        public void SetMinigameReady()
        {
            if (IsOwner)
            {
                SetMinigameReadyRpc();
            }
        }

        public bool IsLocalItemOccupied(int slotIndex)
        {
            return IsOwner && slotIndex >= 0 && slotIndex < GameplayInventory.Capacity &&
                   (_occupiedItemMask.Value & (1 << slotIndex)) != 0;
        }

        public string GetLocalItemName(int slotIndex)
        {
            return IsLocalItemOccupied(slotIndex)
                ? PrototypeItemCatalog.Get(GetLocalItemId(slotIndex)).DisplayName
                : GameText.N("EMPTY");
        }

        public string GetLocalItemDescription(int slotIndex)
        {
            return IsLocalItemOccupied(slotIndex)
                ? PrototypeItemCatalog.Get(GetLocalItemId(slotIndex)).Description
                : GameText.N("This slot is empty.");
        }

        public PrototypeItemId GetLocalItemId(int slotIndex)
        {
            return IsLocalItemOccupied(slotIndex)
                ? GetItemSlotValue(slotIndex)
                : PrototypeItemId.None;
        }

        public PrototypeItemId GetSelectedItemOnServer()
        {
            if (!IsServer)
            {
                return PrototypeItemId.None;
            }

            var selected = _selectedItemSlot.Value;
            return selected >= 0 && selected < GameplayInventory.Capacity &&
                   (_occupiedItemMask.Value & (1 << selected)) != 0
                ? GetItemSlotValue(selected)
                : PrototypeItemId.None;
        }

        public void RequestLocalAppearance(PlayerAppearanceState appearance)
        {
            if (!IsOwner || !IsSpawned)
            {
                return;
            }

            var sanitized = appearance.Sanitized();
            SubmitAppearanceRpc(sanitized);
        }

        public void PresentPunchOnServer()
        {
            if (IsServer)
            {
                var useRightHand = _nextPunchUsesRightHand;
                _nextPunchUsesRightHand = !_nextPunchUsesRightHand;
                PresentPunchRpc(useRightHand);
            }
        }

        public void PresentHitReactionOnServer(PlayerHitRegion region)
        {
            if (IsServer)
            {
                PresentHitRpc((byte)region);
            }
        }

        public void PresentItemUseOnServer(PrototypeItemId itemId)
        {
            if (IsServer && PrototypeItemCatalog.IsValid(itemId))
            {
                PresentItemUseRpc((byte)itemId);
            }
        }

        public bool HasFreeItemSlot =>
            IsOwner && (_occupiedItemMask.Value & 0b0000_0111) != 0b0000_0111;

        /// <summary>Items the owner holds (owner and server only; 0 elsewhere).</summary>
        public int OccupiedItemCount
        {
            get
            {
                if (!IsOwner && !IsServer)
                {
                    return 0;
                }

                var mask = _occupiedItemMask.Value & 0b0000_0111;
                return (mask & 1) + ((mask >> 1) & 1) + ((mask >> 2) & 1);
            }
        }
        public bool HasFreeItemSlotOnServer =>
            IsServer && (_occupiedItemMask.Value & 0b0000_0111) != 0b0000_0111;

        public void PurchaseItemFromShop(int shopIndex, int offerIndex, int expectedRevision)
        {
            if (IsOwner)
            {
                PurchaseItemFromShopRpc(shopIndex, offerIndex, expectedRevision);
            }
        }

        public void ResetMatchStatsOnServer()
        {
            ClearUtilityEffectsOnServer();
            if (!IsServer)
            {
                return;
            }

            _maxHealth.Value = PlayerStatRules.DefaultMaxHealth;
            _currentHealth.Value = PlayerStatRules.DefaultMaxHealth;
            _keyCount.Value = PlayerStatRules.DefaultStartingKeys;
            _gold.Value = PlayerStatRules.DefaultStartingGold;
            _minigameWins.Value = 0;
            _matchAwardProgress.Reset(_gold.Value);
            _occupiedItemMask.Value = 0;
            SetItemSlotValue(0, PrototypeItemId.None);
            SetItemSlotValue(1, PrototypeItemId.None);
            SetItemSlotValue(2, PrototypeItemId.None);
            _selectedItemSlot.Value = -1;
            _equippedItem.Value = 0;
            _itemCharges.Value = 0;
            _actionState.Value = (byte)PlayerBoardActionState.Hidden;
            _combatState.Value = (byte)NetworkCombatState.None;
            _combatHealth.Value = 0;
            _pendingCombatProtection.Value = false;
            _personalProtectionEndsAt.Value = 0d;
            _pausedPersonalProtectionRemaining.Value = 0d;
            _boardDeathEndsAt = 0d;
            _pausedBoardDeathRemaining = 0d;
            _combatKnockbackVelocity = Vector3.zero;
            _serverQuietWalkHeld = false;
            _lastSentQuietWalkHeld = false;
            _isQuietWalking.Value = false;
            _isCrouching.Value = false;
            _footstepCadence.Reset();
            ApplyCrouchControllerState(false);
            ApplyCombatColliderState(NetworkCombatState.None);
        }

        public DamageResult ApplyDamage(DamageRequest request)
        {
            if (!IsServer || request.Amount <= 0 || _currentHealth.Value <= 0)
            {
                return DamageResult.Ignored;
            }

            var match = NetworkMatchState.Instance;
            if (match != null && match.IsGlobalSimulationPaused)
            {
                return DamageResult.Blocked;
            }
            if (request.Kind == DamageKind.Item &&
                ((match != null && match.IsOpeningProtectionActive) ||
                 PersonalItemProtectionRemaining > 0d))
            {
                return DamageResult.Blocked;
            }

            var appliedDamage = MatchAwardRules.GetAppliedDamageAmount(
                _currentHealth.Value,
                request.Amount);
            InterruptSwapOnDamage(appliedDamage);
            _currentHealth.Value = PlayerStatRules.ClampHealth(
                _currentHealth.Value - request.Amount,
                _maxHealth.Value);
            var sourceAvatar = request.Source != null
                ? request.Source.GetComponentInParent<NetworkPlayerAvatar>()
                : null;
            var attacker = MatchAwardRules.CountsAsPlayerDamage(
                request.Kind,
                sourceAvatar != null,
                sourceAvatar == this)
                ? sourceAvatar
                : null;
            RecordDamageOnServer(appliedDamage, attacker);
            PresentHitRpc((byte)request.HitRegion);
            if (_currentHealth.Value == 0)
            {
                BeginBoardDeathOnServer();
            }
            return DamageResult.Applied;
        }

        private void OnBoardHealthChanged(int previous, int current)
        {
            ApplyBoardDeathPresentation();
            PresentKnockOutSound(previous > 0 && current <= 0, previous <= 0 && current > 0);
        }

        /// <summary>
        /// Knock-out and respawn sounds during live board play. A respawn only
        /// sounds after a knock-out this client presented, so health resets at
        /// match start or lobby return stay silent.
        /// </summary>
        private void PresentKnockOutSound(bool knockedOut, bool revived)
        {
            var match = NetworkMatchState.Instance;
            var live = match != null &&
                       match.IsSpawned &&
                       match.GameplayEnabled &&
                       match.FlowState != BoardFlowState.MatchComplete;
            if (knockedOut && live)
            {
                _knockOutSoundPresented = true;
                GameSound.PlayAt(SoundKeys.CombatKnockOut, transform.position);
            }
            else if (revived)
            {
                if (_knockOutSoundPresented && live)
                {
                    GameSound.PlayAt(SoundKeys.CombatRespawn, transform.position);
                }

                _knockOutSoundPresented = false;
            }
        }

        private void ApplyBoardDeathPresentation()
        {
            _avatarVisual?.SetEliminated(_currentHealth.Value <= 0 ||
                CombatState == NetworkCombatState.Eliminated);
        }

        private void BeginBoardDeathOnServer()
        {
            ClearUtilityEffectsOnServer();
            CancelHandGestureOnServer();
            var match = NetworkMatchState.Instance;
            var now = match != null ? match.SynchronizedNow : Time.unscaledTimeAsDouble;
            var droppedGold = BoardDeathRules.DroppedGold(_gold.Value);
            if (droppedGold > 0 && match != null && match.GameplayEnabled)
            {
                _gold.Value -= droppedGold;
                match?.PlaceTombstoneOnServer(transform.position, droppedGold);
            }

            _boardDeathEndsAt = now + BoardDeathRules.DeathPresentationSeconds;
            _pausedBoardDeathRemaining = 0d;
            _personalProtectionEndsAt.Value = Math.Max(
                _personalProtectionEndsAt.Value,
                now + BoardDeathRules.RespawnProtectionSeconds);
            StopServerInputOnServer();
            HideBoundaryWalls();
        }

        public void AdvanceBoardDeathOnServer(double now)
        {
            if (!IsServer || _boardDeathEndsAt <= 0d || now < _boardDeathEndsAt)
            {
                return;
            }

            _boardDeathEndsAt = 0d;
            ResolveTopology();
            var respawn = BoardDeathRules.NearestRespawn(
                _topology != null ? _topology.Tiles : null,
                transform.position);
            if (respawn != null)
            {
                var moves = _remainingMoves.Value;
                EnsureTraversalInitialized();
                _traversal.Relocate(respawn, moves);
                RebaseBoardTravelPreviewAfterRelocationOnServer(respawn);
                TeleportController(respawn.GetRecoveryCenter(1f), transform.rotation);
                SyncLogicalTileOnServer();
            }

            _currentHealth.Value = _maxHealth.Value;
            RefreshBoundaryWallsOnServer();
        }

        public void ApplyPush(Vector3 impulse)
        {
            if (!IsServer ||
                float.IsNaN(impulse.x) || float.IsInfinity(impulse.x) ||
                float.IsNaN(impulse.y) || float.IsInfinity(impulse.y) ||
                float.IsNaN(impulse.z) || float.IsInfinity(impulse.z))
            {
                return;
            }

            _combatKnockbackVelocity += Vector3.ClampMagnitude(impulse, 12f);
        }

        public int HealOnServer(int amount)
        {
            if (!IsServer || amount <= 0 || _currentHealth.Value <= 0)
            {
                return 0;
            }

            var previous = _currentHealth.Value;
            _currentHealth.Value = PlayerStatRules.ClampHealth(
                (int)Math.Min(int.MaxValue, (long)_currentHealth.Value + amount),
                _maxHealth.Value);
            return _currentHealth.Value - previous;
        }

        public int ApplyGoldDeltaOnServer(int delta)
        {
            if (!IsServer || delta == 0)
            {
                return 0;
            }

            var previous = _gold.Value;
            _gold.Value = PlayerStatRules.ApplyGoldDelta(previous, delta);
            _matchAwardProgress.RecordGoldBalance(previous, _gold.Value);
            return _gold.Value - previous;
        }

        public int AddKeysOnServer(int amount)
        {
            if (!IsServer || amount <= 0)
            {
                return 0;
            }

            return ApplyKeyDeltaOnServer(amount);
        }

        public int ApplyKeyDeltaOnServer(int delta)
        {
            if (!IsServer || delta == 0)
            {
                return 0;
            }

            var previous = _keyCount.Value;
            _keyCount.Value = PlayerStatRules.ApplyKeyDelta(previous, delta);
            return _keyCount.Value - previous;
        }

        public bool TryPurchaseKeyOnServer()
        {
            if (!IsServer ||
                !PlayerStatRules.CanPurchaseKey(_gold.Value))
            {
                return false;
            }

            _gold.Value = PlayerStatRules.ApplyGoldDelta(
                _gold.Value,
                -PlayerStatRules.KeyShopGoldPrice);
            _keyCount.Value = PlayerStatRules.AddKeys(_keyCount.Value, 1);
            return true;
        }

        public bool CanAfford(int amount)
        {
            return amount >= 0 && _gold.Value >= amount;
        }

        public bool TrySpendGoldOnServer(int amount)
        {
            if (!IsServer || amount < 0 || _gold.Value < amount)
            {
                return false;
            }

            _gold.Value = PlayerStatRules.ApplyGoldDelta(_gold.Value, -amount);
            return true;
        }

        public bool TryAddItemOnServer(PrototypeItemId itemId)
        {
            if (!IsServer || !PrototypeItemCatalog.IsValid(itemId))
            {
                return false;
            }

            for (var slot = 0; slot < GameplayInventory.Capacity; slot++)
            {
                if ((_occupiedItemMask.Value & (1 << slot)) != 0)
                {
                    continue;
                }

                SetItemSlotValue(slot, itemId);
                _occupiedItemMask.Value = (byte)(_occupiedItemMask.Value | (1 << slot));
                return true;
            }

            return false;
        }

        public void AddMinigameWinOnServer()
        {
            if (IsServer && _minigameWins.Value < int.MaxValue)
            {
                _minigameWins.Value++;
            }
        }

        public void SetActionStateOnServer(PlayerBoardActionState state)
        {
            if (IsServer)
            {
                _actionState.Value = (byte)state;
            }
        }

        public void BeginCombatOnServer(bool participating)
        {
            if (!IsServer)
            {
                return;
            }

            StopServerInputOnServer();
            _combatKnockbackVelocity = Vector3.zero;
            _combatHealth.Value = participating ? BoardCombatRules.TemporaryHealth : 0;
            _combatState.Value = (byte)(participating
                ? NetworkCombatState.Active
                : NetworkCombatState.None);
            SetActionStateOnServer(participating
                ? PlayerBoardActionState.Fighting
                : PlayerBoardActionState.Hidden);
            ApplyCombatColliderState((NetworkCombatState)_combatState.Value);
            if (participating)
            {
                RefreshCombatBoundaryWallsOnServer();
            }
            else
            {
                HideBoundaryWalls();
            }
        }

        public void BeginArenaCombatOnServer(
            Vector3 position,
            Quaternion rotation)
        {
            if (!IsServer)
            {
                return;
            }

            StopServerInputOnServer();
            _combatKnockbackVelocity = Vector3.zero;
            _combatHealth.Value = BoardCombatRules.TemporaryHealth;
            _combatState.Value = (byte)NetworkCombatState.Active;
            _actionState.Value = (byte)PlayerBoardActionState.Fighting;
            _isCrouching.Value = false;
            _isQuietWalking.Value = false;
            ApplyCrouchControllerState(false);
            ApplyCombatColliderState(NetworkCombatState.Active);
            HideBoundaryWalls();
            TeleportController(position, rotation);
            _serverYaw = rotation.eulerAngles.y;
            _serverPitch = 0f;
        }

        public bool ApplyCombatPunchOnServer(
            NetworkPlayerAvatar attacker,
            Vector3 knockbackVelocity)
        {
            if (!IsServer || CombatState != NetworkCombatState.Active ||
                _combatHealth.Value <= 0)
            {
                return false;
            }

            var appliedDamage = MatchAwardRules.GetAppliedDamageAmount(
                _combatHealth.Value,
                BoardCombatRules.PunchDamage);
            _combatHealth.Value -= appliedDamage;
            RecordDamageOnServer(appliedDamage, attacker);
            PresentHitRpc((byte)PlayerHitRegion.Body);
            if (_combatHealth.Value > 0)
            {
                _combatKnockbackVelocity += Vector3.ClampMagnitude(
                    knockbackVelocity,
                    BoardCombatRules.PunchKnockbackSpeed);
                return false;
            }

            _combatState.Value = (byte)NetworkCombatState.Eliminated;
            _combatKnockbackVelocity = Vector3.zero;
            StopServerInputOnServer();
            HideBoundaryWalls();
            ApplyCombatColliderState(NetworkCombatState.Eliminated);
            _avatarVisual?.SetEliminated(true);
            return true;
        }

        public int ApplyCombatRetreatOnServer(int requestedDistance)
        {
            if (!IsServer || requestedDistance <= 0 || !_traversal.IsInitialized)
            {
                return 0;
            }

            var retreatPath = _traversal.Retreat(requestedDistance);
            var actualDistance = retreatPath.Length;
            ResolveTopology();
            while (actualDistance < requestedDistance && _topology != null)
            {
                var incoming = _topology.GetIncomingGates(_traversal.CurrentTile);
                BoardTile fallback = null;
                for (var i = 0; i < incoming.Count; i++)
                {
                    var source = incoming[i] != null ? incoming[i].Source : null;
                    if (source == null || fallback != null &&
                        CompareCoordinates(source.Coordinate, fallback.Coordinate) >= 0)
                    {
                        continue;
                    }
                    fallback = source;
                }

                if (fallback == null)
                {
                    break;
                }

                _traversal.Relocate(fallback);
                actualDistance++;
            }

            // TODO(COMBAT-PRESENTATION): replace this authoritative center snap
            // with authored retreat locomotion while keeping the same path result.
            TeleportController(
                _traversal.CurrentTile.GetRecoveryCenter(1f),
                transform.rotation);
            SyncLogicalTileOnServer();
            return actualDistance;
        }

        public void EndCombatOnServer(bool grantNextActionProtection)
        {
            if (!IsServer)
            {
                return;
            }

            if (grantNextActionProtection)
            {
                _pendingCombatProtection.Value = true;
            }
            _combatState.Value = (byte)NetworkCombatState.None;
            _combatHealth.Value = 0;
            _combatKnockbackVelocity = Vector3.zero;
            _actionState.Value = (byte)PlayerBoardActionState.Hidden;
            StopServerInputOnServer();
            HideBoundaryWalls();
            ApplyCombatColliderState(NetworkCombatState.None);
        }

        public void EndArenaCombatOnServer(
            Vector3 restorePosition,
            Quaternion restoreRotation)
        {
            if (!IsServer)
            {
                return;
            }

            EndCombatOnServer(false);
            TeleportController(restorePosition, restoreRotation);
            _serverYaw = restoreRotation.eulerAngles.y;
            _serverPitch = 0f;
        }

        public void PausePersonalProtectionOnServer(double now)
        {
            if (!IsServer)
            {
                return;
            }

            if (_boardDeathEndsAt > 0d)
            {
                if (now >= _boardDeathEndsAt)
                {
                    AdvanceBoardDeathOnServer(now);
                }
                else
                {
                    _pausedBoardDeathRemaining = _boardDeathEndsAt - now;
                    _boardDeathEndsAt = 0d;
                }
            }
            if (_pausedPersonalProtectionRemaining.Value > 0d)
            {
                return;
            }

            _pausedPersonalProtectionRemaining.Value = _personalProtectionEndsAt.Value > 0d
                ? Math.Max(0d, _personalProtectionEndsAt.Value - now)
                : 0d;
            _personalProtectionEndsAt.Value = 0d;
        }

        public void ResumePersonalProtectionOnServer(double now)
        {
            if (!IsServer)
            {
                return;
            }

            _personalProtectionEndsAt.Value = _pausedPersonalProtectionRemaining.Value > 0d
                ? now + _pausedPersonalProtectionRemaining.Value
                : 0d;
            _pausedPersonalProtectionRemaining.Value = 0d;
            if (_pausedBoardDeathRemaining > 0d)
            {
                _boardDeathEndsAt = now + _pausedBoardDeathRemaining;
                _pausedBoardDeathRemaining = 0d;
            }
        }

        private bool CanUseLobbyInput()
        {
            return LobbyArena.Instance != null && !_boardReady.Value && !IsBoardLoaded();
        }

        private static string GetCurrentSessionPhase()
        {
            var controller = OnlineSessionController.Instance;
            return controller != null && controller.IsInSession
                ? controller.CurrentSession.Phase
                : null;
        }

        private void InitializeLobbyPositionOnServer()
        {
            if (!IsServer || _lobbyPositionInitialized || _slot.Value < 0 ||
                LobbyArena.Instance == null)
            {
                return;
            }

            _serverInput = Vector2.zero;
            _serverQuietWalkHeld = false;
            _serverYaw = 0f;
            _serverPitch = 0f;
            _combatKnockbackVelocity = Vector3.zero;
            TeleportController(
                LobbyArena.Instance.GetSpawnPosition(_slot.Value),
                Quaternion.identity);
            _lobbyPositionInitialized = true;
        }

        private void SimulateLobbyMovementOnServer()
        {
            var arena = LobbyArena.Instance;
            if (arena == null || _characterController == null)
            {
                return;
            }

            InitializeLobbyPositionOnServer();
            if (!_lobbyPositionInitialized || !_characterController.enabled)
            {
                return;
            }

            UpdateCrouchStateOnServer(false);
            transform.rotation = Quaternion.Euler(0f, _serverYaw, 0f);
            if (eyePivot != null)
            {
                eyePivot.localRotation = Quaternion.identity;
            }

            var worldMovement = new Vector3(_serverInput.x, 0f, _serverInput.y);
            if (worldMovement.sqrMagnitude > 1f)
            {
                worldMovement.Normalize();
            }

            var velocity = worldMovement * moveSpeed;
            if (_combatKnockbackVelocity.sqrMagnitude > 0.0001f)
            {
                velocity += _combatKnockbackVelocity;
                _combatKnockbackVelocity = Vector3.MoveTowards(
                    _combatKnockbackVelocity,
                    Vector3.zero,
                    8f * Time.fixedDeltaTime);
            }

            var previousPosition = transform.position;
            MoveControllerWithGrounding(velocity, Time.fixedDeltaTime);

            var radialScale = Mathf.Max(
                Mathf.Abs(transform.lossyScale.x),
                Mathf.Abs(transform.lossyScale.z));
            var clamped = arena.ClampHorizontalPosition(
                transform.position,
                _characterController.radius * radialScale + 0.02f);
            if ((clamped - transform.position).sqrMagnitude > 0.000001f)
            {
                TeleportController(clamped, transform.rotation, false);
            }

            RecordNetworkFootsteps(
                previousPosition,
                transform.position,
                worldMovement.sqrMagnitude > 0.0001f,
                false);
        }

        private void AssignFirstAvailableLobbyColorOnServer()
        {
            if (!IsServer || LobbyArena.Instance == null)
            {
                return;
            }

            var occupiedMask = GetOccupiedLobbyColorMask(this);
            var paletteIndex = LobbyColorPalette.FindFirstAvailable(occupiedMask);
            if (paletteIndex >= 0)
            {
                _appearance.Value = _appearance.Value.WithPaletteColor(paletteIndex);
            }
        }

        private static bool IsLobbyColorAvailableOnServer(
            PlayerAppearanceState appearance,
            NetworkPlayerAvatar requester)
        {
            if (!LobbyColorPalette.TryGetIndex((Color32)appearance.BodyColor, out var index))
            {
                return false;
            }

            return (GetOccupiedLobbyColorMask(requester) & (1 << index)) == 0;
        }

        private static byte GetOccupiedLobbyColorMask(NetworkPlayerAvatar ignored)
        {
            byte occupiedMask = 0;
            var avatars = FindObjectsByType<NetworkPlayerAvatar>();
            for (var avatarIndex = 0; avatarIndex < avatars.Length; avatarIndex++)
            {
                var avatar = avatars[avatarIndex];
                if (avatar == null || avatar == ignored || !avatar.IsSpawned ||
                    avatar._slot.Value < 0 ||
                    !LobbyColorPalette.TryGetIndex(
                        (Color32)avatar._appearance.Value.BodyColor,
                        out var paletteIndex))
                {
                    continue;
                }

                occupiedMask = (byte)(occupiedMask | (1 << paletteIndex));
            }

            return occupiedMask;
        }

        private void TryLobbyPunchOnServer(
            Vector3 claimedOrigin,
            Vector3 claimedDirection)
        {
            if (!IsServer || !CanUseLobbyInput() ||
                float.IsNaN(claimedDirection.x) || float.IsInfinity(claimedDirection.x) ||
                float.IsNaN(claimedDirection.y) || float.IsInfinity(claimedDirection.y) ||
                float.IsNaN(claimedDirection.z) || float.IsInfinity(claimedDirection.z) ||
                claimedDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            var authoritativeOrigin = transform.position + Vector3.up * 0.75f;
            var authoritativeDirection = transform.forward.normalized;
            if (Vector3.Distance(authoritativeOrigin, claimedOrigin) > 1.5f ||
                Vector3.Dot(authoritativeDirection, claimedDirection.normalized) < 0.94f)
            {
                return;
            }

            var now = NetworkManager != null && NetworkManager.IsListening
                ? NetworkManager.ServerTime.Time
                : Time.unscaledTimeAsDouble;
            if (now < _nextLobbyPunchAllowedAt)
            {
                return;
            }

            _nextLobbyPunchAllowedAt = now + PlayerUnarmedRules.PunchCooldownSeconds;
            PresentPunchOnServer();

            var hits = Physics.SphereCastAll(
                authoritativeOrigin,
                PlayerUnarmedRules.PunchRadius,
                authoritativeDirection,
                PlayerUnarmedRules.PunchRange,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            for (var index = 0; index < hits.Length; index++)
            {
                var collider = hits[index].collider;
                if (collider == null || collider.transform == transform ||
                    collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                var target = collider.GetComponentInParent<NetworkPlayerAvatar>();
                if (target != null && target != this && target.IsSpawned &&
                    target.CanUseLobbyInput())
                {
                    var zone = collider.GetComponent<PlayerHitZone>();
                    target.PresentHitReactionOnServer(
                        zone != null ? zone.Region : PlayerHitRegion.Body);
                    var knockbackDirection = target.transform.position - transform.position;
                    knockbackDirection.y = 0f;
                    if (knockbackDirection.sqrMagnitude < 0.0001f)
                    {
                        knockbackDirection = authoritativeDirection;
                    }
                    target._combatKnockbackVelocity +=
                        knockbackDirection.normalized * 2.25f;
                    return;
                }

                if (!collider.isTrigger)
                {
                    return;
                }
            }
        }

        private void RecordNetworkFootsteps(
            Vector3 previousPosition,
            Vector3 currentPosition,
            bool hasMovementIntent,
            bool quietWalking)
        {
            if (!hasMovementIntent || !_characterController.isGrounded)
            {
                return;
            }

            var delta = currentPosition - previousPosition;
            delta.y = 0f;
            var stepCount = _footstepCadence.RecordMovement(
                delta.magnitude,
                quietWalking);
            for (var step = 0; step < stepCount; step++)
            {
                PresentFootstepRpc(transform.position, quietWalking);
            }
        }

        private void ResolveBoardTraversalAfterMovement()
        {
            if (_topology == null || !_traversal.IsInitialized)
            {
                return;
            }

            var currentTile = _traversal.CurrentTile;
            var capsuleCenter = _characterController.transform.TransformPoint(_characterController.center);
            var outgoing = _topology.GetOutgoingGates(currentTile);
            var allowedPartialCrossing = false;

            for (var i = 0; i < outgoing.Count; i++)
            {
                var outcome = _topology.ResolveGate(
                    outgoing[i],
                    _traversal,
                    _characterController,
                    false,
                    1f);
                if (outcome == BoardGateTraversalOutcome.Committed)
                {
                    RecordBoardRouteChoiceOnServer(
                        currentTile,
                        _traversal.CurrentTile);
                    _remainingMoves.Value = _traversal.RemainingMoves;
                    SyncLogicalTileOnServer();
                    RefreshBoundaryWallsOnServer();
                    if (_remainingMoves.Value <= 0)
                    {
                        NetworkMatchState.Instance?.TryReportPlayerArrivedOnServer(this);
                    }

                    return;
                }

                if (outcome == BoardGateTraversalOutcome.PartialCrossing)
                {
                    allowedPartialCrossing = true;
                }
            }

            if (!_topology.ContainsTraversableCapsule(
                    currentTile,
                    _characterController) &&
                !allowedPartialCrossing)
            {
                ClampControllerInsideTile(currentTile);
            }
        }

        private bool TryGetAimedWorldDie(out NetworkWorldDie die, out Ray ray)
        {
            var origin = eyePivot != null
                ? eyePivot.position
                : transform.position + Vector3.up * 0.75f;
            var direction = eyePivot != null ? eyePivot.forward : transform.forward;
            ray = new Ray(origin, direction);
            if (!Physics.Raycast(
                    ray,
                    out var hit,
                    5.5f,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                die = null;
                return false;
            }

            die = hit.collider.GetComponentInParent<NetworkWorldDie>();
            return die != null;
        }

        private bool TryGetAimedBoardShop(out RaycastHit hit)
        {
            var origin = eyePivot != null
                ? eyePivot.position
                : transform.position + Vector3.up * 0.75f;
            var direction = eyePivot != null ? eyePivot.forward : transform.forward;
            return Physics.Raycast(
                new Ray(origin, direction),
                out hit,
                ItemShopRules.InteractionDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
        }

        private void RefreshLocalBoundaryPresentation()
        {
            var match = NetworkMatchState.Instance;
            var showForAction = match != null && match.IsActionPhase;
            var showForCombat = match != null && match.IsCombatPhase;
            var canUseCombatInput = showForCombat &&
                                    match.CanAvatarUseCombatInput(this);
            if (!IsOwner ||
                !BoardBoundaryActivationRules.ShouldActivate(
                    GetCurrentSessionPhase(),
                    match != null && match.GameplayEnabled,
                    showForAction,
                    showForCombat,
                    canUseCombatInput,
                    _hasLogicalTile.Value))
            {
                HideBoundaryWalls();
                return;
            }

            ResolveTopology();
            if (_topology == null ||
                !_topology.TryGetTile(_logicalTileCoordinate.Value, out var tile))
            {
                HideBoundaryWalls();
                return;
            }

            EnsureBoundaryWallsConfigured();
            _boundaryWalls.SetPresentationVisible(true);
            RefreshBoundaryWalls(tile, canUseCombatInput ? 0 : LocalRemainingMoves);
        }

        private void RefreshBoundaryWallsOnServer()
        {
            var match = NetworkMatchState.Instance;
            var showForAction = match != null && match.IsActionPhase;
            var showForCombat = match != null && match.IsCombatPhase;
            var hasActiveCombatState = showForCombat &&
                                       CombatState == NetworkCombatState.Active;
            if (!IsServer || !_traversal.IsInitialized ||
                !BoardBoundaryActivationRules.ShouldActivate(
                    GetCurrentSessionPhase(),
                    match != null && match.GameplayEnabled,
                    showForAction,
                    showForCombat,
                    hasActiveCombatState,
                    _hasLogicalTile.Value))
            {
                HideBoundaryWalls();
                return;
            }

            if (hasActiveCombatState)
            {
                RefreshCombatBoundaryWallsOnServer();
                return;
            }

            if (
                (ItemChoiceResolution)_choiceResolution.Value == ItemChoiceResolution.NotStarted)
            {
                HideBoundaryWalls();
                return;
            }

            EnsureBoundaryWallsConfigured();
            _boundaryWalls.SetPresentationVisible(IsOwner);
            RefreshBoundaryWalls(_traversal.CurrentTile, _remainingMoves.Value);
        }

        private void RefreshCombatBoundaryWallsOnServer()
        {
            if (!IsServer || !_traversal.IsInitialized)
            {
                HideBoundaryWalls();
                return;
            }

            EnsureBoundaryWallsConfigured();
            _boundaryWalls.SetPresentationVisible(IsOwner);
            RefreshBoundaryWalls(_traversal.CurrentTile, 0);
        }

        private void EnsureBoundaryWallsConfigured()
        {
            ResolveTopology();
            if (_boundaryWalls == null || _topology == null || _slot.Value < 0 ||
                _slot.Value >= MultiplayerConstants.MaxPlayers)
            {
                return;
            }

            if (_configuredBoundarySlot == _slot.Value &&
                _configuredBoundaryTopology == _topology)
            {
                return;
            }

            _boundaryWalls.Configure(_slot.Value, _characterController, _topology);
            _configuredBoundarySlot = _slot.Value;
            _configuredBoundaryTopology = _topology;
            _displayedBoundaryTile = null;
            _displayedBoundaryMoves = int.MinValue;
            _boundaryWallsActive = false;
        }

        private void RefreshBoundaryWalls(BoardTile tile, int remainingMoves)
        {
            if (_boundaryWalls == null || tile == null ||
                (_boundaryWallsActive && _displayedBoundaryTile == tile &&
                 _displayedBoundaryMoves == remainingMoves))
            {
                return;
            }

            _boundaryWalls.Refresh(tile, remainingMoves);
            _displayedBoundaryTile = tile;
            _displayedBoundaryMoves = remainingMoves;
            _boundaryWallsActive = true;
        }

        private void HideBoundaryWalls()
        {
            _boundaryWalls?.Hide();

            _displayedBoundaryTile = null;
            _displayedBoundaryMoves = int.MinValue;
            _boundaryWallsActive = false;
        }

        private void SyncLogicalTileOnServer()
        {
            if (!IsServer || !_traversal.IsInitialized)
            {
                return;
            }

            _hasLogicalTile.Value = true;
            _logicalTileCoordinate.Value = _traversal.CurrentTile.Coordinate;
        }

        private void ClampControllerInsideTile(BoardTile tile)
        {
            if (tile == null || _characterController == null)
            {
                return;
            }

            var center = _characterController.transform.TransformPoint(_characterController.center);
            var safeInset = BoardGate.GetMaximumPlanarCapsuleSupport(
                                _characterController,
                                tile.transform.up) +
                            ControllerFootprintPadding;
            var clampedCenter = tile.GetClosestPointInside(center, safeInset);
            var correction = clampedCenter - center;
            if (correction.sqrMagnitude > 0.000001f)
            {
                TeleportController(
                    transform.position + correction,
                    transform.rotation,
                    false);
            }
        }

        private void TryAssignAuthoritativeSlotOnServer()
        {
            if (!IsServer || _slot.Value >= 0)
            {
                return;
            }

            var controller = OnlineSessionController.Instance;
            if (controller != null && controller.TryResolveAuthoritativeSlot(OwnerClientId, out var slot))
            {
                AssignSlotIfAvailableOnServer(slot);
                return;
            }

            if (controller == null)
            {
                for (var candidate = 0; candidate < MultiplayerConstants.MaxPlayers; candidate++)
                {
                    if (AssignSlotIfAvailableOnServer(candidate))
                    {
                        return;
                    }
                }
            }
        }

        private bool AssignSlotIfAvailableOnServer(int candidate)
        {
            if (candidate < 0 || candidate >= MultiplayerConstants.MaxPlayers)
            {
                return false;
            }

            var avatars = FindObjectsByType<NetworkPlayerAvatar>();
            for (var i = 0; i < avatars.Length; i++)
            {
                if (avatars[i] != this && avatars[i].IsSpawned && avatars[i]._slot.Value == candidate)
                {
                    return false;
                }
            }

            // TODO(STEAM-SESSION): keep this server validation, but resolve the slot
            // from the authenticated Steam lobby member when that provider is enabled.
            _slot.Value = candidate;
            var controller = OnlineSessionController.Instance;
            var resolvedName = controller != null &&
                               controller.TryResolveAuthoritativeDisplayName(
                                   OwnerClientId,
                                   out var displayName)
                ? displayName
                : "Player " + (candidate + 1);
            _displayName.Value = new FixedString64Bytes(
                PlayerProfilePreferences.SanitizeDisplayName(resolvedName));
            AssignFirstAvailableLobbyColorOnServer();
            // Setting the slot invokes OnSlotChanged synchronously. A board-ready
            // reconnect can restore its snapshot there, so do not overwrite the
            // restored board pose with the lobby spawn when control returns here.
            if (!_restoredFromSnapshot)
            {
                InitializeLobbyPositionOnServer();
            }
            NetworkMatchState.Instance?.TryRestoreAvatarOnServer(this);
            if (_boardReady.Value)
            {
                InitializeBoardStateOnServer();
            }

            return true;
        }

        private void EnsureTraversalInitialized()
        {
            if (_traversal.IsInitialized)
            {
                // The character center can enter the destination room before the
                // whole capsule clears its directed gate. Rebinding here would skip
                // BoardGate.TryTraverse's commit and its movement decrement.
                return;
            }

            ResolveTopology();
            if (_topology == null)
            {
                return;
            }

            var tile = _topology.FindContainingTile(transform.position, 0.1f);

            if (tile == null)
            {
                tile = FindStartTileForSlot(_slot.Value);
            }

            if (tile == null)
            {
                return;
            }

            _traversal.Begin(tile, _remainingMoves.Value);
            SyncLogicalTileOnServer();
        }

        private void ResolveTopology()
        {
            if (_topology == null)
            {
                var match = NetworkMatchState.Instance;
                _topology = match != null && match.IsSpawned
                    ? match.ActiveBoardTopology
                    : BoardMapRuntimeLoader.ActiveTopology;
                if (_topology == null)
                {
                    _topology = FindAnyObjectByType<BoardTopology>();
                }
            }
        }

        private BoardTile FindStartTileForSlot(int slot)
        {
            ResolveTopology();
            if (_topology == null || slot < 0)
            {
                return null;
            }

            var mapRoot = BoardMapRuntimeLoader.ResolveActiveMapRoot(_topology);
            if (mapRoot != null && PlayerSlotRules.IsValid(slot))
            {
                var authoredStart = mapRoot.GetStartTile(slot);
                if (authoredStart != null)
                {
                    for (var index = 0; index < _topology.Tiles.Count; index++)
                    {
                        if (_topology.Tiles[index] == authoredStart)
                            return authoredStart;
                    }
                }
            }

            var starts = new BoardTile[MultiplayerConstants.MaxPlayers];
            var count = 0;
            for (var i = 0; i < _topology.Tiles.Count && count < starts.Length; i++)
            {
                var tile = _topology.Tiles[i];
                if (tile != null && tile.TileType == BoardTileType.Start)
                {
                    starts[count++] = tile;
                }
            }

            Array.Sort(starts, 0, count, BoardTileCoordinateComparer.Instance);
            return slot < count ? starts[slot] : null;
        }

        private void ResolveInitialBoardPose(
            int slot,
            BoardTile expectedStart,
            out Vector3 position,
            out Quaternion rotation)
        {
            ResolveTopology();
            var mapRoot = BoardMapRuntimeLoader.ResolveActiveMapRoot(_topology);
            TryResolveAuthoredBoardStartPose(
                _topology,
                mapRoot,
                slot,
                expectedStart,
                _characterController,
                out position,
                out rotation);
        }

        internal static bool TryResolveAuthoredBoardStartPose(
            BoardTopology topology,
            BoardMapRoot mapRoot,
            int slot,
            BoardTile expectedStart,
            CharacterController controller,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = expectedStart != null
                ? expectedStart.GetRecoveryCenter(BoardStartRecoveryHeight)
                : default;
            rotation = Quaternion.identity;
            if (topology == null || mapRoot == null || expectedStart == null ||
                controller == null || !PlayerSlotRules.IsValid(slot) ||
                mapRoot.Topology != topology || mapRoot.GetStartTile(slot) != expectedStart)
            {
                return false;
            }

            var startIsRegistered = false;
            for (var index = 0; index < topology.Tiles.Count; index++)
            {
                if (topology.Tiles[index] == expectedStart)
                {
                    startIsRegistered = true;
                    break;
                }
            }

            var anchor = mapRoot.GetSpawnAnchor(slot);
            if (!startIsRegistered || anchor == null ||
                anchor == mapRoot.transform ||
                !anchor.IsChildOf(mapRoot.transform) ||
                !expectedStart.ContainsHorizontalPoint(anchor.position))
            {
                return false;
            }

            var anchorHeight = Vector3.Dot(
                anchor.position - expectedStart.WorldCenter,
                expectedStart.transform.up.normalized);
            if (Mathf.Abs(anchorHeight - BoardStartRecoveryHeight) >
                BoardStartAnchorHeightTolerance)
            {
                return false;
            }

            var controllerScale = controller.transform.lossyScale;
            var radialScale = Mathf.Max(
                Mathf.Abs(controllerScale.x),
                Mathf.Abs(controllerScale.z));
            var safeInset = controller.radius * radialScale +
                            ControllerFootprintPadding;
            var footprint = expectedStart.GetComponent<BoardTileFootprint>();
            if (footprint != null && !footprint.CanContainInset(safeInset))
            {
                return false;
            }

            var scaledCenterOffset = Vector3.Scale(
                controller.center,
                controllerScale);
            var proposedCenter = anchor.position +
                                 anchor.rotation * scaledCenterOffset;
            var clampedCenter = expectedStart.GetClosestPointInside(
                proposedCenter,
                safeInset);
            if (!expectedStart.ContainsHorizontalPoint(clampedCenter))
            {
                return false;
            }

            position = anchor.position + clampedCenter - proposedCenter;
            rotation = anchor.rotation;
            return true;
        }

        private void OnSlotChanged(int _, int current)
        {
            ClearLocalWorldDieCache();
            ApplySlotVisual(current);
            if (IsServer && _boardReady.Value)
            {
                NetworkMatchState.Instance?.TryRestoreAvatarOnServer(this);
                InitializeBoardStateOnServer();
            }
        }

        private void OnCombatStateChanged(byte previous, byte current)
        {
            ApplyCombatColliderState((NetworkCombatState)current);
            if ((NetworkCombatState)current == NetworkCombatState.Eliminated &&
                (NetworkCombatState)previous != NetworkCombatState.Eliminated)
            {
                GameSound.PlayAt(SoundKeys.CombatKnockOut, transform.position);
            }
        }

        private void ApplyCombatColliderState(NetworkCombatState state)
        {
            if (_characterController != null)
            {
                _characterController.enabled = state != NetworkCombatState.Eliminated;
            }
            ApplyBoardDeathPresentation();
        }

        private void OnCrouchingChanged(bool _, bool current)
        {
            ApplyCrouchPresentation(current);
        }

        private void OnAppearanceChanged(
            PlayerAppearanceState _,
            PlayerAppearanceState current)
        {
            ApplyAppearance(current);
            if (IsOwner)
            {
                OnlineSessionController.Instance?.AcceptAuthoritativeAppearance(current);
            }
        }

        private void OnDisplayNameChanged(FixedString64Bytes _, FixedString64Bytes current)
        {
            ApplyDisplayName(current);
        }

        private void ApplyAppearance(PlayerAppearanceState appearance)
        {
            if (_avatarVisual == null)
            {
                return;
            }

            var safe = appearance.Sanitized();
            _avatarVisual.SetBodyColor(safe.BodyColor);
            _avatarVisual.ApplyAppearance(
                safe.EyeId,
                safe.MouthId,
                safe.HatId,
                safe.ExpressionId);
        }

        private void ApplyDisplayName(FixedString64Bytes displayName)
        {
            _avatarVisual?.SetDisplayName(displayName.ToString());
        }

        private void ApplyCrouchPresentation(bool crouching)
        {
            _avatarVisual?.SetCrouching(crouching);
            if (eyePivot != null)
            {
                var localPosition = eyePivot.localPosition;
                localPosition.y = crouching
                    ? PlayerAvatarVisual.CrouchingEyeHeight
                    : PlayerAvatarVisual.StandingEyeHeight;
                eyePivot.localPosition = localPosition;
            }
            if (IsServer)
            {
                ApplyCrouchControllerState(crouching);
            }
        }

        private void UpdateCrouchStateOnServer(bool crouchHeld)
        {
            if (!IsServer)
            {
                return;
            }

            var target = crouchHeld || _isCrouching.Value && !CanStandOnServer();
            if (_isCrouching.Value != target)
            {
                _isCrouching.Value = target;
            }
            ApplyCrouchControllerState(target);
        }

        private void ApplyCrouchControllerState(bool crouching)
        {
            if (_characterController == null)
            {
                return;
            }

            _characterController.height = crouching
                ? PlayerAvatarVisual.CrouchingControllerHeight
                : PlayerAvatarVisual.StandingControllerHeight;
            var center = _characterController.center;
            center.y = crouching
                ? PlayerAvatarVisual.CrouchingControllerCenterY
                : PlayerAvatarVisual.StandingControllerCenterY;
            _characterController.center = center;
        }

        private bool CanStandOnServer()
        {
            if (_characterController == null)
            {
                return true;
            }

            var radius = Mathf.Max(0.01f, _characterController.radius - 0.02f);
            var center = transform.TransformPoint(new Vector3(
                _characterController.center.x,
                PlayerAvatarVisual.StandingControllerCenterY,
                _characterController.center.z));
            var segment = Mathf.Max(
                0f,
                PlayerAvatarVisual.StandingControllerHeight * 0.5f - radius);
            var count = Physics.OverlapCapsuleNonAlloc(
                center + transform.up * segment,
                center - transform.up * segment,
                radius,
                _standingClearanceHits,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (var index = 0; index < count; index++)
            {
                var candidate = _standingClearanceHits[index];
                if (candidate != null &&
                    candidate.transform != transform &&
                    !candidate.transform.IsChildOf(transform))
                {
                    return false;
                }
            }
            return true;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode _)
        {
            ClearLocalWorldDieCache();
            if (scene.name != MultiplayerConstants.BoardScene)
            {
                return;
            }

            _topology = null;
            _configuredBoundarySlot = -1;
            _configuredBoundaryTopology = null;
            HideBoundaryWalls();
            if (IsOwner)
            {
                _boardReadyRequestSent = false;
            }

            if (IsServer && _boardReady.Value)
            {
                InitializeBoardStateOnServer();
            }
        }

        private void ClampControllerInsideArenaCombat()
        {
            if (_characterController == null)
            {
                return;
            }

            var safeX = ArenaCombatRules.ArenaHalfWidth -
                        _characterController.radius - 0.02f;
            var safeZ = ArenaCombatRules.ArenaHalfDepth -
                        _characterController.radius - 0.02f;
            var position = transform.position;
            var clamped = new Vector3(
                Mathf.Clamp(position.x,
                    ArenaCombatRules.ArenaCenterX - safeX,
                    ArenaCombatRules.ArenaCenterX + safeX),
                position.y,
                Mathf.Clamp(position.z, -safeZ, safeZ));
            if ((clamped - position).sqrMagnitude > 0.000001f)
            {
                TeleportController(clamped, transform.rotation, false);
            }
        }

        private NetworkWorldDie ResolveLocalWorldDie(int slot)
        {
            if (slot < 0)
            {
                ClearLocalWorldDieCache();
                return null;
            }

            if (_cachedLocalWorldDie != null &&
                _cachedLocalWorldDieSlot == slot &&
                _cachedLocalWorldDie.IsSpawned &&
                _cachedLocalWorldDie.AssignedSlot == slot)
            {
                return _cachedLocalWorldDie;
            }

            _cachedLocalWorldDie = null;
            _cachedLocalWorldDieSlot = -1;
            if (Time.unscaledTime < _nextLocalWorldDieResolveAt)
            {
                return null;
            }

            _nextLocalWorldDieResolveAt = Time.unscaledTime + 0.25f;
            var coordinator = NetworkWorldDiceCoordinator.Instance;
            if (coordinator != null && coordinator.TryGetDie(slot, out var coordinatedDie))
            {
                CacheLocalWorldDie(coordinatedDie, slot);
                return coordinatedDie;
            }

            var dice = FindObjectsByType<NetworkWorldDie>(
                FindObjectsInactive.Include);
            for (var index = 0; index < dice.Length; index++)
            {
                var candidate = dice[index];
                if (candidate == null || !candidate.IsSpawned ||
                    candidate.AssignedSlot != slot)
                {
                    continue;
                }

                CacheLocalWorldDie(candidate, slot);
                return candidate;
            }

            return null;
        }

        private void CacheLocalWorldDie(NetworkWorldDie die, int slot)
        {
            _cachedLocalWorldDie = die;
            _cachedLocalWorldDieSlot = slot;
        }

        private void ClearLocalWorldDieCache()
        {
            _cachedLocalWorldDie = null;
            _cachedLocalWorldDieSlot = -1;
            _nextLocalWorldDieResolveAt = 0f;
        }

        private void EnsureEyePivot()
        {
            if (eyePivot != null)
            {
                return;
            }

            var existing = transform.Find("CameraPivot");
            if (existing != null)
            {
                eyePivot = existing;
                return;
            }

            var pivot = new GameObject("CameraPivot");
            eyePivot = pivot.transform;
            eyePivot.SetParent(transform, false);
            eyePivot.localPosition = new Vector3(0f, 0.75f, 0f);
        }

        private void ApplyLocalEyeRotation()
        {
            if (eyePivot == null)
            {
                return;
            }

            var localYawOffset = Mathf.DeltaAngle(transform.eulerAngles.y, _localYaw);
            eyePivot.localRotation = Quaternion.Euler(_localPitch, localYawOffset, 0f);
        }

        private void MoveControllerWithGrounding(
            Vector3 velocity,
            float deltaTime)
        {
            if (_characterController == null ||
                !_characterController.enabled ||
                deltaTime <= 0f)
            {
                return;
            }

            _verticalVelocity = PlayerGroundingRules.AdvanceVerticalVelocity(
                _verticalVelocity,
                _characterController.isGrounded,
                deltaTime);
            velocity.y += _verticalVelocity;
            _characterController.Move(velocity * deltaTime);
        }

        private void TeleportController(
            Vector3 position,
            Quaternion rotation,
            bool resetVerticalVelocity = true)
        {
            var wasEnabled = _characterController.enabled;
            if (wasEnabled)
            {
                _characterController.enabled = false;
            }

            transform.SetPositionAndRotation(position, rotation);
            if (resetVerticalVelocity)
            {
                _verticalVelocity = 0f;
            }
            if (wasEnabled)
            {
                _characterController.enabled = true;
            }
        }

        private void ApplySlotVisual(int slot)
        {
            gameObject.name = slot >= 0 ? "NetworkPlayer_" + (slot + 1) : "NetworkPlayer_Unassigned";
            ApplyAppearance(_appearance.Value);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void NotifyBoardReadyRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || !IsBoardLoaded())
            {
                return;
            }

            _boardReady.Value = true;
            TryAssignAuthoritativeSlotOnServer();
            NetworkMatchState.Instance?.TryRestoreAvatarOnServer(this);
            InitializeBoardStateOnServer();
            NetworkMatchState.Instance?.NotifyAvatarBoardReadyOnServer(this);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitMovementRpc(
            Vector2 input,
            float yaw,
            float pitch,
            bool quietWalkHeld,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId ||
                float.IsNaN(input.x) || float.IsInfinity(input.x) ||
                float.IsNaN(input.y) || float.IsInfinity(input.y) ||
                float.IsNaN(yaw) || float.IsInfinity(yaw) ||
                float.IsNaN(pitch) || float.IsInfinity(pitch))
            {
                _serverInput = Vector2.zero;
                return;
            }

            var match = NetworkMatchState.Instance;
            var canUseLobbyInput = CanUseLobbyInput();
            var canUseActionInput = match != null && match.CanAcceptActionInput &&
                                    HasResolvedItemChoice && !IsSwapping &&
                                    !IsBoardDeathInProgressOnServer;
            var canUseCombatInput = match != null && match.CanAvatarUseCombatInput(this);
            if (!canUseLobbyInput && !canUseActionInput && !canUseCombatInput)
            {
                _serverInput = Vector2.zero;
                _serverQuietWalkHeld = false;
                _isQuietWalking.Value = false;
                return;
            }

            _serverInput = Vector2.ClampMagnitude(input, 1f);
            _serverQuietWalkHeld = !canUseLobbyInput && quietWalkHeld;
            _serverYaw = Mathf.Repeat(yaw, 360f);
            _serverPitch = canUseLobbyInput ? 0f : Mathf.Clamp(pitch, -85f, 85f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PresentFootstepRpc(Vector3 worldPosition, bool quietWalking)
        {
            _footstepEmitter?.PresentFootstep(worldPosition, quietWalking);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ResolveItemChoiceRpc(int slotIndex, bool chooseNoItem, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                NetworkMatchState.Instance?.TryResolveItemChoiceOnServer(this, slotIndex, chooseNoItem);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void UseSelectedItemRpc(
            Vector3 claimedOrigin,
            Vector3 claimedDirection,
            uint requestId,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                var accepted =
                    NetworkMatchState.Instance != null &&
                    NetworkMatchState.Instance.TryUseSelectedItemOnServer(
                        this,
                        claimedOrigin,
                        claimedDirection);
                if (requestId != 0u)
                {
                    ConfirmItemUseRpc(requestId, accepted);
                }
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitAppearanceRpc(
            PlayerAppearanceState appearance,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                var sanitized = appearance.Sanitized();
                if (LobbyArena.Instance == null ||
                    IsLobbyColorAvailableOnServer(sanitized, this))
                {
                    _appearance.Value = sanitized;
                }

                ConfirmAppearanceRpc(_appearance.Value);
            }
        }

        [Rpc(SendTo.Owner)]
        private void ConfirmAppearanceRpc(PlayerAppearanceState appearance)
        {
            var sanitized = appearance.Sanitized();
            ApplyAppearance(sanitized);
            OnlineSessionController.Instance?.AcceptAuthoritativeAppearance(sanitized);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PresentPunchRpc(bool useRightHand)
        {
            _avatarVisual?.TriggerPunch(useRightHand);
            GameSound.PlayAt(SoundKeys.CombatPunchSwing, transform.position);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PresentHitRpc(byte region)
        {
            _avatarVisual?.TriggerHit((PlayerHitRegion)region);
            GameSound.PlayAt(
                SoundKeys.CombatHit((PlayerHitRegion)region),
                transform.position);
            if (IsOwner && NetworkMatchState.Instance != null &&
                NetworkMatchState.Instance.IsArenaCombatPhase)
            {
                ArenaCombatHitFlashView.Instance?.Flash();
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PresentItemUseRpc(byte itemId)
        {
            _avatarVisual?.TriggerItemUse((PrototypeItemId)itemId);
            GameSound.PlayAt(
                SoundKeys.ItemUse((PrototypeItemId)itemId),
                transform.position);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SetMinigameReadyRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                NetworkMatchState.Instance?.TrySetMinigameReadyOnServer(this);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitMinefieldInputRpc(
            Vector2 input,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId ||
                float.IsNaN(input.x) || float.IsInfinity(input.x) ||
                float.IsNaN(input.y) || float.IsInfinity(input.y))
            {
                return;
            }

            NetworkMatchState.Instance?.RouteMovementInputOnCurrentMinigameOnServer(
                this,
                Vector2.ClampMagnitude(input, 1f));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitRedLightGreenLightInputRpc(
            Vector2 input,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId ||
                float.IsNaN(input.x) || float.IsInfinity(input.x) ||
                float.IsNaN(input.y) || float.IsInfinity(input.y))
            {
                return;
            }

            NetworkMatchState.Instance?.RouteMovementInputOnCurrentMinigameOnServer(
                this,
                Vector2.ClampMagnitude(input, 1f));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitStableFootingInputRpc(
            Vector2 input,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId ||
                float.IsNaN(input.x) || float.IsInfinity(input.x) ||
                float.IsNaN(input.y) || float.IsInfinity(input.y))
            {
                return;
            }

            NetworkMatchState.Instance?.RouteMovementInputOnCurrentMinigameOnServer(
                this,
                Vector2.ClampMagnitude(input, 1f));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestStableFootingPushRpc(
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                NetworkMatchState.Instance?.RoutePushInputOnCurrentMinigameOnServer(this);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitBalloonBlowHeldRpc(
            bool isHeld,
            byte roundNumber,
            uint inputEpoch,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                NetworkMatchState.Instance?.RouteInflateHeldOnCurrentMinigameOnServer(
                    this,
                    isHeld,
                    roundNumber,
                    inputEpoch);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitGiftGrabInputRpc(
            Vector2 input,
            byte roundNumber,
            uint inputEpoch,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId ||
                float.IsNaN(input.x) || float.IsInfinity(input.x) ||
                float.IsNaN(input.y) || float.IsInfinity(input.y))
            {
                return;
            }

            NetworkMatchState.Instance?.RouteMovementInputOnCurrentMinigameOnServer(
                this,
                Vector2.ClampMagnitude(input, 1f),
                roundNumber,
                inputEpoch);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitCliffBarrageInputRpc(
            Vector2 input,
            byte roundNumber,
            uint inputEpoch,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId ||
                float.IsNaN(input.x) || float.IsInfinity(input.x) ||
                float.IsNaN(input.y) || float.IsInfinity(input.y))
            {
                return;
            }

            NetworkMatchState.Instance?.
                RouteMovementInputOnCurrentMinigameOnServer(
                    this,
                    Vector2.ClampMagnitude(input, 1f),
                    roundNumber,
                    inputEpoch);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitBombPassingInputRpc(
            Vector2 input,
            byte roundNumber,
            uint inputEpoch,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId ||
                float.IsNaN(input.x) || float.IsInfinity(input.x) ||
                float.IsNaN(input.y) || float.IsInfinity(input.y))
            {
                return;
            }

            NetworkMatchState.Instance?.
                RouteMovementInputOnCurrentMinigameOnServer(
                    this,
                    Vector2.ClampMagnitude(input, 1f),
                    roundNumber,
                    inputEpoch);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitSnowySpinInputRpc(
            Vector2 input,
            byte roundNumber,
            uint inputEpoch,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId ||
                float.IsNaN(input.x) || float.IsInfinity(input.x) ||
                float.IsNaN(input.y) || float.IsInfinity(input.y))
            {
                return;
            }

            NetworkMatchState.Instance?.
                RouteMovementInputOnCurrentMinigameOnServer(
                    this,
                    Vector2.ClampMagnitude(input, 1f),
                    roundNumber,
                    inputEpoch);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitTerritoryPaintInputRpc(
            Vector2 input,
            byte roundNumber,
            uint inputEpoch,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId ||
                float.IsNaN(input.x) || float.IsInfinity(input.x) ||
                float.IsNaN(input.y) || float.IsInfinity(input.y))
            {
                return;
            }

            NetworkMatchState.Instance?.
                RouteMovementInputOnCurrentMinigameOnServer(
                    this,
                    Vector2.ClampMagnitude(input, 1f),
                    roundNumber,
                    inputEpoch);
        }

        [Rpc(
            SendTo.Server,
            InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitTagChaseInputRpc(
            Vector2 input,
            float yaw,
            byte roundNumber,
            uint inputEpoch,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId ||
                float.IsNaN(input.x) ||
                float.IsInfinity(input.x) ||
                float.IsNaN(input.y) ||
                float.IsInfinity(input.y) ||
                float.IsNaN(yaw) ||
                float.IsInfinity(yaw))
            {
                return;
            }

            var match = NetworkMatchState.Instance;
            match?.RouteLookInputOnCurrentMinigameOnServer(
                this,
                yaw,
                roundNumber,
                inputEpoch);
            match?.RouteMovementInputOnCurrentMinigameOnServer(
                this,
                Vector2.ClampMagnitude(input, 1f),
                roundNumber,
                inputEpoch);
        }

        [Rpc(
            SendTo.Server,
            InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitRaceStepRpc(
            RaceStepInput input,
            byte roundNumber,
            uint inputEpoch,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId ||
                (input != RaceStepInput.Left &&
                 input != RaceStepInput.Right))
            {
                return;
            }

            NetworkMatchState.Instance?.
                RouteRaceStepOnCurrentMinigameOnServer(
                    this,
                    input,
                    roundNumber,
                    inputEpoch);
        }

        [Rpc(
            SendTo.Server,
            InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitSequenceMemoryInputRpc(
            SequenceMemoryInput input,
            byte roundNumber,
            uint inputEpoch,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId ||
                !SequenceMemoryRules.IsValidInput(input))
            {
                return;
            }

            NetworkMatchState.Instance?.
                RouteSequenceMemoryInputOnCurrentMinigameOnServer(
                    this,
                    input,
                    roundNumber,
                    inputEpoch);
        }

        [Rpc(
            SendTo.Server,
            InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitBouncingShieldAxisRpc(
            float axis,
            byte roundNumber,
            uint inputEpoch,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId ||
                float.IsNaN(axis) || float.IsInfinity(axis) ||
                (axis != -1f && axis != 0f && axis != 1f))
            {
                return;
            }

            NetworkMatchState.Instance?.
                RouteBouncingShieldAxisOnCurrentMinigameOnServer(
                    this,
                    axis,
                    roundNumber,
                    inputEpoch);
        }

        [Rpc(
            SendTo.Server,
            InvokePermission = RpcInvokePermission.Owner)]
        private void RequestTagChaseCatchRpc(
            byte roundNumber,
            uint inputEpoch,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                NetworkMatchState.Instance?.
                    RoutePrimaryActionOnCurrentMinigameOnServer(
                        this,
                        roundNumber,
                        inputEpoch);
            }
        }



        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestGiftGrabActionRpc(
            byte roundNumber,
            uint inputEpoch,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                NetworkMatchState.Instance?.RoutePrimaryActionOnCurrentMinigameOnServer(
                    this,
                    roundNumber,
                    inputEpoch);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestCliffBarragePushRpc(
            byte roundNumber,
            uint inputEpoch,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                NetworkMatchState.Instance?.
                    RoutePrimaryActionOnCurrentMinigameOnServer(
                        this, roundNumber, inputEpoch);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestBombPassingActionRpc(
            byte roundNumber,
            uint inputEpoch,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                NetworkMatchState.Instance?.
                    RoutePrimaryActionOnCurrentMinigameOnServer(
                        this, roundNumber, inputEpoch);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestMinefieldSonarRpc(
            Vector2 currentInput,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId &&
                !float.IsNaN(currentInput.x) &&
                !float.IsInfinity(currentInput.x) &&
                !float.IsNaN(currentInput.y) &&
                !float.IsInfinity(currentInput.y))
            {
                NetworkMatchState.Instance?.RouteSonarInputOnCurrentMinigameOnServer(
                    this,
                    Vector2.ClampMagnitude(currentInput, 1f));
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitWrongWayDirectionRpc(
            byte direction,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId ||
                direction > (byte)WrongWayDirection.Right)
            {
                return;
            }

            NetworkMatchState.Instance?.RouteWrongWayDirectionOnCurrentMinigameOnServer(
                this,
                (WrongWayDirection)direction);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestKeyShopPurchaseRpc(int expectedRevision, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                NetworkMatchState.Instance?.TryPurchaseKeyOnServer(this, expectedRevision);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestTombstonePickupRpc(int tombstoneId, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                NetworkMatchState.Instance?.TryCollectTombstoneOnServer(this, tombstoneId);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void PurchaseItemFromShopRpc(
            int shopIndex,
            int offerIndex,
            int expectedRevision,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                NetworkMatchState.Instance?.TryPurchaseItemOnServer(
                    this,
                    shopIndex,
                    offerIndex,
                    expectedRevision);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestCombatPunchRpc(
            Vector3 claimedOrigin,
            Vector3 claimedDirection,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                var match = NetworkMatchState.Instance;
                if (match != null && match.IsArenaCombatPlaying)
                {
                    NetworkArenaCombatState.Instance?.TryPunchOnServer(
                        this,
                        claimedOrigin,
                        claimedDirection);
                }
                else
                {
                    match?.TryCombatPunchOnServer(
                        this,
                        claimedOrigin,
                        claimedDirection);
                }
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestBoardPunchRpc(
            Vector3 claimedOrigin,
            Vector3 claimedDirection,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                CancelHandGestureOnServer();
                NetworkMatchState.Instance?.TryBoardPunchOnServer(
                    this,
                    claimedOrigin,
                    claimedDirection);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestLobbyPunchRpc(
            Vector3 claimedOrigin,
            Vector3 claimedDirection,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                CancelHandGestureOnServer();
                TryLobbyPunchOnServer(claimedOrigin, claimedDirection);
            }
        }

        private static NetworkVariable<byte> CreatePrivateItemSlot()
        {
            return new NetworkVariable<byte>(
                (byte)PrototypeItemId.None,
                NetworkVariableReadPermission.Owner,
                NetworkVariableWritePermission.Server);
        }

        private PrototypeItemId GetItemSlotValue(int slotIndex)
        {
            switch (slotIndex)
            {
                case 0: return (PrototypeItemId)_itemSlot0.Value;
                case 1: return (PrototypeItemId)_itemSlot1.Value;
                case 2: return (PrototypeItemId)_itemSlot2.Value;
                default: return PrototypeItemId.None;
            }
        }

        private void SetItemSlotValue(int slotIndex, PrototypeItemId value)
        {
            switch (slotIndex)
            {
                case 0:
                    _itemSlot0.Value = (byte)value;
                    break;
                case 1:
                    _itemSlot1.Value = (byte)value;
                    break;
                case 2:
                    _itemSlot2.Value = (byte)value;
                    break;
            }
        }

        private static bool IsBoardLoaded()
        {
            var board = SceneManager.GetSceneByName(MultiplayerConstants.BoardScene);
            var match = NetworkMatchState.Instance;
            return board.IsValid() && board.isLoaded &&
                   match != null && match.IsSpawned && match.IsBoardMapReady;
        }

        private static bool IsPointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private static int CompareCoordinates(Vector2Int left, Vector2Int right)
        {
            var x = left.x.CompareTo(right.x);
            return x != 0 ? x : left.y.CompareTo(right.y);
        }

        private sealed class BoardTileCoordinateComparer : System.Collections.Generic.IComparer<BoardTile>
        {
            public static readonly BoardTileCoordinateComparer Instance = new BoardTileCoordinateComparer();

            public int Compare(BoardTile left, BoardTile right)
            {
                if (ReferenceEquals(left, right))
                {
                    return 0;
                }
                if (left == null)
                {
                    return 1;
                }
                if (right == null)
                {
                    return -1;
                }

                var x = left.Coordinate.x.CompareTo(right.Coordinate.x);
                return x != 0 ? x : left.Coordinate.y.CompareTo(right.Coordinate.y);
            }
        }
    }
}
