using NUnit.Framework;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class CompletedMatchReturnRulesTests
    {
        [Test]
        public void ActiveMatchVoidGate_IsOneWayAndBlocksEveryMatchMutation()
        {
            var gate = new ActiveMatchVoidGate();

            Assert.That(gate.AllowsGameplayMutation, Is.True);
            Assert.That(gate.AllowsResultMutation, Is.True);
            Assert.That(gate.AllowsRecoveryWrite, Is.True);

            Assert.That(gate.TryVoid(), Is.True);
            Assert.That(gate.IsVoided, Is.True);
            Assert.That(gate.AllowsGameplayMutation, Is.False);
            Assert.That(gate.AllowsResultMutation, Is.False);
            Assert.That(gate.AllowsRecoveryWrite, Is.False);
            Assert.That(gate.TryVoid(), Is.False);
        }

        [Test]
        public void ReconnectGrace_ExpiresAtExactlySixtySeconds()
        {
            var endsAt = CompletedMatchReturnRules.GetReconnectGraceEndsAt(25d);
            Assert.That(endsAt, Is.EqualTo(85d));
            Assert.That(CompletedMatchReturnRules.HasReconnectGraceExpired(
                endsAt,
                84.999d), Is.False);
            Assert.That(CompletedMatchReturnRules.HasReconnectGraceExpired(
                endsAt,
                85d), Is.True);
        }

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

        [TestCase(false, false, false, RemoteDisconnectDisposition.Ignore)]
        [TestCase(true, false, false, RemoteDisconnectDisposition.LeaveCompletedMatch)]
        [TestCase(true, true, false, RemoteDisconnectDisposition.QueueLobbyCleanup)]
        [TestCase(true, false, true, RemoteDisconnectDisposition.DeferCleanupUntilLobby)]
        public void RemoteDisconnect_AfterFinalRankingNeverPausesTheRemainingPlayers(
            bool remoteClientLost,
            bool lobbyPhase,
            bool returnInProgress,
            RemoteDisconnectDisposition expected)
        {
            Assert.That(
                CompletedMatchReturnRules.GetRemoteDisconnectDisposition(
                    remoteClientLost,
                    lobbyPhase,
                    returnInProgress,
                    finalRankingLocked: true),
                Is.EqualTo(expected));
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public void PlayingDeparture_KeepsTheRoomOnlyOnceTheMatchIsOver(
            bool finalRankingLocked,
            bool returnInProgress,
            bool expected)
        {
            Assert.That(
                CompletedMatchReturnRules.KeepsRoomOnPlayingDeparture(
                    finalRankingLocked,
                    returnInProgress),
                Is.EqualTo(expected));
        }

        [TestCase(true, true, true, true, true)]
        [TestCase(false, true, true, true, false)]
        [TestCase(true, false, true, true, false)]
        [TestCase(true, true, false, true, false)]
        [TestCase(true, true, true, false, false)]
        public void LobbyReturn_ResetsReadyOnlyForReadyPlayersLeavingPlaying(
            bool observedPlayingPhase,
            bool isInSession,
            bool isLobbyPhase,
            bool localReady,
            bool expected)
        {
            Assert.That(
                CompletedMatchReturnRules.ShouldResetLocalReadyAfterLobbyReturn(
                    observedPlayingPhase,
                    isInSession,
                    isLobbyPhase,
                    localReady),
                Is.EqualTo(expected));
        }

        [TestCase(true, true, true, true)]
        [TestCase(true, true, false, false)]
        [TestCase(true, false, true, false)]
        [TestCase(false, true, true, false)]
        public void ReadyResetRequirement_RemainsLatchedUntilReadyIsObservedFalse(
            bool resetRequired,
            bool isInSession,
            bool localReady,
            bool expected)
        {
            Assert.That(
                CompletedMatchReturnRules.
                    ShouldKeepLocalReadyResetRequired(
                        resetRequired,
                        isInSession,
                        localReady),
                Is.EqualTo(expected));
        }

        [TestCase(17ul, 2, 18ul, 2, true)]
        [TestCase(17ul, -1, 17ul, 3, true)]
        [TestCase(17ul, 2, 18ul, 3, false)]
        public void ReconnectGrace_ResolvesOnlyTheSameClientOrSeat(
            ulong disconnectedClientId,
            int disconnectedSlot,
            ulong connectedClientId,
            int connectedSlot,
            bool expected)
        {
            Assert.That(
                CompletedMatchReturnRules.IsReconnectForTrackedSeat(
                    disconnectedClientId,
                    disconnectedSlot,
                    connectedClientId,
                    connectedSlot),
                Is.EqualTo(expected));
        }
    }
}
