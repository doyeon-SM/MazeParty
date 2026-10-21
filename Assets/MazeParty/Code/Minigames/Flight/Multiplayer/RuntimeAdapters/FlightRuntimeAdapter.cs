using MazeParty.Gameplay;
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

        public override bool TryGetClock(double runningSeconds,
            out double remaining, out double total, out int round)
        {
            remaining = 0d;
            total = runningSeconds;
            round = 1;
            var state = CurrentState;
            if (state == null || state.Phase == MazeParty.Gameplay.Minigames.Flight.FlightPhase.Inactive ||
                state.Phase == MazeParty.Gameplay.Minigames.Flight.FlightPhase.Complete) return false;
            total = state.GameRules.Seconds; round = 1;
            remaining = state.Phase == MazeParty.Gameplay.Minigames.Flight.FlightPhase.Countdown ? total :
                state.Phase == MazeParty.Gameplay.Minigames.Flight.FlightPhase.Running ? state.Remaining : 0d;
            return true;
        }

        public override MinigameResultRow GetResultRow(int slot)
        {
            var state = CurrentState;
            return state == null ? default : new MinigameResultRow(state.GetFinalRank(slot), "{0}.  {1}  SURVIVED {2:0.00}s  GOLD +{3}", state.GetPlayer(slot).Survival);
        }

        public override bool UsesRoundSeriesResults => true;

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
