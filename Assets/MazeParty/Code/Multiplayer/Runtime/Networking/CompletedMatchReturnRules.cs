using Unity.Netcode;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// One-way server latch for a match that can no longer produce gameplay,
    /// results or recovery data. A new Board scene gets a new latch; the
    /// invalidated instance can never be resumed while its scenes unload.
    /// </summary>
    public sealed class ActiveMatchVoidGate
    {
        public bool IsVoided { get; private set; }

        public bool AllowsGameplayMutation => !IsVoided;
        public bool AllowsResultMutation => !IsVoided;
        public bool AllowsRecoveryWrite => !IsVoided;

        public bool TryVoid(bool lobbyReturnPrepared)
        {
            if (!lobbyReturnPrepared || IsVoided)
            {
                return false;
            }

            IsVoided = true;
            return true;
        }
    }

    public enum CompletedMatchPhaseSaveFailureDisposition : byte
    {
        Retry,
        ContinueSceneCleanup,
        FailClosed
    }

    public enum CompletedMatchSceneUnloadDisposition : byte
    {
        WaitForCompletion,
        AdvanceQueue,
        Retry,
        FailClosed
    }

    public enum CompletedMatchTerminationAttemptDisposition : byte
    {
        Wait,
        StartAttempt,
        Exhausted
    }

    public enum RemoteDisconnectDisposition : byte
    {
        Ignore,
        PauseForReconnect,
        QueueLobbyCleanup,
        DeferCleanupUntilLobby,

        /// <summary>
        /// The final ranking is locked: the player left the room for good. The
        /// remaining players keep their ceremony and are not paused; the seat
        /// is cleaned up once the room is back in the lobby phase.
        /// </summary>
        LeaveCompletedMatch
    }

    /// <summary>
    /// Keeps the Playing-to-Lobby disconnect boundary deterministic and
    /// independently testable from NGO and the session service.
    /// </summary>
    public static class CompletedMatchReturnRules
    {
        /// <summary>
        /// A Board-side accepted latch is valid only while the controller still
        /// owns the return. If an immediate start is rejected or later aborted,
        /// the ceremony may submit the same idempotent return again.
        /// </summary>
        public static bool ShouldReleaseLobbyReturnLatch(
            bool returnQueued,
            bool controllerOwnsReturn)
        {
            return returnQueued && !controllerOwnsReturn;
        }

        public const double ReconnectGraceSeconds = 60d;

        public static double GetReconnectGraceEndsAt(double disconnectedAt)
        {
            return disconnectedAt + ReconnectGraceSeconds;
        }

        public static bool HasReconnectGraceExpired(double endsAt, double now)
        {
            return endsAt > 0d && now >= endsAt;
        }

        public static bool ShouldResumeReconnect(
            bool allPlayersReady,
            double endsAt,
            double now)
        {
            return allPlayersReady && endsAt > 0d &&
                   !HasReconnectGraceExpired(endsAt, now);
        }

        public static RemoteDisconnectDisposition GetRemoteDisconnectDisposition(
            bool remoteClientLost,
            bool lobbyPhase,
            bool completedMatchReturnInProgress,
            bool finalRankingLocked = false)
        {
            if (!remoteClientLost)
            {
                return RemoteDisconnectDisposition.Ignore;
            }
            if (lobbyPhase)
            {
                return RemoteDisconnectDisposition.QueueLobbyCleanup;
            }
            if (completedMatchReturnInProgress)
            {
                return RemoteDisconnectDisposition.DeferCleanupUntilLobby;
            }
            return finalRankingLocked
                ? RemoteDisconnectDisposition.LeaveCompletedMatch
                : RemoteDisconnectDisposition.PauseForReconnect;
        }

        /// <summary>
        /// Host decision when a player leaves the session service while the
        /// room is still in the playing phase. The fixed four-player session
        /// normally ends; once the final ranking is locked, or the room is
        /// already returning to the lobby, the match is over and the room
        /// stays open for the others.
        /// </summary>
        public static bool KeepsRoomOnPlayingDeparture(
            bool finalRankingLocked,
            bool completedMatchReturnInProgress)
        {
            return finalRankingLocked || completedMatchReturnInProgress;
        }

        public static bool ShouldResetLocalReadyAfterLobbyReturn(
            bool observedPlayingPhase,
            bool isInSession,
            bool isLobbyPhase,
            bool localReady)
        {
            return observedPlayingPhase &&
                   isInSession &&
                   isLobbyPhase &&
                   localReady;
        }

        public static bool ShouldKeepLocalReadyResetRequired(
            bool resetRequired,
            bool isInSession,
            bool localReady)
        {
            return resetRequired && isInSession && localReady;
        }

        public static bool IsReconnectForTrackedSeat(
            ulong disconnectedClientId,
            int disconnectedSlot,
            ulong connectedClientId,
            int connectedSlot)
        {
            return disconnectedClientId == connectedClientId ||
                   disconnectedSlot >= 0 &&
                   connectedSlot == disconnectedSlot;
        }

        public static bool HasSceneUnloadTimedOut(
            double deadline,
            double now)
        {
            return deadline > 0d && now >= deadline;
        }

        public static double GetSceneOperationDeadline(
            double now,
            double timeoutSeconds)
        {
            return now + System.Math.Max(1d, timeoutSeconds);
        }

        /// <summary>
        /// NGO can report one transient contention status. Every other rejected
        /// unload is permanent for this attempt and must end the session rather
        /// than leave the room permanently wedged between matches.
        /// </summary>
        public static CompletedMatchSceneUnloadDisposition
            GetSceneUnloadDisposition(SceneEventProgressStatus status)
        {
            switch (status)
            {
                case SceneEventProgressStatus.Started:
                    return CompletedMatchSceneUnloadDisposition.WaitForCompletion;
                case SceneEventProgressStatus.SceneNotLoaded:
                    return CompletedMatchSceneUnloadDisposition.AdvanceQueue;
                case SceneEventProgressStatus.SceneEventInProgress:
                    return CompletedMatchSceneUnloadDisposition.Retry;
                default:
                    return CompletedMatchSceneUnloadDisposition.FailClosed;
            }
        }

        public static CompletedMatchTerminationAttemptDisposition
            GetTerminationAttemptDisposition(
                bool terminationPending,
                bool operationInFlight,
                bool previousCallTimedOut,
                int attemptsStarted,
                int attemptLimit,
                double retryAt,
                double now)
        {
            if (!terminationPending)
            {
                return CompletedMatchTerminationAttemptDisposition.Wait;
            }

            if (previousCallTimedOut || attemptsStarted >= attemptLimit)
            {
                return CompletedMatchTerminationAttemptDisposition.Exhausted;
            }

            return operationInFlight || now < retryAt
                ? CompletedMatchTerminationAttemptDisposition.Wait
                : CompletedMatchTerminationAttemptDisposition.StartAttempt;
        }

        /// <summary>
        /// A transient Playing-to-Lobby save is retried only within a bounded
        /// budget. An observed Lobby snapshot means the remote write succeeded
        /// despite the exception, while every other state fails closed.
        /// </summary>
        public static CompletedMatchPhaseSaveFailureDisposition
            GetPhaseSaveFailureDisposition(
                bool returnInProgress,
                bool isInSession,
                bool isHost,
                string phase,
                int consecutiveFailureCount,
                int failureLimit)
        {
            if (!returnInProgress || !isInSession)
            {
                return CompletedMatchPhaseSaveFailureDisposition.FailClosed;
            }

            if (phase == MultiplayerConstants.LobbyPhase)
            {
                return CompletedMatchPhaseSaveFailureDisposition.
                    ContinueSceneCleanup;
            }

            return isHost &&
                   phase == MultiplayerConstants.PlayingPhase &&
                   consecutiveFailureCount < failureLimit
                ? CompletedMatchPhaseSaveFailureDisposition.Retry
                : CompletedMatchPhaseSaveFailureDisposition.FailClosed;
        }
    }
}
