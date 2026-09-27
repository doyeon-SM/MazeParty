namespace MazeParty.Multiplayer
{
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
    }
}
