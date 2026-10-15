using NUnit.Framework;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class SessionRecoveryRulesTests
    {
        [Test]
        public void ExplicitLeave_OnlyTheOriginalHostMayDeleteTheRoom()
        {
            foreach (var test in new[] {
                (true, "host", "host", true), (false, "host", "host", false),
                (true, "caretaker", "host", false), (false, "client", "host", false),
                (true, "", "", false) })
                Assert.That(SessionRecoveryRules.ShouldDeleteRoomOnExplicitLeave(test.Item1, test.Item2, test.Item3), Is.EqualTo(test.Item4));
        }

        [Test]
        public void HostReturn_RequiresTheOriginalAccountAndCurrentRecoveryEpoch()
        {
            foreach (var test in new[] {
                ("host", "host", "epoch-2", "epoch-2", true),
                ("host", "guest", "epoch-2", "epoch-2", false),
                ("host", "host", "epoch-2", "epoch-1", false),
                ("host", "host", "", "", false),
                ("", "", "epoch-2", "epoch-2", false) })
                Assert.That(SessionRecoveryRules.CanReturnHostAuthority(test.Item1, test.Item2, test.Item3, test.Item4), Is.EqualTo(test.Item5));
        }

        [Test]
        public void StartMatch_IsBlockedWhileWaitingForTheOriginalHost()
        {
            var players = new[] {
                new OnlinePlayerSnapshot("host", "Host", 0, true, true),
                new OnlinePlayerSnapshot("a", "A", 1, true, false),
                new OnlinePlayerSnapshot("b", "B", 2, true, false),
                new OnlinePlayerSnapshot("c", "C", 3, true, false) };
            foreach (var phase in new[] { MultiplayerConstants.HostRecoveryPhase, MultiplayerConstants.PlayingPhase, MultiplayerConstants.LobbyPhase })
                Assert.That(new SessionSnapshot("ROOM", true, phase, "host", players).CanStart,
                    Is.EqualTo(phase == MultiplayerConstants.LobbyPhase));
        }
    }
}
