using MazeParty.Gameplay.Minigames;

namespace MazeParty.Multiplayer
{
    internal sealed class ArenaCombatRuntimeAdapter :
        MinigameRuntimeAdapter<NetworkArenaCombatState>
    {
        public override ScheduledMinigameId Id =>
            ScheduledMinigameId.ArenaCombat;

        protected override NetworkArenaCombatState CurrentState =>
            NetworkArenaCombatState.Instance;

        public override bool TryGetInitialCountdown(
            out double remainingSeconds)
        {
            var state = CurrentState;
            remainingSeconds = state != null &&
                state.Phase == NetworkArenaCombatPhase.Countdown
                    ? state.Remaining
                    : 0d;
            return remainingSeconds > 0d;
        }

        public override bool CanAcceptInputForSlot(int slot)
        {
            var state = CurrentState;
            return state != null &&
                state.CanAcceptInputForSlot(slot);
        }

        public override bool UsesFirstPersonControlsForSlot(int slot)
        {
            var state = CurrentState;
            return state != null &&
                state.CanAcceptInputForSlot(slot);
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
    }
}
