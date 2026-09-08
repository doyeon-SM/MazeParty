using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

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
        private readonly NetworkVariable<byte> _keyShopLifecycle =
            new NetworkVariable<byte>((byte)KeyShopLifecycleState.Inactive);
        private readonly NetworkVariable<bool> _keyShopHasLocation = new NetworkVariable<bool>();
        private readonly NetworkVariable<Vector2Int> _keyShopLocation =
            new NetworkVariable<Vector2Int>();
        private readonly NetworkVariable<int> _keyShopRevision = new NetworkVariable<int>();

        private readonly ReconnectSnapshot[] _reconnectSnapshots =
            new ReconnectSnapshot[MultiplayerConstants.MaxPlayers];
        private BoardFlowStateMachine _flow;
        private bool _endingForReconnectTimeout;
        private KeyShopRuntimeState _keyShopRuntime;
        private BoardTopology _boardTopology;
        private double _keyShopAppearanceEndsAt;
        private NetworkWorldDiceCoordinator _diceCoordinator;

        public static NetworkMatchState Instance { get; private set; }

        public bool GameplayEnabled => _gameplayEnabled.Value;
        public BoardFlowState FlowState => (BoardFlowState)_flowState.Value;
        public int Turn => _turn.Value;
        public int StateRevision => _stateRevision.Value;
        public bool IsReconnectPaused => _reconnectPaused.Value;
        public BoardActionEndReason LastActionEndReason =>
            (BoardActionEndReason)_lastActionEndReason.Value;
        public bool IsActionPhase => GameplayEnabled && FlowState == BoardFlowState.Action;
        public bool CanAcceptActionInput =>
            IsActionPhase && !IsReconnectPaused && ActionRemaining > 0d;
        public bool IsOpeningProtectionActive => ShieldRemaining > 0d;
        public KeyShopLifecycleState KeyShopLifecycle =>
            (KeyShopLifecycleState)_keyShopLifecycle.Value;
        public bool KeyShopHasLocation => _keyShopHasLocation.Value;
        public Vector2Int KeyShopLocation => _keyShopLocation.Value;
        public int KeyShopRevision => _keyShopRevision.Value;

        public static bool IsGameplayReady =>
            Instance != null && Instance.IsSpawned && Instance.GameplayEnabled &&
            !Instance.IsReconnectPaused;

        public double StateRemaining => RemainingUntil(_stateEndsAt.Value, _pausedStateRemaining.Value);
        public double ActionRemaining => RemainingUntil(_actionEndsAt.Value, _pausedActionRemaining.Value);
        public double ChoiceRemaining => RemainingUntil(_choiceEndsAt.Value, _pausedChoiceRemaining.Value);
        public double ShieldRemaining => RemainingUntil(_shieldEndsAt.Value, _pausedShieldRemaining.Value);
        public double ReconnectRemaining => _reconnectPaused.Value
            ? Math.Max(0d, _reconnectGraceEndsAt.Value - ServerNow)
            : 0d;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer)
            {
                EnsureFlowModel();
                EnsureKeyShopRuntime();
                ResolveWorldDiceCoordinator();
                RefreshPresentMask();
            }
        }

        public override void OnNetworkDespawn()
        {
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

            EnsureFlowModel();
            _flow.Tick(now);
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
            SyncKeyShopSnapshot();
            var now = ServerNow;
            _flow.Start(now, 1);
            _gameplayEnabled.Value = true;
            _rolledMask.Value = 0;
            _arrivedMask.Value = 0;
            _readyMask.Value = 0;
            _snapshotRestoredMask.Value = AllPlayersMask;
            _lastActionEndReason.Value = (byte)BoardActionEndReason.None;
            InitializeAllAvatarsOnBoard();
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

            var avatar = FindAvatarForSlot(slot);
            if (!CanProcessActionRequest(avatar) || !avatar.HasResolvedItemChoice || avatar.HasRolled)
            {
                return false;
            }

            avatar.SetRollOnServer(face);
            _rolledMask.Value = (byte)(_rolledMask.Value | (1 << slot));
            return true;
        }

        public bool TryUseSelectedItemOnServer(NetworkPlayerAvatar avatar)
        {
            if (!CanProcessActionRequest(avatar))
            {
                return false;
            }

            // TODO(ITEM-COMBAT): execute the concrete authoritative effect before
            // consuming the selected prototype item/charge.
            return avatar.ConsumeSelectedItemOnServer();
        }

        public bool TryReportPlayerArrivedOnServer(NetworkPlayerAvatar avatar)
        {
            if (!CanProcessActionRequest(avatar) ||
                HasArrived(avatar.AssignedSlot))
            {
                return false;
            }

            if (!_flow.TryReportPlayerArrived(avatar.AssignedSlot, ServerNow))
            {
                return false;
            }

            _arrivedMask.Value = (byte)(_arrivedMask.Value | (1 << avatar.AssignedSlot));
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
                // Development-only seam: no reward/economy mutation is performed.
                _flow.TrySkipMinigame(ServerNow);
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
            _flow.Pause(now);
            _pausedStateRemaining.Value = _flow.GetStateRemaining(now);
            _pausedActionRemaining.Value = _flow.GetActionRemaining(now);
            _pausedChoiceRemaining.Value = GetPersonalChoiceRemainingOnServer(now);
            _pausedShieldRemaining.Value = _flow.GetOpeningProtectionRemaining(now);
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
            if (_reconnectPaused.Value && HasFourBoardReadyPlayers())
            {
                ResumeAfterReconnectOnServer(ServerNow);
            }
        }

        public bool HasRolled(int slot) => IsSlotSet(_rolledMask.Value, slot);
        public bool HasArrived(int slot) => IsSlotSet(_arrivedMask.Value, slot);
        public bool IsMinigameReady(int slot) => IsSlotSet(_readyMask.Value, slot);
        public bool IsPlayerPresent(int slot) => IsSlotSet(_presentMask.Value, slot);

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
                    _rolledMask.Value = 0;
                    _arrivedMask.Value = 0;
                    _readyMask.Value = 0;
                    ForEachAvatar(avatar => avatar.PrepareForOverviewOnServer());
                    TryBeginInitialKeyShopPlacementOnServer(transition.Turn);
                    break;
                case BoardFlowState.Action:
                    _rolledMask.Value = 0;
                    _arrivedMask.Value = 0;
                    _readyMask.Value = 0;
                    ForEachAvatar(avatar => avatar.BeginActionOnServer(transition.Turn));
                    break;
                case BoardFlowState.AscendingResolve:
                    ForEachAvatar(avatar => avatar.EndActionOnServer());
                    break;
                case BoardFlowState.MinigameIntroReady:
                    _readyMask.Value = 0;
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
            _flow.Resume(now);
            _reconnectPaused.Value = false;
            _reconnectGraceEndsAt.Value = 0d;
            _endingForReconnectTimeout = false;
            SyncFlowSnapshot(now);
            ForEachAvatar(avatar => avatar.StopServerInputOnServer());
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
            ForEachAvatar(avatar => avatar.InitializeBoardStateOnServer());
            RefreshPresentMask();
        }

        private void StopAllAvatarInputOnServer()
        {
            ForEachAvatar(avatar => avatar.StopServerInputOnServer());
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
            return IsServer && _gameplayEnabled.Value && !_reconnectPaused.Value &&
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

        private NetworkPlayerAvatar FindAvatarForSlot(int slot)
        {
            if (slot < 0 || slot >= MultiplayerConstants.MaxPlayers ||
                NetworkManager == null || NetworkManager.SpawnManager == null)
            {
                return null;
            }

            foreach (var clientId in NetworkManager.ConnectedClientsIds)
            {
                var playerObject = NetworkManager.SpawnManager.GetPlayerNetworkObject(clientId);
                var avatar = playerObject != null
                    ? playerObject.GetComponent<NetworkPlayerAvatar>()
                    : null;
                if (avatar != null && avatar.IsSpawned && avatar.AssignedSlot == slot)
                {
                    return avatar;
                }
            }

            return null;
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

            var occupied = new List<Vector2Int>(MultiplayerConstants.MaxPlayers);
            ForEachAvatar(avatar =>
            {
                var currentTile = avatar.CurrentBoardTileOnServer;
                if (currentTile != null && !occupied.Contains(currentTile.Coordinate))
                {
                    occupied.Add(currentTile.Coordinate);
                }
            });

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
            if (_reconnectPaused.Value)
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
        public bool HasLogicalCurrentTile;
        public Vector2Int LogicalCurrentTileCoordinate;
    }
}
