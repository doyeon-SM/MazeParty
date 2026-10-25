using System;
using System.Threading;
using System.Threading.Tasks;

namespace MazeParty.Multiplayer
{
    public static class SessionRecoveryRules
    {
        // The state may change while StopAsync yields; never release an active handler.
        public static async Task RetireStoppedNetworkHandlerAsync(Func<bool> isStopped,
            Func<Task> stopHandler, Action releaseHandler, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!isStopped()) throw new InvalidOperationException("Waiting for the previous network transition to finish.");
            await stopHandler();
            token.ThrowIfCancellationRequested();
            if (!isStopped()) throw new InvalidOperationException("The network changed while retiring the previous handler.");
            releaseHandler();
        }
        public static bool ShouldDeleteRoomOnExplicitLeave(bool isServiceHost, string localPlayerId, string originalHostId) =>
            isServiceHost && !string.IsNullOrWhiteSpace(originalHostId) &&
            string.Equals(localPlayerId, originalHostId, StringComparison.Ordinal);

        public static bool CanReturnHostAuthority(string originalHostId, string candidateId, string epoch, string request) =>
            !string.IsNullOrWhiteSpace(originalHostId) && !string.IsNullOrWhiteSpace(epoch) &&
            string.Equals(originalHostId, candidateId, StringComparison.Ordinal) &&
            string.Equals(epoch, request, StringComparison.Ordinal);
    }
}
