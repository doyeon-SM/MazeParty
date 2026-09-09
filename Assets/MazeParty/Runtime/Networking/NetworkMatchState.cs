using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.Minefield;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Host-authoritative online board timeline. Clients only submit intent through
    /// their owned avatar; phase changes, timers, dice and arrival are validated here.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkMatchState : NetworkBehaviour
    {
        // The MPS/Lobby backend Disconnect Removal Time must outlive transport
        // detection plus this grace. Configure 75-90 seconds, not exactly 60.
        public const double ReconnectGraceSeconds = 60d;
        public const double KeyShopRevealSeconds = 5d;
        public const double AllPlayersArrivalGraceSeconds = 3d;
        private const int AllPlayersMask = (1 << MultiplayerConstants.MaxPlayers) - 1;

        private readonly NetworkVariable<bool> _gameplayEnabled = new NetworkVariable<bool>();
        private readonly NetworkVariable<byte> _flowState =
            new NetworkVariable<byte>((byte)BoardFlowState.TurnOverview);
        private readonly NetworkVariable<int> _turn = new NetworkVariable<int>(1);
        private readonly NetworkVariable<int> _stateRevision = new NetworkVariable<int>();
        private readonly NetworkVariable<double> _stateEndsAt = new NetworkVariable<double>();
        private readonly NetworkVariable<double> _actionEndsAt = new NetworkVariable<double>();
        private readonly NetworkVariable<double> _choiceEndsAt = new NetworkVariable<double>();
        private readonly NetworkVariable<double> _shieldEndsAt = new NetworkVariable<double>();
        private readonly NetworkVariable<double> _arrivalGraceEndsAt =
            new NetworkVariable<double>();
        private readonly NetworkVariable<byte> _rolledMask = new NetworkVariable<byte>();
        private readonly NetworkVariable<byte> _arrivedMask = new NetworkVariable<byte>();
        private readonly NetworkVariable<byte> _readyMask = new NetworkVariable<byte>();
        private readonly NetworkVariable<byte> _presentMask = new NetworkVariable<byte>();
        private readonly NetworkVariable<byte> _snapshotRestoredMask = new NetworkVariable<byte>();
        private readonly NetworkVariable<byte> _lastActionEndReason =
            new NetworkVariable<byte>((byte)BoardActionEndReason.None);
        private readonly NetworkVariable<bool> _reconnectPaused = new NetworkVariable<bool>();
        private readonly NetworkVariable<double> _reconnectGraceEndsAt = new NetworkVariable<double>();
        private readonly NetworkVariable<double> _pausedStateRemaining = new NetworkVariable<double>();
        private readonly NetworkVariable<double> _pausedActionRemaining = new NetworkVariable<double>();
        private readonly NetworkVariable<double> _pausedChoiceRemaining = new NetworkVariable<double>();
        private readonly NetworkVariable<double> _pausedShieldRemaining = new NetworkVariable<double>();
        private readonly NetworkVariable<double> _pausedArrivalGraceRemaining =
            new NetworkVariable<double>();
        private readonly NetworkVariable<byte> _keyShopLifecycle =
            new NetworkVariable<byte>((byte)KeyShopLifecycleState.Inactive);
        private readonly NetworkVariable<bool> _keyShopHasLocation = new NetworkVariable<bool>();
        private readonly NetworkVariable<Vector2Int> _keyShopLocation =
            new NetworkVariable<Vector2Int>();
        private readonly NetworkVariable<int> _keyShopRevision = new NetworkVariable<int>();
        private readonly NetworkVariable<bool> _keyShopRevealActive = new NetworkVariable<bool>();
        private readonly NetworkVariable<double> _keyShopRevealEndsAt = new NetworkVariable<double>();
        private readonly NetworkVariable<int> _keyShopRevealRevision = new NetworkVariable<int>();
        private readonly NetworkVariable<ItemShopSnapshot> _itemShop0 =
            new NetworkVariable<ItemShopSnapshot>();
        private readonly NetworkVariable<ItemShopSnapshot> _itemShop1 =
            new NetworkVariable<ItemShopSnapshot>();
        private readonly NetworkVariable<int> _boardEffectSeed = new NetworkVariable<int>();
        private readonly NetworkVariable<int> _boardEffectRevision = new NetworkVariable<int>();
        private readonly NetworkVariable<bool> _combatActive = new NetworkVariable<bool>();
        private readonly NetworkVariable<Vector2Int> _combatTile =
            new NetworkVariable<Vector2Int>();
        private readonly NetworkVariable<byte> _combatParticipantMask =
            new NetworkVariable<byte>();
        private readonly NetworkVariable<byte> _combatAliveMask =
            new NetworkVariable<byte>();
        private readonly NetworkVariable<int> _combatSequenceIndex =
            new NetworkVariable<int>();
        private readonly NetworkVariable<int> _combatQueueCount =
            new NetworkVariable<int>();
        private readonly NetworkVariable<double> _combatEndsAt =
            new NetworkVariable<double>();
        private readonly NetworkVariable<double> _pausedCombatRemaining =
            new NetworkVariable<double>();

        private readonly ReconnectSnapshot[] _reconnectSnapshots =
            new ReconnectSnapshot[MultiplayerConstants.MaxPlayers];
        private BoardFlowStateMachine _flow;
        private bool _endingForReconnectTimeout;
        private int _arrivalGracePendingSlot = -1;
        private KeyShopRuntimeState _keyShopRuntime;
        private BoardTopology _boardTopology;
        private double _keyShopAppearanceEndsAt;
        private double _keyShopRevealRemainingDuringReconnect;
        private readonly ItemShopStock[] _itemShopStocks =
            new ItemShopStock[ItemShopRules.ShopCount];
        private NetworkWorldDiceCoordinator _diceCoordinator;
        private BoardLandingEffectLayout _boardEffectLayout;
        private int _cachedBoardEffectSeed;
        private int _cachedBoardEffectRevision = -1;
        private int _nextLandingEffectSlot;
        private readonly Queue<BoardCombatGroup> _combatQueue =
            new Queue<BoardCombatGroup>();
        private readonly double[] _arrivalTimes =
            new double[MultiplayerConstants.MaxPlayers];
        private readonly double[] _combatEliminatedAt =
            new double[MultiplayerConstants.MaxPlayers];
        private readonly double[] _nextPunchAllowedAt =
            new double[MultiplayerConstants.MaxPlayers];
        private int[] _combatOverallRanks = new int[MultiplayerConstants.MaxPlayers];
        private int _fightsResolvedThisTurn;
        private int _settledMinigameTurn = -1;
        private bool _minefieldNetworkLoadCompleted;
        private readonly NetworkPlayerAvatar[] _avatarLookupCache =
            new NetworkPlayerAvatar[MultiplayerConstants.MaxPlayers];
        private float _nextAvatarLookupRefresh;

        public static NetworkMatchState Instance { get; private set; }

        public bool GameplayEnabled => _gameplayEnabled.Value;
        public BoardFlowState FlowState => (BoardFlowState)_flowState.Value;
        public int Turn => _turn.Value;
        public int StateRevision => _stateRevision.Value;
        public bool IsReconnectPaused => _reconnectPaused.Value;
        public bool IsKeyShopRevealActive => _keyShopRevealActive.Value;
        public bool IsGlobalSimulationPaused => IsReconnectPaused || IsKeyShopRevealActive;
        public BoardActionEndReason LastActionEndReason =>
            (BoardActionEndReason)_lastActionEndReason.Value;
        public bool IsActionPhase => GameplayEnabled && FlowState == BoardFlowState.Action;
        public bool CanAcceptActionInput =>
            IsActionPhase && !IsGlobalSimulationPaused && ActionRemaining > 0d;
        public bool IsOpeningProtectionActive => ShieldRemaining > 0d;
        public double ArrivalGraceRemaining => RemainingUntil(
            _arrivalGraceEndsAt.Value,
            _pausedArrivalGraceRemaining.Value);
        public bool IsArrivalGraceActive =>
            IsActionPhase &&
            (_arrivedMask.Value & AllPlayersMask) == AllPlayersMask &&
            ArrivalGraceRemaining > 0d;
        public KeyShopLifecycleState KeyShopLifecycle =>
            (KeyShopLifecycleState)_keyShopLifecycle.Value;
        public bool KeyShopHasLocation => _keyShopHasLocation.Value;
        public Vector2Int KeyShopLocation => _keyShopLocation.Value;
        public int KeyShopRevision => _keyShopRevision.Value;
        public int KeyShopRevealRevision => _keyShopRevealRevision.Value;
        public int BoardEffectSeed => _boardEffectSeed.Value;
        public int BoardEffectRevision => _boardEffectRevision.Value;
        public bool IsCombatActive => _combatActive.Value;
        public Vector2Int CombatTile => _combatTile.Value;
        public byte CombatParticipantMask => _combatParticipantMask.Value;
        public byte CombatAliveMask => _combatAliveMask.Value;
        public int CombatSequenceIndex => _combatSequenceIndex.Value;
        public int CombatQueueCount => _combatQueueCount.Value;
        public bool IsCombatPhase =>
            GameplayEnabled && FlowState == BoardFlowState.CombatResolve;
        public bool IsMinefieldPhase =>
            GameplayEnabled &&
            (FlowState == BoardFlowState.MinigameLoading ||
             FlowState == BoardFlowState.MinigamePlaying ||
             FlowState == BoardFlowState.SkippedResult);
        public bool IsMinefieldPlaying =>
            GameplayEnabled && FlowState == BoardFlowState.MinigamePlaying;

        public static bool IsGameplayReady =>
            Instance != null && Instance.IsSpawned && Instance.GameplayEnabled &&
            !Instance.IsGlobalSimulationPaused;

        public double StateRemaining => RemainingUntil(_stateEndsAt.Value, _pausedStateRemaining.Value);
        public double ActionRemaining => RemainingUntil(_actionEndsAt.Value, _pausedActionRemaining.Value);
        public double ChoiceRemaining => RemainingUntil(_choiceEndsAt.Value, _pausedChoiceRemaining.Value);
        public double ShieldRemaining => RemainingUntil(_shieldEndsAt.Value, _pausedShieldRemaining.Value);
        public double CombatRemaining => RemainingUntil(
            _combatEndsAt.Value,
            _pausedCombatRemaining.Value);
        public double ReconnectRemaining => _reconnectPaused.Value
            ? Math.Max(0d, _reconnectGraceEndsAt.Value - ServerNow)
            : 0d;
        public double KeyShopRevealRemaining => _keyShopRevealActive.Value
            ? _reconnectPaused.Value
                ? Math.Max(0d, _keyShopRevealRemainingDuringReconnect)
                : Math.Max(0d, _keyShopRevealEndsAt.Value - ServerNow)
            : 0d;
        public double SynchronizedNow => ServerNow;

        public ItemShopSnapshot GetItemShopSnapshot(int shopIndex)
        {
            switch (shopIndex)
            {
                case 0: return _itemShop0.Value;
                case 1: return _itemShop1.Value;
                default: return default;
            }
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer)
            {
                EnsureFlowModel();
                EnsureKeyShopRuntime();
                ResolveWorldDiceCoordinator();
                RefreshPresentMask();
                if (NetworkManager != null && NetworkManager.SceneManager != null)
                {
                    NetworkManager.SceneManager.OnLoadEventCompleted +=
                        OnNetworkLoadEventCompleted;
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null && NetworkManager.SceneManager != null)
            {
                NetworkManager.SceneManager.OnLoadEventCompleted -=
                    OnNetworkLoadEventCompleted;
            }

            if (_flow != null)
            {
                _flow.Transitioned -= OnFlowTransitioned;
                _flow = null;
            }

            if (_diceCoordinator != null)
            {
                _diceCoordinator.DieSettledOnServer -= OnWorldDieSettledOnServer;
                _diceCoordinator = null;
            }

            if (Instance == this)
            {
                Instance = null;
            }

            Array.Clear(
                _avatarLookupCache,
                0,
                _avatarLookupCache.Length);
            _nextAvatarLookupRefresh = 0f;
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            ResolveWorldDiceCoordinator();
            if (!_gameplayEnabled.Value)
            {
                return;
            }

            if (_boardEffectRevision.Value <= 0)
            {
                InitializeBoardLandingEffectsOnServer();
            }

            RefreshPresentMask();
            var now = ServerNow;
            if (_reconnectPaused.Value)
            {
                StopAllAvatarInputOnServer();
                if (HasFourBoardReadyPlayers())
                {
                    ResumeAfterReconnectOnServer(now);
                }
                else if (!_endingForReconnectTimeout && now >= _reconnectGraceEndsAt.Value)
                {
                    _endingForReconnectTimeout = true;
                    OnlineSessionController.Instance?.EndActiveMatchForNetworkFailure(
                        "A player did not reconnect within 60 seconds. The fixed four-player match is ending.");
                }

                return;
            }

            if (_keyShopRevealActive.Value)
            {
                StopAllAvatarInputOnServer();
                AdvanceKeyShopLifecycleOnServer(now);
                if (_keyShopRevealEndsAt.Value > 0d && now >= _keyShopRevealEndsAt.Value)
                {
                    CompleteKeyShopRevealOnServer(now);
                }

                return;
            }

            EnsureFlowModel();
            _flow.Tick(now);
            AdvanceArrivalGraceOnServer(now);
            TryStartLoadedMinefieldOnServer(now);
            AdvanceCombatOnServer(now);
            AdvanceLandingEffectResolutionOnServer(now);
            ResolveExpiredPersonalChoicesOnServer(now);
            AdvanceKeyShopLifecycleOnServer(now);
        }

        public void EnableGameplayOnServer()
        {
            if (!IsServer || _gameplayEnabled.Value)
            {
                return;
            }

            EnsureFlowModel();
            EnsureKeyShopRuntime();
            _keyShopRuntime.ResetToInactive();
            _keyShopAppearanceEndsAt = 0d;
            _keyShopRevealActive.Value = false;
            _keyShopRevealEndsAt.Value = 0d;
            _keyShopRevealRemainingDuringReconnect = 0d;
            _itemShopStocks[0] = null;
            _itemShopStocks[1] = null;
            _itemShop0.Value = default;
            _itemShop1.Value = default;
            ResetCombatRuntimeOnServer();
            SyncKeyShopSnapshot();
            InitializeBoardLandingEffectsOnServer();
            var now = ServerNow;
            _flow.Start(now, 1);
            _gameplayEnabled.Value = true;
            _rolledMask.Value = 0;
            _arrivedMask.Value = 0;
            _arrivalGracePendingSlot = -1;
            _arrivalGraceEndsAt.Value = 0d;
            _pausedArrivalGraceRemaining.Value = 0d;
            _readyMask.Value = 0;
            _snapshotRestoredMask.Value = AllPlayersMask;
            _lastActionEndReason.Value = (byte)BoardActionEndReason.None;
            ResetArrivalTimes();
            InitializeAllAvatarsOnBoard();
            ForEachAvatar(avatar => avatar.PrepareForOverviewOnServer());
            RefreshItemShopsForTurnOnServer(1);
            SyncFlowSnapshot(now);
        }

        public bool TryResolveItemChoiceOnServer(NetworkPlayerAvatar avatar, int slotIndex, bool chooseNoItem)
        {
            if (!CanProcessActionRequest(avatar))
            {
                return false;
            }

            var now = ServerNow;
            if (_choiceEndsAt.Value <= 0d || now >= _choiceEndsAt.Value)
            {
                avatar.ResolveNoItemChoiceOnServer(true);
                return false;
            }

            return chooseNoItem
                ? avatar.ResolveNoItemChoiceOnServer(false)
                : avatar.ResolveItemChoiceOnServer(slotIndex);
        }

        public bool TryRollForAvatarOnServer(NetworkPlayerAvatar avatar)
        {
            // Direct/HUD rolling is intentionally disabled. The owner must aim at
            // their visible world die; its server-authoritative settle event supplies
            // the result to ApplyWorldDieResultOnServer.
            return false;
        }

        public bool ApplyWorldDieResultOnServer(int slot, int face)
        {
            if (!IsServer || slot < 0 || slot >= MultiplayerConstants.MaxPlayers ||
                face < WorldDieAuthorityModel.MinimumFace ||
                face > WorldDieAuthorityModel.MaximumFace || HasRolled(slot) || HasArrived(slot))
            {
                return false;
            }

            var avatar = GetAvatarForSlot(slot);
            if (!CanProcessActionRequest(avatar) || !avatar.HasResolvedItemChoice || avatar.HasRolled)
            {
                return false;
            }

            avatar.SetRollOnServer(face);
            _rolledMask.Value = (byte)(_rolledMask.Value | (1 << slot));
            return true;
        }

        public bool TryUseSelectedItemOnServer(
            NetworkPlayerAvatar avatar,
            Vector3 claimedOrigin,
            Vector3 claimedDirection)
        {
            if (!CanProcessActionRequest(avatar))
            {
                return false;
            }

            var item = avatar.GetSelectedItemOnServer();
            if (item == PrototypeItemId.None)
            {
                return false;
            }

            if (item == PrototypeItemId.PulseBlaster)
            {
                if (!IsFinite(claimedOrigin) || !IsFinite(claimedDirection) ||
                    claimedDirection.sqrMagnitude < 0.0001f)
                {
                    return false;
                }

                var authoritativeOrigin = avatar.EyePivot != null
                    ? avatar.EyePivot.position
                    : avatar.transform.position +
                      Vector3.up * PlayerAvatarVisual.StandingEyeHeight;
                if (Vector3.Distance(authoritativeOrigin, claimedOrigin) > 1.5f)
                {
                    return false;
                }

                var authoritativeDirection = avatar.EyePivot != null
                    ? avatar.EyePivot.forward.normalized
                    : avatar.transform.forward.normalized;
                if (Vector3.Dot(
                        authoritativeDirection,
                        claimedDirection.normalized) < 0.94f)
                {
                    return false;
                }

                var direction = authoritativeDirection;
                FirearmHitResolver.Raycast(
                    avatar.gameObject,
                    authoritativeOrigin,
                    direction,
                    FirearmDamageRules.PulseBlasterRange,
                    FirearmDamageRules.PulseBlasterBaseDamage,
                    direction * FirearmDamageRules.PulseBlasterPush + Vector3.up * 0.5f);
            }

            // Push Mine and Med Kit still use their existing prototype consume-only
            // behavior until their authoritative effects are designed.
            var consumed = avatar.ConsumeSelectedItemOnServer();
            if (consumed)
            {
                avatar.PresentItemUseOnServer(item);
            }
            return consumed;
        }

        public bool TryPurchaseKeyOnServer(
            NetworkPlayerAvatar avatar,
            int expectedRevision)
        {
            if (!CanProcessActionRequest(avatar) || _keyShopRuntime == null ||
                !_keyShopRuntime.IsActive || expectedRevision != _keyShopRevision.Value ||
                !PlayerStatRules.CanPurchaseKey(avatar.Gold) ||
                !CanAccessCoordinateOnServer(avatar, _keyShopRuntime.Location))
            {
                return false;
            }

            var blocked = GetOccupiedAndItemShopCoordinates();
            if (!_keyShopRuntime.TryBeginPurchaseRelocation(
                    _boardTopology.Tiles,
                    blocked,
                    out _))
            {
                return false;
            }

            if (!avatar.TryPurchaseKeyOnServer())
            {
                throw new InvalidOperationException(
                    "A validated key purchase failed after reserving its relocation.");
            }

            SyncKeyShopSnapshot();
            var now = ServerNow;
            _keyShopAppearanceEndsAt = now + 0.75d;
            BeginKeyShopRevealOnServer(now);
            return true;
        }

        public bool TryPurchaseItemOnServer(
            NetworkPlayerAvatar avatar,
            int shopIndex,
            int offerIndex,
            int expectedRevision)
        {
            if (!CanProcessActionRequest(avatar) ||
                shopIndex < 0 || shopIndex >= ItemShopRules.ShopCount ||
                offerIndex < 0 || offerIndex >= ItemShopRules.OfferCount)
            {
                return false;
            }

            var snapshot = GetItemShopSnapshot(shopIndex);
            var stock = _itemShopStocks[shopIndex];
            if (!snapshot.Active || stock == null || snapshot.Revision != expectedRevision ||
                snapshot.IsSold(offerIndex) || stock.IsSold(offerIndex) ||
                !CanAccessCoordinateOnServer(avatar, snapshot.Location) ||
                !avatar.HasFreeItemSlotOnServer)
            {
                return false;
            }

            var itemId = snapshot.GetOffer(offerIndex);
            if (!PrototypeItemCatalog.IsValid(itemId))
            {
                return false;
            }

            var price = PrototypeItemCatalog.Get(itemId).Price;
            if (!avatar.CanAfford(price) || !stock.TrySell(offerIndex))
            {
                return false;
            }

            if (!avatar.TryAddItemOnServer(itemId) || !avatar.TrySpendGoldOnServer(price))
            {
                throw new InvalidOperationException(
                    "A validated item-shop transaction failed after reserving its stock.");
            }

            SetItemShopSnapshot(shopIndex, snapshot.WithSoldMask(stock.SoldMask));
            return true;
        }

        public bool CanLocalAvatarAccessItemShop(NetworkPlayerAvatar avatar, int shopIndex)
        {
            if (avatar == null || !avatar.IsOwner || !CanAcceptActionInput ||
                shopIndex < 0 || shopIndex >= ItemShopRules.ShopCount)
            {
                return false;
            }

            var snapshot = GetItemShopSnapshot(shopIndex);
            if (!snapshot.Active || !avatar.HasLogicalBoardTile ||
                avatar.LogicalBoardTileCoordinate != snapshot.Location)
            {
                return false;
            }

            if (_boardTopology == null)
            {
                _boardTopology = FindAnyObjectByType<BoardTopology>();
            }

            return _boardTopology != null &&
                   _boardTopology.TryGetTile(snapshot.Location, out var tile) && tile != null &&
                   HorizontalDistance(avatar.transform.position, tile.WorldCenter) <=
                   ItemShopRules.InteractionDistance;
        }

        public bool TryReportPlayerArrivedOnServer(NetworkPlayerAvatar avatar)
        {
            if (!CanProcessActionRequest(avatar) ||
                HasArrived(avatar.AssignedSlot))
            {
                return false;
            }

            var slot = avatar.AssignedSlot;
            var arrivalTime = ServerNow;
            var previousArrival = _arrivalTimes[slot];
            _arrivalTimes[slot] = arrivalTime;
            var nextArrivedMask = (byte)(_arrivedMask.Value | (1 << slot));
            var completesAllPlayers =
                (nextArrivedMask & AllPlayersMask) == AllPlayersMask;
            if (!completesAllPlayers &&
                !_flow.TryReportPlayerArrived(slot, arrivalTime))
            {
                _arrivalTimes[slot] = previousArrival;
                return false;
            }

            _arrivedMask.Value = nextArrivedMask;
            if (FlowState == BoardFlowState.Action)
            {
                avatar.MarkArrivedOnServer();
            }

            if (completesAllPlayers)
            {
                // Keep the board in first-person for a short, visible grace
                // period. The pure flow model receives the fourth arrival only
                // when this authoritative deadline expires.
                _arrivalGracePendingSlot = slot;
                _arrivalGraceEndsAt.Value =
                    arrivalTime + AllPlayersArrivalGraceSeconds;
                _pausedArrivalGraceRemaining.Value = 0d;
                _stateRevision.Value++;
            }
            return true;
        }

        public bool TrySetMinigameReadyOnServer(NetworkPlayerAvatar avatar)
        {
            if (!CanProcessAvatarRequest(avatar) || FlowState != BoardFlowState.MinigameIntroReady)
            {
                return false;
            }

            _readyMask.Value = (byte)(_readyMask.Value | (1 << avatar.AssignedSlot));
            if ((_readyMask.Value & AllPlayersMask) == AllPlayersMask)
            {
                _flow.TryBeginMinigameLoading(ServerNow);
            }

            return true;
        }

        public bool TryCompleteMinefieldOnServer(
            IReadOnlyList<MinefieldLeaderboardEntry> leaderboard)
        {
            if (!IsServer || leaderboard == null ||
                leaderboard.Count != MultiplayerConstants.MaxPlayers ||
                FlowState != BoardFlowState.MinigamePlaying ||
                _settledMinigameTurn == Turn)
            {
                return false;
            }

            var seenSlots = 0;
            var seenRanks = 0;
            var rewardAvatars =
                new NetworkPlayerAvatar[MultiplayerConstants.MaxPlayers];
            for (var index = 0; index < leaderboard.Count; index++)
            {
                var entry = leaderboard[index];
                if (!MinefieldRules.IsValidPlayerSlot(entry.PlayerSlot) ||
                    entry.Rank < 1 || entry.Rank > MultiplayerConstants.MaxPlayers)
                {
                    return false;
                }

                var slotBit = 1 << entry.PlayerSlot;
                var rankBit = 1 << (entry.Rank - 1);
                if ((seenSlots & slotBit) != 0 || (seenRanks & rankBit) != 0)
                {
                    return false;
                }

                seenSlots |= slotBit;
                seenRanks |= rankBit;
                var avatar = GetAvatarForSlot(entry.PlayerSlot);
                if (avatar == null || !avatar.IsSpawned)
                {
                    return false;
                }

                rewardAvatars[entry.PlayerSlot] = avatar;
            }

            if (seenSlots != AllPlayersMask || seenRanks != AllPlayersMask)
            {
                return false;
            }

            if (!_flow.TryCompleteMinigame(ServerNow))
            {
                return false;
            }

            _settledMinigameTurn = Turn;
            for (var index = 0; index < leaderboard.Count; index++)
            {
                var entry = leaderboard[index];
                var avatar = rewardAvatars[entry.PlayerSlot];

                // Until a separate economy table is authored, the final placement
                // uses the same transparent 3/2/1/0 schedule as each round.
                avatar.ApplyGoldDeltaOnServer(MinefieldRules.GetPointsForRank(entry.Rank));
                if (entry.Rank == 1)
                {
                    avatar.AddMinigameWinOnServer();
                }
            }

            return true;
        }

        public void PauseForReconnectOnServer(ulong disconnectedClientId)
        {
            if (!IsServer || !_gameplayEnabled.Value)
            {
                return;
            }

            var playerObject = NetworkManager.SpawnManager != null
                ? NetworkManager.SpawnManager.GetPlayerNetworkObject(disconnectedClientId)
                : null;
            var avatar = playerObject != null ? playerObject.GetComponent<NetworkPlayerAvatar>() : null;
            if (avatar != null)
            {
                CaptureDisconnectedAvatarOnServer(avatar);
            }

            if (_reconnectPaused.Value)
            {
                RefreshPresentMask();
                _snapshotRestoredMask.Value = (byte)(
                    _snapshotRestoredMask.Value & _presentMask.Value & AllPlayersMask);
                return;
            }

            EnsureFlowModel();
            var now = ServerNow;
            NetworkMinefieldState.Instance?.PauseOnServer(now);
            PauseCombatAndPersonalProtectionOnServer(now);
            if (_keyShopRevealActive.Value)
            {
                _keyShopRevealRemainingDuringReconnect =
                    Math.Max(0d, _keyShopRevealEndsAt.Value - now);
                _keyShopRevealEndsAt.Value = 0d;
                // The flow was already paused for the reveal. Preserve the
                // original action, choice, and shield remainders so nesting a
                // reconnect pause cannot consume any of those clocks.
            }
            else
            {
                _flow.Pause(now);
                _pausedStateRemaining.Value = _flow.GetStateRemaining(now);
                _pausedActionRemaining.Value = _flow.GetActionRemaining(now);
                _pausedChoiceRemaining.Value = GetPersonalChoiceRemainingOnServer(now);
                _pausedShieldRemaining.Value = _flow.GetOpeningProtectionRemaining(now);
                _pausedArrivalGraceRemaining.Value =
                    _arrivalGraceEndsAt.Value > 0d
                        ? Math.Max(0d, _arrivalGraceEndsAt.Value - now)
                        : 0d;
                _arrivalGraceEndsAt.Value = 0d;
            }
            RefreshPresentMask();
            _snapshotRestoredMask.Value = (byte)(_presentMask.Value & AllPlayersMask);
            _reconnectPaused.Value = true;
            _reconnectGraceEndsAt.Value = now + ReconnectGraceSeconds;
            StopAllAvatarInputOnServer();
        }

        public void CaptureDisconnectedAvatarOnServer(NetworkPlayerAvatar avatar)
        {
            if (!IsServer || avatar == null)
            {
                return;
            }

            var slot = avatar.AssignedSlot;
            if (slot < 0 || slot >= _reconnectSnapshots.Length)
            {
                return;
            }

            _reconnectSnapshots[slot] = avatar.CreateReconnectSnapshotOnServer();
            _snapshotRestoredMask.Value = (byte)(_snapshotRestoredMask.Value & ~(1 << slot));
            _presentMask.Value = (byte)(_presentMask.Value & ~(1 << slot));
            _readyMask.Value = (byte)(_readyMask.Value & ~(1 << slot));
        }

        public bool TryRestoreAvatarOnServer(NetworkPlayerAvatar avatar)
        {
            if (!IsServer || avatar == null)
            {
                return false;
            }

            var slot = avatar.AssignedSlot;
            if (slot < 0 || slot >= _reconnectSnapshots.Length)
            {
                return false;
            }

            var snapshot = _reconnectSnapshots[slot];
            if (!snapshot.IsValid)
            {
                return false;
            }

            if (!avatar.RestoreReconnectSnapshotOnServer(snapshot))
            {
                return false;
            }

            _reconnectSnapshots[slot] = default;
            _snapshotRestoredMask.Value = (byte)(_snapshotRestoredMask.Value | (1 << slot));
            if (avatar.HasRolled)
            {
                _rolledMask.Value = (byte)(_rolledMask.Value | (1 << slot));
            }

            return true;
        }

        public void NotifyAvatarBoardReadyOnServer(NetworkPlayerAvatar avatar)
        {
            if (!IsServer || avatar == null)
            {
                return;
            }

            RefreshPresentMask();
            NetworkMinefieldState.Instance?.RestoreAvatarForReconnectOnServer(avatar);
            if (_reconnectPaused.Value && HasFourBoardReadyPlayers())
            {
                ResumeAfterReconnectOnServer(ServerNow);
            }
        }

        public bool HasRolled(int slot) => IsSlotSet(_rolledMask.Value, slot);
        public bool HasArrived(int slot) => IsSlotSet(_arrivedMask.Value, slot);
        public bool IsMinigameReady(int slot) => IsSlotSet(_readyMask.Value, slot);
        public bool IsPlayerPresent(int slot) => IsSlotSet(_presentMask.Value, slot);

        public bool IsCombatParticipant(int slot)
        {
            return IsSlotSet(_combatParticipantMask.Value, slot);
        }

        public bool IsCombatAlive(int slot)
        {
            return IsSlotSet(_combatAliveMask.Value, slot);
        }

        public bool CanAvatarUseCombatInput(NetworkPlayerAvatar avatar)
        {
            return avatar != null && avatar.IsSpawned &&
                   IsCombatPhase && IsCombatActive && !IsGlobalSimulationPaused &&
                   IsCombatParticipant(avatar.AssignedSlot) &&
                   IsCombatAlive(avatar.AssignedSlot) && avatar.IsCombatAlive;
        }

        private void EnsureFlowModel()
        {
            if (_flow != null)
            {
                return;
            }

            _flow = new BoardFlowStateMachine();
            _flow.Transitioned += OnFlowTransitioned;
        }

        private void OnFlowTransitioned(BoardFlowTransition transition)
        {
            if (!IsServer)
            {
                return;
            }

            switch (transition.Current)
            {
                case BoardFlowState.TurnOverview:
                    NetworkMinefieldState.Instance?.EndMatchOnServer();
                    ResetCombatRuntimeOnServer();
                    _rolledMask.Value = 0;
                    _arrivedMask.Value = 0;
                    ClearArrivalGraceOnServer();
                    _readyMask.Value = 0;
                    ForEachAvatar(avatar => avatar.PrepareForOverviewOnServer());
                    RefreshItemShopsForTurnOnServer(transition.Turn);
                    TryBeginInitialKeyShopPlacementOnServer(transition.Turn);
                    break;
                case BoardFlowState.Action:
                    ResetArrivalTimes();
                    _rolledMask.Value = 0;
                    _arrivedMask.Value = 0;
                    ClearArrivalGraceOnServer();
                    _readyMask.Value = 0;
                    ForEachAvatar(avatar => avatar.BeginActionOnServer(transition.Turn));
                    break;
                case BoardFlowState.AscendingResolve:
                    if (_flow.LastActionEndReason == BoardActionEndReason.TimeExpired)
                    {
                        ForceTimedOutPlayersOneTileOnServer();
                    }
                    ClearArrivalGraceOnServer();
                    FillMissingArrivalTimes(transition.OccurredAt);
                    ForEachAvatar(avatar => avatar.EndActionOnServer());
                    break;
                case BoardFlowState.CombatResolve:
                    BeginCombatSequenceOnServer(transition.OccurredAt);
                    break;
                case BoardFlowState.LandingEffectResolve:
                    BeginLandingEffectsOnServer();
                    break;
                case BoardFlowState.MinigameIntroReady:
                    ResolveAllRemainingLandingEffectsOnServer();
                    _readyMask.Value = 0;
                    break;
                case BoardFlowState.MinigameLoading:
                    StopAllAvatarInputOnServer();
                    RequestMinefieldLoadOnServer();
                    break;
                case BoardFlowState.MinigamePlaying:
                    StopAllAvatarInputOnServer();
                    break;
                case BoardFlowState.SkippedResult:
                    StopAllAvatarInputOnServer();
                    break;
            }

            _lastActionEndReason.Value = (byte)_flow.LastActionEndReason;
            SyncFlowSnapshot(ServerNow);
        }

        private void SyncFlowSnapshot(double now)
        {
            _flowState.Value = (byte)_flow.State;
            _turn.Value = _flow.CurrentTurn;
            _stateEndsAt.Value = Deadline(now, _flow.GetStateRemaining(now));
            _actionEndsAt.Value = Deadline(now, _flow.GetActionRemaining(now));
            _choiceEndsAt.Value = Deadline(now, _flow.GetChoiceRemaining(now));
            _shieldEndsAt.Value = Deadline(now, _flow.GetOpeningProtectionRemaining(now));
            _lastActionEndReason.Value = (byte)_flow.LastActionEndReason;
            _stateRevision.Value++;
        }

        private void ResumeAfterReconnectOnServer(double now)
        {
            if (!_reconnectPaused.Value)
            {
                return;
            }

            EnsureFlowModel();
            _reconnectPaused.Value = false;
            _reconnectGraceEndsAt.Value = 0d;
            _endingForReconnectTimeout = false;
            if (_keyShopRevealActive.Value)
            {
                _keyShopRevealEndsAt.Value = now +
                                             Math.Max(0d, _keyShopRevealRemainingDuringReconnect);
                _keyShopRevealRemainingDuringReconnect = 0d;
            }
            else
            {
                _flow.Resume(now);
                ResumeCombatAndPersonalProtectionOnServer(now);
                if (_arrivalGracePendingSlot >= 0 &&
                    _pausedArrivalGraceRemaining.Value > 0d)
                {
                    _arrivalGraceEndsAt.Value =
                        now + _pausedArrivalGraceRemaining.Value;
                }
                _pausedArrivalGraceRemaining.Value = 0d;
            }
            NetworkMinefieldState.Instance?.ResumeOnServer(now);
            SyncFlowSnapshot(now);
            ForEachAvatar(avatar => avatar.StopServerInputOnServer());
            TryStartLoadedMinefieldOnServer(now);
        }

        private void RequestMinefieldLoadOnServer()
        {
            if (!IsServer || NetworkManager == null || NetworkManager.SceneManager == null)
            {
                return;
            }

            var scene = SceneManager.GetSceneByName(MultiplayerConstants.MinefieldScene);
            if (scene.IsValid() && scene.isLoaded)
            {
                _minefieldNetworkLoadCompleted = true;
                return;
            }

            _minefieldNetworkLoadCompleted = false;
            var status = NetworkManager.SceneManager.LoadScene(
                MultiplayerConstants.MinefieldScene,
                LoadSceneMode.Additive);
            if (status != SceneEventProgressStatus.Started)
            {
                OnlineSessionController.Instance?.EndActiveMatchForNetworkFailure(
                    "Could not synchronize the Minefield minigame scene: " + status);
            }
        }

        private void OnNetworkLoadEventCompleted(
            string sceneName,
            LoadSceneMode _,
            List<ulong> clientsCompleted,
            List<ulong> clientsTimedOut)
        {
            if (!IsServer || sceneName != MultiplayerConstants.MinefieldScene)
            {
                return;
            }

            var scene = SceneManager.GetSceneByName(MultiplayerConstants.MinefieldScene);
            _minefieldNetworkLoadCompleted = scene.IsValid() && scene.isLoaded;
            if (!_minefieldNetworkLoadCompleted)
            {
                OnlineSessionController.Instance?.EndActiveMatchForNetworkFailure(
                    "Minefield scene synchronization completed without a loaded scene.");
                return;
            }

            // A disconnected client may appear in clientsTimedOut while the global
            // reconnect pause is active. The replacement client is synchronized to
            // every server-loaded scene before its Board-ready handshake completes.
            if (!_reconnectPaused.Value && clientsTimedOut != null &&
                clientsTimedOut.Count > 0)
            {
                OnlineSessionController.Instance?.EndActiveMatchForNetworkFailure(
                    "Minefield scene synchronization timed out for a player.");
            }
        }

        private void TryStartLoadedMinefieldOnServer(double now)
        {
            if (!IsServer || _flow == null || _flow.State != BoardFlowState.MinigameLoading ||
                _flow.IsPaused || _reconnectPaused.Value || !_minefieldNetworkLoadCompleted ||
                !HasFourBoardReadyPlayers())
            {
                return;
            }

            var minefield = NetworkMinefieldState.Instance;
            if (minefield == null || !minefield.IsSpawned)
            {
                return;
            }

            if (_flow.TryBeginMinigame(now))
            {
                minefield.BeginMatchOnServer();
            }
        }

        private void ResolveExpiredPersonalChoicesOnServer(double now)
        {
            if (FlowState != BoardFlowState.Action || _choiceEndsAt.Value <= 0d || now < _choiceEndsAt.Value)
            {
                return;
            }

            ForEachAvatar(avatar => avatar.ResolveNoItemChoiceOnServer(true));
        }

        private double GetPersonalChoiceRemainingOnServer(double now)
        {
            return FlowState == BoardFlowState.Action && _choiceEndsAt.Value > 0d
                ? Math.Max(0d, _choiceEndsAt.Value - now)
                : 0d;
        }

        private void InitializeAllAvatarsOnBoard()
        {
            ForEachAvatar(avatar =>
            {
                avatar.ResetMatchStatsOnServer();
                avatar.InitializeBoardStateOnServer();
            });
            RefreshPresentMask();
        }

        private void StopAllAvatarInputOnServer()
        {
            ForEachAvatar(avatar => avatar.StopServerInputOnServer());
        }

        private void AdvanceArrivalGraceOnServer(double now)
        {
            if (!IsServer || FlowState != BoardFlowState.Action ||
                _arrivalGracePendingSlot < 0 ||
                _arrivalGraceEndsAt.Value <= 0d ||
                now < _arrivalGraceEndsAt.Value)
            {
                return;
            }

            var pendingSlot = _arrivalGracePendingSlot;
            ClearArrivalGraceOnServer();
            _flow.TryReportPlayerArrived(pendingSlot, now);
        }

        private void ClearArrivalGraceOnServer()
        {
            _arrivalGracePendingSlot = -1;
            _arrivalGraceEndsAt.Value = 0d;
            _pausedArrivalGraceRemaining.Value = 0d;
        }

        private void ForceTimedOutPlayersOneTileOnServer()
        {
            ForEachAvatar(avatar =>
            {
                var slot = avatar.AssignedSlot;
                if (!HasArrived(slot))
                {
                    avatar.ForceAdvanceOneTileOnServer(_turn.Value);
                    _arrivedMask.Value = (byte)(_arrivedMask.Value | (1 << slot));
                    avatar.MarkArrivedOnServer();
                }
            });
        }

        private void RefreshPresentMask()
        {
            if (!IsServer || NetworkManager == null || NetworkManager.SpawnManager == null)
            {
                return;
            }

            var mask = 0;
            foreach (var clientId in NetworkManager.ConnectedClientsIds)
            {
                var playerObject = NetworkManager.SpawnManager.GetPlayerNetworkObject(clientId);
                var avatar = playerObject != null ? playerObject.GetComponent<NetworkPlayerAvatar>() : null;
                if (avatar == null || !avatar.IsSpawned || !avatar.IsBoardReady ||
                    avatar.AssignedSlot < 0 || avatar.AssignedSlot >= MultiplayerConstants.MaxPlayers)
                {
                    continue;
                }

                mask |= 1 << avatar.AssignedSlot;
            }

            var next = (byte)mask;
            if (_presentMask.Value != next)
            {
                _presentMask.Value = next;
            }
        }

        private bool HasFourBoardReadyPlayers()
        {
            RefreshPresentMask();
            return (_presentMask.Value & AllPlayersMask) == AllPlayersMask &&
                   (_snapshotRestoredMask.Value & AllPlayersMask) == AllPlayersMask;
        }

        private bool CanProcessAvatarRequest(NetworkPlayerAvatar avatar)
        {
            return IsServer && _gameplayEnabled.Value && !IsGlobalSimulationPaused &&
                   avatar != null && avatar.IsSpawned && avatar.IsBoardReady &&
                   avatar.AssignedSlot >= 0 && avatar.AssignedSlot < MultiplayerConstants.MaxPlayers;
        }

        private bool CanProcessActionRequest(NetworkPlayerAvatar avatar)
        {
            return CanProcessAvatarRequest(avatar) &&
                   FlowState == BoardFlowState.Action &&
                   ActionRemaining > 0d;
        }

        private void ForEachAvatar(Action<NetworkPlayerAvatar> action)
        {
            if (NetworkManager == null || NetworkManager.SpawnManager == null || action == null)
            {
                return;
            }

            foreach (var clientId in NetworkManager.ConnectedClientsIds)
            {
                var playerObject = NetworkManager.SpawnManager.GetPlayerNetworkObject(clientId);
                var avatar = playerObject != null ? playerObject.GetComponent<NetworkPlayerAvatar>() : null;
                if (avatar != null && avatar.IsSpawned)
                {
                    action(avatar);
                }
            }
        }

        public NetworkPlayerAvatar GetAvatarForSlot(int slot)
        {
            if (slot < 0 || slot >= MultiplayerConstants.MaxPlayers)
            {
                return null;
            }

            var cached = _avatarLookupCache[slot];
            if (cached != null && cached.IsSpawned && cached.AssignedSlot == slot)
            {
                return cached;
            }

            if (Time.unscaledTime < _nextAvatarLookupRefresh)
            {
                return null;
            }

            _nextAvatarLookupRefresh = Time.unscaledTime + 0.1f;
            Array.Clear(
                _avatarLookupCache,
                0,
                _avatarLookupCache.Length);

            // Client-side ConnectedClientsIds is not a complete remote-avatar
            // registry. Refresh every slot in one scene search, then reuse the
            // references for Minefield and the public Board stat panels.
            var avatars = FindObjectsByType<NetworkPlayerAvatar>();
            for (var i = 0; i < avatars.Length; i++)
            {
                var avatar = avatars[i];
                if (avatar == null || !avatar.IsSpawned ||
                    avatar.AssignedSlot < 0 ||
                    avatar.AssignedSlot >= _avatarLookupCache.Length)
                {
                    continue;
                }

                _avatarLookupCache[avatar.AssignedSlot] = avatar;
            }

            return _avatarLookupCache[slot];
        }

        public bool TryGetBoardLandingEffect(
            Vector2Int coordinate,
            out BoardLandingEffectType effect)
        {
            if (EnsureBoardLandingEffectLayout())
            {
                return _boardEffectLayout.TryGetEffect(coordinate, out effect);
            }

            effect = BoardLandingEffectType.None;
            return false;
        }

        private void InitializeBoardLandingEffectsOnServer()
        {
            if (!IsServer)
            {
                return;
            }

            if (_boardTopology == null)
            {
                _boardTopology = FindAnyObjectByType<BoardTopology>();
            }
            if (_boardTopology == null)
            {
                return;
            }

            _boardEffectSeed.Value = UnityEngine.Random.Range(1, int.MaxValue);
            _boardEffectRevision.Value++;
            _cachedBoardEffectRevision = -1;
            EnsureBoardLandingEffectLayout();
        }

        private bool EnsureBoardLandingEffectLayout()
        {
            if (_boardEffectRevision.Value <= 0)
            {
                return false;
            }

            if (_boardTopology == null)
            {
                _boardTopology = FindAnyObjectByType<BoardTopology>();
            }
            if (_boardTopology == null)
            {
                return false;
            }

            if (_boardEffectLayout != null &&
                _cachedBoardEffectSeed == _boardEffectSeed.Value &&
                _cachedBoardEffectRevision == _boardEffectRevision.Value)
            {
                return true;
            }

            _boardEffectLayout = BoardLandingEffectLayout.Create(
                _boardTopology.Tiles,
                _boardEffectSeed.Value);
            _cachedBoardEffectSeed = _boardEffectSeed.Value;
            _cachedBoardEffectRevision = _boardEffectRevision.Value;
            return true;
        }

        private void ResetArrivalTimes()
        {
            for (var slot = 0; slot < _arrivalTimes.Length; slot++)
            {
                _arrivalTimes[slot] = double.NaN;
            }
        }

        private void FillMissingArrivalTimes(double now)
        {
            for (var slot = 0; slot < _arrivalTimes.Length; slot++)
            {
                if (double.IsNaN(_arrivalTimes[slot]) ||
                    double.IsInfinity(_arrivalTimes[slot]))
                {
                    _arrivalTimes[slot] = now;
                }
            }
        }

        private void BeginCombatSequenceOnServer(double now)
        {
            ResetCombatRuntimeOnServer();
            FillMissingArrivalTimes(now);
            CalculateOverallRanksOnServer();

            var placements = new List<BoardCombatPlacement>(
                MultiplayerConstants.MaxPlayers);
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                var avatar = GetAvatarForSlot(slot);
                if (avatar == null || !avatar.HasLogicalBoardTile)
                {
                    continue;
                }

                placements.Add(new BoardCombatPlacement(
                    slot,
                    avatar.LogicalBoardTileCoordinate,
                    _arrivalTimes[slot]));
            }

            var initialQueue = BoardCombatRules.BuildInitialQueue(
                placements,
                _combatOverallRanks);
            for (var i = 0; i < initialQueue.Count; i++)
            {
                _combatQueue.Enqueue(initialQueue[i]);
            }

            _combatQueueCount.Value = _combatQueue.Count;
            BeginNextCombatOrFinishOnServer(now);
        }

        private void CalculateOverallRanksOnServer()
        {
            var stats = new PlayerRankingStats[MultiplayerConstants.MaxPlayers];
            for (var slot = 0; slot < stats.Length; slot++)
            {
                var avatar = GetAvatarForSlot(slot);
                stats[slot] = avatar != null
                    ? new PlayerRankingStats(
                        avatar.KeyCount,
                        avatar.Gold,
                        avatar.MinigameWins)
                    : default;
            }

            _combatOverallRanks = PlayerRankingRules.Calculate(stats);
        }

        private void BeginNextCombatOrFinishOnServer(double now)
        {
            while (_combatQueue.Count > 0 &&
                   _fightsResolvedThisTurn < BoardCombatRules.MaximumFightsPerTurn)
            {
                var group = _combatQueue.Dequeue();
                var participantMask = GetOccupantMaskAt(group.Coordinate);
                _combatQueueCount.Value = _combatQueue.Count;
                if (!HasMultipleSlots(participantMask))
                {
                    continue;
                }

                _combatTile.Value = group.Coordinate;
                _combatParticipantMask.Value = participantMask;
                _combatAliveMask.Value = participantMask;
                _combatSequenceIndex.Value++;
                _combatEndsAt.Value = now + BoardCombatRules.FightDurationSeconds;
                _pausedCombatRemaining.Value = 0d;
                for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                {
                    _combatEliminatedAt[slot] = double.NaN;
                    _nextPunchAllowedAt[slot] = now;
                }

                _combatActive.Value = true;
                ForEachAvatar(avatar =>
                    avatar.BeginCombatOnServer(IsSlotSet(
                        participantMask,
                        avatar.AssignedSlot)));
                StopAllAvatarInputOnServer();
                _stateRevision.Value++;
                return;
            }

            ResetCurrentCombatOnServer();
            _combatQueue.Clear();
            _combatQueueCount.Value = 0;
            _flow.TryCompleteCombat(now);
        }

        public bool TryCombatPunchOnServer(
            NetworkPlayerAvatar attacker,
            Vector3 claimedOrigin,
            Vector3 claimedDirection)
        {
            if (!IsServer || attacker == null ||
                !CanAvatarUseCombatInput(attacker) ||
                !IsFinite(claimedOrigin) || !IsFinite(claimedDirection))
            {
                return false;
            }

            var slot = attacker.AssignedSlot;
            var now = ServerNow;
            if (now < _nextPunchAllowedAt[slot] ||
                claimedDirection.sqrMagnitude < 0.0001f ||
                !attacker.HasLogicalBoardTile ||
                attacker.LogicalBoardTileCoordinate != _combatTile.Value)
            {
                return false;
            }

            var authoritativeOrigin = attacker.EyePivot != null
                ? attacker.EyePivot.position
                : attacker.transform.position + Vector3.up * 0.75f;
            if (Vector3.Distance(authoritativeOrigin, claimedOrigin) > 1.5f)
            {
                return false;
            }

            var authoritativeDirection = attacker.EyePivot != null
                ? attacker.EyePivot.forward.normalized
                : attacker.transform.forward.normalized;
            if (Vector3.Dot(
                    authoritativeDirection,
                    claimedDirection.normalized) < 0.94f)
            {
                return false;
            }

            _nextPunchAllowedAt[slot] = now + BoardCombatRules.PunchCooldownSeconds;
            attacker.PresentPunchOnServer();
            var direction = authoritativeDirection;
            var hits = Physics.SphereCastAll(
                authoritativeOrigin,
                BoardCombatRules.PunchRadius,
                direction,
                BoardCombatRules.PunchRange,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            NetworkPlayerAvatar target = null;
            var closestDistance = float.MaxValue;
            for (var i = 0; i < hits.Length; i++)
            {
                var candidate = hits[i].collider != null
                    ? hits[i].collider.GetComponentInParent<NetworkPlayerAvatar>()
                    : null;
                if (candidate == null || candidate == attacker ||
                    !IsCombatParticipant(candidate.AssignedSlot) ||
                    !IsCombatAlive(candidate.AssignedSlot) ||
                    candidate.LogicalBoardTileCoordinate != _combatTile.Value ||
                    hits[i].distance >= closestDistance)
                {
                    continue;
                }

                target = candidate;
                closestDistance = hits[i].distance;
            }

            if (target == null)
            {
                return true;
            }

            var knockbackDirection = direction;
            knockbackDirection.y = 0f;
            if (knockbackDirection.sqrMagnitude < 0.0001f)
            {
                knockbackDirection = attacker.transform.forward;
                knockbackDirection.y = 0f;
            }
            knockbackDirection.Normalize();

            var eliminated = target.ApplyCombatPunchOnServer(
                knockbackDirection * BoardCombatRules.PunchKnockbackSpeed);
            if (!eliminated)
            {
                return true;
            }

            var targetSlot = target.AssignedSlot;
            _combatEliminatedAt[targetSlot] = now;
            _combatAliveMask.Value = (byte)(
                _combatAliveMask.Value & ~(1 << targetSlot));
            _stateRevision.Value++;
            if (BoardCombatRules.IsFightComplete(
                    _combatParticipantMask.Value,
                    _combatAliveMask.Value))
            {
                ResolveCurrentCombatOnServer(now);
            }
            return true;
        }

        public bool TryBoardPunchOnServer(
            NetworkPlayerAvatar attacker,
            Vector3 claimedOrigin,
            Vector3 claimedDirection)
        {
            if (!IsServer || !CanProcessActionRequest(attacker) ||
                attacker.GetSelectedItemOnServer() != PrototypeItemId.None ||
                !IsFinite(claimedOrigin) || !IsFinite(claimedDirection) ||
                claimedDirection.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            var slot = attacker.AssignedSlot;
            if (slot < 0 || slot >= MultiplayerConstants.MaxPlayers)
            {
                return false;
            }

            var authoritativeOrigin = attacker.EyePivot != null
                ? attacker.EyePivot.position
                : attacker.transform.position +
                  Vector3.up * PlayerAvatarVisual.StandingEyeHeight;
            if (Vector3.Distance(authoritativeOrigin, claimedOrigin) > 1.5f)
            {
                return false;
            }

            var authoritativeDirection = attacker.EyePivot != null
                ? attacker.EyePivot.forward.normalized
                : attacker.transform.forward.normalized;
            if (Vector3.Dot(authoritativeDirection, claimedDirection.normalized) < 0.94f)
            {
                return false;
            }

            var now = ServerNow;
            if (now < _nextPunchAllowedAt[slot])
            {
                return false;
            }

            _nextPunchAllowedAt[slot] = now + PlayerUnarmedRules.PunchCooldownSeconds;
            attacker.PresentPunchOnServer();

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
                if (collider == null || collider.transform == attacker.transform ||
                    collider.transform.IsChildOf(attacker.transform))
                {
                    continue;
                }

                var zone = collider.GetComponent<PlayerHitZone>();
                if (zone != null)
                {
                    var target = collider.GetComponentInParent<NetworkPlayerAvatar>();
                    if (target != null && target != attacker && target.IsSpawned)
                    {
                        target.PresentHitReactionOnServer(zone.Region);
                        return true;
                    }
                    continue;
                }

                // The movement collider is not a hit part. It must not mask the
                // smaller body/head/hand trigger volumes on the same player.
                if (collider.GetComponentInParent<PlayerHitZoneOwner>() != null)
                {
                    continue;
                }

                if (!collider.isTrigger)
                {
                    break;
                }
            }

            return true;
        }

        private void AdvanceCombatOnServer(double now)
        {
            if (!IsServer || !_combatActive.Value || IsGlobalSimulationPaused ||
                _combatEndsAt.Value <= 0d || now < _combatEndsAt.Value)
            {
                return;
            }

            ResolveCurrentCombatOnServer(now);
        }

        private void ResolveCurrentCombatOnServer(double now)
        {
            if (!IsServer || !_combatActive.Value)
            {
                return;
            }

            var entries = new List<BoardCombatRankingEntry>(
                MultiplayerConstants.MaxPlayers);
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                if (!IsCombatParticipant(slot))
                {
                    continue;
                }

                var avatar = GetAvatarForSlot(slot);
                entries.Add(new BoardCombatRankingEntry(
                    slot,
                    avatar != null ? avatar.CombatHealth : 0,
                    _combatEliminatedAt[slot],
                    _arrivalTimes[slot],
                    _combatOverallRanks[slot]));
            }

            var standings = BoardCombatRules.ResolveStandings(entries);
            var possibleChainTiles = new List<Vector2Int>(standings.Length);
            for (var index = 0; index < standings.Length; index++)
            {
                var standing = standings[index];
                var avatar = GetAvatarForSlot(standing.Slot);
                if (avatar == null)
                {
                    continue;
                }

                avatar.ApplyCombatRetreatOnServer(standing.RetreatDistance);
                if (avatar.HasLogicalBoardTile &&
                    !possibleChainTiles.Contains(avatar.LogicalBoardTileCoordinate))
                {
                    possibleChainTiles.Add(avatar.LogicalBoardTileCoordinate);
                }
                avatar.EndCombatOnServer(true);
            }

            _fightsResolvedThisTurn++;
            ResetCurrentCombatOnServer();
            for (var i = 0; i < possibleChainTiles.Count; i++)
            {
                EnqueueChainFightIfNeeded(possibleChainTiles[i], now);
            }
            _combatQueueCount.Value = _combatQueue.Count;
            BeginNextCombatOrFinishOnServer(now);
        }

        private void EnqueueChainFightIfNeeded(Vector2Int coordinate, double formedAt)
        {
            var mask = GetOccupantMaskAt(coordinate);
            if (!HasMultipleSlots(mask) || QueueContainsCoordinate(coordinate))
            {
                return;
            }

            var lowestOverallRank = 1;
            for (var slot = 0; slot < _combatOverallRanks.Length; slot++)
            {
                lowestOverallRank = Math.Max(
                    lowestOverallRank,
                    _combatOverallRanks[slot]);
            }

            var containsLowestRank = false;
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                containsLowestRank |= IsSlotSet(mask, slot) &&
                                      _combatOverallRanks[slot] == lowestOverallRank;
            }
            _combatQueue.Enqueue(new BoardCombatGroup(
                coordinate,
                mask,
                formedAt,
                containsLowestRank));
        }

        private bool QueueContainsCoordinate(Vector2Int coordinate)
        {
            foreach (var queued in _combatQueue)
            {
                if (queued.Coordinate == coordinate)
                {
                    return true;
                }
            }
            return false;
        }

        private byte GetOccupantMaskAt(Vector2Int coordinate)
        {
            byte mask = 0;
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                var avatar = GetAvatarForSlot(slot);
                if (avatar != null && avatar.HasLogicalBoardTile &&
                    avatar.LogicalBoardTileCoordinate == coordinate)
                {
                    mask = (byte)(mask | (1 << slot));
                }
            }
            return mask;
        }

        private void ResetCombatRuntimeOnServer()
        {
            _combatQueue.Clear();
            _fightsResolvedThisTurn = 0;
            _combatSequenceIndex.Value = 0;
            _combatQueueCount.Value = 0;
            ResetCurrentCombatOnServer();
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                _combatEliminatedAt[slot] = double.NaN;
                _nextPunchAllowedAt[slot] = 0d;
            }
            ForEachAvatar(avatar => avatar.EndCombatOnServer(false));
        }

        private void ResetCurrentCombatOnServer()
        {
            _combatActive.Value = false;
            _combatParticipantMask.Value = 0;
            _combatAliveMask.Value = 0;
            _combatEndsAt.Value = 0d;
            _pausedCombatRemaining.Value = 0d;
        }

        private void PauseCombatAndPersonalProtectionOnServer(double now)
        {
            if (_combatActive.Value && _combatEndsAt.Value > 0d)
            {
                _pausedCombatRemaining.Value = Math.Max(
                    0d,
                    _combatEndsAt.Value - now);
                _combatEndsAt.Value = 0d;
            }
            ForEachAvatar(avatar => avatar.PausePersonalProtectionOnServer(now));
        }

        private void ResumeCombatAndPersonalProtectionOnServer(double now)
        {
            if (_combatActive.Value && _pausedCombatRemaining.Value > 0d)
            {
                _combatEndsAt.Value = now + _pausedCombatRemaining.Value;
                _pausedCombatRemaining.Value = 0d;
            }
            ForEachAvatar(avatar => avatar.ResumePersonalProtectionOnServer(now));
        }

        private static bool HasMultipleSlots(byte mask)
        {
            return mask != 0 && (mask & (mask - 1)) != 0;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private void BeginLandingEffectsOnServer()
        {
            _nextLandingEffectSlot = 0;
            ResolveNextLandingEffectOnServer();
        }

        private void AdvanceLandingEffectResolutionOnServer(double now)
        {
            if (!IsServer || _flow == null ||
                _flow.State != BoardFlowState.LandingEffectResolve)
            {
                return;
            }

            var elapsed = Math.Max(0d, _flow.ToFlowTime(now) - _flow.StateStartedAt);
            var targetResolvedCount = Mathf.Clamp(
                Mathf.FloorToInt((float)elapsed) + 1,
                1,
                MultiplayerConstants.MaxPlayers);
            while (_nextLandingEffectSlot < targetResolvedCount)
            {
                ResolveNextLandingEffectOnServer();
            }
        }

        private void ResolveAllRemainingLandingEffectsOnServer()
        {
            if (!IsServer || !EnsureBoardLandingEffectLayout())
            {
                return;
            }

            while (_nextLandingEffectSlot < MultiplayerConstants.MaxPlayers)
            {
                ResolveNextLandingEffectOnServer();
            }
        }

        private void ResolveNextLandingEffectOnServer()
        {
            if (!IsServer || _nextLandingEffectSlot >= MultiplayerConstants.MaxPlayers)
            {
                return;
            }

            if (!EnsureBoardLandingEffectLayout())
            {
                return;
            }

            var slot = _nextLandingEffectSlot++;
            var avatar = GetAvatarForSlot(slot);
            var tile = avatar != null ? avatar.CurrentBoardTileOnServer : null;
            if (tile == null || !_boardEffectLayout.TryGetEffect(tile.Coordinate, out var effect))
            {
                return;
            }

            switch (effect)
            {
                case BoardLandingEffectType.GoldGain:
                case BoardLandingEffectType.GoldLoss:
                    avatar.ApplyGoldDeltaOnServer(
                        BoardLandingEffectLayout.GetGoldDelta(effect));
                    break;
                case BoardLandingEffectType.Healing:
                    avatar.HealOnServer(BoardLandingEffectLayout.HealingAmount);
                    break;
                case BoardLandingEffectType.ItemReward:
                    var random = new System.Random(unchecked(
                        _boardEffectSeed.Value ^ (_turn.Value * 486187739) ^
                        (slot * 16777619)));
                    avatar.TryAddItemOnServer(PrototypeItemCatalog.GetRandomId(random));
                    break;
            }
        }

        private void ResolveWorldDiceCoordinator()
        {
            var resolved = NetworkWorldDiceCoordinator.Instance;
            if (resolved == _diceCoordinator)
            {
                return;
            }

            if (_diceCoordinator != null)
            {
                _diceCoordinator.DieSettledOnServer -= OnWorldDieSettledOnServer;
            }

            _diceCoordinator = resolved;
            if (_diceCoordinator != null)
            {
                _diceCoordinator.DieSettledOnServer += OnWorldDieSettledOnServer;
            }
        }

        private void OnWorldDieSettledOnServer(int slot, int face)
        {
            ApplyWorldDieResultOnServer(slot, face);
        }

        private void EnsureKeyShopRuntime()
        {
            if (_keyShopRuntime == null)
            {
                _keyShopRuntime = GetComponent<KeyShopRuntimeState>();
            }

            if (_keyShopRuntime == null)
            {
                _keyShopRuntime = gameObject.AddComponent<KeyShopRuntimeState>();
            }

            if (_boardTopology == null)
            {
                _boardTopology = FindAnyObjectByType<BoardTopology>();
            }
        }

        private void RefreshItemShopsForTurnOnServer(int turn)
        {
            if (!IsServer)
            {
                return;
            }

            EnsureKeyShopRuntime();
            if (_boardTopology == null)
            {
                return;
            }

            for (var shopIndex = 0; shopIndex < ItemShopRules.ShopCount; shopIndex++)
            {
                var snapshot = GetItemShopSnapshot(shopIndex);
                var requiresPlacement = !snapshot.Active || snapshot.IsSoldOut ||
                                        turn - snapshot.AppearedTurn >=
                                        ItemShopRules.TurnsBeforeRefresh;
                if (requiresPlacement)
                {
                    TryPlaceItemShopOnServer(shopIndex, turn, snapshot);
                }
            }
        }

        private bool TryPlaceItemShopOnServer(
            int shopIndex,
            int turn,
            ItemShopSnapshot previous)
        {
            var occupied = GetOccupiedPlayerCoordinates();
            var reserved = GetReservedShopCoordinates(shopIndex);
            if (!ItemShopPlacementPolicy.TryChoose(
                    _boardTopology.Tiles,
                    occupied,
                    reserved,
                    UnityKeyShopRandomSource.Shared,
                    out var selectedTile,
                    previous.Active,
                    previous.Location))
            {
                return false;
            }

            var stock = new ItemShopStock(UnityEngine.Random.Range(1, int.MaxValue));
            _itemShopStocks[shopIndex] = stock;
            SetItemShopSnapshot(
                shopIndex,
                ItemShopSnapshot.Create(
                    selectedTile.Coordinate,
                    previous.Revision + 1,
                    turn,
                    stock));
            return true;
        }

        private void SetItemShopSnapshot(int shopIndex, ItemShopSnapshot snapshot)
        {
            if (shopIndex == 0)
            {
                _itemShop0.Value = snapshot;
            }
            else if (shopIndex == 1)
            {
                _itemShop1.Value = snapshot;
            }
        }

        private List<Vector2Int> GetOccupiedPlayerCoordinates()
        {
            var occupied = new List<Vector2Int>(MultiplayerConstants.MaxPlayers);
            ForEachAvatar(avatar =>
            {
                var currentTile = avatar.CurrentBoardTileOnServer;
                AddUnique(occupied, currentTile != null ? currentTile.Coordinate : default,
                    currentTile != null);
            });
            return occupied;
        }

        private List<Vector2Int> GetReservedShopCoordinates(int excludedItemShop = -1)
        {
            var reserved = new List<Vector2Int>(ItemShopRules.ShopCount + 1);
            if (_keyShopRuntime != null && _keyShopRuntime.HasLocation)
            {
                AddUnique(reserved, _keyShopRuntime.Location, true);
            }

            for (var shopIndex = 0; shopIndex < ItemShopRules.ShopCount; shopIndex++)
            {
                if (shopIndex == excludedItemShop)
                {
                    continue;
                }

                var snapshot = GetItemShopSnapshot(shopIndex);
                AddUnique(reserved, snapshot.Location, snapshot.Active);
            }

            return reserved;
        }

        private List<Vector2Int> GetOccupiedAndItemShopCoordinates()
        {
            var blocked = GetOccupiedPlayerCoordinates();
            for (var shopIndex = 0; shopIndex < ItemShopRules.ShopCount; shopIndex++)
            {
                var snapshot = GetItemShopSnapshot(shopIndex);
                AddUnique(blocked, snapshot.Location, snapshot.Active);
            }
            return blocked;
        }

        private bool CanAccessCoordinateOnServer(
            NetworkPlayerAvatar avatar,
            Vector2Int coordinate)
        {
            if (avatar == null || !avatar.HasLogicalBoardTile ||
                avatar.LogicalBoardTileCoordinate != coordinate)
            {
                return false;
            }

            EnsureKeyShopRuntime();
            return _boardTopology != null &&
                   _boardTopology.TryGetTile(coordinate, out var tile) && tile != null &&
                   HorizontalDistance(avatar.transform.position, tile.WorldCenter) <=
                   ItemShopRules.InteractionDistance;
        }

        private static float HorizontalDistance(Vector3 left, Vector3 right)
        {
            left.y = 0f;
            right.y = 0f;
            return Vector3.Distance(left, right);
        }

        private static void AddUnique(
            List<Vector2Int> coordinates,
            Vector2Int coordinate,
            bool shouldAdd)
        {
            if (shouldAdd && !coordinates.Contains(coordinate))
            {
                coordinates.Add(coordinate);
            }
        }

        private void TryBeginInitialKeyShopPlacementOnServer(int turn)
        {
            if (!IsServer || turn != KeyShopRuntimeState.InitialPlacementTurn)
            {
                return;
            }

            EnsureKeyShopRuntime();
            if (_keyShopRuntime == null || _boardTopology == null)
            {
                return;
            }

            var occupied = GetOccupiedAndItemShopCoordinates();

            if (_keyShopRuntime.TryBeginInitialPlacement(
                    turn,
                    _boardTopology.Tiles,
                    occupied,
                    out _))
            {
                SyncKeyShopSnapshot();
                _keyShopAppearanceEndsAt = ServerNow + 0.75d;
            }
        }

        private void AdvanceKeyShopLifecycleOnServer(double now)
        {
            if (!IsServer || _keyShopRuntime == null ||
                _keyShopRuntime.State != KeyShopLifecycleState.Appearing ||
                _keyShopAppearanceEndsAt <= 0d || now < _keyShopAppearanceEndsAt)
            {
                return;
            }

            if (_keyShopRuntime.TryCompleteAppearance())
            {
                _keyShopAppearanceEndsAt = 0d;
                SyncKeyShopSnapshot();
            }
        }

        private void BeginKeyShopRevealOnServer(double now)
        {
            EnsureFlowModel();
            if (_keyShopRevealActive.Value || !_flow.Pause(now))
            {
                return;
            }

            _pausedStateRemaining.Value = _flow.GetStateRemaining(now);
            _pausedActionRemaining.Value = _flow.GetActionRemaining(now);
            _pausedChoiceRemaining.Value = GetPersonalChoiceRemainingOnServer(now);
            _pausedShieldRemaining.Value = _flow.GetOpeningProtectionRemaining(now);
            PauseCombatAndPersonalProtectionOnServer(now);
            _keyShopRevealActive.Value = true;
            _keyShopRevealEndsAt.Value = now + KeyShopRevealSeconds;
            _keyShopRevealRevision.Value++;
            StopAllAvatarInputOnServer();
            SyncFlowSnapshot(now);
        }

        private void CompleteKeyShopRevealOnServer(double now)
        {
            if (!IsServer || !_keyShopRevealActive.Value || _reconnectPaused.Value)
            {
                return;
            }

            _keyShopRevealActive.Value = false;
            _keyShopRevealEndsAt.Value = 0d;
            _keyShopRevealRemainingDuringReconnect = 0d;
            _flow.Resume(now);
            ResumeCombatAndPersonalProtectionOnServer(now);
            SyncFlowSnapshot(now);
            StopAllAvatarInputOnServer();
        }

        private void SyncKeyShopSnapshot()
        {
            if (!IsServer || _keyShopRuntime == null)
            {
                return;
            }

            _keyShopLifecycle.Value = (byte)_keyShopRuntime.State;
            _keyShopHasLocation.Value = _keyShopRuntime.HasLocation;
            _keyShopLocation.Value = _keyShopRuntime.Location;
            _keyShopRevision.Value = _keyShopRuntime.PlacementRevision;
        }

        private double RemainingUntil(double deadline, double pausedRemaining)
        {
            if (IsGlobalSimulationPaused)
            {
                return Math.Max(0d, pausedRemaining);
            }

            return deadline > 0d ? Math.Max(0d, deadline - ServerNow) : 0d;
        }

        private double ServerNow =>
            NetworkManager != null && NetworkManager.IsListening
                ? NetworkManager.ServerTime.Time
                : Time.unscaledTimeAsDouble;

        private static bool IsSlotSet(byte mask, int slot)
        {
            return slot >= 0 && slot < MultiplayerConstants.MaxPlayers &&
                   (mask & (1 << slot)) != 0;
        }

        private static double Deadline(double now, double remaining)
        {
            return remaining > 0d ? now + remaining : 0d;
        }
    }

    /// <summary>
    /// Server-only state retained while the authenticated session-seat owner uses
    /// the gameplay reconnect grace period. It is never client-authored.
    /// </summary>
    public struct ReconnectSnapshot
    {
        public bool IsValid;
        public Vector3 Position;
        public Quaternion Rotation;
        public int Roll;
        public int RemainingMoves;
        public ItemChoiceResolution ChoiceResolution;
        public int SelectedItemSlot;
        public byte OccupiedItemMask;
        public byte ItemSlot0;
        public byte ItemSlot1;
        public byte ItemSlot2;
        public int MaxHealth;
        public int CurrentHealth;
        public int KeyCount;
        public int Gold;
        public int MinigameWins;
        public PlayerBoardActionState ActionState;
        public NetworkCombatState CombatState;
        public int CombatHealth;
        public bool PendingCombatProtection;
        public double PersonalProtectionRemaining;
        public PlayerAppearanceState Appearance;
        public string DisplayName;
        public bool HasLogicalCurrentTile;
        public Vector2Int LogicalCurrentTileCoordinate;
        public Vector2Int[] TraversalHistory;
    }
}
