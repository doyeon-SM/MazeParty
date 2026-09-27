using System;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Pure rules for a player-requested match pause. The pause reuses the
    /// disconnect-reconnect global suspension; these rules decide when it may
    /// start, who may end it, and how its timer behaves while a disconnect
    /// pause temporarily takes precedence.
    /// </summary>
    public static class PlayerPauseRules
    {
        public const double PauseDurationSeconds = 300d;

        /// <summary>
        /// Requests are allowed anywhere on the board, during minigame READY and
        /// play, and while the award ceremony presentation is timed. Minigame
        /// scene loading, the ceremony return wait, a completed-match lobby return,
        /// a disconnect pause and an existing player pause reject new requests.
        /// </summary>
        public static bool CanRequest(
            bool gameplayEnabled,
            bool reconnectPaused,
            bool playerPauseActive,
            bool lobbyReturnQueued,
            BoardFlowState flowState,
            AwardCeremonyPhase ceremonyPhase)
        {
            if (!gameplayEnabled || reconnectPaused || playerPauseActive || lobbyReturnQueued)
            {
                return false;
            }

            if (flowState == BoardFlowState.MinigameLoading)
            {
                return false;
            }

            if (flowState == BoardFlowState.MatchComplete)
            {
                return AwardCeremonyFlowRules.IsTimedPhase(ceremonyPhase);
            }

            return true;
        }

        /// <summary>Only the player who requested the pause may end it early.</summary>
        public static bool CanRelease(
            bool playerPauseActive,
            int requesterSlot,
            int senderSlot)
        {
            return playerPauseActive && requesterSlot >= 0 && requesterSlot == senderSlot;
        }

        public static double GetEndsAt(double now)
        {
            return now + PauseDurationSeconds;
        }

        /// <summary>
        /// Remaining pause time. While a disconnect pause holds the timer,
        /// <paramref name="endsAt"/> is zero and the stored remainder is used.
        /// </summary>
        public static double GetRemaining(
            double endsAt,
            double heldRemaining,
            double now)
        {
            return endsAt > 0d
                ? Math.Max(0d, endsAt - now)
                : Math.Max(0d, heldRemaining);
        }

        public static bool HasExpired(double endsAt, double now)
        {
            return endsAt > 0d && now >= endsAt;
        }

        /// <summary>Freezes the running timer when a disconnect pause begins.</summary>
        public static double HoldRemaining(double endsAt, double now)
        {
            return endsAt > 0d
                ? Math.Min(PauseDurationSeconds, Math.Max(0d, endsAt - now))
                : 0d;
        }

        /// <summary>
        /// Restarts a held timer when every player has reconnected. A held
        /// remainder of zero means the player pause should end with the
        /// disconnect pause.
        /// </summary>
        public static bool TryResumeHeld(
            double heldRemaining,
            double now,
            out double endsAt)
        {
            if (heldRemaining > 0d)
            {
                endsAt = now + Math.Min(PauseDurationSeconds, heldRemaining);
                return true;
            }

            endsAt = 0d;
            return false;
        }
    }
}
