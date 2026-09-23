using MazeParty.Gameplay;
using Unity.Netcode;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        public void RequestCompletedMatchReturn()
        {
            if (IsOwner && IsSpawned)
            {
                RequestCompletedMatchReturnRpc();
            }
        }

        public void PrepareForLobbyOnServer()
        {
            if (!IsServer)
            {
                return;
            }

            StopServerInputOnServer();
            CancelHandGestureOnServer();
            ResetMatchStatsOnServer();

            _privateRoll.Value = 0;
            _remainingMoves.Value = 0;
            _choiceResolution.Value = (byte)ItemChoiceResolution.NotStarted;
            _selectedItemSlot.Value = -1;
            _equippedItem.Value = 0;
            _itemCharges.Value = 0;
            _itemCooldownRemaining = 0d;
            _rangeDieItem.Value = 0;
            _doubleDice.Value = false;
            _firstDieResult.Value = 0;
            _secondDieResult.Value = 0;
            _actionState.Value = (byte)PlayerBoardActionState.Hidden;

            _boardReady.Value = false;
            _boardPositionInitialized = false;
            _lobbyPositionInitialized = false;
            _restoredFromSnapshot = false;
            _hasLogicalTile.Value = false;
            _logicalTileCoordinate.Value = default;
            _traversal.Dispose();
            _topology = null;
            _configuredBoundarySlot = -1;
            _configuredBoundaryTopology = null;
            _displayedBoundaryTile = null;
            _displayedBoundaryMoves = int.MinValue;
            HideBoundaryWalls();
            ClearLocalWorldDieCache();

            InitializeLobbyPositionOnServer();
            PrepareForLobbyPresentationRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestCompletedMatchReturnRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            NetworkMatchState.Instance?.TrySetCeremonyReturnReadyOnServer(this);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PrepareForLobbyPresentationRpc()
        {
            DisposeBoardItems();
            ClearLocalWorldDieCache();
            LocalUtilityNotice = BoardUtilityNotice.None;
            LocalUtilityNoticeSlot = -1;
            LocalUtilityNoticeUntil = 0f;
            _avatarVisual?.SetOwnerFirstPerson(false);
            _avatarVisual?.SetTopViewHighlight(false);
            _avatarVisual?.SetHiddenFromViewer(false);
        }
    }
}
