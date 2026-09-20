using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.SequenceMemory;

namespace MazeParty.Multiplayer
{
    internal sealed class SequenceMemoryRuntimeAdapter :
        MinigameRuntimeAdapter<NetworkSequenceMemoryState>
    {
        public override ScheduledMinigameId Id =>
            ScheduledMinigameId.SequenceMemory;

        protected override NetworkSequenceMemoryState CurrentState =>
            NetworkSequenceMemoryState.Instance;

        public override bool TryGetInitialCountdown(out double remainingSeconds)
        {
            var state = CurrentState;
            remainingSeconds = state != null && state.RoundNumber == 1 &&
                               state.Phase == NetworkSequenceMemoryPhase.Countdown
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
                state.Phase != NetworkSequenceMemoryPhase.AcceptingInput)
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

        public override void TrySubmitSequenceMemoryInputOnServer(
            NetworkPlayerAvatar avatar,
            SequenceMemoryInput input,
            byte roundNumber,
            uint inputEpoch)
        {
            CurrentState?.RequestInputOnServer(
                avatar,
                input,
                roundNumber,
                inputEpoch);
        }
    }
}
