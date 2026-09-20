using MazeParty.Gameplay.Minigames;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    internal sealed class SnowySpinRuntimeAdapter :
        MinigameRuntimeAdapter<NetworkSnowySpinState>
    {
        public override ScheduledMinigameId Id =>
            ScheduledMinigameId.SnowySpin;

        protected override NetworkSnowySpinState CurrentState =>
            NetworkSnowySpinState.Instance;

        public override bool TryGetInitialCountdown(out double remainingSeconds)
        {
            var state = CurrentState;
            remainingSeconds = state != null && state.RoundNumber == 1 &&
                               state.Phase == NetworkSnowySpinPhase.Countdown
                ? state.Remaining
                : 0d;
            return remainingSeconds > 0d;
        }

        public override bool CanAcceptInputForSlot(int slot)
        {
            return CurrentState != null &&
                CurrentState.CanAcceptInputForSlot(slot);
        }

        public override bool TryGetRoundAndInputEpoch(
            out byte roundNumber,
            out uint inputEpoch)
        {
            var state = CurrentState;
            if (state == null ||
                state.Phase != NetworkSnowySpinPhase.Playing)
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
            CurrentState?.RequestMovementInputOnServer(
                avatar,
                input,
                roundNumber,
                inputEpoch);
        }
    }
}
