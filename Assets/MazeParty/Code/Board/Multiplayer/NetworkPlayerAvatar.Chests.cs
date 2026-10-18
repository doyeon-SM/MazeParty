using MazeParty.Gameplay;
using Unity.Netcode;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        public bool IsAimingAtChestOnServer(int index) => IsServer &&
            TryGetAimedBoardInteractionHit(out var hit) && hit.collider != null &&
            hit.collider.GetComponentInParent<BoardChestVisual>() is { } chest && chest.ChestIndex == index;

        public bool EquipChestItemOnServer(PrototypeItemId id)
        {
            if (!IsServer || !PrototypeItemCatalog.IsValid(id) ||
                PrototypeItemCatalog.Get(id).UseTiming != BoardItemUseTiming.Immediate) return false;
            var dice = NetworkWorldDiceCoordinator.Instance;
            var rollStarted = !HasRolled && (HasDieResultOnServer(0) || HasDieResultOnServer(1) ||
                dice != null && dice.IsRollInFlightOnServer(AssignedSlot));
            _swapChannel.Clear();
            _swapSeconds.Value = 0f;
            ConsumeSelectedItemOnServer();
            // The chest grants a temporary equipped item, never an inventory slot.
            // Do not clear utility effects here: already activated cloak lasts until its usual end.
            _selectedItemSlot.Value = -1;
            _equippedItem.Value = (byte)id;
            _itemCharges.Value = PrototypeItemCatalog.Get(id).Charges;
            _itemCooldownRemaining = 0;
            if (!rollStarted)
            {
                _rangeDieItem.Value = 0;
                _doubleDice.Value = false;
                if (!HasRolled) dice?.DiscardPreparedDiceOnServer(AssignedSlot);
            }
            ActivateSelectedCloakOnServer();
            return true;
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void OpenChestRpc(int index, int revision, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
                NetworkMatchState.Instance?.TryOpenChestOnServer(this, index, revision);
        }
        public void PresentChestAcquiredOnServer(PrototypeItemId id)
        {
            if (IsServer) ChestAcquiredRpc((byte)id);
        }
        [Rpc(SendTo.Owner)]
        private void ChestAcquiredRpc(byte item)
        {
            BoardChestNoticeView.Instance?.Present((PrototypeItemId)item);
        }
    }
}
