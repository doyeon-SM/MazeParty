using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardCombatRulesTests
    {
        [Test]
        public void PunchSequence_CyclesAndResetsOnlyForMissTimeoutOrNewFight()
        {
            var sequence = new CombatPunchSequence();
            var cases = new[]
            {
                (time: 0d, hit: true, damage: 5),
                (time: 0.65d, hit: true, damage: 7),
                (time: 1.30d, hit: true, damage: 10),
                (time: 1.95d, hit: true, damage: 5),
                (time: 2.60d, hit: false, damage: 0),
                (time: 3.25d, hit: true, damage: 5),
                (time: 4.249999d, hit: true, damage: 7),
                (time: 5.249999d, hit: true, damage: 5),
                (time: 20d, hit: true, damage: 5)
            };
            foreach (var entry in cases)
                Assert.That(sequence.Resolve(entry.hit, entry.time, 100),
                    Is.EqualTo(entry.damage), "time " + entry.time);

            sequence.Reset();
            Assert.That(sequence.Resolve(true, 20.65d, 100), Is.EqualTo(5));
        }

        [Test]
        public void PunchDamage_UsesCurrentAttackerHealthInclusiveThresholdsAndRoundsHalvesUp()
        {
            foreach (var entry in new[]
            {
                (health: 100, expected: new[] { 5, 7, 10, 5 }),
                (health: 51, expected: new[] { 5, 7, 10, 5 }),
                (health: 50, expected: new[] { 6, 8, 12, 6 }),
                (health: 11, expected: new[] { 6, 8, 12, 6 }),
                (health: 10, expected: new[] { 8, 11, 15, 8 }),
                (health: 1, expected: new[] { 8, 11, 15, 8 })
            })
            {
                var sequence = new CombatPunchSequence();
                for (var index = 0; index < entry.expected.Length; index++)
                    Assert.That(sequence.Resolve(true, index * 0.65d, entry.health),
                        Is.EqualTo(entry.expected[index]), "health " + entry.health);
            }

            var injured = new CombatPunchSequence();
            Assert.That(injured.Resolve(true, 0d, 100), Is.EqualTo(5));
            Assert.That(injured.Resolve(true, 0.65d, 50), Is.EqualTo(8));
            Assert.That(injured.Resolve(true, 1.3d, 10), Is.EqualTo(15));
        }

        [Test]
        public void PunchSequence_PausePreservesRemainingWindowAndExpiredSequencesStayExpired()
        {
            foreach (var entry in new[]
            {
                (pause: 10.75d, attack: 100.249999d, expected: 7),
                (pause: 10.75d, attack: 100.25d, expected: 5),
                (pause: 11d, attack: 100d, expected: 5)
            })
            {
                var sequence = new CombatPunchSequence();
                Assert.That(sequence.Resolve(true, 10d, 100), Is.EqualTo(5));
                sequence.Pause(entry.pause);
                sequence.Pause(50d);
                Assert.That(sequence.Resolve(true, 60d, 100), Is.Zero);
                sequence.Resume(100d);
                sequence.Resume(101d);
                Assert.That(sequence.Resolve(true, entry.attack, 100), Is.EqualTo(entry.expected));
            }
        }

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
