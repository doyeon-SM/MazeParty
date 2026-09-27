using MazeParty.Gameplay;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine.InputSystem;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Owner requests sent from the common game menu: player pause, pause
    /// release and leaving an in-progress match. The server validates every
    /// request; the match-ended announcement is broadcast to all players.
    /// </summary>
    public sealed partial class NetworkPlayerAvatar
    {
        /// <summary>
        /// Local keyboard for gameplay input. Null while the common menu owns
        /// input, so every sampler sends neutral movement and no actions.
        /// </summary>
        private static Keyboard LocalKeyboard =>
            LocalInputGate.BlocksGameplayInput ? null : Keyboard.current;

        /// <summary>Local mouse for gameplay input; null while the common menu owns input.</summary>
        private static Mouse LocalMouse =>
            LocalInputGate.BlocksGameplayInput ? null : Mouse.current;

        public void RequestPlayerPause()
        {
            if (IsOwner && IsSpawned)
            {
                RequestPlayerPauseRpc();
            }
        }

        public void RequestPlayerPauseRelease()
        {
            if (IsOwner && IsSpawned)
            {
                RequestPlayerPauseReleaseRpc();
            }
        }

        public void RequestVoluntaryMatchLeave()
        {
            if (IsOwner && IsSpawned)
            {
                RequestVoluntaryMatchLeaveRpc();
            }
        }

        /// <summary>
        /// Server-only. Tells every player who ended the match. The leaving
        /// player's own client treats the message as permission to leave.
        /// </summary>
        public void AnnounceMatchEndedByPlayerOnServer()
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }

            MatchEndedByPlayerRpc(new FixedString64Bytes(
                PlayerProfilePreferences.SanitizeDisplayName(DisplayName)));
        }

        /// <summary>
        /// Server-only. After the final ranking a guest leaves without ending
        /// anything for the others, so only the leaving player is told.
        /// </summary>
        public void AcknowledgeCompletedMatchLeaveOnServer()
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }

            CompletedMatchLeaveAcknowledgedRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestPlayerPauseRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            NetworkMatchState.Instance?.TryBeginPlayerPauseOnServer(this);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestPlayerPauseReleaseRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            NetworkMatchState.Instance?.TryEndPlayerPauseOnServer(this);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestVoluntaryMatchLeaveRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            OnlineSessionController.Instance?.HandleVoluntaryMatchLeaveOnServer(this);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MatchEndedByPlayerRpc(FixedString64Bytes displayName)
        {
            OnlineSessionController.Instance?.ReceiveMatchEndedByPlayer(
                this,
                displayName.ToString());
        }

        [Rpc(SendTo.Owner)]
        private void CompletedMatchLeaveAcknowledgedRpc()
        {
            OnlineSessionController.Instance?.AcknowledgeVoluntaryLeave();
        }
    }
}
