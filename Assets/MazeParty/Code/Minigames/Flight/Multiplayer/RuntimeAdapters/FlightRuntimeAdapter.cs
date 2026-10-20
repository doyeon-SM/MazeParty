using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.Flight;

namespace MazeParty.Multiplayer
{
    internal sealed class FlightRuntimeAdapter :
        MinigameRuntimeAdapter<NetworkFlightState>,
        IMinigameRoundEpochCapability,
        IMinigameReconnectCapability
    {
        public override ScheduledMinigameId Id =>
            ScheduledMinigameId.Flight;

        protected override NetworkFlightState CurrentState =>
            NetworkFlightState.Instance;

        public override bool TryGetRoundCountdown(out double remainingSeconds)
        {
            var state = CurrentState;
            remainingSeconds = state != null &&
                               state.Phase == FlightPhase.Countdown
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
            roundNumber = state != null ? (byte)1 : (byte)0;
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
