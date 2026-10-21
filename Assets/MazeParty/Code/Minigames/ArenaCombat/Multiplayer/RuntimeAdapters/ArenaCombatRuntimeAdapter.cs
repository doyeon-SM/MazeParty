using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;

namespace MazeParty.Multiplayer
{
    internal sealed class ArenaCombatRuntimeAdapter :
        MinigameRuntimeAdapter<NetworkArenaCombatState>,
        IMinigameFirstPersonCapability,
        IMinigameReconnectCapability
    {
        public override ScheduledMinigameId Id =>
            ScheduledMinigameId.ArenaCombat;

        protected override NetworkArenaCombatState CurrentState =>
            NetworkArenaCombatState.Instance;

        public override bool TryGetClock(double runningSeconds,
            out double remaining, out double total, out int round)
        {
            remaining = 0d;
            total = runningSeconds;
            round = 1;
            var state = CurrentState;
            if (state == null ||
                state.Phase == NetworkArenaCombatPhase.Inactive)
                return false;
            remaining = state.Remaining;
            total = state.Phase == NetworkArenaCombatPhase.Countdown
                ? 3d : state.Phase == NetworkArenaCombatPhase.Complete
                    ? MazeParty.Gameplay.Minigames.ArenaCombat.ArenaCombatRules.ResultSeconds
                    : runningSeconds;
            return true;
        }

        public override MinigameResultRow GetResultRow(int slot)
        {
            var state = CurrentState;
            return state == null ? default : new MinigameResultRow(state.GetFinalRank(slot), "{0}.  {1}  GOLD +{2}");
        }

        public override bool UsesRoundSeriesResults => true;

        public override bool TryGetRoundCountdown(
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

        public bool UsesFirstPersonControlsForSlot(int slot)
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
