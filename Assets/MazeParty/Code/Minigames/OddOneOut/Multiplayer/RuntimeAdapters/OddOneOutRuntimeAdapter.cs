using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.OddOneOut;

namespace MazeParty.Multiplayer
{
    internal sealed class OddOneOutRuntimeAdapter :
        MinigameRuntimeAdapter<NetworkOddOneOutState>,
        IMinigameRoundEpochCapability,
        IMinigameReconnectCapability
    {
        public override ScheduledMinigameId Id =>
            ScheduledMinigameId.OddOneOut;

        protected override NetworkOddOneOutState CurrentState =>
            NetworkOddOneOutState.Instance;

        public override bool TryGetClock(double runningSeconds,
            out double remaining, out double total, out int round)
        {
            remaining = 0d;
            total = runningSeconds;
            round = 1;
            var state = CurrentState;
            if (state == null || state.Phase == MazeParty.Gameplay.Minigames.OddOneOut.OddOneOutPhase.Inactive ||
                state.Phase == MazeParty.Gameplay.Minigames.OddOneOut.OddOneOutPhase.Complete) return false;
            remaining = state.Remaining; round = state.RoundNumber;
            total = state.Phase == OddOneOutPhase.Countdown ? OddOneOutMatchState.CountdownSeconds :
                state.Phase == OddOneOutPhase.Choosing ? OddOneOutMatchState.SelectionSeconds : OddOneOutMatchState.ResultSeconds;
            return true;
        }

        public override MinigameResultRow GetResultRow(int slot)
        {
            var state = CurrentState;
            return state == null ? default : new MinigameResultRow(state.GetFinalRank(slot), "{0}.  {1}  SCORE {2}  GOLD +{3}", state.GetScore(slot));
        }

        public override bool TryGetRoundCountdown(out double remainingSeconds)
        {
            var state = CurrentState;
            remainingSeconds = state != null &&
                               state.Phase == OddOneOutPhase.Countdown
                ? state.Remaining
                : 0d;
            return remainingSeconds > 0d;
        }

        public override bool CanAcceptInputForSlot(int slot)
        {
            var state = CurrentState;
            return state != null && state.CanAcceptInputForSlot(slot);
        }

        public bool TryGetRoundAndInputEpoch(
            out byte roundNumber,
            out uint inputEpoch)
        {
            var state = CurrentState;
            roundNumber = state != null ? (byte)state.RoundNumber : (byte)0;
            inputEpoch = state != null ? state.InputEpoch : 0U;
            return true;
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

    }
}
