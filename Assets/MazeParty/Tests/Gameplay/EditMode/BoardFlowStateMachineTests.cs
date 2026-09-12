using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardFlowStateMachineTests
    {
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
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.SkippedResult));

            flow.Tick(125d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.TurnOverview));
            Assert.That(flow.CurrentTurn, Is.EqualTo(2));
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

        private static void ReportAllPlayersArrived(BoardFlowStateMachine flow, double finalTime)
        {
            Assert.That(flow.TryReportPlayerArrived(0, finalTime - 0.3d), Is.True);
            Assert.That(flow.TryReportPlayerArrived(1, finalTime - 0.2d), Is.True);
            Assert.That(flow.TryReportPlayerArrived(2, finalTime - 0.1d), Is.True);
            Assert.That(flow.TryReportPlayerArrived(3, finalTime), Is.True);
        }
    }
}
