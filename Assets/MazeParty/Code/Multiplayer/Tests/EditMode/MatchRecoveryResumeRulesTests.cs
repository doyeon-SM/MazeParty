using System;
using MazeParty.Gameplay;
using NUnit.Framework;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MatchRecoveryResumeRulesTests
    {
        [Test]
        public void Resume_UsesTheCorrectBoundaryAndDoesNotRepeatSettledMinigames()
        {
            foreach (var row in new[] {
                (MatchRecoveryCheckpoint.TurnOverview, 2, BoardFlowState.TurnOverview, 2),
                (MatchRecoveryCheckpoint.MinigameIntroReady, 2, BoardFlowState.MinigameIntroReady, 2),
                (MatchRecoveryCheckpoint.MinigameSettled, 2, BoardFlowState.TurnOverview, 3),
                (MatchRecoveryCheckpoint.MinigameSettled, 15, BoardFlowState.MatchComplete, 15) })
            {
                var saved = new MatchRecoverySnapshot { checkpoint = row.Item1, turn = row.Item2,
                    settledMinigameTurn = row.Item2, scheduleTurnCount = 15 };
                Assert.That(MatchRecoveryResumeRules.Resolve(saved), Is.EqualTo((row.Item3, row.Item4)));
            }
            Assert.Throws<ArgumentException>(() => MatchRecoveryResumeRules.Resolve(new MatchRecoverySnapshot {
                checkpoint = MatchRecoveryCheckpoint.MinigameSettled, turn = 2, scheduleTurnCount = 15, settledMinigameTurn = 1 }));
        }
        [Test]
        public void RestartAuthority_UsesOriginalAccountIncludingLegacySlotZeroSaves()
        {
            var saved = new MatchRecoverySnapshot { players = new[] {
                new MatchRecoveryPlayerSnapshot { playerKey = MatchRecoveryFingerprint.CreatePlayerKey("original") },
                new MatchRecoveryPlayerSnapshot(), new MatchRecoveryPlayerSnapshot(), new MatchRecoveryPlayerSnapshot() } };
            Assert.That(MatchRecoveryResumeRules.IsOriginalHost(saved, "original"), Is.True);
            foreach (var guest in new[] { "caretaker", "same-nickname", "", null })
                Assert.That(MatchRecoveryResumeRules.IsOriginalHost(saved, guest), Is.False);
        }
    }
}
