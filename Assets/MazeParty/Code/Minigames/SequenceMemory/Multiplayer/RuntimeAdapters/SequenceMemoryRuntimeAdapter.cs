using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.SequenceMemory;

namespace MazeParty.Multiplayer
{
    internal sealed class SequenceMemoryRuntimeAdapter :
        MinigameRuntimeAdapter<NetworkSequenceMemoryState>,
        IMinigameRoundEpochCapability,
        IMinigameReconnectCapability,
        ISequenceMemoryInputCapability
    {
        public override ScheduledMinigameId Id =>
            ScheduledMinigameId.SequenceMemory;

        protected override NetworkSequenceMemoryState CurrentState =>
            NetworkSequenceMemoryState.Instance;

        public override bool TryGetClock(double runningSeconds,
            out double remaining, out double total, out int round)
        {
            remaining = 0d;
            total = runningSeconds;
            round = 1;
            var state = CurrentState;
            if (state == null ||
                state.Phase == NetworkSequenceMemoryPhase.Inactive)
                return false;
            remaining = state.Remaining;
            round = state.RoundNumber;
            total = state.Phase == NetworkSequenceMemoryPhase.Countdown
                ? SequenceMemoryRules.CountdownSeconds
                : state.Phase ==
                    NetworkSequenceMemoryPhase.PresentingProblem
                    ? SequenceMemoryRules.GetProblemPresentationSeconds(
                        UnityEngine.Mathf.Clamp(round, 1,
                            state.GameRules.Rounds), rules: state.GameRules)
                    : state.Phase ==
                        NetworkSequenceMemoryPhase.RevealingAnswer
                        ? SequenceMemoryRules.AnswerRevealSeconds
                        : state.Phase ==
                            NetworkSequenceMemoryPhase.Complete
                            ? SequenceMemoryRules.ResultSeconds
                        : SequenceMemoryRules.InputWindowSeconds;
            return true;
        }

        public override MinigameResultRow GetResultRow(int slot)
        {
            var state = CurrentState;
            return state == null ? default : new MinigameResultRow(state.GetFinalRank(slot),
                state.IsPlayerEliminated(slot) ? "{0}.  {1}  OUT  GOLD +{2}" : "{0}.  {1}  SURVIVED  GOLD +{2}");
        }

        public override bool TryGetRoundCountdown(out double remainingSeconds)
        {
            var state = CurrentState;
            remainingSeconds = state != null &&
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

        public bool TryGetRoundAndInputEpoch(
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

        public void RestoreAvatarForReconnectOnServer(
            NetworkPlayerAvatar avatar)
        {
            CurrentState?.RestoreAvatarForReconnectOnServer(avatar);
        }

        public override void EndMatchOnServer()
        {
            CurrentState?.EndMatchOnServer();
        }

        public void TrySubmitSequenceMemoryInputOnServer(
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
