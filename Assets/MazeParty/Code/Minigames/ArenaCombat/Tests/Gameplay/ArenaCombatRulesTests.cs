using System;
using MazeParty.Gameplay.Minigames.ArenaCombat;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class ArenaCombatRulesTests
    {
        [Test]
        public void Deadline_IsExactAndOneSurvivorEndsEarly()
        {
            Assert.That(
                ArenaCombatRules.ShouldEndMatch(
                    ArenaCombatRules.MatchDurationSeconds - 0.001d, 2),
                Is.False);
            Assert.That(
                ArenaCombatRules.ShouldEndMatch(
                    ArenaCombatRules.MatchDurationSeconds, 2),
                Is.True);
            Assert.That(
                ArenaCombatRules.ShouldEndMatch(1d, 1),
                Is.True);
        }

        [Test]
        public void Ranking_PrefersSurvivingHealthThenLaterElimination()
        {
            var ranks = ArenaCombatRules.ResolveRanks(new[]
            {
                new ArenaCombatRankingEntry(0, 20, double.NaN),
                new ArenaCombatRankingEntry(1, 70, double.NaN),
                new ArenaCombatRankingEntry(2, 0, 12d),
                new ArenaCombatRankingEntry(3, 0, 25d)
            });

            Assert.That(ranks, Is.EqualTo(new[] { 2, 1, 4, 3 }));
        }

        [Test]
        public void Ranking_UsesSlotWhenFactsTie()
        {
            var ranks = ArenaCombatRules.ResolveRanks(new[]
            {
                new ArenaCombatRankingEntry(3, 0, 20d),
                new ArenaCombatRankingEntry(2, 0, 20d),
                new ArenaCombatRankingEntry(1, 50, double.NaN),
                new ArenaCombatRankingEntry(0, 50, double.NaN)
            });

            Assert.That(ranks, Is.EqualTo(new[] { 1, 2, 3, 4 }));
        }

        [Test]
        public void Ranking_RejectsDuplicateSlots()
        {
            Assert.Throws<ArgumentException>(() =>
                ArenaCombatRules.ResolveRanks(new[]
                {
                    new ArenaCombatRankingEntry(0, 100, double.NaN),
                    new ArenaCombatRankingEntry(0, 100, double.NaN),
                    new ArenaCombatRankingEntry(2, 100, double.NaN),
                    new ArenaCombatRankingEntry(3, 100, double.NaN)
                }));
        }
    }
}
