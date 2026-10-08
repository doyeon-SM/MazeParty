using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardFlowStateMachineTests
    {
        [Test]
        public void RestoreCheckpoint_RebuildsOnlyStableStates()
        {
            var cases = new[]
            {
                (Checkpoint: BoardFlowState.TurnOverview, Turn: 7),
                (Checkpoint: BoardFlowState.MinigameIntroReady, Turn: 7),
                (Checkpoint: BoardFlowState.MatchComplete, Turn: 15)
            };

            foreach (var testCase in cases)
            {
                var flow = new BoardFlowStateMachine(totalTurns: 15);
                var context = testCase.Checkpoint + " / turn " + testCase.Turn;

                flow.RestoreCheckpoint(
                    testCase.Checkpoint,
                    testCase.Turn,
                    100d);

                Assert.That(flow.IsStarted, Is.True, context);
                Assert.That(flow.IsPaused, Is.False, context);
                Assert.That(
                    flow.State,
                    Is.EqualTo(testCase.Checkpoint),
                    context);
                Assert.That(
                    flow.CurrentTurn,
                    Is.EqualTo(testCase.Turn),
                    context);
                Assert.That(flow.StateStartedAt, Is.EqualTo(100d), context);
                Assert.That(flow.ArrivedPlayerCount, Is.Zero, context);
            }
        }

        [Test]
        public void RestoreCheckpoint_RejectsTransientState()
        {
            var flow = new BoardFlowStateMachine();

            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                flow.RestoreCheckpoint(BoardFlowState.Action, 1, 0d));
        }

        [Test]
        public void RestoreCheckpoint_RejectsMatchCompleteBeforeFinalTurn()
        {
            var flow = new BoardFlowStateMachine(totalTurns: 15);

            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                flow.RestoreCheckpoint(BoardFlowState.MatchComplete, 14, 0d));
        }

        [Test]
        public void FullTurn_UsesExactBoundariesAndAdvancesOnlyAfterResult()
        {
            var flow = new BoardFlowStateMachine();
            flow.Start(100d);
            Assert.That(flow.TrySkipMinigame(100d), Is.False);

            flow.Tick(104.999d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.TurnOverview));
            flow.Tick(105d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.Descending));
            flow.Tick(106d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.Action));
            Assert.That(flow.ActionClock.StartedAt, Is.EqualTo(106d));

            ReportAllPlayersArrived(flow, 113d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.AscendingResolve));
            flow.Tick(117.999d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.AscendingResolve));
            flow.Tick(118d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.CombatResolve));
            Assert.That(flow.TryCompleteCombat(118d), Is.True);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.LandingEffectResolve));
            flow.Tick(122d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigameIntroReady));
            Assert.That(flow.Pause(122d), Is.True);
            Assert.That(flow.TrySkipMinigame(122d), Is.False);
            Assert.That(flow.Resume(122d), Is.True);
            Assert.That(flow.TrySkipMinigame(122d), Is.True);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigameResult));

            flow.Tick(125d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.TurnOverview));
            Assert.That(flow.CurrentTurn, Is.EqualTo(2));
        }

        [Test]
        public void MinigameResult_UsesExactBoundaryForNextAndFinalTurns()
        {
            var cases = new[]
            {
                (
                    TotalTurns: BoardFlowStateMachine.DefaultTotalTurns,
                    ExpectedState: BoardFlowState.TurnOverview,
                    ExpectedTurn: 2),
                (
                    TotalTurns: 1,
                    ExpectedState: BoardFlowState.MatchComplete,
                    ExpectedTurn: 1)
            };

            foreach (var testCase in cases)
            {
                var flow = new BoardFlowStateMachine(
                    totalTurns: testCase.TotalTurns);
                flow.Start(0d);
                flow.Tick(6d);
                ReportAllPlayersArrived(flow, 10d);
                flow.Tick(15d);
                Assert.That(flow.TryCompleteCombat(15d), Is.True);
                flow.Tick(19d);
                Assert.That(
                    flow.State,
                    Is.EqualTo(BoardFlowState.MinigameIntroReady));

                Assert.That(flow.TryBeginMinigameLoading(20d), Is.True);
                Assert.That(flow.TryBeginMinigame(21d), Is.True);
                Assert.That(flow.TryCompleteMinigame(22d), Is.True);
                Assert.That(
                    flow.GetStateRemaining(22d),
                    Is.EqualTo(3d));

                flow.Tick(24.999d);
                Assert.That(
                    flow.State,
                    Is.EqualTo(BoardFlowState.MinigameResult));

                flow.Tick(25d);
                Assert.That(flow.State, Is.EqualTo(testCase.ExpectedState));
                Assert.That(flow.CurrentTurn, Is.EqualTo(testCase.ExpectedTurn));
            }
        }

        [Test]
        public void AscendingResolve_WaitsForDeathPresentationAndPreservesPauseTime()
        {
            var flow = StartInAction();
            ReportAllPlayersArrived(flow, 13d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.AscendingResolve));
            Assert.That(flow.StateStartedAt, Is.EqualTo(13d));

            flow.Tick(18d, false, true);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.AscendingResolve));
            Assert.That(flow.Pause(18.2d, false, true), Is.True);
            Assert.That(flow.Resume(28.2d), Is.True);
            flow.Tick(28.7d, false, true);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.AscendingResolve));
            flow.Tick(29d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.CombatResolve));
            Assert.That(flow.StateStartedAt, Is.EqualTo(19d));
        }

        [Test]
        public void LandingEffectResolve_UsesRequestedDurationAndExactBoundary()
        {
            var flow = StartInCombat();

            Assert.That(flow.TryCompleteCombat(15d, 2.5d), Is.True);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.LandingEffectResolve));
            Assert.That(flow.GetStateRemaining(15d), Is.EqualTo(2.5d));

            flow.Tick(17.499d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.LandingEffectResolve));
            Assert.That(
                flow.GetStateRemaining(17.499d),
                Is.EqualTo(0.001d).Within(0.0000001d));

            flow.Tick(17.5d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigameIntroReady));
            Assert.That(flow.StateStartedAt, Is.EqualTo(17.5d));
        }

        [Test]
        public void LandingEffectResolve_CanShortenZeroTransferToResultBoundary()
        {
            var flow = StartInCombat();
            Assert.That(flow.TryCompleteCombat(15d, 9d), Is.True);

            Assert.That(
                flow.TryShortenLandingEffectResolve(18d, 4d),
                Is.True);
            Assert.That(flow.GetStateRemaining(18d), Is.EqualTo(2d));

            flow.Tick(19.999d);
            Assert.That(
                flow.State,
                Is.EqualTo(BoardFlowState.LandingEffectResolve));
            flow.Tick(20d);
            Assert.That(
                flow.State,
                Is.EqualTo(BoardFlowState.MinigameIntroReady));
            Assert.That(flow.StateStartedAt, Is.EqualTo(20d));
        }

        [Test]
        public void LandingEffectResolve_DefersExactBoundaryAndPauseUntilPresentationFinishes()
        {
            var flow = StartInCombat();
            Assert.That(flow.TryCompleteCombat(15d, 2d), Is.True);

            flow.Tick(17d, false, false, true);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.LandingEffectResolve));
            Assert.That(flow.GetStateRemaining(17d), Is.Zero);

            Assert.That(flow.Pause(17.25d, false, false, true), Is.True);
            Assert.That(flow.GetStateRemaining(27.25d), Is.Zero);
            Assert.That(flow.Resume(27.25d), Is.True);
            flow.Tick(27.5d, false, false, true);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.LandingEffectResolve));

            flow.Tick(28d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigameIntroReady));
            Assert.That(flow.StateStartedAt, Is.EqualTo(18d));
            Assert.That(flow.GetStateRemaining(28d), Is.EqualTo(60d));
        }

        [Test]
        public void LandingEffectDuration_RejectsInvalidValuesWithoutLeavingCombat()
        {
            var flow = StartInCombat();
            var invalidDurations = new[]
            {
                -0.001d,
                0d,
                double.NaN,
                double.PositiveInfinity
            };

            foreach (var duration in invalidDurations)
            {
                Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                    flow.TryCompleteCombat(15d, duration));
                Assert.That(flow.State, Is.EqualTo(BoardFlowState.CombatResolve));
            }
        }

        [Test]
        public void MinigameReadyAndLoadingDeadlines_PreserveOneMinuteAcrossReconnectPause()
        {
            var flow = new BoardFlowStateMachine();
            flow.Start(100d);
            flow.Tick(106d);
            ReportAllPlayersArrived(flow, 113d);
            flow.Tick(118d);
            Assert.That(flow.TryCompleteCombat(118d), Is.True);
            flow.Tick(122d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigameIntroReady));
            Assert.That(flow.GetStateRemaining(122d), Is.EqualTo(60d));
            Assert.That(flow.GetStateRemaining(129.999d), Is.GreaterThan(52d));

            Assert.That(flow.Pause(130d), Is.True);
            Assert.That(flow.GetStateRemaining(190d), Is.EqualTo(52d));
            Assert.That(flow.Resume(190d), Is.True);
            Assert.That(flow.GetStateRemaining(241.999d), Is.GreaterThan(0d));
            Assert.That(flow.GetStateRemaining(242d), Is.Zero);
            Assert.That(flow.TryBeginMinigameLoading(242d), Is.True);

            Assert.That(flow.GetStateRemaining(242d), Is.EqualTo(60d));
            Assert.That(flow.Pause(250d), Is.True);
            Assert.That(flow.GetStateRemaining(280d), Is.EqualTo(52d));
            Assert.That(flow.TryBeginMinigame(280d), Is.False);
            flow.Tick(280d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigameLoading));
            Assert.That(flow.Resume(280d), Is.True);
            Assert.That(flow.GetStateRemaining(331.999d), Is.GreaterThan(0d));
            Assert.That(flow.GetStateRemaining(332d), Is.Zero);
            Assert.That(flow.TryBeginMinigame(332d), Is.True);

            Assert.That(flow.Pause(333d), Is.True);
            Assert.That(flow.TryCompleteMinigame(400d), Is.False);
            flow.Tick(400d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigamePlaying));
            Assert.That(flow.Resume(400d), Is.True);
            Assert.That(flow.TryCompleteMinigame(400d), Is.True);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigameResult));
        }

        [Test]
        public void ActionDeadline_WinsExactArrivalAndWaitsForApprovedRollSettlement()
        {
            var timedOut = StartInAction();
            timedOut.Tick(185.999d);
            Assert.That(timedOut.State, Is.EqualTo(BoardFlowState.Action));
            timedOut.Tick(186d);
            Assert.That(timedOut.State, Is.EqualTo(BoardFlowState.AscendingResolve));
            Assert.That(timedOut.LastActionEndReason, Is.EqualTo(BoardActionEndReason.TimeExpired));

            var exactArrival = StartInAction();
            Assert.That(exactArrival.TryReportPlayerArrived(0, 10d), Is.True);
            Assert.That(exactArrival.TryReportPlayerArrived(0, 11d), Is.False);
            Assert.That(exactArrival.ArrivedPlayerCount, Is.EqualTo(1));
            Assert.That(exactArrival.TryReportPlayerArrived(1, 20d), Is.True);
            Assert.That(exactArrival.TryReportPlayerArrived(2, 30d), Is.True);
            Assert.That(exactArrival.TryReportPlayerArrived(3, 186d), Is.False);
            Assert.That(exactArrival.ArrivedPlayerCount, Is.EqualTo(3));
            Assert.That(exactArrival.LastActionEndReason, Is.EqualTo(BoardActionEndReason.TimeExpired));

            var settling = StartInAction();
            settling.Tick(186d, true);
            Assert.That(settling.State, Is.EqualTo(BoardFlowState.Action));
            settling.Tick(186.999d, true);
            Assert.That(settling.State, Is.EqualTo(BoardFlowState.Action));
            settling.Tick(187d, false);
            Assert.That(settling.State, Is.EqualTo(BoardFlowState.AscendingResolve));
            Assert.That(settling.StateStartedAt, Is.EqualTo(187d));
        }

        [Test]
        public void ReconnectPauses_PreserveActionClockChoiceProtectionAndArrivals()
        {
            var flow = StartInAction();
            Assert.That(flow.TryReportPlayerArrived(0, 7d), Is.True);

            Assert.That(flow.Pause(8d), Is.True);
            flow.Tick(108d);
            Assert.That(flow.ArrivedPlayerCount, Is.EqualTo(1));
            Assert.That(flow.GetActionRemaining(108d), Is.EqualTo(178d));
            Assert.That(flow.GetChoiceRemaining(108d), Is.EqualTo(28d));
            Assert.That(flow.GetOpeningProtectionRemaining(108d), Is.EqualTo(3d));
            Assert.That(flow.Resume(108d), Is.True);

            flow.Tick(110d);
            Assert.That(flow.Pause(110d), Is.True);
            Assert.That(flow.Resume(140d), Is.True);
            flow.Tick(315.999d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.Action));
            flow.Tick(316d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.AscendingResolve));
            Assert.That(flow.StateStartedAt, Is.EqualTo(186d));
        }

        [Test]
        public void LateTickUsesLogicalBoundaries_AndRestartClearsPausedResidue()
        {
            var late = new BoardFlowStateMachine();
            late.Start(10d);
            late.Tick(250d);
            Assert.That(late.ActionClock.StartedAt, Is.EqualTo(16d));
            Assert.That(late.State, Is.EqualTo(BoardFlowState.CombatResolve));
            Assert.That(late.StateStartedAt, Is.EqualTo(201d));
            Assert.That(late.LastActionEndReason, Is.EqualTo(BoardActionEndReason.TimeExpired));

            var restarted = StartInAction();
            Assert.That(restarted.TryReportPlayerArrived(0, 7d), Is.True);
            Assert.That(restarted.Pause(8d), Is.True);
            restarted.Start(100d, 9);
            Assert.That(restarted.IsPaused, Is.False);
            Assert.That(restarted.State, Is.EqualTo(BoardFlowState.TurnOverview));
            Assert.That(restarted.CurrentTurn, Is.EqualTo(9));
            Assert.That(restarted.ArrivedPlayerCount, Is.Zero);
            Assert.That(restarted.LastActionEndReason, Is.EqualTo(BoardActionEndReason.None));
            Assert.That(restarted.ActionClock.IsRunning, Is.False);
        }

        private static BoardFlowStateMachine StartInAction()
        {
            var flow = new BoardFlowStateMachine();
            flow.Start(0d);
            flow.Tick(6d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.Action));
            return flow;
        }

        private static BoardFlowStateMachine StartInCombat()
        {
            var flow = StartInAction();
            ReportAllPlayersArrived(flow, 10d);
            flow.Tick(15d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.CombatResolve));
            return flow;
        }

        private static void ReportAllPlayersArrived(BoardFlowStateMachine flow, double finalTime)
        {
            Assert.That(flow.TryReportPlayerArrived(0, finalTime - 0.3d), Is.True);
            Assert.That(flow.TryReportPlayerArrived(1, finalTime - 0.2d), Is.True);
            Assert.That(flow.TryReportPlayerArrived(2, finalTime - 0.1d), Is.True);
            Assert.That(flow.TryReportPlayerArrived(3, finalTime), Is.True);
        }
    }
}
