using MazeParty.Gameplay;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum BoardUtilityNotice : byte { None, SwapComplete, SwappedBy, SwapInterrupted, SwapCancelled, InvalidTarget }

    public sealed partial class NetworkPlayerAvatar
    {
        private readonly NetworkVariable<bool> _cloaked = new NetworkVariable<bool>();
        private readonly NetworkVariable<float> _swapSeconds = new NetworkVariable<float>(0, NetworkVariableReadPermission.Owner);
        private readonly NetworkVariable<byte> _rangeDieItem = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Owner);
        private readonly BoardSwapChannel _swapChannel = new BoardSwapChannel();
        public bool IsCloaked => _cloaked.Value;
        public bool IsSwapping => IsServer ? _swapChannel.Active : IsOwner && _swapSeconds.Value > 0;
        public float LocalSwapRemaining => IsOwner ? _swapSeconds.Value : 0f;
        public bool HasRangeDie => (IsServer || IsOwner) && _rangeDieItem.Value != 0;
        public PrototypeItemId LocalEquippedItem => IsOwner ? (PrototypeItemId)_equippedItem.Value : PrototypeItemId.None;
        public BoardUtilityNotice LocalUtilityNotice { get; private set; }
        public int LocalUtilityNoticeSlot { get; private set; }
        public float LocalUtilityNoticeUntil { get; private set; }

        public int RollBoardFaceOnServer(int dieIndex)
        {
            var match = NetworkMatchState.Instance;
            if (!IsServer || match == null || match.Turn < 1 || AssignedSlot < 0 || AssignedSlot >= 4 ||
                dieIndex < 0 || dieIndex > 1 || (!UsesDoubleDice && dieIndex != 0)) return 0;
            var minimum = 1;
            var maximum = 12;
            if (HasRangeDie)
            {
                var definition = PrototypeItemCatalog.Get((PrototypeItemId)_rangeDieItem.Value);
                minimum = Mathf.Clamp(definition.DiceMinimum, 1, 12);
                maximum = Mathf.Clamp(definition.DiceMaximum, minimum, 12);
            }
            return BoardDiceProgress.RollFace(match.BoardEffectSeed, match.Turn, AssignedSlot, dieIndex, minimum, maximum);
        }

        public void ActivateCloakOnServer()
        { if (IsServer) _cloaked.Value = true; }

        private void ActivateSelectedCloakOnServer()
        {
            if (GetSelectedItemOnServer() != PrototypeItemId.Cloak ||
                !SpendItemChargeOnServer(PrototypeItemCatalog.Get(PrototypeItemId.Cloak)))
                return;

            // Selection and chest acquisition consume and activate the cloak atomically.
            // The presentation keeps its sound but does not play a use gesture.
            ActivateCloakOnServer();
            PresentItemUseOnServer(PrototypeItemId.Cloak);
        }

        public bool BeginSwapOnServer(int targetSlot)
        {
            var match = NetworkMatchState.Instance;
            if (!IsServer || match == null || !match.CanAcceptActionInput || !IsValidSwapTarget(this) || !HasResolvedItemChoice || IsSwapping ||
                IsBoardDeathInProgressOnServer || IsKnockedDown || !CanUseItemChargeOnServer ||
                GetSelectedItemOnServer() != PrototypeItemId.PositionSwapper) return false;
            var target = match.GetAvatarForSlot(targetSlot);
            if (!IsValidSwapTarget(target) || target == this)
            { NotifyUtilityOnServer(BoardUtilityNotice.InvalidTarget, targetSlot); return false; }
            var item = PrototypeItemCatalog.Get(PrototypeItemId.PositionSwapper);
            if (!_swapChannel.Begin(AssignedSlot, targetSlot, Mathf.Max(.01f, item.CastDuration))) return false;
            if (!SpendItemChargeOnServer(item)) { _swapChannel.Clear(); return false; }
            _equippedItem.Value = (byte)PrototypeItemId.PositionSwapper;
            _swapSeconds.Value = (float)_swapChannel.Remaining;
            StopServerInputOnServer();
            PresentItemUseOnServer(PrototypeItemId.PositionSwapper, animate: false);
            return true;
        }

        public static bool IsValidSwapTarget(NetworkPlayerAvatar target) => target != null && target.IsSpawned &&
            target.IsBoardReady && target.CurrentHealth > 0 && target.HasLogicalBoardTile &&
            !target.IsBoardDeathInProgressOnServer;

        public void ChooseSwapTarget(int targetSlot)
        { if (IsOwner && IsSpawned) RequestSwapRpc(targetSlot); }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestSwapRpc(int targetSlot, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId) BeginSwapOnServer(targetSlot);
        }

        private void TickUtilityItems()
        {
            var match = NetworkMatchState.Instance;
            if (IsServer)
            {
                if (match == null || !match.IsActionPhase || CurrentHealth <= 0) _cloaked.Value = false;
                if (_swapChannel.Active)
                {
                    var slot = _swapChannel.TargetSlot;
                    var target = match != null ? match.GetAvatarForSlot(slot) : null;
                    bool available = match != null && match.IsActionPhase && match.ActionRemaining > 0;
                    // Reconnect and player pauses keep the stable seat target; revalidate after the pause ends.
                    bool paused = match != null && match.IsGlobalSimulationPaused;
                    bool targetAvailable = paused && match.IsSimulationSuspended ||
                        IsValidSwapTarget(target) && !target.IsBoardDeathInProgressOnServer;
                    var result = _swapChannel.Tick(Time.unscaledDeltaTime, paused, available, targetAvailable);
                    _swapSeconds.Value = (float)_swapChannel.Remaining;
                    if (result == BoardSwapProgress.Completed)
                    {
                        _equippedItem.Value = 0;
                        if (!TryCompleteSwapOnServer(target)) NotifyUtilityOnServer(BoardUtilityNotice.SwapCancelled, slot);
                    }
                    else if (result == BoardSwapProgress.Cancelled)
                    { _equippedItem.Value = 0; NotifyUtilityOnServer(BoardUtilityNotice.SwapCancelled, slot); }
                }
            }
            if (_avatarVisual != null) _avatarVisual.SetCloakVisibility(IsCloaked, IsOwner);
        }

        private bool TryCompleteSwapOnServer(NetworkPlayerAvatar target)
        {
            if (!IsServer || !IsValidSwapTarget(target) || target.IsBoardDeathInProgressOnServer ||
                CurrentHealth <= 0 || CurrentBoardTileOnServer == null || target.CurrentBoardTileOnServer == null) return false;
            var myPosition = transform.position;
            var theirPosition = target.transform.position;
            var myTile = CurrentBoardTileOnServer;
            var theirTile = target.CurrentBoardTileOnServer;
            // Send the destination player's notice before the authoritative teleport.
            target.NotifyUtilityOnServer(BoardUtilityNotice.SwappedBy, AssignedSlot);
            NotifyUtilityOnServer(BoardUtilityNotice.SwapComplete, target.AssignedSlot);
            RelocateForSwap(theirPosition, theirTile);
            target.RelocateForSwap(myPosition, myTile);
            NetworkMatchState.Instance?.RecordAchievementOnServer(AssignedSlot, PartyAchievement.Swap);
            NetworkWorldDiceCoordinator.Instance?.RelocatePendingDiceOnServer(this);
            NetworkWorldDiceCoordinator.Instance?.RelocatePendingDiceOnServer(target);
            return true;
        }

        private void RelocateForSwap(Vector3 position, BoardTile tile)
        {
            StopServerInputOnServer();
            _traversal.Relocate(tile, _remainingMoves.Value);
            RebaseBoardTravelPreviewAfterRelocationOnServer(tile);
            TeleportController(position, transform.rotation);
            var networkTransform = GetComponent<NetworkTransform>();
            if (networkTransform != null) networkTransform.Teleport(position, transform.rotation, transform.localScale);
            SyncLogicalTileOnServer();
            RefreshBoundaryWallsOnServer();
        }

        private void InterruptSwapOnDamage(int actualDamage)
        {
            if (!_swapChannel.InterruptByDamage(actualDamage)) return;
            _swapSeconds.Value = 0; _equippedItem.Value = 0;
            NotifyUtilityOnServer(BoardUtilityNotice.SwapInterrupted, -1);
        }
        public void ClearUtilityEffectsOnServer()
        {
            if (!IsServer) return;
            _cloaked.Value = false;
            if (_swapChannel.Active)
            {
                _swapChannel.Clear(); _swapSeconds.Value = 0; _equippedItem.Value = 0;
                NotifyUtilityOnServer(BoardUtilityNotice.SwapCancelled, -1);
            }
        }
        private void NotifyUtilityOnServer(BoardUtilityNotice notice, int slot)
        { if (IsServer && IsSpawned) UtilityNoticeRpc((byte)notice, slot); }
        [Rpc(SendTo.Owner)]
        private void UtilityNoticeRpc(byte notice, int slot)
        {
            LocalUtilityNotice = (BoardUtilityNotice)notice;
            LocalUtilityNoticeSlot = slot;
            LocalUtilityNoticeUntil = Time.unscaledTime + 3.5f;
        }
    }
}
