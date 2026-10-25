using MazeParty.Gameplay.Minigames.OddOneOut;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class OddOneOutMatchStateTests
    {
        [Test]
        public void AllSixteenChoices_ScoreOnlyTheUniquePlayer_AndStayPrivateUntilDeadline()
        {
            for (var mask = 0; mask < 16; mask++)
            {
                var game = new OddOneOutMatchState(); var count = 0; var expected = -1;
                Assert.That(game.TrySetChoice(0, true), Is.False);
                game.AdvanceTo(2.999999d);
                Assert.That(game.Phase, Is.EqualTo(OddOneOutPhase.Countdown));
                game.AdvanceTo(3d);
                Assert.That(game.Phase, Is.EqualTo(OddOneOutPhase.Choosing));
                Assert.That(game.Remaining, Is.EqualTo(5d));
                for (var slot = 0; slot < 4; slot++) { game.TrySetChoice(slot, (mask & (1 << slot)) != 0); if (game.GetChoice(slot)) count++; }
                if (count == 1 || count == 3)
                    for (var i = 0; i < 4; i++) if (game.GetChoice(i) == (count == 1)) expected = i;
                game.AdvanceTo(7.999999d);
                Assert.That(game.RevealedMask, Is.Zero);
                for (var viewer = -1; viewer < 4; viewer++)
                    for (var slot = 0; slot < 4; slot++)
                        Assert.That(game.GetVisibleChoice(slot, viewer), Is.EqualTo(slot == viewer && game.GetChoice(slot)));
                game.AdvanceTo(8d);
                Assert.That(game.Phase, Is.EqualTo(OddOneOutPhase.Reveal));
                Assert.That(game.RevealedMask, Is.EqualTo(mask));
                Assert.That(game.WinnerSlot, Is.EqualTo(expected));
                for (var slot = 0; slot < 4; slot++) Assert.That(game.GetScore(slot), Is.EqualTo(slot == expected ? 1 : 0));
                Assert.That(game.TrySetChoice(0, !game.GetChoice(0)), Is.False);
                game.AdvanceTo(9.999999d); Assert.That(game.RoundNumber, Is.EqualTo(1));
                game.AdvanceTo(10d); Assert.That(game.RoundNumber, Is.EqualTo(2));
                Assert.That(game.Phase, Is.EqualTo(OddOneOutPhase.Choosing));
                Assert.That(game.Remaining, Is.EqualTo(5d));
                for (var slot = 0; slot < 4; slot++) { Assert.That(game.GetChoice(slot), Is.False); Assert.That(game.GetScore(slot), Is.EqualTo(slot == expected ? 1 : 0)); }
            }
        }
        [Test]
        public void UnlimitedInstantToggles_TenRounds_KeepScoresAndCompetitionRanks()
        {
            var game = new OddOneOutMatchState(); game.AdvanceTo(3d);
            var winners = new[] { 0, 1, 2, 0, 1, 2, 0, 3, 0, 3 };
            for (var round = 0; round < 10; round++)
            {
                for (var click = 0; click < 101; click++) Assert.That(game.TrySetChoice(winners[round], !game.GetChoice(winners[round])), Is.True);
                game.AdvanceTo(3d + round * 7d + 5d);
                Assert.That(game.WinnerSlot, Is.EqualTo(winners[round]));
                var score = game.GetScore(winners[round]);
                game.AdvanceTo(3d + round * 7d + 5d); game.AdvanceTo(double.NaN); game.AdvanceTo(-1d);
                Assert.That(game.GetScore(winners[round]), Is.EqualTo(score));
                game.AdvanceTo(3d + round * 7d + 7d);
            }
            Assert.That(game.Phase, Is.EqualTo(OddOneOutPhase.Complete)); Assert.That(game.Elapsed, Is.EqualTo(73d));
            Assert.That(game.GetScore(0), Is.EqualTo(4));
            Assert.That(new[] { game.GetRank(0), game.GetRank(1), game.GetRank(2), game.GetRank(3) }, Is.EqualTo(new[] { 1, 2, 2, 2 }));
            Assert.That(game.TrySetChoice(0, true), Is.False);
            game.AdvanceTo(100d); Assert.That(game.GetScore(0), Is.EqualTo(4));
            var tied = new OddOneOutMatchState(); tied.AdvanceTo(73d);
            for (var slot = 0; slot < 4; slot++) Assert.That(tied.GetRank(slot), Is.EqualTo(1));
        }
    }
}
