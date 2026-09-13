using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.Race;

namespace MazeParty.Multiplayer
{
    internal sealed class RaceRuntimeAdapter :
        MinigameRuntimeAdapter<NetworkRaceState>
    {
        public override ScheduledMinigameId Id =>
            ScheduledMinigameId.Race;

        protected override NetworkRaceState CurrentState =>
            NetworkRaceState.Instance;

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

        public override void RestoreAvatarForReconnectOnServer(
            NetworkPlayerAvatar avatar)
        {
            CurrentState?.RestoreAvatarForReconnectOnServer(avatar);
        }

        public override void EndMatchOnServer()
        {
            CurrentState?.EndMatchOnServer();
        }

        public override void TrySubmitRaceStepOnServer(
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
