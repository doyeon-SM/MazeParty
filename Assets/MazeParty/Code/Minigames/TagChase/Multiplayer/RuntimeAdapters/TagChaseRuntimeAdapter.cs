using MazeParty.Gameplay.Minigames;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    internal sealed class TagChaseRuntimeAdapter :
        MinigameRuntimeAdapter<NetworkTagChaseState>,
        IMinigameFirstPersonCapability,
        IMinigameRoundEpochCapability,
        IMinigameReconnectCapability,
        IMinigameMovementInputCapability,
        IMinigameLookInputCapability,
        IMinigamePrimaryActionCapability
    {
        public override ScheduledMinigameId Id =>
            ScheduledMinigameId.TagChase;

        protected override NetworkTagChaseState CurrentState =>
            NetworkTagChaseState.Instance;

        public override bool TryGetRoundCountdown(out double remainingSeconds)
        {
            var state = CurrentState;
            remainingSeconds = state != null &&
                               state.Phase == NetworkTagChasePhase.Countdown
                ? state.Remaining
                : 0d;
            return remainingSeconds > 0d;
        }

        public override bool CanAcceptInputForSlot(int slot)
        {
            var state = CurrentState;
            return state != null &&
                   state.CanAcceptInputForSlot(slot);
        }

        public bool UsesFirstPersonControlsForSlot(int slot)
        {
            var state = CurrentState;
            return state != null &&
                   state.IsTagger(slot) &&
                   state.IsMatchActive;
        }


        public bool TryGetRoundAndInputEpoch(
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

        public void RestoreAvatarForReconnectOnServer(
            NetworkPlayerAvatar avatar)
        {
            CurrentState?.RestoreAvatarForReconnectOnServer(avatar);
        }

        public override void EndMatchOnServer()
        {
            CurrentState?.EndMatchOnServer();
        }

        public void ReceiveMovementInputOnServer(
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

        public void ReceiveLookInputOnServer(
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

        public void RequestPrimaryActionOnServer(
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
