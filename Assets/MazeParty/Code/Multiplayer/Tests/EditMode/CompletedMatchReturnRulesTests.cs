using NUnit.Framework;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class CompletedMatchReturnRulesTests
    {
        [TestCase(false, false, false, RemoteDisconnectDisposition.Ignore)]
        [TestCase(true, false, false, RemoteDisconnectDisposition.PauseForReconnect)]
        [TestCase(true, true, false, RemoteDisconnectDisposition.QueueLobbyCleanup)]
        [TestCase(true, false, true, RemoteDisconnectDisposition.DeferCleanupUntilLobby)]
        [TestCase(true, true, true, RemoteDisconnectDisposition.QueueLobbyCleanup)]
        public void RemoteDisconnect_UsesPhaseSafeCleanupDisposition(
            bool remoteClientLost,
            bool lobbyPhase,
            bool returnInProgress,
            RemoteDisconnectDisposition expected)
        {
            Assert.That(
                CompletedMatchReturnRules.GetRemoteDisconnectDisposition(
                    remoteClientLost,
                    lobbyPhase,
                    returnInProgress),
                Is.EqualTo(expected));
        }
    }
}
