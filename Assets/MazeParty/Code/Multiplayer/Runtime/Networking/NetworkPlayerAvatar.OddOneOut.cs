using MazeParty.Gameplay.Minigames.OddOneOut;
using Unity.Netcode;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        // Owner-only replication is essential: hiding a public field in the view would leak choices.
        private readonly NetworkVariable<OddOneOutPrivateChoice> _oddOneOutChoice = new NetworkVariable<OddOneOutPrivateChoice>(
            default, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        private OddOneOutPrivateChoice _predictedOddOneOutChoice;

        public void SetOddOneOutChoiceOnServer(OddOneOutPrivateChoice choice)
        {
            if (IsSpawned && IsServer) _oddOneOutChoice.Value = choice;
        }
        public bool GetLocalOddOneOutChoice(byte round, uint epoch)
        {
            if (!IsOwner) return false;
            var confirmed = _oddOneOutChoice.Value;
            var prediction = _predictedOddOneOutChoice;
            if (prediction.Round == round && prediction.Epoch == epoch &&
                (confirmed.Round != round || confirmed.Epoch != epoch || prediction.Sequence > confirmed.Sequence)) return prediction.Sitting;
            return confirmed.Round == round && confirmed.Epoch == epoch && confirmed.Sitting;
        }
        private bool SubmitLocalOddOneOutInput()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsOddOneOutPlaying) return false;
            var state = NetworkOddOneOutState.Instance;
            var mouse = LocalMouse;
            if (state == null || !state.CanAcceptInputForSlot(AssignedSlot) || mouse == null || !mouse.leftButton.wasPressedThisFrame) return true;
            var confirmed = _oddOneOutChoice.Value;
            var round = (byte)state.RoundNumber; var epoch = state.InputEpoch;
            if (confirmed.Round != round || confirmed.Epoch != epoch) return true;
            var sequence = _predictedOddOneOutChoice.Round == round && _predictedOddOneOutChoice.Epoch == epoch
                ? System.Math.Max(confirmed.Sequence, _predictedOddOneOutChoice.Sequence) : confirmed.Sequence;
            if (sequence == uint.MaxValue) return true;
            _predictedOddOneOutChoice = new OddOneOutPrivateChoice { Round = round, Epoch = epoch,
                Sequence = sequence + 1, Sitting = !GetLocalOddOneOutChoice(round, epoch) };
            // This is local prediction feedback, never a broadcast of a secret choice.
            MazeParty.Gameplay.GameSound.PlayOddOneOutToggle(_predictedOddOneOutChoice.Sitting);
            RequestOddOneOutChoiceRpc(_predictedOddOneOutChoice.Sitting, round, epoch, sequence + 1);
            return true;
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestOddOneOutChoiceRpc(bool sitting, byte round, uint epoch, uint sequence, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
                NetworkOddOneOutState.Instance?.TrySetChoiceOnServer(this, sitting, round, epoch, sequence);
        }
    }
}
