using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class PlayerPauseRulesTests
    {
        [Test]
        public void Request_IsAllowedOnBoardAndDuringMinigameReadyAndPlay()
        {
            var states = new[]
            {
                BoardFlowState.TurnOverview,
                BoardFlowState.Descending,
                BoardFlowState.Action,
                BoardFlowState.AscendingResolve,
                BoardFlowState.CombatResolve,
                BoardFlowState.LandingEffectResolve,
                BoardFlowState.MinigameIntroReady,
                BoardFlowState.MinigameResult,
                BoardFlowState.MinigamePlaying
            };

            foreach (var state in states)
            {
                Assert.That(PlayerPauseRules.CanRequest(
                    true, false, false, false,
                    state,
                    AwardCeremonyPhase.None), Is.True, state.ToString());
            }
        }

        [Test]
        public void Request_IsRejectedWhileMinigameSceneLoads()
        {
            Assert.That(PlayerPauseRules.CanRequest(
                true, false, false, false,
                BoardFlowState.MinigameLoading,
                AwardCeremonyPhase.None), Is.False);
        }

        [Test]
        public void Request_DuringCeremony_OnlyInTimedPhases()
        {
            var cases = new[]
            {
                (AwardCeremonyPhase.BonusAwardOneReady, true),
                (AwardCeremonyPhase.BonusAwardOne, true),
                (AwardCeremonyPhase.BonusAwardTwoReady, true),
                (AwardCeremonyPhase.BonusAwardTwo, true),
                (AwardCeremonyPhase.FinalPodiumLocked, true),
                (AwardCeremonyPhase.AwaitingReturn, false),
                (AwardCeremonyPhase.None, false)
            };

            foreach (var testCase in cases)
            {
                Assert.That(PlayerPauseRules.CanRequest(
                    true, false, false, false,
                    BoardFlowState.MatchComplete,
                    testCase.Item1), Is.EqualTo(testCase.Item2));
            }
        }

        [Test]
        public void Request_IsRejectedWhenGameplayStoppedPausedOrReturning()
        {
            const BoardFlowState state = BoardFlowState.Action;
            const AwardCeremonyPhase phase = AwardCeremonyPhase.None;
            Assert.That(PlayerPauseRules.CanRequest(false, false, false, false, state, phase), Is.False);
            Assert.That(PlayerPauseRules.CanRequest(true, true, false, false, state, phase), Is.False,
                "No player pause may start during a disconnect pause.");
            Assert.That(PlayerPauseRules.CanRequest(true, false, true, false, state, phase), Is.False,
                "Only one player pause may exist at a time.");
            Assert.That(PlayerPauseRules.CanRequest(true, false, false, true, state, phase), Is.False);
        }

        [Test]
        public void Release_OnlyByTheRequester()
        {
            Assert.That(PlayerPauseRules.CanRelease(true, 2, 2), Is.True);
            Assert.That(PlayerPauseRules.CanRelease(true, 2, 1), Is.False);
            Assert.That(PlayerPauseRules.CanRelease(false, 2, 2), Is.False);
            Assert.That(PlayerPauseRules.CanRelease(true, -1, -1), Is.False);
        }

        [Test]
        public void Timer_ExpiresAtDeadline()
        {
            Assert.That(PlayerPauseRules.HasExpired(310d, 309.999d), Is.False);
            Assert.That(PlayerPauseRules.HasExpired(310d, 310d), Is.True);
            Assert.That(PlayerPauseRules.HasExpired(0d, 9999d), Is.False,
                "A held timer never expires.");
            Assert.That(PlayerPauseRules.GetRemaining(310d, 0d, 100d), Is.EqualTo(210d));
            Assert.That(PlayerPauseRules.GetRemaining(310d, 0d, 400d), Is.EqualTo(0d));
        }

        [Test]
        public void DisconnectPause_HoldsAndResumesTheRemainingTime()
        {
            var held = PlayerPauseRules.HoldRemaining(310d, 190d);
            Assert.That(held, Is.EqualTo(120d));
            Assert.That(PlayerPauseRules.GetRemaining(0d, held, 250d), Is.EqualTo(120d),
                "The reconnect window must not consume the player pause.");

            Assert.That(PlayerPauseRules.TryResumeHeld(held, 500d, out var endsAt), Is.True);
            Assert.That(endsAt, Is.EqualTo(620d));

            Assert.That(PlayerPauseRules.TryResumeHeld(0d, 500d, out endsAt), Is.False);
            Assert.That(endsAt, Is.EqualTo(0d));
        }
    }
}
