using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    internal sealed class MinefieldRuntimeAdapter :
        MinigameRuntimeAdapter<NetworkMinefieldState>,
        IMinigameMovementInputCapability,
        IMinigameSonarInputCapability
    {
        public override ScheduledMinigameId Id =>
            ScheduledMinigameId.Minefield;

        protected override NetworkMinefieldState CurrentState =>
            NetworkMinefieldState.Instance;

        public override bool TryGetClock(double runningSeconds,
            out double remaining, out double total, out int round)
        {
            remaining = 0d;
            total = runningSeconds;
            round = 1;
            var state = CurrentState;
            if (state == null ||
                state.Phase == NetworkMinefieldPhase.Inactive ||
                state.Phase == NetworkMinefieldPhase.Complete)
                return false;
            remaining = state.Remaining;
            round = state.RoundNumber;
            total = state.Phase == NetworkMinefieldPhase.Countdown
                ? NetworkMinefieldState.CountdownSeconds
                : state.Phase == NetworkMinefieldPhase.RoundResult
                    ? NetworkMinefieldState.RoundResultSeconds
                    : state.GameRules.Seconds;
            return true;
        }

        public override MinigameResultRow GetResultRow(int slot)
        {
            var state = CurrentState;
            return state == null ? default : new MinigameResultRow(state.GetFinalRank(slot), "{0}.  {1}  SCORE {2}  GOLD +{3}", state.GetScore(slot));
        }

        public override bool TryGetRoundCountdown(out double remainingSeconds)
        {
            var state = CurrentState;
            remainingSeconds = state != null &&
                               state.Phase == NetworkMinefieldPhase.Countdown
                ? state.Remaining
                : 0d;
            return remainingSeconds > 0d;
        }

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

        public void ReceiveMovementInputOnServer(
            NetworkPlayerAvatar avatar,
            Vector2 input,
            byte roundNumber,
            uint inputEpoch)
        {
            CurrentState?.ReceiveInputOnServer(avatar, input);
        }

        public void TrySonarOnServer(
            NetworkPlayerAvatar avatar,
            Vector2 input)
        {
            CurrentState?.TrySonarOnServer(avatar, input);
        }
    }
}
