using MazeParty.Gameplay.Minigames;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    internal sealed class RedLightGreenLightRuntimeAdapter :
        MinigameRuntimeAdapter<NetworkRedLightGreenLightState>
    {
        public override ScheduledMinigameId Id =>
            ScheduledMinigameId.RedLightGreenLight;

        protected override NetworkRedLightGreenLightState CurrentState =>
            NetworkRedLightGreenLightState.Instance;

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

        public override void RestoreAvatarForReconnectOnServer(
            NetworkPlayerAvatar avatar)
        {
            CurrentState?.RestoreAvatarForReconnectOnServer(avatar);
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
    }
}
