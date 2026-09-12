using System;
using MazeParty.Gameplay;
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
    public sealed class NetworkPlayerAvatar : NetworkBehaviour, IDamageable, IPushReceiver
    {
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
                NetworkVariableReadPermission.Owner,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<double> _pausedPersonalProtectionRemaining =
            new NetworkVariable<double>(
                0d,
                NetworkVariableReadPermission.Owner,
                NetworkVariableWritePermission.Server);

        private CharacterController _characterController;
        private FootstepAudioEmitter _footstepEmitter;
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
        private bool _lastSentBalloonBlowHeld;
        private int _lastSentBalloonBlowRound = -1;
        private uint _lastSentBalloonBlowInputEpoch;
        private int _lastSentGiftGrabRound = -1;
        private uint _lastSentGiftGrabInputEpoch;
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
        private float _nextBalloonBlowInputRefresh;
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
        private readonly Collider[] _standingClearanceHits = new Collider[16];
        private NetworkWorldDie _cachedLocalWorldDie;
        private int _cachedLocalWorldDieSlot = -1;
        private float _nextLocalWorldDieResolveAt;

        public int AssignedSlot => _slot.Value;
        public int LocalVisibleRoll
        {
            get
            {
                if (!IsOwner)
                {
                    return 0;
                }

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
            _isCrouching.OnValueChanged += OnCrouchingChanged;
            _appearance.OnValueChanged += OnAppearanceChanged;
            _displayName.OnValueChanged += OnDisplayNameChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplySlotVisual(_slot.Value);
            ApplyAppearance(_appearance.Value);
            ApplyDisplayName(_displayName.Value);
            ApplyCrouchPresentation(_isCrouching.Value);
            ApplyCombatColliderState(CombatState);
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
            _isCrouching.OnValueChanged -= OnCrouchingChanged;
            _appearance.OnValueChanged -= OnAppearanceChanged;
            _displayName.OnValueChanged -= OnDisplayNameChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
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
            var handlingMinigame =
                SubmitLocalGiftGrabInput();
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
            HandleLocalActionButtons();
            if (!handlingMinigame)
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
            if (!IsSpawned || !IsServer || _slot.Value < 0)
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
                                  HasResolvedItemChoice;
            var canMoveInCombat = match != null && match.CanAvatarUseCombatInput(this);
            if (!canMoveInAction && !canMoveInCombat)
            {
                StopServerInputOnServer();
                _combatKnockbackVelocity = Vector3.zero;
                return;
            }

            EnsureTraversalInitialized();
            if (!_traversal.IsInitialized)
            {
                StopServerInputOnServer();
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
            if (velocity.sqrMagnitude > 0.0001f)
            {
                _characterController.Move(velocity * Time.fixedDeltaTime);
            }
            RecordNetworkFootsteps(
                previousPosition,
                transform.position,
                hasMovementIntent,
                quietWalking);

            if (canMoveInCombat)
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
                : "EMPTY";
        }

        public string GetLocalItemDescription(int slotIndex)
        {
            return IsLocalItemOccupied(slotIndex)
                ? PrototypeItemCatalog.Get(GetLocalItemId(slotIndex)).Description
                : "This slot is empty.";
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
            if (!IsServer)
            {
                return;
            }

            _maxHealth.Value = PlayerStatRules.DefaultMaxHealth;
            _currentHealth.Value = PlayerStatRules.DefaultMaxHealth;
            _keyCount.Value = PlayerStatRules.DefaultStartingKeys;
            _gold.Value = PlayerStatRules.DefaultStartingGold;
            _minigameWins.Value = 0;
            _occupiedItemMask.Value = 0;
            SetItemSlotValue(0, PrototypeItemId.None);
            SetItemSlotValue(1, PrototypeItemId.None);
            SetItemSlotValue(2, PrototypeItemId.None);
            _selectedItemSlot.Value = -1;
            _actionState.Value = (byte)PlayerBoardActionState.Hidden;
            _combatState.Value = (byte)NetworkCombatState.None;
            _combatHealth.Value = 0;
            _pendingCombatProtection.Value = false;
            _personalProtectionEndsAt.Value = 0d;
            _pausedPersonalProtectionRemaining.Value = 0d;
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
            if (request.Kind == DamageKind.Item &&
                ((match != null && match.IsOpeningProtectionActive) ||
                 PersonalItemProtectionRemaining > 0d))
            {
                return DamageResult.Blocked;
            }

            _currentHealth.Value = PlayerStatRules.ClampHealth(
                _currentHealth.Value - request.Amount,
                _maxHealth.Value);
            PresentHitRpc((byte)request.HitRegion);
            return DamageResult.Applied;
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
            return _gold.Value - previous;
        }

        public int AddKeysOnServer(int amount)
        {
            if (!IsServer || amount <= 0)
            {
                return 0;
            }

            var previous = _keyCount.Value;
            _keyCount.Value = PlayerStatRules.AddKeys(previous, amount);
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

        public bool ApplyCombatPunchOnServer(Vector3 knockbackVelocity)
        {
            if (!IsServer || CombatState != NetworkCombatState.Active ||
                _combatHealth.Value <= 0)
            {
                return false;
            }

            _combatHealth.Value = Math.Max(
                0,
                _combatHealth.Value - BoardCombatRules.PunchDamage);
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

        public void PausePersonalProtectionOnServer(double now)
        {
            if (!IsServer || _pausedPersonalProtectionRemaining.Value > 0d)
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
        }

        public void InitializeBoardStateOnServer()
        {
            if (!IsServer || _slot.Value < 0 || !IsBoardLoaded())
            {
                return;
            }

            ResolveTopology();
            if (_topology == null)
            {
                return;
            }

            if (!_restoredFromSnapshot && !_boardPositionInitialized)
            {
                var start = FindStartTileForSlot(_slot.Value);
                if (start != null)
                {
                    TeleportController(start.GetRecoveryCenter(1f), Quaternion.identity);
                }
            }

            _boardPositionInitialized = true;
            EnsureTraversalInitialized();
            RefreshBoundaryWallsOnServer();
        }

        public void PrepareForOverviewOnServer()
        {
            if (!IsServer)
            {
                return;
            }

            StopServerInputOnServer();
            _privateRoll.Value = 0;
            _remainingMoves.Value = 0;
            _choiceResolution.Value = (byte)ItemChoiceResolution.NotStarted;
            _selectedItemSlot.Value = -1;
            _actionState.Value = (byte)PlayerBoardActionState.Hidden;
            if (_traversal.IsInitialized)
            {
                _traversal.ResetMoves(0);
            }
            HideBoundaryWalls();
        }

        public void BeginActionOnServer(int _)
        {
            if (!IsServer)
            {
                return;
            }

            StopServerInputOnServer();
            _privateRoll.Value = 0;
            _remainingMoves.Value = 0;
            _choiceResolution.Value = (byte)ItemChoiceResolution.Pending;
            _selectedItemSlot.Value = -1;
            _actionState.Value = (byte)PlayerBoardActionState.Dice;
            if (_pendingCombatProtection.Value)
            {
                var match = NetworkMatchState.Instance;
                var now = match != null ? match.SynchronizedNow : Time.unscaledTimeAsDouble;
                _personalProtectionEndsAt.Value =
                    now + BoardCombatRules.NextActionItemProtectionSeconds;
                _pausedPersonalProtectionRemaining.Value = 0d;
                _pendingCombatProtection.Value = false;
            }
            EnsureTraversalInitialized();
            if (_traversal.IsInitialized)
            {
                _traversal.ResetMoves(0);
            }
            RefreshBoundaryWallsOnServer();
        }

        public void EndActionOnServer()
        {
            if (!IsServer)
            {
                return;
            }

            StopServerInputOnServer();
            if (_selectedItemSlot.Value >= 0)
            {
                ConsumeSelectedItemOnServer();
            }

            _choiceResolution.Value = (byte)ItemChoiceResolution.NotStarted;
            _privateRoll.Value = 0;
            _remainingMoves.Value = 0;
            _actionState.Value = (byte)PlayerBoardActionState.Hidden;
            if (_traversal.IsInitialized)
            {
                _traversal.ResetMoves(0);
            }
            HideBoundaryWalls();
        }

        public bool ResolveItemChoiceOnServer(int slotIndex)
        {
            if (!IsServer || (ItemChoiceResolution)_choiceResolution.Value != ItemChoiceResolution.Pending ||
                slotIndex < 0 || slotIndex >= GameplayInventory.Capacity ||
                (_occupiedItemMask.Value & (1 << slotIndex)) == 0)
            {
                return false;
            }

            _selectedItemSlot.Value = slotIndex;
            _choiceResolution.Value = (byte)ItemChoiceResolution.ItemSelected;
            return true;
        }

        public bool ResolveNoItemChoiceOnServer(bool timedOut)
        {
            if (!IsServer || (ItemChoiceResolution)_choiceResolution.Value != ItemChoiceResolution.Pending)
            {
                return false;
            }

            _selectedItemSlot.Value = -1;
            _choiceResolution.Value = (byte)(timedOut
                ? ItemChoiceResolution.TimedOut
                : ItemChoiceResolution.DoNotUse);
            return true;
        }

        public bool ConsumeSelectedItemOnServer()
        {
            if (!IsServer)
            {
                return false;
            }

            var selected = _selectedItemSlot.Value;
            if (selected < 0 || selected >= GameplayInventory.Capacity ||
                (_occupiedItemMask.Value & (1 << selected)) == 0)
            {
                return false;
            }

            _occupiedItemMask.Value = (byte)(_occupiedItemMask.Value & ~(1 << selected));
            SetItemSlotValue(selected, PrototypeItemId.None);
            _selectedItemSlot.Value = -1;
            return true;
        }

        public void SetRollOnServer(int roll)
        {
            if (!IsServer)
            {
                return;
            }

            var safeRoll = Mathf.Clamp(
                roll,
                0,
                WorldDieAuthorityModel.MaximumFace);
            _privateRoll.Value = safeRoll;
            _remainingMoves.Value = safeRoll;
            _actionState.Value = safeRoll > 0
                ? (byte)PlayerBoardActionState.Moving
                : (byte)PlayerBoardActionState.Dice;
            EnsureTraversalInitialized();
            if (_traversal.IsInitialized)
            {
                _traversal.ResetMoves(safeRoll);
            }
            RefreshBoundaryWallsOnServer();
        }

        public void MarkArrivedOnServer()
        {
            SetActionStateOnServer(PlayerBoardActionState.Arrived);
        }

        public bool ForceAdvanceOneTileOnServer(int turn)
        {
            if (!IsServer)
            {
                return false;
            }

            ResolveTopology();
            EnsureTraversalInitialized();
            if (_topology == null || !_traversal.IsInitialized)
            {
                return false;
            }

            var source = _traversal.CurrentTile;
            var outgoing = _topology.GetOutgoingGates(source);
            if (outgoing.Count == 0)
            {
                return false;
            }

            // Stable selection keeps every peer/replay deterministic while still
            // distributing timed-out players across branching exits.
            var gateIndex = Mathf.Abs(turn + _slot.Value) % outgoing.Count;
            var destination = outgoing[gateIndex].Destination;
            if (destination == null)
            {
                return false;
            }

            var direction = destination.WorldCenter - source.WorldCenter;
            direction.y = 0f;
            var rotation = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : transform.rotation;
            _traversal.Relocate(destination, 0);
            _remainingMoves.Value = 0;
            TeleportController(destination.GetRecoveryCenter(1f), rotation);
            _serverYaw = rotation.eulerAngles.y;
            _serverPitch = 0f;
            SyncLogicalTileOnServer();
            RefreshBoundaryWallsOnServer();
            return true;
        }

        public void StopServerInputOnServer()
        {
            if (IsServer)
            {
                _serverInput = Vector2.zero;
                _serverQuietWalkHeld = false;
                _isQuietWalking.Value = false;
                UpdateCrouchStateOnServer(false);
            }
        }

        public ReconnectSnapshot CreateReconnectSnapshotOnServer()
        {
            var logicalTile = _traversal.IsInitialized ? _traversal.CurrentTile : null;
            return new ReconnectSnapshot
            {
                IsValid = IsServer && _slot.Value >= 0,
                Position = transform.position,
                Rotation = transform.rotation,
                Roll = _privateRoll.Value,
                RemainingMoves = _remainingMoves.Value,
                ChoiceResolution = (ItemChoiceResolution)_choiceResolution.Value,
                SelectedItemSlot = _selectedItemSlot.Value,
                OccupiedItemMask = _occupiedItemMask.Value,
                ItemSlot0 = _itemSlot0.Value,
                ItemSlot1 = _itemSlot1.Value,
                ItemSlot2 = _itemSlot2.Value,
                MaxHealth = _maxHealth.Value,
                CurrentHealth = _currentHealth.Value,
                KeyCount = _keyCount.Value,
                Gold = _gold.Value,
                MinigameWins = _minigameWins.Value,
                ActionState = (PlayerBoardActionState)_actionState.Value,
                CombatState = CombatState,
                CombatHealth = _combatHealth.Value,
                PendingCombatProtection = _pendingCombatProtection.Value,
                PersonalProtectionRemaining = PersonalItemProtectionRemaining,
                Appearance = _appearance.Value,
                DisplayName = _displayName.Value.ToString(),
                HasLogicalCurrentTile = logicalTile != null,
                LogicalCurrentTileCoordinate = logicalTile != null
                    ? logicalTile.Coordinate
                    : default,
                TraversalHistory = _traversal.CaptureHistoryCoordinates()
            };
        }

        public bool RestoreReconnectSnapshotOnServer(ReconnectSnapshot snapshot)
        {
            if (!IsServer || !snapshot.IsValid)
            {
                return false;
            }

            ResolveTopology();
            if (_topology == null)
            {
                return false;
            }

            BoardTile logicalTile = null;
            if (snapshot.HasLogicalCurrentTile &&
                (!_topology.TryGetTile(snapshot.LogicalCurrentTileCoordinate, out logicalTile) ||
                 logicalTile == null))
            {
                return false;
            }

            _privateRoll.Value = Mathf.Clamp(
                snapshot.Roll,
                0,
                WorldDieAuthorityModel.MaximumFace);
            _remainingMoves.Value = Mathf.Max(0, snapshot.RemainingMoves);
            _choiceResolution.Value = (byte)snapshot.ChoiceResolution;
            _selectedItemSlot.Value = snapshot.SelectedItemSlot;
            _occupiedItemMask.Value = snapshot.OccupiedItemMask;
            _itemSlot0.Value = snapshot.ItemSlot0;
            _itemSlot1.Value = snapshot.ItemSlot1;
            _itemSlot2.Value = snapshot.ItemSlot2;
            _maxHealth.Value = Mathf.Max(1, snapshot.MaxHealth);
            _currentHealth.Value = PlayerStatRules.ClampHealth(
                snapshot.CurrentHealth,
                _maxHealth.Value);
            _keyCount.Value = Mathf.Max(0, snapshot.KeyCount);
            _gold.Value = Mathf.Max(0, snapshot.Gold);
            _minigameWins.Value = Mathf.Max(0, snapshot.MinigameWins);
            _actionState.Value = (byte)snapshot.ActionState;
            _combatState.Value = (byte)snapshot.CombatState;
            _combatHealth.Value = Mathf.Clamp(
                snapshot.CombatHealth,
                0,
                BoardCombatRules.TemporaryHealth);
            _pendingCombatProtection.Value = snapshot.PendingCombatProtection;
            _pausedPersonalProtectionRemaining.Value = 0d;
            _personalProtectionEndsAt.Value = snapshot.PersonalProtectionRemaining > 0d
                ? (NetworkMatchState.Instance != null
                    ? NetworkMatchState.Instance.SynchronizedNow
                    : Time.unscaledTimeAsDouble) + snapshot.PersonalProtectionRemaining
                : 0d;
            _appearance.Value = snapshot.Appearance.Sanitized();
            _displayName.Value = new FixedString64Bytes(
                PlayerProfilePreferences.SanitizeDisplayName(snapshot.DisplayName));
            _serverYaw = snapshot.Rotation.eulerAngles.y;
            TeleportController(snapshot.Position, snapshot.Rotation);
            _restoredFromSnapshot = true;
            _boardPositionInitialized = true;
            if (snapshot.TraversalHistory != null &&
                _traversal.RestoreHistory(
                    _topology,
                    snapshot.TraversalHistory,
                    _remainingMoves.Value))
            {
                SyncLogicalTileOnServer();
            }
            else if (logicalTile != null)
            {
                _traversal.Begin(logicalTile, _remainingMoves.Value);
                SyncLogicalTileOnServer();
            }
            else
            {
                EnsureTraversalInitialized();
            }

            RefreshBoundaryWallsOnServer();
            ApplyCombatColliderState(CombatState);
            return _traversal.IsInitialized;
        }

        private bool CanUseLobbyInput()
        {
            return LobbyArena.Instance != null && !_boardReady.Value && !IsBoardLoaded();
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
            if (velocity.sqrMagnitude > 0.0001f)
            {
                _characterController.Move(velocity * Time.fixedDeltaTime);
            }

            var radialScale = Mathf.Max(
                Mathf.Abs(transform.lossyScale.x),
                Mathf.Abs(transform.lossyScale.z));
            var clamped = arena.ClampHorizontalPosition(
                transform.position,
                _characterController.radius * radialScale + 0.02f);
            if ((clamped - transform.position).sqrMagnitude > 0.000001f)
            {
                TeleportController(clamped, transform.rotation);
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

        private void HandleLocalLook()
        {
            var match = NetworkMatchState.Instance;
            var canLook = match != null &&
                          ((match.CanAcceptActionInput && HasResolvedItemChoice &&
                            !BoardFlowView.IsItemShopOpen) ||
                           match.CanAvatarUseCombatInput(this)) &&
                          Cursor.lockState == CursorLockMode.Locked;
            var mouse = Mouse.current;
            if (!canLook || mouse == null)
            {
                return;
            }

            var delta = mouse.delta.ReadValue() * lookSensitivity;
            _localYaw += delta.x;
            _localPitch = Mathf.Clamp(_localPitch - delta.y, -85f, 85f);
            ApplyLocalEyeRotation();
        }

        private void HandleLocalActionButtons()
        {
            var match = NetworkMatchState.Instance;
            if (match != null && match.IsRedLightGreenLightPlaying)
            {
                return;
            }

            if (match != null && match.IsStableFootingPlaying)
            {
                return;
            }

            if (match != null && match.IsBalloonBlowPlaying)
            {
                return;
            }

            if (match != null && match.IsGiftGrabPlaying)
            {
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (match != null && match.IsWrongWayPlaying)
            {
                return;
            }

            if (match != null && match.IsMinefieldPlaying)
            {
                var minefield = NetworkMinefieldState.Instance;
                if (minefield != null &&
                    minefield.CanAcceptInputForSlot(AssignedSlot) &&
                    mouse.rightButton.wasPressedThisFrame)
                {
                    RequestMinefieldSonarRpc(_lastSentMinefieldInput);
                }
                return;
            }

            if (IsPointerOverUi())
            {
                return;
            }

            var lobbyInput = CanUseLobbyInput();
            var combatInput = match != null && match.CanAvatarUseCombatInput(this);
            var repeatPrimary = ShouldRepeatPrimaryAction(
                mouse,
                combatInput
                    ? BoardCombatRules.PunchCooldownSeconds
                    : PlayerUnarmedRules.PunchCooldownSeconds);

            if (lobbyInput)
            {
                if (repeatPrimary)
                {
                    var origin = transform.position + Vector3.up * 0.75f;
                    RequestLobbyPunchRpc(origin, transform.forward);
                }
                return;
            }

            if (match == null)
            {
                return;
            }

            if (combatInput)
            {
                if (repeatPrimary)
                {
                    var origin = eyePivot != null
                        ? eyePivot.position
                        : transform.position + Vector3.up * 0.75f;
                    var direction = eyePivot != null ? eyePivot.forward : transform.forward;
                    RequestCombatPunchRpc(origin, direction);
                }
                return;
            }

            if (!match.CanAcceptActionInput || !HasResolvedItemChoice ||
                BoardFlowView.IsItemShopOpen)
            {
                return;
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                if (TryGetAimedBoardShop(out var shopHit))
                {
                    var keyTarget = shopHit.collider.GetComponentInParent<KeyShopWorldTarget>();
                    if (keyTarget != null)
                    {
                        RequestKeyShopPurchaseRpc(match.KeyShopRevision);
                        return;
                    }

                    var itemTarget = shopHit.collider.GetComponentInParent<ItemShopWorldTarget>();
                    if (itemTarget != null)
                    {
                        BoardFlowView.Instance?.OpenItemShop(itemTarget.ShopIndex);
                        return;
                    }
                }

                if (TryGetAimedWorldDie(out var aimedDie, out var aimedRay) &&
                    aimedDie.AssignedSlot == AssignedSlot && !HasRolled)
                {
                    aimedDie.RequestRollFromLocalRay(aimedRay);
                }
            }

            if (repeatPrimary)
            {
                if (TryGetAimedWorldDie(out var aimedDie, out var aimedRay))
                {
                    if (aimedDie.AssignedSlot == AssignedSlot && !HasRolled)
                    {
                        aimedDie.RequestNudgeFromLocalRay(aimedRay);
                    }
                    return;
                }

                if (_selectedItemSlot.Value >= 0)
                {
                    // Item activation remains edge-triggered so holding LMB cannot
                    // consume several inventory slots as replication catches up.
                    if (mouse.leftButton.wasPressedThisFrame)
                    {
                        var origin = eyePivot != null
                            ? eyePivot.position
                            : transform.position +
                              Vector3.up * PlayerAvatarVisual.StandingEyeHeight;
                        var direction = eyePivot != null
                            ? eyePivot.forward
                            : transform.forward;
                        UseSelectedItemRpc(origin, direction);
                    }
                }
                else
                {
                    var origin = eyePivot != null
                        ? eyePivot.position
                        : transform.position + Vector3.up * PlayerAvatarVisual.StandingEyeHeight;
                    var direction = eyePivot != null ? eyePivot.forward : transform.forward;
                    RequestBoardPunchRpc(origin, direction);
                }
            }
        }

        private bool ShouldRepeatPrimaryAction(Mouse mouse, double intervalSeconds)
        {
            if (mouse == null || !mouse.leftButton.isPressed)
            {
                _nextLocalPrimaryRepeatAt = 0d;
                return false;
            }

            var now = Time.unscaledTimeAsDouble;
            if (!mouse.leftButton.wasPressedThisFrame &&
                now < _nextLocalPrimaryRepeatAt)
            {
                return false;
            }

            _nextLocalPrimaryRepeatAt =
                now + Math.Max(0.01d, intervalSeconds);
            return true;
        }

        private void SubmitLocalMovement()
        {
            var input = Vector2.zero;
            var quietWalkHeld = false;
            var keyboard = Keyboard.current;
            var match = NetworkMatchState.Instance;
            var lobbyInput = CanUseLobbyInput();
            var canMove = lobbyInput || (match != null &&
                          ((match.CanAcceptActionInput && HasResolvedItemChoice &&
                            !BoardFlowView.IsItemShopOpen) ||
                           match.CanAvatarUseCombatInput(this)));
            if (keyboard != null && canMove)
            {
                input.x = (keyboard.dKey.isPressed ? 1f : 0f) -
                          (keyboard.aKey.isPressed ? 1f : 0f);
                input.y = (keyboard.wKey.isPressed ? 1f : 0f) -
                          (keyboard.sKey.isPressed ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
                quietWalkHeld = !lobbyInput && keyboard.leftCtrlKey.isPressed;
            }

            if (lobbyInput && input.sqrMagnitude > 0.0001f)
            {
                _localYaw = Mathf.Atan2(input.x, input.y) * Mathf.Rad2Deg;
                _localPitch = 0f;
                ApplyLocalEyeRotation();
            }

            var predictedCrouch = quietWalkHeld || _isCrouching.Value;
            _avatarVisual?.SetCrouching(predictedCrouch);
            if (eyePivot != null)
            {
                var eye = eyePivot.localPosition;
                eye.y = predictedCrouch
                    ? PlayerAvatarVisual.CrouchingEyeHeight
                    : PlayerAvatarVisual.StandingEyeHeight;
                eyePivot.localPosition = eye;
            }

            if (input != _lastSentInput ||
                quietWalkHeld != _lastSentQuietWalkHeld ||
                Mathf.Abs(Mathf.DeltaAngle(_lastSentYaw, _localYaw)) >= 1f ||
                Mathf.Abs(_lastSentPitch - _localPitch) >= 1f ||
                Time.unscaledTime >= _nextInputRefresh)
            {
                _lastSentInput = input;
                _lastSentQuietWalkHeld = quietWalkHeld;
                _lastSentYaw = _localYaw;
                _lastSentPitch = _localPitch;
                _nextInputRefresh = Time.unscaledTime + 0.1f;
                SubmitMovementRpc(input, _localYaw, _localPitch, quietWalkHeld);
            }
        }

        private bool SubmitLocalMinefieldMovement()
        {
            var match = NetworkMatchState.Instance;
            var minefield = NetworkMinefieldState.Instance;
            if (match == null || !match.IsMinefieldPlaying || minefield == null)
            {
                _lastSentMinefieldInput = Vector2.zero;
                return false;
            }

            var input = Vector2.zero;
            var keyboard = Keyboard.current;
            if (keyboard != null && minefield.CanAcceptInputForSlot(AssignedSlot))
            {
                input.x = (keyboard.dKey.isPressed ? 1f : 0f) -
                          (keyboard.aKey.isPressed ? 1f : 0f);
                input.y = (keyboard.wKey.isPressed ? 1f : 0f) -
                          (keyboard.sKey.isPressed ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
            }

            if (input != _lastSentMinefieldInput ||
                Time.unscaledTime >= _nextMinefieldInputRefresh)
            {
                _lastSentMinefieldInput = input;
                _nextMinefieldInputRefresh = Time.unscaledTime + 0.1f;
                SubmitMinefieldInputRpc(input);
            }

            return true;
        }

        private bool SubmitLocalRedLightGreenLightMovement()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsRedLightGreenLightPlaying)
            {
                _lastSentRedLightGreenLightInput = Vector2.zero;
                return false;
            }

            var state = NetworkRedLightGreenLightState.Instance;
            var input = Vector2.zero;
            var keyboard = Keyboard.current;
            if (state != null && keyboard != null &&
                state.CanAcceptInputForSlot(AssignedSlot))
            {
                input.x = (keyboard.dKey.isPressed ? 1f : 0f) -
                          (keyboard.aKey.isPressed ? 1f : 0f);
                input.y = (keyboard.wKey.isPressed ? 1f : 0f) -
                          (keyboard.sKey.isPressed ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
            }

            if (input != _lastSentRedLightGreenLightInput ||
                Time.unscaledTime >=
                _nextRedLightGreenLightInputRefresh)
            {
                _lastSentRedLightGreenLightInput = input;
                _nextRedLightGreenLightInputRefresh =
                    Time.unscaledTime + 0.1f;
                SubmitRedLightGreenLightInputRpc(input);
            }

            return true;
        }

        private bool SubmitLocalStableFootingMovement()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsStableFootingPlaying)
            {
                _lastSentStableFootingInput = Vector2.zero;
                return false;
            }

            var state = NetworkStableFootingState.Instance;
            var input = Vector2.zero;
            var canAccept = state != null &&
                            state.CanAcceptInputForSlot(AssignedSlot);
            var keyboard = Keyboard.current;
            if (canAccept && keyboard != null)
            {
                input.x = (keyboard.dKey.isPressed ? 1f : 0f) -
                          (keyboard.aKey.isPressed ? 1f : 0f);
                input.y = (keyboard.wKey.isPressed ? 1f : 0f) -
                          (keyboard.sKey.isPressed ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
            }

            if (input != _lastSentStableFootingInput ||
                Time.unscaledTime >= _nextStableFootingInputRefresh)
            {
                _lastSentStableFootingInput = input;
                _nextStableFootingInputRefresh = Time.unscaledTime + 0.1f;
                SubmitStableFootingInputRpc(input);
            }

            var mouse = Mouse.current;
            if (canAccept && mouse != null &&
                mouse.leftButton.wasPressedThisFrame)
            {
                RequestStableFootingPushRpc();
            }

            return true;
        }

        private bool SubmitLocalBalloonBlowInput()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsBalloonBlowPlaying)
            {
                _lastSentBalloonBlowHeld = false;
                _lastSentBalloonBlowRound = -1;
                _lastSentBalloonBlowInputEpoch = 0U;
                return false;
            }

            var state = NetworkBalloonBlowState.Instance;
            if (state == null || !state.IsSpawned ||
                state.InputEpoch == 0U)
            {
                return true;
            }

            var mouse = Mouse.current;
            var isHeld = mouse != null &&
                         mouse.leftButton.isPressed &&
                         !IsPointerOverUi();
            if (isHeld != _lastSentBalloonBlowHeld ||
                state.RoundNumber != _lastSentBalloonBlowRound ||
                state.InputEpoch != _lastSentBalloonBlowInputEpoch ||
                Time.unscaledTime >= _nextBalloonBlowInputRefresh)
            {
                _lastSentBalloonBlowHeld = isHeld;
                _lastSentBalloonBlowRound = state.RoundNumber;
                _lastSentBalloonBlowInputEpoch = state.InputEpoch;
                _nextBalloonBlowInputRefresh =
                    Time.unscaledTime + 0.1f;
                SubmitBalloonBlowHeldRpc(
                    isHeld,
                    (byte)state.RoundNumber,
                    state.InputEpoch);
            }

            return true;
        }

        private bool SubmitLocalGiftGrabInput()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsGiftGrabPlaying)
            {
                _lastSentGiftGrabInput = Vector2.zero;
                _lastSentGiftGrabRound = -1;
                _lastSentGiftGrabInputEpoch = 0U;
                return false;
            }

            var state = NetworkGiftGrabState.Instance;
            if (state == null || !state.IsSpawned || state.InputEpoch == 0U)
            {
                return true;
            }

            var input = Vector2.zero;
            var canAccept = state.CanAcceptInputForSlot(AssignedSlot);
            var keyboard = Keyboard.current;
            if (canAccept && keyboard != null)
            {
                input.x = (keyboard.dKey.isPressed ? 1f : 0f) -
                          (keyboard.aKey.isPressed ? 1f : 0f);
                input.y = (keyboard.wKey.isPressed ? 1f : 0f) -
                          (keyboard.sKey.isPressed ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
            }

            if (input != _lastSentGiftGrabInput ||
                state.RoundNumber != _lastSentGiftGrabRound ||
                state.InputEpoch != _lastSentGiftGrabInputEpoch ||
                Time.unscaledTime >= _nextGiftGrabInputRefresh)
            {
                _lastSentGiftGrabInput = input;
                _lastSentGiftGrabRound = state.RoundNumber;
                _lastSentGiftGrabInputEpoch = state.InputEpoch;
                _nextGiftGrabInputRefresh = Time.unscaledTime + 0.1f;
                SubmitGiftGrabInputRpc(
                    input,
                    (byte)state.RoundNumber,
                    state.InputEpoch);
            }

            var mouse = Mouse.current;
            if (canAccept && mouse != null &&
                mouse.leftButton.wasPressedThisFrame)
            {
                RequestGiftGrabActionRpc(
                    (byte)state.RoundNumber,
                    state.InputEpoch);
            }

            return true;
        }

        private bool SubmitLocalWrongWayDirection()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsWrongWayPlaying)
            {
                return false;
            }

            var state = NetworkWrongWayState.Instance;
            var keyboard = Keyboard.current;
            if (state == null || keyboard == null ||
                !state.CanAcceptInputForSlot(AssignedSlot))
            {
                return true;
            }

            WrongWayDirection? direction = null;
            if (keyboard.wKey.wasPressedThisFrame)
            {
                direction = WrongWayDirection.Up;
            }
            else if (keyboard.sKey.wasPressedThisFrame)
            {
                direction = WrongWayDirection.Down;
            }
            else if (keyboard.aKey.wasPressedThisFrame)
            {
                direction = WrongWayDirection.Left;
            }
            else if (keyboard.dKey.wasPressedThisFrame)
            {
                direction = WrongWayDirection.Right;
            }

            if (direction.HasValue)
            {
                SubmitWrongWayDirectionRpc((byte)direction.Value);
            }

            return true;
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

            if (!currentTile.ContainsHorizontalPoint(capsuleCenter) && !allowedPartialCrossing)
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
            var showForCombat = match != null && match.CanAvatarUseCombatInput(this);
            if (!IsOwner || match == null || (!showForAction && !showForCombat) ||
                !_hasLogicalTile.Value)
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
            RefreshBoundaryWalls(tile, showForCombat ? 0 : LocalRemainingMoves);
        }

        private void RefreshBoundaryWallsOnServer()
        {
            if (!IsServer || !_traversal.IsInitialized)
            {
                HideBoundaryWalls();
                return;
            }

            if (CombatState == NetworkCombatState.Active)
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
            var offset = center - tile.WorldCenter;
            var right = tile.transform.right.normalized;
            var forward = tile.transform.forward.normalized;
            var up = tile.transform.up.normalized;
            var radialScale = Mathf.Max(
                Mathf.Abs(transform.lossyScale.x),
                Mathf.Abs(transform.lossyScale.z));
            var safeExtent = Mathf.Max(
                0.1f,
                BoardTile.HalfRoomSize - _characterController.radius * radialScale - 0.02f);
            var horizontal = Mathf.Clamp(Vector3.Dot(offset, right), -safeExtent, safeExtent);
            var depth = Mathf.Clamp(Vector3.Dot(offset, forward), -safeExtent, safeExtent);
            var height = Vector3.Dot(offset, up);
            var clampedCenter = tile.WorldCenter +
                                right * horizontal +
                                forward * depth +
                                up * height;
            var correction = clampedCenter - center;
            if (correction.sqrMagnitude > 0.000001f)
            {
                TeleportController(transform.position + correction, transform.rotation);
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
            InitializeLobbyPositionOnServer();
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
                _topology = FindAnyObjectByType<BoardTopology>();
            }
        }

        private BoardTile FindStartTileForSlot(int slot)
        {
            ResolveTopology();
            if (_topology == null || slot < 0)
            {
                return null;
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

        private void OnCombatStateChanged(byte _, byte current)
        {
            ApplyCombatColliderState((NetworkCombatState)current);
        }

        private void ApplyCombatColliderState(NetworkCombatState state)
        {
            if (_characterController != null)
            {
                _characterController.enabled = state != NetworkCombatState.Eliminated;
            }
            _avatarVisual?.SetEliminated(state == NetworkCombatState.Eliminated);
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
            _avatarVisual.ApplyAppearance(safe.EyeId, safe.MouthId, safe.HatId);
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

        private void TeleportController(Vector3 position, Quaternion rotation)
        {
            var wasEnabled = _characterController.enabled;
            if (wasEnabled)
            {
                _characterController.enabled = false;
            }

            transform.SetPositionAndRotation(position, rotation);
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
                                    HasResolvedItemChoice;
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
        private void RequestRollRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                NetworkMatchState.Instance?.TryRollForAvatarOnServer(this);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void UseSelectedItemRpc(
            Vector3 claimedOrigin,
            Vector3 claimedDirection,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                NetworkMatchState.Instance?.TryUseSelectedItemOnServer(
                    this,
                    claimedOrigin,
                    claimedDirection);
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
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PresentHitRpc(byte region)
        {
            _avatarVisual?.TriggerHit((PlayerHitRegion)region);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PresentItemUseRpc(byte itemId)
        {
            _avatarVisual?.TriggerItemUse((PrototypeItemId)itemId);
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

            NetworkMinefieldState.Instance?.ReceiveInputOnServer(
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

            NetworkRedLightGreenLightState.Instance?.ReceiveInputOnServer(
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

            NetworkStableFootingState.Instance?.ReceiveInputOnServer(
                this,
                Vector2.ClampMagnitude(input, 1f));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestStableFootingPushRpc(
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                NetworkStableFootingState.Instance?.TryPushOnServer(this);
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
                NetworkBalloonBlowState.Instance?.SetInflateHeldOnServer(
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

            NetworkGiftGrabState.Instance?.ReceiveInputOnServer(
                this,
                Vector2.ClampMagnitude(input, 1f),
                roundNumber,
                inputEpoch);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestGiftGrabActionRpc(
            byte roundNumber,
            uint inputEpoch,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                NetworkGiftGrabState.Instance?.TryPrimaryActionOnServer(
                    this,
                    roundNumber,
                    inputEpoch);
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
                NetworkMinefieldState.Instance?.TrySonarOnServer(
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

            NetworkWrongWayState.Instance?.TrySubmitDirectionOnServer(
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
                NetworkMatchState.Instance?.TryCombatPunchOnServer(
                    this,
                    claimedOrigin,
                    claimedDirection);
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
            return board.IsValid() && board.isLoaded;
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
