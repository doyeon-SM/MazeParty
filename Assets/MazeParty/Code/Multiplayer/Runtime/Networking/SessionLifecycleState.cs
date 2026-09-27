namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Local lifecycle of the online session. This is intentionally not a
    /// NetworkVariable: the authoritative room phase still comes from the
    /// session provider and NGO, while this state prevents overlapping local
    /// operations and makes shutdown deterministic.
    /// </summary>
    public enum SessionLifecycleState : byte
    {
        Offline = 0,
        Connecting = 1,
        Reconnecting = 2,
        Lobby = 3,
        StartingMatch = 4,
        Playing = 5,
        ReturningToLobby = 6,
        Leaving = 7,
        Terminating = 8,
        Disposing = 9,
        Disposed = 10
    }
}
