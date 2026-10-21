using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.Race;

namespace MazeParty.Multiplayer
{
    internal sealed class RaceRuntimeAdapter :
        MinigameRuntimeAdapter<NetworkRaceState>,
        IMinigameRoundEpochCapability,
        IMinigameReconnectCapability,
        IRaceStepInputCapability
    {
        public override ScheduledMinigameId Id =>
            ScheduledMinigameId.Race;

        protected override NetworkRaceState CurrentState =>
            NetworkRaceState.Instance;

        public override bool TryGetClock(double runningSeconds,
            out double remaining, out double total, out int round)
        {
            remaining = 0d;
            total = runningSeconds;
            round = 1;
            var state = CurrentState;
            if (state == null ||
                state.Phase == NetworkRacePhase.Inactive ||
                state.Phase == NetworkRacePhase.Complete)
                return false;
            remaining = state.Remaining;
            round = state.RoundNumber;
            total = state.Phase == NetworkRacePhase.Countdown
                ? NetworkRaceState.CountdownSeconds
                : state.Phase == NetworkRacePhase.RoundResult
                    ? NetworkRaceState.RoundResultSeconds
                    : runningSeconds;
            return true;
        }

        public override MinigameResultRow GetResultRow(int slot)
        {
            var state = CurrentState;
            return state == null ? default : new MinigameResultRow(state.GetFinalRank(slot), "{0}.  {1}  SCORE {2}  GOLD +{3}", state.GetTotalScore(slot));
        }

        public override bool TryGetRoundCountdown(out double remainingSeconds)
        {
            var state = CurrentState;
            remainingSeconds = state != null &&
                               state.Phase == NetworkRacePhase.Countdown
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
            if (state == null || state.Phase != NetworkRacePhase.Running)
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

        public void TrySubmitRaceStepOnServer(
            NetworkPlayerAvatar avatar,
            RaceStepInput input,
            byte roundNumber,
            uint inputEpoch)
        {
            CurrentState?.RequestStepOnServer(
                avatar,
                input,
                roundNumber,
                inputEpoch);
        }
    }
}
