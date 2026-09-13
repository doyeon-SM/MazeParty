using MazeParty.Gameplay.Minigames;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    internal sealed class MinefieldRuntimeAdapter :
        MinigameRuntimeAdapter<NetworkMinefieldState>
    {
        public override ScheduledMinigameId Id =>
            ScheduledMinigameId.Minefield;

        protected override NetworkMinefieldState CurrentState =>
            NetworkMinefieldState.Instance;

        public override bool CanAcceptInputForSlot(int slot)
        {
            var state = CurrentState;
            return state != null && state.CanAcceptInputForSlot(slot);
        }

        public override void BeginMatchOnServer(ulong matchSeed)
        {
            CurrentState?.BeginMatchOnServer();
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

        public override void ReceiveMovementInputOnServer(
            NetworkPlayerAvatar avatar,
            Vector2 input,
            byte roundNumber,
            uint inputEpoch)
        {
            CurrentState?.ReceiveInputOnServer(avatar, input);
        }

        public override void TrySonarOnServer(
            NetworkPlayerAvatar avatar,
            Vector2 input)
        {
            CurrentState?.TrySonarOnServer(avatar, input);
        }
    }
}
