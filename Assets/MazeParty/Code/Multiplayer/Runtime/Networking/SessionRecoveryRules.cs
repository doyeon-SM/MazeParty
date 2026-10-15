using System;

namespace MazeParty.Multiplayer
{
    public static class SessionRecoveryRules
    {
        public static bool ShouldDeleteRoomOnExplicitLeave(bool isServiceHost, string localPlayerId, string originalHostId) =>
            isServiceHost && !string.IsNullOrWhiteSpace(originalHostId) &&
            string.Equals(localPlayerId, originalHostId, StringComparison.Ordinal);

        public static bool CanReturnHostAuthority(string originalHostId, string candidateId, string epoch, string request) =>
            !string.IsNullOrWhiteSpace(originalHostId) && !string.IsNullOrWhiteSpace(epoch) &&
            string.Equals(originalHostId, candidateId, StringComparison.Ordinal) &&
            string.Equals(epoch, request, StringComparison.Ordinal);
    }
}
