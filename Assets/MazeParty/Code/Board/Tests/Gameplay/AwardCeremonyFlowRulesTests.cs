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
        public void LobbyReturn_RequiresAllFourAndCanRetryUntilAccepted()
        {
            const byte allPlayers = 0b0000_1111;
            Assert.That(AwardCeremonyFlowRules.ShouldBeginLobbyReturn(
                AwardCeremonyPhase.AwaitingReturn,
                readyMask: allPlayers,
                allPlayersMask: allPlayers,
                returnQueued: false), Is.True);
            Assert.That(AwardCeremonyFlowRules.ShouldBeginLobbyReturn(
                AwardCeremonyPhase.AwaitingReturn,
                readyMask: 0b0000_0111,
                allPlayersMask: allPlayers,
                returnQueued: false), Is.False);
            Assert.That(AwardCeremonyFlowRules.ShouldBeginLobbyReturn(
                AwardCeremonyPhase.AwaitingReturn,
                readyMask: allPlayers,
                allPlayersMask: allPlayers,
                returnQueued: true), Is.False);
        }
    }
}
