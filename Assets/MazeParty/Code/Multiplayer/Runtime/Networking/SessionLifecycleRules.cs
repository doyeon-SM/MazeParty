namespace MazeParty.Multiplayer
{
    /// <summary>Pure transition rules for the local online-session lifecycle.</summary>
    public static class SessionLifecycleRules
    {
        public static bool IsTransient(SessionLifecycleState state)
        {
            switch (state)
            {
                case SessionLifecycleState.Connecting:
                case SessionLifecycleState.Reconnecting:
                case SessionLifecycleState.StartingMatch:
                case SessionLifecycleState.ReturningToLobby:
                case SessionLifecycleState.Leaving:
                case SessionLifecycleState.Terminating:
                case SessionLifecycleState.Disposing:
                    return true;
                default:
                    return false;
            }
        }

        public static bool CanTransition(
            SessionLifecycleState current,
            SessionLifecycleState next)
        {
            if (current == next)
            {
                return true;
            }

            if (current == SessionLifecycleState.Disposed)
            {
                return false;
            }

            if (next == SessionLifecycleState.Disposing)
            {
                return true;
            }

            if (current == SessionLifecycleState.Disposing)
            {
                return next == SessionLifecycleState.Disposed;
            }

            if (next == SessionLifecycleState.Terminating)
            {
                return true;
            }

            switch (current)
            {
                case SessionLifecycleState.Offline:
                    return next == SessionLifecycleState.Connecting ||
                           next == SessionLifecycleState.Reconnecting ||
                           next == SessionLifecycleState.Lobby ||
                           next == SessionLifecycleState.Playing;
                case SessionLifecycleState.Connecting:
                    return next == SessionLifecycleState.Offline ||
                           next == SessionLifecycleState.Lobby ||
                           next == SessionLifecycleState.Playing ||
                           next == SessionLifecycleState.Leaving;
                case SessionLifecycleState.Reconnecting:
                    return next == SessionLifecycleState.Offline ||
                           next == SessionLifecycleState.Lobby ||
                           next == SessionLifecycleState.Playing ||
                           next == SessionLifecycleState.Leaving;
                case SessionLifecycleState.Lobby:
                    return next == SessionLifecycleState.Offline ||
                           next == SessionLifecycleState.StartingMatch ||
                           next == SessionLifecycleState.Playing ||
                           next == SessionLifecycleState.Leaving;
                case SessionLifecycleState.StartingMatch:
                    return next == SessionLifecycleState.Offline ||
                           next == SessionLifecycleState.Lobby ||
                           next == SessionLifecycleState.Playing ||
                           next == SessionLifecycleState.Leaving;
                case SessionLifecycleState.Playing:
                    return next == SessionLifecycleState.Offline ||
                           next == SessionLifecycleState.Lobby ||
                           next == SessionLifecycleState.ReturningToLobby ||
                           next == SessionLifecycleState.Leaving;
                case SessionLifecycleState.ReturningToLobby:
                    return next == SessionLifecycleState.Offline ||
                           next == SessionLifecycleState.Lobby ||
                           next == SessionLifecycleState.Playing ||
                           next == SessionLifecycleState.Leaving;
                case SessionLifecycleState.Leaving:
                case SessionLifecycleState.Terminating:
                    return next == SessionLifecycleState.Offline ||
                           next == SessionLifecycleState.Lobby ||
                           next == SessionLifecycleState.Playing;
                default:
                    return false;
            }
        }
    }
}
