using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class AwardCeremonyFlowRulesTests
    {
        [Test]
        public void PhaseDurations_MatchCeremonyContract()
        {
            var cases = new[]
            {
                (AwardCeremonyPhase.BonusAwardOneReady, 2d),
                (AwardCeremonyPhase.BonusAwardOne, 4d),
                (AwardCeremonyPhase.BonusAwardTwoReady, 2d),
                (AwardCeremonyPhase.BonusAwardTwo, 4d),
                (AwardCeremonyPhase.FinalPodiumLocked, 5d),
                (AwardCeremonyPhase.AwaitingReturn, 0d)
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    AwardCeremonyFlowRules.GetPhaseDuration(testCase.Item1),
                    Is.EqualTo(testCase.Item2));
            }
        }

        [Test]
        public void ReadyPhases_AppendWithoutChangingReplicatedPhaseValues()
        {
            var cases = new[]
            {
                (AwardCeremonyPhase.None, (byte)0),
                (AwardCeremonyPhase.BonusAwardOne, (byte)1),
                (AwardCeremonyPhase.BonusAwardTwo, (byte)2),
                (AwardCeremonyPhase.FinalPodiumLocked, (byte)3),
                (AwardCeremonyPhase.AwaitingReturn, (byte)4),
                (AwardCeremonyPhase.BonusAwardOneReady, (byte)5),
                (AwardCeremonyPhase.BonusAwardTwoReady, (byte)6)
            };

            foreach (var testCase in cases)
            {
                Assert.That((byte)testCase.Item1, Is.EqualTo(testCase.Item2));
            }
        }

        [Test]
        public void TimedPhase_AdvancesAtExactDeadlineButNotBefore()
        {
            Assert.That(AwardCeremonyFlowRules.HasTimedPhaseEnded(
                AwardCeremonyPhase.BonusAwardOneReady,
                102d,
                101.999d), Is.False);
            Assert.That(AwardCeremonyFlowRules.HasTimedPhaseEnded(
                AwardCeremonyPhase.BonusAwardOneReady,
                102d,
                102d), Is.True);
            Assert.That(AwardCeremonyFlowRules.HasTimedPhaseEnded(
                AwardCeremonyPhase.AwaitingReturn,
                0d,
                999d), Is.False);
        }

        [Test]
        public void TimedTransitions_GrantEachAwardAndRevealRanksExactlyOnce()
        {
            var cases = new[]
            {
                (AwardCeremonyPhase.BonusAwardOneReady, 2d,
                    AwardCeremonyPhase.BonusAwardOne,
                    AwardCeremonyServerAction.GrantFirstAward),
                (AwardCeremonyPhase.BonusAwardOne, 6d,
                    AwardCeremonyPhase.BonusAwardTwoReady,
                    AwardCeremonyServerAction.None),
                (AwardCeremonyPhase.BonusAwardTwoReady, 8d,
                    AwardCeremonyPhase.BonusAwardTwo,
                    AwardCeremonyServerAction.GrantSecondAward),
                (AwardCeremonyPhase.BonusAwardTwo, 12d,
                    AwardCeremonyPhase.FinalPodiumLocked,
                    AwardCeremonyServerAction.CalculateFinalRanks),
                (AwardCeremonyPhase.FinalPodiumLocked, 17d,
                    AwardCeremonyPhase.AwaitingReturn,
                    AwardCeremonyServerAction.None)
            };

            foreach (var testCase in cases)
            {
                Assert.That(AwardCeremonyFlowRules.TryGetTimedTransition(
                    testCase.Item1,
                    endsAt: testCase.Item2,
                    now: testCase.Item2 - 0.001d,
                    out _,
                    out _), Is.False);
                Assert.That(AwardCeremonyFlowRules.TryGetTimedTransition(
                    testCase.Item1,
                    endsAt: testCase.Item2,
                    now: testCase.Item2,
                    out var phase,
                    out var action), Is.True);
                Assert.That(phase, Is.EqualTo(testCase.Item3));
                Assert.That(action, Is.EqualTo(testCase.Item4));
            }

            Assert.That(AwardCeremonyFlowRules.TryGetTimedTransition(
                AwardCeremonyPhase.AwaitingReturn,
                endsAt: 0d,
                now: 999d,
                out _,
                out _), Is.False);
        }

        [Test]
        public void ReconnectPause_PreservesRemainingPresentationTime()
        {
            var remaining = AwardCeremonyFlowRules.GetPauseRemaining(
                AwardCeremonyPhase.BonusAwardOneReady,
                25d,
                24d);
            Assert.That(remaining, Is.EqualTo(1d));
            Assert.That(AwardCeremonyFlowRules.GetResumedEndsAt(
                AwardCeremonyPhase.BonusAwardOneReady,
                100d,
                remaining), Is.EqualTo(101d));

            Assert.That(AwardCeremonyFlowRules.GetPauseRemaining(
                AwardCeremonyPhase.AwaitingReturn,
                25d,
                22d), Is.Zero);
        }

        [Test]
        public void FinalResultsAutoReturn_UsesExactSixtyActiveSecondBoundary()
        {
            var endsAt = AwardCeremonyFlowRules.
                GetFinalResultsAutoReturnEndsAt(100d);
            Assert.That(endsAt, Is.EqualTo(160d));
            Assert.That(AwardCeremonyFlowRules.HasFinalResultsAutoReturnEnded(
                endsAt,
                159.999d), Is.False);
            Assert.That(AwardCeremonyFlowRules.HasFinalResultsAutoReturnEnded(
                endsAt,
                160d), Is.True);

            var held = AwardCeremonyFlowRules.
                GetFinalResultsAutoReturnPauseRemaining(endsAt, 125d);
            Assert.That(held, Is.EqualTo(35d));
            Assert.That(AwardCeremonyFlowRules.
                GetResumedFinalResultsAutoReturnEndsAt(500d, held),
                Is.EqualTo(535d));
        }

        [Test]
        public void ReturnSubmission_RequiresUnlockedUnpausedCeremony()
        {
            Assert.That(AwardCeremonyFlowRules.CanSubmitReturn(
                AwardCeremonyPhase.AwaitingReturn,
                reconnectPaused: false,
                returnQueued: false), Is.True);
            Assert.That(AwardCeremonyFlowRules.CanSubmitReturn(
                AwardCeremonyPhase.FinalPodiumLocked,
                reconnectPaused: false,
                returnQueued: false), Is.False);
            Assert.That(AwardCeremonyFlowRules.CanSubmitReturn(
                AwardCeremonyPhase.AwaitingReturn,
                reconnectPaused: true,
                returnQueued: false), Is.False);
        }

        [Test]
        public void LobbyReturn_RequiresEveryRemainingPlayerAndCanRetryUntilAccepted()
        {
            const byte allPlayers = 0b0000_1111;
            Assert.That(AwardCeremonyFlowRules.ShouldBeginLobbyReturn(
                AwardCeremonyPhase.AwaitingReturn,
                readyMask: allPlayers,
                remainingMask: allPlayers,
                returnQueued: false), Is.True);
            Assert.That(AwardCeremonyFlowRules.ShouldBeginLobbyReturn(
                AwardCeremonyPhase.AwaitingReturn,
                readyMask: 0b0000_0111,
                remainingMask: allPlayers,
                returnQueued: false), Is.False);
            Assert.That(AwardCeremonyFlowRules.ShouldBeginLobbyReturn(
                AwardCeremonyPhase.AwaitingReturn,
                readyMask: 0b0000_0111,
                remainingMask: allPlayers,
                returnQueued: false,
                autoReturnExpired: true), Is.True);
            Assert.That(AwardCeremonyFlowRules.ShouldBeginLobbyReturn(
                AwardCeremonyPhase.AwaitingReturn,
                readyMask: allPlayers,
                remainingMask: allPlayers,
                returnQueued: true,
                autoReturnExpired: true), Is.False);
            Assert.That(AwardCeremonyFlowRules.ShouldBeginLobbyReturn(
                AwardCeremonyPhase.FinalPodiumLocked,
                readyMask: allPlayers,
                remainingMask: allPlayers,
                returnQueued: false), Is.False);
        }

        [Test]
        public void LobbyReturn_DoesNotWaitForPlayersWhoLeftTheRoom()
        {
            // Seat 3 cleaned up the board and left; seats 0-2 remain.
            var remaining = AwardCeremonyFlowRules.KeepConnectedPlayers(
                remainingMask: 0b0000_1111,
                connectedMask: 0b0000_0111);
            Assert.That(remaining, Is.EqualTo((byte)0b0000_0111));
            Assert.That(AwardCeremonyFlowRules.ShouldBeginLobbyReturn(
                AwardCeremonyPhase.AwaitingReturn,
                readyMask: 0b0000_1011,
                remainingMask: remaining,
                returnQueued: false), Is.False);
            Assert.That(AwardCeremonyFlowRules.ShouldBeginLobbyReturn(
                AwardCeremonyPhase.AwaitingReturn,
                readyMask: 0b0000_1111,
                remainingMask: remaining,
                returnQueued: false), Is.True);

            // A departed seat stays out even if the same seat reconnects.
            Assert.That(AwardCeremonyFlowRules.KeepConnectedPlayers(
                remaining,
                connectedMask: 0b0000_1111), Is.EqualTo((byte)0b0000_0111));
            Assert.That(AwardCeremonyFlowRules.ShouldBeginLobbyReturn(
                AwardCeremonyPhase.AwaitingReturn,
                readyMask: 0b0000_1111,
                remainingMask: 0,
                returnQueued: false), Is.False);
        }

        [Test]
        public void CleanUpBoard_MovesOnlyThatPlayerToTheWaitingRoom()
        {
            const byte seatOneReady = 0b0000_0010;
            Assert.That(AwardCeremonyFlowRules.IsBackInWaitingRoom(
                AwardCeremonyPhase.AwaitingReturn, seatOneReady, 1), Is.True);
            Assert.That(AwardCeremonyFlowRules.IsBackInWaitingRoom(
                AwardCeremonyPhase.AwaitingReturn, seatOneReady, 0), Is.False);
            Assert.That(AwardCeremonyFlowRules.IsBackInWaitingRoom(
                AwardCeremonyPhase.FinalPodiumLocked, seatOneReady, 1), Is.False);
            Assert.That(AwardCeremonyFlowRules.IsBackInWaitingRoom(
                AwardCeremonyPhase.AwaitingReturn, seatOneReady, -1), Is.False);
        }
    }
}
