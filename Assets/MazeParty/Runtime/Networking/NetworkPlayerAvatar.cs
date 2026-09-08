using System;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(CharacterController))]
    public sealed class NetworkPlayerAvatar : NetworkBehaviour, IDamageable
    {
        private static readonly Color[] PlayerColors =
        {
            new Color(0.95f, 0.25f, 0.25f),
            new Color(0.25f, 0.55f, 1f),
            new Color(0.25f, 0.85f, 0.4f),
            new Color(1f, 0.75f, 0.2f)
        };

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
        private readonly BoardTraversalState _traversal = new BoardTraversalState();
        private readonly FootstepCadenceTracker _footstepCadence =
            new FootstepCadenceTracker();
        private Vector2 _serverInput;
        private Vector2 _lastSentInput;
        private bool _serverQuietWalkHeld;
        private bool _lastSentQuietWalkHeld;
        private float _serverYaw;
        private float _localYaw;
        private float _localPitch;
        private float _nextInputRefresh;
        private float _nextSlotResolveAttempt;
        private bool _boardReadyRequestSent;
        private bool _boardPositionInitialized;
        private bool _restoredFromSnapshot;
        private int _configuredBoundarySlot = -1;
        private BoardTopology _configuredBoundaryTopology;
        private BoardTile _displayedBoundaryTile;
        private int _displayedBoundaryMoves = int.MinValue;
        private bool _boundaryWallsActive;
        private Vector3 _combatKnockbackVelocity;

        public int AssignedSlot => _slot.Value;
        public int LocalVisibleRoll => IsOwner ? _privateRoll.Value : 0;
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
        }

        public override void OnNetworkSpawn()
        {
            _slot.OnValueChanged += OnSlotChanged;
            _combatState.OnValueChanged += OnCombatStateChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplySlotVisual(_slot.Value);
            ApplyCombatColliderState(CombatState);
            _localYaw = transform.eulerAngles.y;
            _serverYaw = _localYaw;

            if (IsServer)
            {
                TryAssignAuthoritativeSlotOnServer();
            }

            if (IsOwner && IsBoardLoaded())
            {
                _boardReadyRequestSent = false;
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
            SceneManager.sceneLoaded -= OnSceneLoaded;
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
            HandleLocalActionButtons();
            SubmitLocalMovement();
            RefreshLocalBoundaryPresentation();
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || _slot.Value < 0 || !_boardReady.Value)
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

            transform.rotation = Quaternion.Euler(0f, _serverYaw, 0f);
            var localMovement = new Vector3(_serverInput.x, 0f, _serverInput.y);
            var worldMovement = transform.TransformDirection(localMovement);
            worldMovement.y = 0f;
            if (worldMovement.sqrMagnitude > 1f)
            {
                worldMovement.Normalize();
            }

            var hasMovementIntent = FootstepRules.HasMovementIntent(_serverInput);
            var quietWalking = hasMovementIntent && _serverQuietWalkHeld;
            if (_isQuietWalking.Value != quietWalking)
            {
                _isQuietWalking.Value = quietWalking;
            }

            var velocity = worldMovement *
                           (moveSpeed * FootstepRules.SpeedMultiplier(quietWalking));
            if (canMoveInCombat)
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
            _footstepCadence.Reset();
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
            return DamageResult.Applied;
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

            var safeRoll = Mathf.Clamp(roll, 0, 10);
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

        public void StopServerInputOnServer()
        {
            if (IsServer)
            {
                _serverInput = Vector2.zero;
                _serverQuietWalkHeld = false;
                _isQuietWalking.Value = false;
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

            _privateRoll.Value = Mathf.Clamp(snapshot.Roll, 0, 10);
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
            if (match == null || IsPointerOverUi())
            {
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (match.CanAvatarUseCombatInput(this))
            {
                if (mouse.leftButton.wasPressedThisFrame)
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

            if (mouse.leftButton.wasPressedThisFrame)
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
                    UseSelectedItemRpc();
                }
            }
        }

        private void SubmitLocalMovement()
        {
            var input = Vector2.zero;
            var quietWalkHeld = false;
            var keyboard = Keyboard.current;
            var match = NetworkMatchState.Instance;
            var canMove = match != null &&
                          ((match.CanAcceptActionInput && HasResolvedItemChoice &&
                            !BoardFlowView.IsItemShopOpen) ||
                           match.CanAvatarUseCombatInput(this));
            if (keyboard != null && canMove)
            {
                input.x = (keyboard.dKey.isPressed ? 1f : 0f) -
                          (keyboard.aKey.isPressed ? 1f : 0f);
                input.y = (keyboard.wKey.isPressed ? 1f : 0f) -
                          (keyboard.sKey.isPressed ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
                quietWalkHeld = keyboard.leftCtrlKey.isPressed;
            }

            if (input != _lastSentInput ||
                quietWalkHeld != _lastSentQuietWalkHeld ||
                Time.unscaledTime >= _nextInputRefresh)
            {
                _lastSentInput = input;
                _lastSentQuietWalkHeld = quietWalkHeld;
                _nextInputRefresh = Time.unscaledTime + 0.1f;
                SubmitMovementRpc(input, _localYaw, quietWalkHeld);
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
            if (_boundaryWalls != null && _boundaryWallsActive)
            {
                _boundaryWalls.Hide();
            }

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
            NetworkMatchState.Instance?.TryRestoreAvatarOnServer(this);
            if (_boardReady.Value)
            {
                InitializeBoardStateOnServer();
            }

            return true;
        }

        private void EnsureTraversalInitialized()
        {
            ResolveTopology();
            if (_topology == null)
            {
                return;
            }

            var tile = _topology.FindContainingTile(transform.position, 0.1f);
            if (tile == null && _traversal.IsInitialized)
            {
                // A capsule may legitimately be inside the narrow gap while crossing a
                // directed gate. Preserve its logical source tile until the gate commits.
                return;
            }

            if (tile == null)
            {
                tile = FindStartTileForSlot(_slot.Value);
            }

            if (tile == null)
            {
                return;
            }

            if (!_traversal.IsInitialized || _traversal.CurrentTile != tile)
            {
                _traversal.Begin(tile, _remainingMoves.Value);
                SyncLogicalTileOnServer();
            }
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
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode _)
        {
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
            var meshRenderer = GetComponentInChildren<MeshRenderer>();
            if (meshRenderer == null || slot < 0 || slot >= PlayerColors.Length)
            {
                return;
            }

            var properties = new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", PlayerColors[slot]);
            properties.SetColor("_Color", PlayerColors[slot]);
            meshRenderer.SetPropertyBlock(properties);
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
            bool quietWalkHeld,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId ||
                float.IsNaN(input.x) || float.IsInfinity(input.x) ||
                float.IsNaN(input.y) || float.IsInfinity(input.y) ||
                float.IsNaN(yaw) || float.IsInfinity(yaw))
            {
                _serverInput = Vector2.zero;
                return;
            }

            var match = NetworkMatchState.Instance;
            var canUseActionInput = match != null && match.CanAcceptActionInput &&
                                    HasResolvedItemChoice;
            var canUseCombatInput = match != null && match.CanAvatarUseCombatInput(this);
            if (!canUseActionInput && !canUseCombatInput)
            {
                _serverInput = Vector2.zero;
                _serverQuietWalkHeld = false;
                _isQuietWalking.Value = false;
                return;
            }

            _serverInput = Vector2.ClampMagnitude(input, 1f);
            _serverQuietWalkHeld = quietWalkHeld;
            _serverYaw = Mathf.Repeat(yaw, 360f);
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
        private void UseSelectedItemRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                NetworkMatchState.Instance?.TryUseSelectedItemOnServer(this);
            }
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
