using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardCombatRulesTests
    {
        [Test]
        public void InitialQueue_UsesFormationThenCurrentLastPlacePriority()
        {
            var coordinateA = new Vector2Int(1, 2);
            var coordinateB = new Vector2Int(3, 4);
            var queue = BoardCombatRules.BuildInitialQueue(
                new[]
                {
                    new BoardCombatPlacement(0, coordinateA, 1d),
                    new BoardCombatPlacement(1, coordinateA, 3d),
                    new BoardCombatPlacement(2, coordinateB, 2d),
                    new BoardCombatPlacement(3, coordinateB, 3d)
                },
                new[] { 1, 2, 3, 4 });

            Assert.That(queue, Has.Count.EqualTo(2));
            Assert.That(queue[0].Coordinate, Is.EqualTo(coordinateB));
            Assert.That(queue[0].ParticipantMask, Is.EqualTo((byte)0b1100));
            Assert.That(queue[1].Coordinate, Is.EqualTo(coordinateA));
        }

        [Test]
        public void Standings_UseHealthEliminationTimeRubberBandAndSlotOrder()
        {
            var timeout = BoardCombatRules.ResolveStandings(new[]
            {
                new BoardCombatRankingEntry(0, 70, double.NaN, 2d, 1),
                new BoardCombatRankingEntry(1, 80, double.NaN, 4d, 2),
                new BoardCombatRankingEntry(2, 80, double.NaN, 4d, 4),
                new BoardCombatRankingEntry(3, 80, double.NaN, 4d, 4)
            });
            Assert.That(
                new[] { timeout[0].Slot, timeout[1].Slot, timeout[2].Slot, timeout[3].Slot },
                Is.EqualTo(new[] { 2, 3, 1, 0 }));
            Assert.That(timeout[3].RetreatDistance, Is.EqualTo(3));

            var knockout = BoardCombatRules.ResolveStandings(new[]
            {
                new BoardCombatRankingEntry(0, 25, double.NaN, 3d, 1),
                new BoardCombatRankingEntry(1, 0, 20d, 1d, 2),
                new BoardCombatRankingEntry(2, 0, 15d, 2d, 3)
            });
            Assert.That(
                new[] { knockout[0].Slot, knockout[1].Slot, knockout[2].Slot },
                Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(knockout[0].RetreatDistance, Is.Zero);
            Assert.That(knockout[2].RetreatDistance, Is.EqualTo(2));
        }

        [Test]
        public void CombatFlow_HoldsUntilAuthoritativeCompletionSignal()
        {
            var flow = new BoardFlowStateMachine();
            flow.Start(0d);
            flow.Tick(6d);
            for (var slot = 0; slot < BoardFlowStateMachine.RequiredPlayerCount; slot++)
                Assert.That(flow.TryReportPlayerArrived(slot, 10d + slot), Is.True);

            flow.Tick(18d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.CombatResolve));

            flow.Tick(1_000d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.CombatResolve));
            Assert.That(flow.TryCompleteCombat(1_000d), Is.True);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.LandingEffectResolve));
        }
    }
}
