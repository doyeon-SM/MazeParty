using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.Flight;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        private uint _flightEpoch, _flightSequence;
        private double _flightY, _flightVelocity, _flightPredictedAt;
        private int _flightFlaps;
        public void GetLocalFlightPose(NetworkFlightState state, out double y, out double velocity, out int flaps)
        {
            var confirmed = state.GetPlayer(AssignedSlot);
            if (IsOwner && state.CanAcceptInputForSlot(AssignedSlot) && _flightEpoch == state.InputEpoch &&
                _flightSequence > confirmed.Sequence)
            {
                y = _flightY; velocity = _flightVelocity; flaps = _flightFlaps;
                FlightRules.Integrate(ref y, ref velocity, Math.Min(.25, Math.Max(0, Time.unscaledTimeAsDouble - _flightPredictedAt)));
                return;
            }
            state.GetPresentation(AssignedSlot, out y, out velocity); flaps = confirmed.Flaps;
        }
        private bool SubmitLocalFlightInput()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsFlightPlaying) return false;
            var state = NetworkFlightState.Instance; var mouse = LocalMouse;
            if (state == null || !state.CanAcceptInputForSlot(AssignedSlot) || mouse == null || !mouse.leftButton.wasPressedThisFrame) return true;
            var confirmed = state.GetPlayer(AssignedSlot);
            var sequence = _flightEpoch == state.InputEpoch ? Math.Max(_flightSequence, confirmed.Sequence) : confirmed.Sequence;
            if (sequence == uint.MaxValue) return true;
            GetLocalFlightPose(state, out _flightY, out _flightVelocity, out _flightFlaps);
            _flightEpoch = state.InputEpoch; _flightSequence = sequence + 1;
            _flightVelocity = FlightRules.FlapVelocity; _flightFlaps++; _flightPredictedAt = Time.unscaledTimeAsDouble;
            GameSound.PlayPitched(SoundKeys.MinigameFlightFlap, 1.2f, 1f);
            RequestFlightFlapRpc(_flightEpoch, _flightSequence);
            return true;
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestFlightFlapRpc(uint epoch, uint sequence, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
                NetworkFlightState.Instance?.TryFlapOnServer(this, epoch, sequence);
        }
    }
}
