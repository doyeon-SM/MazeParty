using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class AwardCeremonyFlowRulesTests
    {
        [TestCase(AwardCeremonyPhase.BonusAwardOne, 4d)]
        [TestCase(AwardCeremonyPhase.BonusAwardTwo, 4d)]
        [TestCase(AwardCeremonyPhase.FinalPodiumLocked, 5d)]
        [TestCase(AwardCeremonyPhase.AwaitingReturn, 0d)]
        public void PhaseDurations_MatchCeremonyContract(
            AwardCeremonyPhase phase,
            double expectedSeconds)
        {
            Assert.That(
                AwardCeremonyFlowRules.GetPhaseDuration(phase),
                Is.EqualTo(expectedSeconds));
        }

        [Test]
        public void TimedPhase_AdvancesAtExactDeadlineButNotBefore()
        {
            Assert.That(AwardCeremonyFlowRules.HasTimedPhaseEnded(
                AwardCeremonyPhase.BonusAwardOne,
                104d,
                103.999d), Is.False);
            Assert.That(AwardCeremonyFlowRules.HasTimedPhaseEnded(
                AwardCeremonyPhase.BonusAwardOne,
                104d,
                104d), Is.True);
            Assert.That(AwardCeremonyFlowRules.HasTimedPhaseEnded(
                AwardCeremonyPhase.AwaitingReturn,
                0d,
                999d), Is.False);
        }

        [Test]
        public void TimedTransitions_GrantEachAwardAndRevealRanksExactlyOnce()
        {
            Assert.That(AwardCeremonyFlowRules.TryGetTimedTransition(
                AwardCeremonyPhase.BonusAwardOne,
                endsAt: 4d,
                now: 4d,
                out var phase,
                out var action), Is.True);
            Assert.That(phase, Is.EqualTo(AwardCeremonyPhase.BonusAwardTwo));
            Assert.That(
                action,
                Is.EqualTo(AwardCeremonyServerAction.GrantSecondAward));

            Assert.That(AwardCeremonyFlowRules.TryGetTimedTransition(
                phase,
                endsAt: 8d,
                now: 8d,
                out phase,
                out action), Is.True);
            Assert.That(
                phase,
                Is.EqualTo(AwardCeremonyPhase.FinalPodiumLocked));
            Assert.That(
                action,
                Is.EqualTo(AwardCeremonyServerAction.CalculateFinalRanks));

            Assert.That(AwardCeremonyFlowRules.TryGetTimedTransition(
                phase,
                endsAt: 13d,
                now: 13d,
                out phase,
                out action), Is.True);
            Assert.That(phase, Is.EqualTo(AwardCeremonyPhase.AwaitingReturn));
            Assert.That(action, Is.EqualTo(AwardCeremonyServerAction.None));
            Assert.That(AwardCeremonyFlowRules.TryGetTimedTransition(
                phase,
                endsAt: 0d,
                now: 999d,
                out _,
                out _), Is.False);
        }

        [Test]
        public void ReconnectPause_PreservesRemainingPresentationTime()
        {
            var remaining = AwardCeremonyFlowRules.GetPauseRemaining(
                AwardCeremonyPhase.FinalPodiumLocked,
                25d,
                22d);
            Assert.That(remaining, Is.EqualTo(3d));
            Assert.That(AwardCeremonyFlowRules.GetResumedEndsAt(
                AwardCeremonyPhase.FinalPodiumLocked,
                100d,
                remaining), Is.EqualTo(103d));

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
