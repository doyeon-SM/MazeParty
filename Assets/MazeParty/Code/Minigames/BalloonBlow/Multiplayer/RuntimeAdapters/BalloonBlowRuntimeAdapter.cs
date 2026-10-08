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
