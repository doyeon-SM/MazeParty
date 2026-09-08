using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardFlowStateMachineTests
    {
        [Test]
        public void TimedOpening_UsesExactOverviewAndDescendingBoundaries()
        {
            var flow = new BoardFlowStateMachine();
            flow.Start(100d);

            Assert.That(flow.State, Is.EqualTo(BoardFlowState.TurnOverview));
            Assert.That(flow.CurrentTurn, Is.EqualTo(1));
            Assert.That(flow.GetStateRemaining(100d), Is.EqualTo(5d));

            flow.Tick(104.999d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.TurnOverview));

            flow.Tick(105d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.Descending));
            Assert.That(flow.GetStateRemaining(105d), Is.EqualTo(1d));

            flow.Tick(105.999d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.Descending));

            flow.Tick(106d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.Action));
            Assert.That(flow.ActionClock.StartedAt, Is.EqualTo(106d));
            Assert.That(flow.GetActionRemaining(106d), Is.EqualTo(180d));
            Assert.That(flow.GetChoiceRemaining(106d), Is.EqualTo(30d));
            Assert.That(flow.GetOpeningProtectionRemaining(106d), Is.EqualTo(5d));
        }

        [Test]
        public void LateTick_StartsActionAtScheduledDescendingCompletion()
        {
            var flow = new BoardFlowStateMachine();
            flow.Start(10d);

            flow.Tick(16.5d);

            Assert.That(flow.State, Is.EqualTo(BoardFlowState.Action));
            Assert.That(flow.ActionClock.StartedAt, Is.EqualTo(16d));
            Assert.That(flow.GetActionRemaining(16.5d), Is.EqualTo(179.5d));
            Assert.That(flow.GetChoiceRemaining(16.5d), Is.EqualTo(29.5d));
            Assert.That(flow.GetOpeningProtectionRemaining(16.5d), Is.EqualTo(4.5d));
        }

        [Test]
        public void FourUniqueArrivals_EndActionEarly()
        {
            var flow = StartInAction();
            Assert.That(flow.TryReportPlayerArrived(0, 10d), Is.True);
            Assert.That(flow.TryReportPlayerArrived(1, 11d), Is.True);
            Assert.That(flow.TryReportPlayerArrived(1, 12d), Is.False);
            Assert.That(flow.TryReportPlayerArrived(2, 12d), Is.True);

            Assert.That(flow.State, Is.EqualTo(BoardFlowState.Action));
            Assert.That(flow.ArrivedPlayerCount, Is.EqualTo(3));

            Assert.That(flow.TryReportPlayerArrived(3, 13d), Is.True);

            Assert.That(flow.State, Is.EqualTo(BoardFlowState.AscendingResolve));
            Assert.That(flow.StateStartedAt, Is.EqualTo(13d));
            Assert.That(flow.LastActionEndReason, Is.EqualTo(BoardActionEndReason.AllPlayersArrived));
            Assert.That(flow.ActionClock.IsRunning, Is.False);
            Assert.That(
                flow.ActionClock.ChoiceResolution,
                Is.EqualTo(ItemChoiceResolution.DoNotUse));
        }

        [Test]
        public void ActionTimeout_EntersAscendingAtExactOneHundredEightySecondBoundary()
        {
            var flow = StartInAction();

            flow.Tick(185.999d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.Action));

            flow.Tick(186d);

            Assert.That(flow.State, Is.EqualTo(BoardFlowState.AscendingResolve));
            Assert.That(flow.StateStartedAt, Is.EqualTo(186d));
            Assert.That(flow.LastActionEndReason, Is.EqualTo(BoardActionEndReason.TimeExpired));
        }

        [Test]
        public void AscendingAndLandingEffectsThenDevelopmentSkip_ShowsResultBeforeNextTurn()
        {
            var flow = StartInAction();
            ReportAllPlayersArrived(flow, 10d);

            flow.Tick(14.999d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.AscendingResolve));

            flow.Tick(15d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.CombatResolve));
            Assert.That(flow.GetStateRemaining(15d), Is.Zero);

            Assert.That(flow.TryCompleteCombat(15d), Is.True);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.LandingEffectResolve));
            Assert.That(flow.GetStateRemaining(15d), Is.EqualTo(4d));

            flow.Tick(18.999d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.LandingEffectResolve));

            flow.Tick(19d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigameIntroReady));
            Assert.That(flow.CurrentTurn, Is.EqualTo(1));

            Assert.That(flow.TrySkipMinigame(19d), Is.True);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.SkippedResult));
            Assert.That(flow.CurrentTurn, Is.EqualTo(1));
            Assert.That(flow.GetStateRemaining(19d), Is.EqualTo(3d));

            flow.Tick(21.999d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.SkippedResult));
            Assert.That(flow.GetStateRemaining(21.999d), Is.EqualTo(0.001d).Within(0.000001d));

            flow.Tick(22d);

            Assert.That(flow.State, Is.EqualTo(BoardFlowState.TurnOverview));
            Assert.That(flow.CurrentTurn, Is.EqualTo(2));
            Assert.That(flow.GetStateRemaining(22d), Is.EqualTo(5d));
            Assert.That(flow.LastActionEndReason, Is.EqualTo(BoardActionEndReason.None));
        }

        [Test]
        public void MinigameSkip_IsRejectedOutsideIntroAndWhilePaused()
        {
            var flow = new BoardFlowStateMachine();
            flow.Start(0d);
            Assert.That(flow.TrySkipMinigame(0d), Is.False);

            flow.Tick(195d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.CombatResolve));
            Assert.That(flow.TryCompleteCombat(195d), Is.True);
            flow.Tick(199d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigameIntroReady));

            Assert.That(flow.Pause(199d), Is.True);
            Assert.That(flow.TrySkipMinigame(300d), Is.False);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigameIntroReady));
        }

        [Test]
        public void PauseResume_PreservesTurnOverviewRemaining()
        {
            var flow = new BoardFlowStateMachine();
            flow.Start(0d);
            flow.Tick(2d);

            Assert.That(flow.Pause(2d), Is.True);
            flow.Tick(102d);
            Assert.That(flow.GetStateRemaining(102d), Is.EqualTo(3d));

            Assert.That(flow.Resume(102d), Is.True);
            flow.Tick(104.999d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.TurnOverview));

            flow.Tick(105d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.Descending));
        }

        [Test]
        public void PauseResume_PreservesDescendingRemaining()
        {
            var flow = new BoardFlowStateMachine();
            flow.Start(0d);
            flow.Tick(5.25d);

            Assert.That(flow.State, Is.EqualTo(BoardFlowState.Descending));
            Assert.That(flow.Pause(5.25d), Is.True);
            Assert.That(flow.GetStateRemaining(100d), Is.EqualTo(0.75d));

            Assert.That(flow.Resume(100d), Is.True);
            flow.Tick(100.749d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.Descending));

            flow.Tick(100.75d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.Action));
            Assert.That(flow.ActionClock.StartedAt, Is.EqualTo(6d));
        }

        [Test]
        public void PauseResume_PreservesActionChoiceProtectionAndArrivals()
        {
            var flow = StartInAction();
            Assert.That(flow.TryReportPlayerArrived(0, 7d), Is.True);

            Assert.That(flow.Pause(8d), Is.True);
            flow.Tick(108d);

            Assert.That(flow.State, Is.EqualTo(BoardFlowState.Action));
            Assert.That(flow.ArrivedPlayerCount, Is.EqualTo(1));
            Assert.That(flow.GetActionRemaining(108d), Is.EqualTo(178d));
            Assert.That(flow.GetChoiceRemaining(108d), Is.EqualTo(28d));
            Assert.That(flow.GetOpeningProtectionRemaining(108d), Is.EqualTo(3d));

            Assert.That(flow.Resume(108d), Is.True);
            Assert.That(flow.GetActionRemaining(108d), Is.EqualTo(178d));
            Assert.That(flow.GetChoiceRemaining(108d), Is.EqualTo(28d));
            Assert.That(flow.GetOpeningProtectionRemaining(108d), Is.EqualTo(3d));

            flow.Tick(111d);
            Assert.That(flow.GetActionRemaining(111d), Is.EqualTo(175d));
            Assert.That(flow.GetChoiceRemaining(111d), Is.EqualTo(25d));
            Assert.That(flow.IsOpeningProtectionActive(111d), Is.False);
        }

        [Test]
        public void PauseResume_PreservesAscendingResolveRemaining()
        {
            var flow = StartInAction();
            ReportAllPlayersArrived(flow, 10d);
            flow.Tick(12d);

            Assert.That(flow.Pause(12d), Is.True);
            Assert.That(flow.GetStateRemaining(112d), Is.EqualTo(3d));

            Assert.That(flow.Resume(112d), Is.True);
            flow.Tick(114.999d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.AscendingResolve));

            flow.Tick(115d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.CombatResolve));
            Assert.That(flow.TryCompleteCombat(115d), Is.True);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.LandingEffectResolve));
            Assert.That(flow.GetStateRemaining(115d), Is.EqualTo(4d));

            flow.Tick(119d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigameIntroReady));
        }

        [Test]
        public void PauseResume_PreservesSkippedResultRemaining()
        {
            var flow = StartInAction();
            ReportAllPlayersArrived(flow, 10d);
            flow.Tick(19d);
            Assert.That(flow.TryCompleteCombat(19d), Is.True);
            flow.Tick(23d);
            Assert.That(flow.TrySkipMinigame(23d), Is.True);

            flow.Tick(24d);
            Assert.That(flow.Pause(24d), Is.True);
            Assert.That(flow.GetStateRemaining(120d), Is.EqualTo(2d));

            Assert.That(flow.Resume(120d), Is.True);
            flow.Tick(121.999d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.SkippedResult));

            flow.Tick(122d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.TurnOverview));
            Assert.That(flow.CurrentTurn, Is.EqualTo(2));
        }

        [Test]
        public void LateTick_CatchesUpUsingScheduledPhaseBoundaries()
        {
            var flow = new BoardFlowStateMachine();
            flow.Start(10d);

            flow.Tick(250d);

            Assert.That(flow.ActionClock.StartedAt, Is.EqualTo(16d));
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.CombatResolve));
            Assert.That(flow.StateStartedAt, Is.EqualTo(201d));
            Assert.That(flow.LastActionEndReason, Is.EqualTo(BoardActionEndReason.TimeExpired));
        }

        private static BoardFlowStateMachine StartInAction()
        {
            var flow = new BoardFlowStateMachine();
            flow.Start(0d);
            flow.Tick(6d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.Action));
            return flow;
        }

        private static void ReportAllPlayersArrived(
            BoardFlowStateMachine flow,
            double fourthArrivalTime)
        {
            Assert.That(flow.TryReportPlayerArrived(0, fourthArrivalTime - 0.3d), Is.True);
            Assert.That(flow.TryReportPlayerArrived(1, fourthArrivalTime - 0.2d), Is.True);
            Assert.That(flow.TryReportPlayerArrived(2, fourthArrivalTime - 0.1d), Is.True);
            Assert.That(flow.TryReportPlayerArrived(3, fourthArrivalTime), Is.True);
        }
    }
}
