using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;

namespace MazeParty.Multiplayer
{
    internal sealed class BouncingBallsRuntimeAdapter :
        MinigameRuntimeAdapter<NetworkBouncingBallsState>,
        IMinigameRoundEpochCapability,
        IMinigameReconnectCapability,
        IBouncingShieldInputCapability
    {
        public override ScheduledMinigameId Id =>
            ScheduledMinigameId.BouncingBalls;

        protected override NetworkBouncingBallsState CurrentState =>
            NetworkBouncingBallsState.Instance;

        public override bool TryGetClock(double runningSeconds,
            out double remaining, out double total, out int round)
        {
            remaining = 0d;
            total = runningSeconds;
            round = 1;
            var state = CurrentState;
            if (state == null ||
                state.Phase == NetworkBouncingBallsPhase.Inactive)
                return false;
            remaining = state.Remaining;
            round = state.RoundNumber;
            total = state.Phase == NetworkBouncingBallsPhase.Countdown
                ? 3d : state.Phase == NetworkBouncingBallsPhase.RoundBreak
                    ? NetworkBouncingBallsState.RoundBreakSeconds
                    : state.Phase == NetworkBouncingBallsPhase.Complete
                        ? MazeParty.Gameplay.Minigames.BouncingBalls.BouncingBallsRules.ResultSeconds
                    : runningSeconds;
            return true;
        }

        public override MinigameResultRow GetResultRow(int slot)
        {
            var state = CurrentState;
            return state == null ? default : new MinigameResultRow(state.GetFinalRank(slot), "{0}.  {1}  GOALS {2}  CONCEDED {3}  GOLD +{4}", state.GetScore(slot), state.GetConceded(slot));
        }

        public override bool TryGetRoundCountdown(out double remainingSeconds)
        {
            var state = CurrentState;
            remainingSeconds = state != null &&
                               state.Phase == NetworkBouncingBallsPhase.Countdown
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
                state.Phase != NetworkBouncingBallsPhase.Playing)
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

        public void SetBouncingShieldAxisOnServer(
            NetworkPlayerAvatar avatar,
            float axis,
            byte roundNumber,
            uint inputEpoch)
        {
            CurrentState?.RequestShieldAxisOnServer(
                avatar,
                axis,
                roundNumber,
                inputEpoch);
        }
    }
}
