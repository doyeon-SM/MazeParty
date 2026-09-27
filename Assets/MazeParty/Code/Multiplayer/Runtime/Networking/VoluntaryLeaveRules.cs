namespace MazeParty.Multiplayer
{
    public enum VoluntaryLeaveDisposition : byte
    {
        /// <summary>No match is running; the leaving player simply leaves the session.</summary>
        Ignore,

        /// <summary>
        /// Announce and let the player leave. Used when the host leaves (the
        /// session ends for everyone) or a lobby return is already running.
        /// </summary>
        AcknowledgeOnly,

        /// <summary>Announce, then return the remaining players to the same room.</summary>
        ReturnRemainingPlayersToLobby,

        /// <summary>
        /// The final ranking is locked. Only the leaving player is acknowledged;
        /// nobody is told the game ended and the others keep their ceremony.
        /// </summary>
        LeaveCompletedMatch
    }

    /// <summary>
    /// Server decision for a player who leaves an in-progress match through the
    /// menu confirmation. The fixed four-player match cannot continue, so the
    /// remaining players go back to the ready screen of the same room. A host
    /// departure ends the session because hosts do not migrate. Once the final
    /// ranking is locked the match is over, so a guest simply leaves the room.
    /// </summary>
    public static class VoluntaryLeaveRules
    {
        public static VoluntaryLeaveDisposition Resolve(
            bool matchInProgress,
            bool leaverIsHost,
            bool lobbyReturnInProgress,
            bool finalRankingLocked = false)
        {
            if (!matchInProgress)
            {
                return VoluntaryLeaveDisposition.Ignore;
            }

            if (leaverIsHost)
            {
                return VoluntaryLeaveDisposition.AcknowledgeOnly;
            }

            // Checked before the lobby return: a guest who pressed "clean up
            // board" and then leaves while the room return starts must not be
            // announced as having ended the game.
            if (finalRankingLocked)
            {
                return VoluntaryLeaveDisposition.LeaveCompletedMatch;
            }

            if (lobbyReturnInProgress)
            {
                return VoluntaryLeaveDisposition.AcknowledgeOnly;
            }

            return VoluntaryLeaveDisposition.ReturnRemainingPlayersToLobby;
        }
    }
}
