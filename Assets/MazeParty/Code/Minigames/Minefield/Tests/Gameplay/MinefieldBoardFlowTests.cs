using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class MinefieldBoardFlowTests
    {
        [Test]
        public void MinigameLifecycle_TransitionsAtTheExactResultBoundary()
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
                var flow = AdvanceNormallyToMinigameIntro(testCase.TotalTurns);

                Assert.That(flow.TryBeginMinigameLoading(20d), Is.True);
                Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigameLoading));
                Assert.That(flow.TryBeginMinigame(21d), Is.True);
                Assert.That(flow.State, Is.EqualTo(BoardFlowState.MinigamePlaying));
                Assert.That(flow.TryCompleteMinigame(22d), Is.True);
                Assert.That(flow.State, Is.EqualTo(BoardFlowState.SkippedResult));
                Assert.That(flow.GetStateRemaining(22d), Is.EqualTo(3d));

                flow.Tick(24.999d);
                Assert.That(flow.State, Is.EqualTo(BoardFlowState.SkippedResult));

                flow.Tick(25d);
                Assert.That(flow.State, Is.EqualTo(testCase.ExpectedState));
                Assert.That(flow.CurrentTurn, Is.EqualTo(testCase.ExpectedTurn));
            }
        }

        [Test]
        public void Pause_BlocksLoadingAndPlayingTransitionsUntilResume()
        {
            var loadingFlow = AdvanceNormallyToMinigameIntro();
            Assert.That(loadingFlow.TryBeginMinigameLoading(20d), Is.True);

            Assert.That(loadingFlow.Pause(21d), Is.True);
            Assert.That(loadingFlow.TryBeginMinigame(100d), Is.False);
            loadingFlow.Tick(100d);

            Assert.That(loadingFlow.IsPaused, Is.True);
            Assert.That(
                loadingFlow.State,
                Is.EqualTo(BoardFlowState.MinigameLoading));

            Assert.That(loadingFlow.Resume(100d), Is.True);
            Assert.That(loadingFlow.TryBeginMinigame(100d), Is.True);
            Assert.That(loadingFlow.IsPaused, Is.False);
            Assert.That(
                loadingFlow.State,
                Is.EqualTo(BoardFlowState.MinigamePlaying));

            var playingFlow = AdvanceNormallyToMinigameIntro();
            Assert.That(playingFlow.TryBeginMinigameLoading(20d), Is.True);
            Assert.That(playingFlow.TryBeginMinigame(21d), Is.True);

            Assert.That(playingFlow.Pause(22d), Is.True);
            Assert.That(playingFlow.TryCompleteMinigame(200d), Is.False);
            playingFlow.Tick(200d);

            Assert.That(playingFlow.IsPaused, Is.True);
            Assert.That(
                playingFlow.State,
                Is.EqualTo(BoardFlowState.MinigamePlaying));

            Assert.That(playingFlow.Resume(200d), Is.True);
            Assert.That(playingFlow.TryCompleteMinigame(200d), Is.True);
            Assert.That(playingFlow.IsPaused, Is.False);
            Assert.That(
                playingFlow.State,
                Is.EqualTo(BoardFlowState.SkippedResult));
        }

        private static BoardFlowStateMachine AdvanceNormallyToMinigameIntro(
            int totalTurns = BoardFlowStateMachine.DefaultTotalTurns)
        {
            var flow = new BoardFlowStateMachine(
                totalTurns: totalTurns);
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
