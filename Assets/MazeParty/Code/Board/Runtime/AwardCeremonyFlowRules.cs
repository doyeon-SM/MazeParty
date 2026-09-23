using System;

namespace MazeParty.Gameplay
{
    public enum AwardCeremonyServerAction : byte
    {
        None,
        GrantSecondAward,
        CalculateFinalRanks
    }

    /// <summary>
    /// Pure timing and readiness rules shared by the server ceremony flow and
    /// its EditMode boundary tests.
    /// </summary>
    public static class AwardCeremonyFlowRules
    {
        public const double BonusAwardPresentationSeconds = 4d;
        public const double FinalPodiumInputLockSeconds = 5d;

        public static bool IsTimedPhase(AwardCeremonyPhase phase)
        {
            return phase == AwardCeremonyPhase.BonusAwardOne ||
                   phase == AwardCeremonyPhase.BonusAwardTwo ||
                   phase == AwardCeremonyPhase.FinalPodiumLocked;
        }

        public static double GetPhaseDuration(AwardCeremonyPhase phase)
        {
            switch (phase)
            {
                case AwardCeremonyPhase.BonusAwardOne:
                case AwardCeremonyPhase.BonusAwardTwo:
                    return BonusAwardPresentationSeconds;
                case AwardCeremonyPhase.FinalPodiumLocked:
                    return FinalPodiumInputLockSeconds;
                default:
                    return 0d;
            }
        }

        public static bool HasTimedPhaseEnded(
            AwardCeremonyPhase phase,
            double endsAt,
            double now)
        {
            return IsTimedPhase(phase) && now >= endsAt;
        }

        public static bool TryGetTimedTransition(
            AwardCeremonyPhase phase,
            double endsAt,
            double now,
            out AwardCeremonyPhase nextPhase,
            out AwardCeremonyServerAction action)
        {
            nextPhase = phase;
            action = AwardCeremonyServerAction.None;
            if (!HasTimedPhaseEnded(phase, endsAt, now))
            {
                return false;
            }

            switch (phase)
            {
                case AwardCeremonyPhase.BonusAwardOne:
                    nextPhase = AwardCeremonyPhase.BonusAwardTwo;
                    action = AwardCeremonyServerAction.GrantSecondAward;
                    return true;
                case AwardCeremonyPhase.BonusAwardTwo:
                    nextPhase = AwardCeremonyPhase.FinalPodiumLocked;
                    action = AwardCeremonyServerAction.CalculateFinalRanks;
                    return true;
                case AwardCeremonyPhase.FinalPodiumLocked:
                    nextPhase = AwardCeremonyPhase.AwaitingReturn;
                    return true;
                default:
                    return false;
            }
        }

        public static double GetPauseRemaining(
            AwardCeremonyPhase phase,
            double endsAt,
            double now)
        {
            return IsTimedPhase(phase)
                ? Math.Max(0d, endsAt - now)
                : 0d;
        }

        public static double GetResumedEndsAt(
            AwardCeremonyPhase phase,
            double now,
            double pausedRemaining)
        {
            return IsTimedPhase(phase)
                ? now + Math.Max(0d, pausedRemaining)
                : 0d;
        }

        public static bool CanSubmitReturn(
            AwardCeremonyPhase phase,
            bool reconnectPaused,
            bool returnQueued)
        {
            return phase == AwardCeremonyPhase.AwaitingReturn &&
                   !reconnectPaused &&
                   !returnQueued;
        }

        public static bool ShouldBeginLobbyReturn(
            AwardCeremonyPhase phase,
            byte readyMask,
            byte allPlayersMask,
            bool returnQueued)
        {
            return phase == AwardCeremonyPhase.AwaitingReturn &&
                   !returnQueued &&
                   (readyMask & allPlayersMask) == allPlayersMask;
        }
    }
}
