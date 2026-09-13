using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.WrongWay;

namespace MazeParty.Multiplayer
{
    internal sealed class WrongWayRuntimeAdapter :
        MinigameRuntimeAdapter<NetworkWrongWayState>
    {
        public override ScheduledMinigameId Id =>
            ScheduledMinigameId.WrongWay;

        protected override NetworkWrongWayState CurrentState =>
            NetworkWrongWayState.Instance;

        public override bool CanAcceptInputForSlot(int slot)
        {
            var state = CurrentState;
            return state != null && state.CanAcceptInputForSlot(slot);
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

        public override void EndMatchOnServer()
        {
            CurrentState?.EndMatchOnServer();
        }

        public override void TrySubmitDirectionOnServer(
            NetworkPlayerAvatar avatar,
            WrongWayDirection direction)
        {
            CurrentState?.TrySubmitDirectionOnServer(avatar, direction);
        }
    }
}
