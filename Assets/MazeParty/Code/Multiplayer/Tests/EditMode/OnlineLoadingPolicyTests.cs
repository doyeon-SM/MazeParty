using NUnit.Framework;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class OnlineLoadingPolicyTests
    {
        [Test]
        public void EntryAndBoardReadiness_ReleaseOnSuccessFailureAndPriorityRecovery()
        {
            var cases = new[]
            {
                (SessionLifecycleState.Offline, false, false, "", false, false, false, false),
                (SessionLifecycleState.Connecting, true, false, "", false, false, false, true),
                (SessionLifecycleState.Reconnecting, true, false, "", false, false, false, true),
                (SessionLifecycleState.Lobby, false, true, MultiplayerConstants.LobbyPhase, false, false, false, false),
                (SessionLifecycleState.StartingMatch, true, true, MultiplayerConstants.LobbyPhase, false, false, false, true),
                (SessionLifecycleState.Playing, false, true, MultiplayerConstants.PlayingPhase, false, false, false, true),
                (SessionLifecycleState.Lobby, false, true, MultiplayerConstants.LobbyPhase, true, false, false, true),
                (SessionLifecycleState.Playing, false, true, MultiplayerConstants.PlayingPhase, true, false, false, true),
                (SessionLifecycleState.Playing, false, true, MultiplayerConstants.PlayingPhase, true, true, false, false),
                (SessionLifecycleState.Playing, false, true, MultiplayerConstants.PlayingPhase, true, false, true, false),
                (SessionLifecycleState.Connecting, true, true, MultiplayerConstants.HostRecoveryPhase, false, false, false, false),
                (SessionLifecycleState.ReturningToLobby, true, true, MultiplayerConstants.PlayingPhase, true, false, false, false),
                (SessionLifecycleState.Leaving, true, true, MultiplayerConstants.PlayingPhase, true, false, false, false),
                (SessionLifecycleState.Terminating, true, true, MultiplayerConstants.PlayingPhase, true, false, false, false),
                (SessionLifecycleState.Disposing, true, true, MultiplayerConstants.PlayingPhase, true, false, false, false),
                (SessionLifecycleState.Disposed, false, false, "", false, false, false, false),
                (SessionLifecycleState.Playing, false, false, MultiplayerConstants.PlayingPhase, true, false, false, false)
            };
            foreach (var c in cases)
                Assert.That(OnlineLoadingPolicy.ShouldShow(c.Item1, c.Item2, c.Item3, c.Item4,
                    c.Item5, c.Item6, c.Item7), Is.EqualTo(c.Item8), c.ToString());
        }
    }
}
