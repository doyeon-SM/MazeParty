using System;
using MazeParty.Gameplay;
using Unity.Netcode;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        private byte _race2PredictionRound, _race2PredictionLane;
        private uint _race2PredictionEpoch, _race2PredictionSequence;
        public byte GetLocalRace2Lane(NetworkRace2State state)
        {
            if (IsOwner && _race2PredictionRound == state.RoundNumber && _race2PredictionEpoch == state.InputEpoch &&
                _race2PredictionSequence > state.GetSequence(AssignedSlot)) return _race2PredictionLane;
            return state.GetLane(AssignedSlot);
        }
        private bool SubmitLocalRace2Input()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsRace2Playing) return false;
            var state = NetworkRace2State.Instance; var keys = LocalKeyboard;
            if (state == null || !state.CanAcceptInputForSlot(AssignedSlot) || keys == null ||
                keys.aKey.wasPressedThisFrame == keys.dKey.wasPressedThisFrame) return true;
            var lane = keys.dKey.wasPressedThisFrame ? (byte)1 : (byte)0;
            if (GetLocalRace2Lane(state) == lane) return true;
            var sequence = _race2PredictionRound == state.RoundNumber && _race2PredictionEpoch == state.InputEpoch
                ? Math.Max(_race2PredictionSequence, state.GetSequence(AssignedSlot)) : state.GetSequence(AssignedSlot);
            if (sequence == uint.MaxValue) return true;
            _race2PredictionRound = (byte)state.RoundNumber; _race2PredictionEpoch = state.InputEpoch;
            _race2PredictionSequence = sequence + 1; _race2PredictionLane = lane;
            GameSound.Play(SoundKeys.MinigameRace2Lane);
            RequestRace2LaneRpc(lane, _race2PredictionRound, _race2PredictionEpoch, _race2PredictionSequence);
            return true;
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestRace2LaneRpc(byte lane, byte round, uint epoch, uint sequence, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
                NetworkRace2State.Instance?.TrySetLaneOnServer(this, lane, round, epoch, sequence);
        }
    }
}
