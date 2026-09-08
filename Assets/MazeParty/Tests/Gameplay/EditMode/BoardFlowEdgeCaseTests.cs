using System.Collections.Generic;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardFlowEdgeCaseTests
    {
        [Test]
        public void TransitionEvents_LateTicksUseScheduledLogicalBoundaries()
        {
            var transitions = new List<BoardFlowTransition>();
            var flow = new BoardFlowStateMachine();
            flow.Transitioned += transitions.Add;
            flow.Start(50d);

            flow.Tick(236d);
            flow.Tick(241d);
            Assert.That(flow.TrySkipMinigame(250d), Is.True);
            flow.Tick(253d);

            Assert.That(transitions, Has.Count.EqualTo(7));
            AssertTransition(
                transitions[0],
                BoardFlowState.TurnOverview,
                BoardFlowState.Descending,
                1,
                55d);
            AssertTransition(
                transitions[1],
                BoardFlowState.Descending,
                BoardFlowState.Action,
                1,
                56d);
            AssertTransition(
                transitions[2],
                BoardFlowState.Action,
                BoardFlowState.AscendingResolve,
                1,
                236d);
            AssertTransition(
                transitions[3],
                BoardFlowState.AscendingResolve,
                BoardFlowState.LandingEffectResolve,
                1,
                241d);
            AssertTransition(
                transitions[4],
                BoardFlowState.LandingEffectResolve,
                BoardFlowState.MinigameIntroReady,
                1,
                245d);
            AssertTransition(
                transitions[5],
                BoardFlowState.MinigameIntroReady,
                BoardFlowState.SkippedResult,
                1,
                250d);
            AssertTransition(
                transitions[6],
                BoardFlowState.SkippedResult,
                BoardFlowState.TurnOverview,
                2,
                253d);
        }

        [Test]
        public void MultipleReconnectPauses_PreserveExactActionExpiry()
        {
            var flow = StartInAction();

            Assert.That(flow.Pause(8d), Is.True);
            Assert.That(flow.Resume(18d), Is.True);
            Assert.That(flow.Pause(20d), Is.True);
            Assert.That(flow.Resume(50d), Is.True);

            flow.Tick(225.999d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.Action));
            Assert.That(flow.GetActionRemaining(225.999d), Is.EqualTo(0.001d).Within(0.000001d));

            flow.Tick(226d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.AscendingResolve));
            Assert.That(flow.StateStartedAt, Is.EqualTo(186d));
            Assert.That(flow.GetStateRemaining(226d), Is.EqualTo(5d));
        }

        [Test]
        public void FourthArrivalAtExactActionDeadline_LosesToTimeout()
        {
            var flow = StartInAction();
            Assert.That(flow.TryReportPlayerArrived(0, 10d), Is.True);
            Assert.That(flow.TryReportPlayerArrived(1, 20d), Is.True);
            Assert.That(flow.TryReportPlayerArrived(2, 30d), Is.True);

            var accepted = flow.TryReportPlayerArrived(3, 186d);

            Assert.That(accepted, Is.False);
            Assert.That(flow.ArrivedPlayerCount, Is.EqualTo(3));
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.AscendingResolve));
            Assert.That(flow.LastActionEndReason, Is.EqualTo(BoardActionEndReason.TimeExpired));
        }

        [Test]
        public void RestartWhilePaused_ClearsTurnAndArrivalResidue()
        {
            var flow = StartInAction();
            Assert.That(flow.TryReportPlayerArrived(0, 7d), Is.True);
            Assert.That(flow.Pause(8d), Is.True);

            flow.Start(100d, 9);

            Assert.That(flow.IsStarted, Is.True);
            Assert.That(flow.IsPaused, Is.False);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.TurnOverview));
            Assert.That(flow.CurrentTurn, Is.EqualTo(9));
            Assert.That(flow.ArrivedPlayerCount, Is.Zero);
            Assert.That(flow.LastActionEndReason, Is.EqualTo(BoardActionEndReason.None));
            Assert.That(flow.ActionClock.IsRunning, Is.False);
            Assert.That(flow.StateStartedAt, Is.EqualTo(100d));
            Assert.That(flow.GetStateRemaining(100d), Is.EqualTo(5d));
        }

        private static BoardFlowStateMachine StartInAction()
        {
            var flow = new BoardFlowStateMachine();
            flow.Start(0d);
            flow.Tick(6d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.Action));
            return flow;
        }

        private static void AssertTransition(
            BoardFlowTransition transition,
            BoardFlowState previous,
            BoardFlowState current,
            int turn,
            double occurredAt)
        {
            Assert.That(transition.Previous, Is.EqualTo(previous));
            Assert.That(transition.Current, Is.EqualTo(current));
            Assert.That(transition.Turn, Is.EqualTo(turn));
            Assert.That(transition.OccurredAt, Is.EqualTo(occurredAt));
        }
    }
}
