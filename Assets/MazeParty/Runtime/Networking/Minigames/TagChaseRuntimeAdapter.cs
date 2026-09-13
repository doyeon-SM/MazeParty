using MazeParty.Gameplay.Minigames;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    internal sealed class TagChaseRuntimeAdapter :
        MinigameRuntimeAdapter<NetworkTagChaseState>
    {
        public override ScheduledMinigameId Id =>
            ScheduledMinigameId.TagChase;

        protected override NetworkTagChaseState CurrentState =>
            NetworkTagChaseState.Instance;

        public override bool CanAcceptInputForSlot(int slot)
        {
            var state = CurrentState;
            return state != null &&
                   state.CanAcceptInputForSlot(slot);
        }

        public override bool UsesFirstPersonControlsForSlot(int slot)
        {
            var state = CurrentState;
            return state != null &&
                   state.IsTagger(slot) &&
                   state.IsMatchActive;
        }


        public override bool TryGetRoundAndInputEpoch(
            out byte roundNumber,
            out uint inputEpoch)
        {
            var state = CurrentState;
            if (state == null ||
                state.Phase != NetworkTagChasePhase.Running)
            {
                roundNumber = 0;
                inputEpoch = 0U;
                return false;
            }

            roundNumber = (byte)state.RoundNumber;
            inputEpoch = state.InputEpoch;
            return inputEpoch != 0U;
        }

        public override void BeginMatchOnServer(ulong matchSeed)
        {
            CurrentState?.BeginMatchOnServer(matchSeed);
        }

        public override void PauseOnServer(double now)
        {
            CurrentState?.PauseOnServer(now);
        }

        public override void ResumeOnServer(double now)
        {
            CurrentState?.ResumeOnServer(now);
        }

        public override void RestoreAvatarForReconnectOnServer(
            NetworkPlayerAvatar avatar)
        {
            CurrentState?.RestoreAvatarForReconnectOnServer(avatar);
        }

        public override void EndMatchOnServer()
        {
            CurrentState?.EndMatchOnServer();
        }

        public override void ReceiveMovementInputOnServer(
            NetworkPlayerAvatar avatar,
            Vector2 input,
            byte roundNumber,
            uint inputEpoch)
        {
            CurrentState?.ReceiveInputOnServer(
                avatar,
                input,
                roundNumber,
                inputEpoch);
        }

        public override void ReceiveLookInputOnServer(
            NetworkPlayerAvatar avatar,
            float yaw,
            byte roundNumber,
            uint inputEpoch)
        {
            CurrentState?.ReceiveLookOnServer(
                avatar,
                yaw,
                roundNumber,
                inputEpoch);
        }

        public override void RequestPrimaryActionOnServer(
            NetworkPlayerAvatar avatar,
            byte roundNumber,
            uint inputEpoch)
        {
            CurrentState?.RequestCatchOnServer(
                avatar,
                roundNumber,
                inputEpoch);
        }
    }
}
