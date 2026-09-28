using NUnit.Framework;
using Unity.Netcode;

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

        [Test]
        public void SceneUnloadTimeout_ExpiresAtExactDeadline()
        {
            Assert.That(
                CompletedMatchReturnRules.HasSceneUnloadTimedOut(0d, 0d),
                Is.False);
            Assert.That(
                CompletedMatchReturnRules.HasSceneUnloadTimedOut(
                    deadline: 135d,
                    now: 134.999d),
                Is.False);
            Assert.That(
                CompletedMatchReturnRules.HasSceneUnloadTimedOut(
                    deadline: 135d,
                    now: 135d),
                Is.True);
        }

        [Test]
        public void SceneUnloadStatus_DistinguishesCompletionRetryAndPermanentFailure()
        {
            var cases = new[]
            {
                (SceneEventProgressStatus.Started,
                    CompletedMatchSceneUnloadDisposition.WaitForCompletion),
                (SceneEventProgressStatus.SceneNotLoaded,
                    CompletedMatchSceneUnloadDisposition.AdvanceQueue),
                (SceneEventProgressStatus.SceneEventInProgress,
                    CompletedMatchSceneUnloadDisposition.Retry),
                (SceneEventProgressStatus.None,
                    CompletedMatchSceneUnloadDisposition.FailClosed),
                (SceneEventProgressStatus.InvalidSceneName,
                    CompletedMatchSceneUnloadDisposition.FailClosed),
                (SceneEventProgressStatus.SceneFailedVerification,
                    CompletedMatchSceneUnloadDisposition.FailClosed),
                (SceneEventProgressStatus.InternalNetcodeError,
                    CompletedMatchSceneUnloadDisposition.FailClosed),
                (SceneEventProgressStatus.SceneManagementNotEnabled,
                    CompletedMatchSceneUnloadDisposition.FailClosed),
                (SceneEventProgressStatus.ServerOnlyAction,
                    CompletedMatchSceneUnloadDisposition.FailClosed),
                (SceneEventProgressStatus.SessionOwnerOnlyAction,
                    CompletedMatchSceneUnloadDisposition.FailClosed)
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    CompletedMatchReturnRules.GetSceneUnloadDisposition(
                        testCase.Item1),
                    Is.EqualTo(testCase.Item2));
            }
        }

        [Test]
        public void PhaseSaveFailure_IsBoundedAndFailsClosed()
        {
            const int failureLimit = 3;

            Assert.That(
                CompletedMatchReturnRules.GetPhaseSaveFailureDisposition(
                    returnInProgress: true,
                    isInSession: true,
                    isHost: true,
                    phase: MultiplayerConstants.PlayingPhase,
                    consecutiveFailureCount: failureLimit - 1,
                    failureLimit: failureLimit),
                Is.EqualTo(
                    CompletedMatchPhaseSaveFailureDisposition.Retry));
            Assert.That(
                CompletedMatchReturnRules.GetPhaseSaveFailureDisposition(
                    returnInProgress: true,
                    isInSession: true,
                    isHost: true,
                    phase: MultiplayerConstants.PlayingPhase,
                    consecutiveFailureCount: failureLimit,
                    failureLimit: failureLimit),
                Is.EqualTo(
                    CompletedMatchPhaseSaveFailureDisposition.FailClosed));
            Assert.That(
                CompletedMatchReturnRules.GetPhaseSaveFailureDisposition(
                    returnInProgress: true,
                    isInSession: true,
                    isHost: true,
                    phase: MultiplayerConstants.LobbyPhase,
                    consecutiveFailureCount: failureLimit,
                    failureLimit: failureLimit),
                Is.EqualTo(
                    CompletedMatchPhaseSaveFailureDisposition.
                        ContinueSceneCleanup));
            Assert.That(
                CompletedMatchReturnRules.GetPhaseSaveFailureDisposition(
                    returnInProgress: true,
                    isInSession: true,
                    isHost: false,
                    phase: MultiplayerConstants.PlayingPhase,
                    consecutiveFailureCount: 1,
                    failureLimit: failureLimit),
                Is.EqualTo(
                    CompletedMatchPhaseSaveFailureDisposition.FailClosed));
        }

        [Test]
        public void LobbyReturnLatch_ReleasesOnlyWhenControllerNoLongerOwnsIt()
        {
            var cases = new[]
            {
                (false, false, false),
                (false, true, false),
                (true, true, false),
                (true, false, true)
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    CompletedMatchReturnRules.ShouldReleaseLobbyReturnLatch(
                        testCase.Item1,
                        testCase.Item2),
                    Is.EqualTo(testCase.Item3));
            }
        }

        [Test]
        public void TerminationAttempts_AreBoundedAndTimeoutIsTerminal()
        {
            const int attemptLimit = 3;

            var cases = new[]
            {
                (false, false, false, 0, 0d, 0d,
                    CompletedMatchTerminationAttemptDisposition.Wait),
                (true, true, false, 0, 0d, 0d,
                    CompletedMatchTerminationAttemptDisposition.Wait),
                (true, false, false, 2, 10d, 9.999d,
                    CompletedMatchTerminationAttemptDisposition.Wait),
                (true, false, false, 2, 10d, 10d,
                    CompletedMatchTerminationAttemptDisposition.StartAttempt),
                (true, false, false, attemptLimit, 0d, 0d,
                    CompletedMatchTerminationAttemptDisposition.Exhausted),
                (true, false, true, 1, 0d, 0d,
                    CompletedMatchTerminationAttemptDisposition.Exhausted)
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    CompletedMatchReturnRules.GetTerminationAttemptDisposition(
                        terminationPending: testCase.Item1,
                        operationInFlight: testCase.Item2,
                        previousCallTimedOut: testCase.Item3,
                        attemptsStarted: testCase.Item4,
                        attemptLimit: attemptLimit,
                        retryAt: testCase.Item5,
                        now: testCase.Item6),
                    Is.EqualTo(testCase.Item7));
            }
        }

        [Test]
        public void SceneOperationDeadline_RestartsFromAcceptedOperationTime()
        {
            var transientWaitDeadline =
                CompletedMatchReturnRules.GetSceneOperationDeadline(
                    now: 10d,
                    timeoutSeconds: 135d);
            var acceptedUnloadDeadline =
                CompletedMatchReturnRules.GetSceneOperationDeadline(
                    now: 40d,
                    timeoutSeconds: 135d);

            Assert.That(transientWaitDeadline, Is.EqualTo(145d));
            Assert.That(acceptedUnloadDeadline, Is.EqualTo(175d));
            Assert.That(
                CompletedMatchReturnRules.HasSceneUnloadTimedOut(
                    acceptedUnloadDeadline,
                    now: 174.999d),
                Is.False);
        }
    }
}
