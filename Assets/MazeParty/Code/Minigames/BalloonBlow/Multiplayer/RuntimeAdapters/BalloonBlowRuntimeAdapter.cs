using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;

namespace MazeParty.Multiplayer
{
    internal sealed class BalloonBlowRuntimeAdapter :
        MinigameRuntimeAdapter<NetworkBalloonBlowState>,
        IMinigameRoundEpochCapability,
        IMinigameReconnectCapability,
        IMinigameInflateInputCapability
    {
        public override ScheduledMinigameId Id =>
            ScheduledMinigameId.BalloonBlow;

        protected override NetworkBalloonBlowState CurrentState =>
            NetworkBalloonBlowState.Instance;

        public override bool TryGetClock(double runningSeconds,
            out double remaining, out double total, out int round)
        {
            remaining = 0d;
            total = runningSeconds;
            round = 1;
            var state = CurrentState;
            if (state == null ||
                state.Phase == NetworkBalloonBlowPhase.Inactive ||
                state.Phase == NetworkBalloonBlowPhase.Complete)
                return false;
            remaining = state.Remaining;
            round = state.RoundNumber;
            total = state.Phase == NetworkBalloonBlowPhase.Countdown
                ? NetworkBalloonBlowState.CountdownSeconds
                : state.Phase == NetworkBalloonBlowPhase.RoundResult
                    ? NetworkBalloonBlowState.RoundResultSeconds
                    : runningSeconds;
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
                               state.Phase == NetworkBalloonBlowPhase.Countdown
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

        public void SetInflateHeldOnServer(
            NetworkPlayerAvatar avatar,
            bool isHeld,
            byte roundNumber,
            uint inputEpoch)
        {
            CurrentState?.SetInflateHeldOnServer(
                avatar,
                isHeld,
                roundNumber,
                inputEpoch);
        }
    }
}
