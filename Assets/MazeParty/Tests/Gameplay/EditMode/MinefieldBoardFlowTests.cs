using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class MinefieldBoardFlowTests
    {
        [Test]
        public void MinigameLifecycle_CompletesAndBeginsNextTurn()
        {
            var flow = AdvanceNormallyToMinigameIntro();

            Assert.That(flow.TryBeginMinigameLoading(20d), Is.True);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigameLoading));
            Assert.That(flow.CurrentTurn, Is.EqualTo(1));

            Assert.That(flow.TryBeginMinigame(21d), Is.True);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigamePlaying));

            Assert.That(flow.TryCompleteMinigame(22d), Is.True);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.SkippedResult));
            Assert.That(flow.CurrentTurn, Is.EqualTo(1));
            Assert.That(flow.GetStateRemaining(22d), Is.EqualTo(3d));

            flow.Tick(24.999d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.SkippedResult));

            flow.Tick(25d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.TurnOverview));
            Assert.That(flow.CurrentTurn, Is.EqualTo(2));
            Assert.That(flow.GetStateRemaining(25d), Is.EqualTo(5d));
        }

        [Test]
        public void LoadingPause_BlocksPlayingTransitionUntilResume()
        {
            var flow = AdvanceNormallyToMinigameIntro();
            Assert.That(flow.TryBeginMinigameLoading(20d), Is.True);

            Assert.That(flow.Pause(21d), Is.True);
            Assert.That(flow.TryBeginMinigame(100d), Is.False);
            flow.Tick(100d);

            Assert.That(flow.IsPaused, Is.True);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigameLoading));

            Assert.That(flow.Resume(100d), Is.True);
            Assert.That(flow.TryBeginMinigame(100d), Is.True);
            Assert.That(flow.IsPaused, Is.False);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigamePlaying));
        }

        [Test]
        public void PlayingPause_BlocksCompletionUntilResume()
        {
            var flow = AdvanceNormallyToMinigameIntro();
            Assert.That(flow.TryBeginMinigameLoading(20d), Is.True);
            Assert.That(flow.TryBeginMinigame(21d), Is.True);

            Assert.That(flow.Pause(22d), Is.True);
            Assert.That(flow.TryCompleteMinigame(200d), Is.False);
            flow.Tick(200d);

            Assert.That(flow.IsPaused, Is.True);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigamePlaying));

            Assert.That(flow.Resume(200d), Is.True);
            Assert.That(flow.TryCompleteMinigame(200d), Is.True);
            Assert.That(flow.IsPaused, Is.False);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.SkippedResult));
        }

        private static BoardFlowStateMachine AdvanceNormallyToMinigameIntro()
        {
            var flow = new BoardFlowStateMachine();
            flow.Start(0d);

            flow.Tick(6d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.Action));

            Assert.That(flow.TryReportPlayerArrived(0, 7d), Is.True);
            Assert.That(flow.TryReportPlayerArrived(1, 8d), Is.True);
            Assert.That(flow.TryReportPlayerArrived(2, 9d), Is.True);
            Assert.That(flow.TryReportPlayerArrived(3, 10d), Is.True);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.AscendingResolve));

            flow.Tick(15d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.CombatResolve));
            Assert.That(flow.TryCompleteCombat(15d), Is.True);

            flow.Tick(19d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigameIntroReady));
            return flow;
        }
    }
}
