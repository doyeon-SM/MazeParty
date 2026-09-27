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

            Assert.That(gate.TryVoid(lobbyReturnPrepared: false), Is.False);
            Assert.That(gate.IsVoided, Is.False);
            Assert.That(gate.AllowsGameplayMutation, Is.True);

            Assert.That(gate.TryVoid(lobbyReturnPrepared: true), Is.True);
            Assert.That(gate.IsVoided, Is.True);
            Assert.That(gate.AllowsGameplayMutation, Is.False);
            Assert.That(gate.AllowsResultMutation, Is.False);
            Assert.That(gate.AllowsRecoveryWrite, Is.False);
            Assert.That(gate.TryVoid(lobbyReturnPrepared: true), Is.False);
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

            Assert.That(CompletedMatchReturnRules.ShouldResumeReconnect(
                allPlayersReady: true,
                endsAt,
                84.999d), Is.True);
            Assert.That(CompletedMatchReturnRules.ShouldResumeReconnect(
                allPlayersReady: true,
                endsAt,
                85d), Is.False);
            Assert.That(CompletedMatchReturnRules.ShouldResumeReconnect(
                allPlayersReady: false,
                endsAt,
                84.999d), Is.False);
            Assert.That(CompletedMatchReturnRules.ShouldResumeReconnect(
                allPlayersReady: true,
                endsAt: 0d,
                now: 0d), Is.False);
        }

        [Test]
        public void RemoteDisconnect_UsesPhaseSafeCleanupDisposition()
        {
            var cases = new[]
            {
                (false, false, false, RemoteDisconnectDisposition.Ignore),
                (true, false, false, RemoteDisconnectDisposition.PauseForReconnect),
                (true, true, false, RemoteDisconnectDisposition.QueueLobbyCleanup),
                (true, false, true, RemoteDisconnectDisposition.DeferCleanupUntilLobby),
                (true, true, true, RemoteDisconnectDisposition.QueueLobbyCleanup)
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    CompletedMatchReturnRules.GetRemoteDisconnectDisposition(
                        testCase.Item1,
                        testCase.Item2,
                        testCase.Item3),
                    Is.EqualTo(testCase.Item4));
            }
        }

        [Test]
        public void RemoteDisconnect_AfterFinalRankingNeverPausesTheRemainingPlayers()
        {
            var cases = new[]
            {
                (false, false, false, RemoteDisconnectDisposition.Ignore),
                (true, false, false, RemoteDisconnectDisposition.LeaveCompletedMatch),
                (true, true, false, RemoteDisconnectDisposition.QueueLobbyCleanup),
                (true, false, true, RemoteDisconnectDisposition.DeferCleanupUntilLobby)
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    CompletedMatchReturnRules.GetRemoteDisconnectDisposition(
                        testCase.Item1,
                        testCase.Item2,
                        testCase.Item3,
                        finalRankingLocked: true),
                    Is.EqualTo(testCase.Item4));
            }
        }

        [Test]
        public void PlayingDeparture_KeepsTheRoomOnlyOnceTheMatchIsOver()
        {
            var cases = new[]
            {
                (false, false, false),
                (true, false, true),
                (false, true, true),
                (true, true, true)
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    CompletedMatchReturnRules.KeepsRoomOnPlayingDeparture(
                        testCase.Item1,
                        testCase.Item2),
                    Is.EqualTo(testCase.Item3));
            }
        }

        [Test]
        public void LobbyReturn_ResetsReadyOnlyForReadyPlayersLeavingPlaying()
        {
            var cases = new[]
            {
                (true, true, true, true, true),
                (false, true, true, true, false),
                (true, false, true, true, false),
                (true, true, false, true, false),
                (true, true, true, false, false)
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    CompletedMatchReturnRules.ShouldResetLocalReadyAfterLobbyReturn(
                        testCase.Item1,
                        testCase.Item2,
                        testCase.Item3,
                        testCase.Item4),
                    Is.EqualTo(testCase.Item5));
            }
        }

        [Test]
        public void ReadyResetRequirement_RemainsLatchedUntilReadyIsObservedFalse()
        {
            var cases = new[]
            {
                (true, true, true, true),
                (true, true, false, false),
                (true, false, true, false),
                (false, true, true, false)
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    CompletedMatchReturnRules.ShouldKeepLocalReadyResetRequired(
                        testCase.Item1,
                        testCase.Item2,
                        testCase.Item3),
                    Is.EqualTo(testCase.Item4));
            }
        }

        [Test]
        public void ReconnectGrace_ResolvesOnlyTheSameClientOrSeat()
        {
            var cases = new[]
            {
                (17ul, 2, 18ul, 2, true),
                (17ul, -1, 17ul, 3, true),
                (17ul, 2, 18ul, 3, false)
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    CompletedMatchReturnRules.IsReconnectForTrackedSeat(
                        testCase.Item1,
                        testCase.Item2,
                        testCase.Item3,
                        testCase.Item4),
                    Is.EqualTo(testCase.Item5));
            }
        }
    }
}
