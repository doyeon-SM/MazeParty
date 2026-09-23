namespace MazeParty.Multiplayer
{
    public enum RemoteDisconnectDisposition : byte
    {
        Ignore,
        PauseForReconnect,
        QueueLobbyCleanup,
        DeferCleanupUntilLobby
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
            bool completedMatchReturnInProgress)
        {
            if (!remoteClientLost)
            {
                return RemoteDisconnectDisposition.Ignore;
            }
            if (lobbyPhase)
            {
                return RemoteDisconnectDisposition.QueueLobbyCleanup;
            }
            return completedMatchReturnInProgress
                ? RemoteDisconnectDisposition.DeferCleanupUntilLobby
                : RemoteDisconnectDisposition.PauseForReconnect;
        }
    }
}
