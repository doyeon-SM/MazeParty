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

        /// <summary>
        /// The final ranking is calculated and on screen, so the match is over.
        /// From here a player who leaves the room does not end the ceremony
        /// for the others and is not waited for.
        /// </summary>
        public static bool IsFinalRankingLocked(AwardCeremonyPhase phase)
        {
            return phase == AwardCeremonyPhase.FinalPodiumLocked ||
                   phase == AwardCeremonyPhase.AwaitingReturn;
        }

        /// <summary>
        /// The whole room returns once every player still in the room has
        /// pressed "clean up board". Players who already left the room are no
        /// longer in <paramref name="remainingMask"/> and are not waited for.
        /// </summary>
        public static bool ShouldBeginLobbyReturn(
            AwardCeremonyPhase phase,
            byte readyMask,
            byte remainingMask,
            bool returnQueued)
        {
            return phase == AwardCeremonyPhase.AwaitingReturn &&
                   !returnQueued &&
                   remainingMask != 0 &&
                   (readyMask & remainingMask) == remainingMask;
        }

        /// <summary>
        /// True once this player pressed "clean up board" after the final
        /// ranking: that player sees the waiting room and may leave the room
        /// while the others are still at the ceremony.
        /// </summary>
        public static bool IsBackInWaitingRoom(
            AwardCeremonyPhase phase,
            byte readyMask,
            int slot)
        {
            return phase == AwardCeremonyPhase.AwaitingReturn &&
                   slot >= 0 &&
                   slot < 8 &&
                   (readyMask & (1 << slot)) != 0;
        }

        /// <summary>
        /// Players who left the room after the final ranking drop out of the
        /// remaining set for good; a later reconnect does not add them back.
        /// </summary>
        public static byte KeepConnectedPlayers(
            byte remainingMask,
            byte connectedMask)
        {
            return (byte)(remainingMask & connectedMask);
        }
    }
}
