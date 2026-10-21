using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    internal sealed class SnowySpinRuntimeAdapter :
        MinigameRuntimeAdapter<NetworkSnowySpinState>,
        IMinigameRoundEpochCapability,
        IMinigameReconnectCapability,
        IMinigameMovementInputCapability
    {
        public override ScheduledMinigameId Id =>
            ScheduledMinigameId.SnowySpin;

        protected override NetworkSnowySpinState CurrentState =>
            NetworkSnowySpinState.Instance;

        public override bool TryGetClock(double runningSeconds,
            out double remaining, out double total, out int round)
        {
            remaining = 0d;
            total = runningSeconds;
            round = 1;
            var state = CurrentState;
            if (state == null ||
                state.Phase == NetworkSnowySpinPhase.Inactive ||
                state.Phase == NetworkSnowySpinPhase.Complete)
                return false;
            remaining = state.Remaining;
            round = state.RoundNumber;
            total = state.Phase == NetworkSnowySpinPhase.Countdown
                ? NetworkSnowySpinState.CountdownSeconds
                : state.Phase == NetworkSnowySpinPhase.RoundResult
                    ? NetworkSnowySpinState.ResultSeconds
                    : runningSeconds;
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
                               state.Phase == NetworkSnowySpinPhase.Countdown
                ? state.Remaining
                : 0d;
            return remainingSeconds > 0d;
        }

        public override bool CanAcceptInputForSlot(int slot)
        {
            return CurrentState != null &&
                CurrentState.CanAcceptInputForSlot(slot);
        }

        public bool TryGetRoundAndInputEpoch(
            out byte roundNumber,
            out uint inputEpoch)
        {
            var state = CurrentState;
            if (state == null ||
                state.Phase != NetworkSnowySpinPhase.Playing)
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

        public void RestoreAvatarForReconnectOnServer(
            NetworkPlayerAvatar avatar)
        {
            CurrentState?.RestoreAvatarForReconnectOnServer(avatar);
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
            CurrentState?.RequestMovementInputOnServer(
                avatar,
                input,
                roundNumber,
                inputEpoch);
        }
    }
}
