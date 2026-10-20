using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        private readonly NetworkVariable<ShellGamePrivateChoice> _shellGameChoice = new NetworkVariable<ShellGamePrivateChoice>(
            default, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        private ShellGamePrivateChoice _predictedShellGameChoice;
        public void SetShellGameChoiceOnServer(ShellGamePrivateChoice choice)
        {
            if (IsSpawned && IsServer) _shellGameChoice.Value = choice;
        }
        public int GetLocalShellGameChoice(byte round, uint epoch)
        {
            if (!IsOwner) return -1;
            var confirmed = _shellGameChoice.Value;
            var predicted = _predictedShellGameChoice;
            if (predicted.Round == round && predicted.Epoch == epoch &&
                (confirmed.Round != round || confirmed.Epoch != epoch || predicted.Sequence > confirmed.Sequence)) return predicted.Box;
            return confirmed.Round == round && confirmed.Epoch == epoch ? confirmed.Box : -1;
        }
        private bool SubmitLocalShellGameInput()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsShellGamePlaying) return false;
            var state = NetworkShellGameState.Instance;
            var mouse = LocalMouse;
            if (state == null || !state.CanAcceptInputForSlot(AssignedSlot) || mouse == null || !mouse.leftButton.wasPressedThisFrame) return true;
            var round = (byte)state.RoundNumber; var epoch = state.InputEpoch;
            var confirmed = _shellGameChoice.Value;
            if (confirmed.Round != round || confirmed.Epoch != epoch || GetLocalShellGameChoice(round,epoch) >= 0) return true;
            var view = ShellGameNetworkView.Instance;
            if (view == null || !view.TryPickBox(Camera.main, mouse.position.ReadValue(), out var box)) return true;
            if (confirmed.Sequence == uint.MaxValue) return true;
            var sequence = confirmed.Sequence + 1;
            _predictedShellGameChoice = new ShellGamePrivateChoice { Round=round, Epoch=epoch, Sequence=sequence, Box=box };
            GameSound.Play(SoundKeys.MinigameShellSelect);
            RequestShellGameChoiceRpc(box,round,epoch,sequence);
            return true;
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestShellGameChoiceRpc(int box, byte round, uint epoch, uint sequence, RpcParams rpcParams=default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
                NetworkShellGameState.Instance?.TrySetChoiceOnServer(this,box,round,epoch,sequence);
        }
    }
}
