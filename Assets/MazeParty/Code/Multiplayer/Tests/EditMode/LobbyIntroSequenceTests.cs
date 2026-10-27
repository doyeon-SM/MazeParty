using NUnit.Framework;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class LobbyIntroSequenceTests
    {
        [Test]
        public void CompletionAndSkip_RevealControlsOnlyAfterLogoSettles()
        {
            var sequence = new LobbyIntroSequence();
            sequence.Begin();
            Assert.That(sequence.Stage, Is.EqualTo(LobbyIntroStage.Intro));
            sequence.Advance(100f, 1.2f);
            Assert.That(sequence.Stage, Is.EqualTo(LobbyIntroStage.Intro));
            // Natural completion and Esc use the same idempotent transition.
            sequence.FinishIntro(10f);
            sequence.FinishIntro(10.5f);
            Assert.That(sequence.LogoStartedAt, Is.EqualTo(10f));
            sequence.Advance(11.2 - 0.001, 1.2);
            Assert.That(sequence.Stage, Is.EqualTo(LobbyIntroStage.Logo));
            sequence.Advance(11.2, 1.2);
            Assert.That(sequence.Stage, Is.EqualTo(LobbyIntroStage.Ready));
        }

        [Test]
        public void ReturnToLobby_DoesNotReplayEvenAfterInterruptedIntro()
        {
            foreach (var finish in new[] { false, true })
            {
                var sequence = new LobbyIntroSequence();
                sequence.Begin();
                if (finish) { sequence.FinishIntro(0f); sequence.Advance(1.2f, 1.2f); }
                sequence.Hide();
                Assert.That(sequence.Stage, Is.EqualTo(LobbyIntroStage.Hidden));
                sequence.Begin();
                Assert.That(sequence.Stage, Is.EqualTo(LobbyIntroStage.Ready));
                sequence.FinishIntro(5f);
                Assert.That(sequence.Stage, Is.EqualTo(LobbyIntroStage.Ready));
            }
        }
    }
}
