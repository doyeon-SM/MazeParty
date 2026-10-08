using System.Collections.Generic;
using System.Linq;
using MazeParty.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardTravelRoutePreviewTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (var index = _objects.Count - 1; index >= 0; index--)
                Object.DestroyImmediate(_objects[index]);
            _objects.Clear();
        }

        [Test]
        public void Preview_StopsEveryRouteAtTheCommittedDiceResult()
        {
            var start = CreateTile("Start", 0, 0);
            var primary = CreateTile("Primary", 1, 0);
            var primaryEnd = CreateTile("Primary End", 2, 0);
            var primaryBeyond = CreateTile("Primary Beyond", 3, 0);
            var branch = CreateTile("Branch", 0, 1);
            var branchEnd = CreateTile("Branch End", 0, 2);
            var branchBeyond = CreateTile("Branch Beyond", 0, 3);
            var topology = CreateTopology(
                new[]
                {
                    start,
                    primary,
                    primaryEnd,
                    primaryBeyond,
                    branch,
                    branchEnd,
                    branchBeyond
                },
                CreateGate(start, primary),
                CreateGate(start, branch),
                CreateGate(primary, primaryEnd),
                CreateGate(primaryEnd, primaryBeyond),
                CreateGate(primaryBeyond, start),
                CreateGate(branch, branchEnd),
                CreateGate(branchEnd, branchBeyond),
                CreateGate(branchBeyond, start));

            var preview = new BoardTravelRoutePreview(topology, start, 2);
            var steps = preview.GetVisibleSteps();

            Assert.That(preview.RouteCount, Is.EqualTo(2));
            Assert.That(preview.GetCompatibleRouteCount(null), Is.EqualTo(2));
            AssertStep(steps, start.Coordinate, 0, false);
            AssertStep(steps, primary.Coordinate, 1, false);
            AssertStep(steps, primaryEnd.Coordinate, 2, false);
            AssertStep(steps, branch.Coordinate, 1, true);
            AssertStep(steps, branchEnd.Coordinate, 2, true);
            Assert.That(steps.Any(step =>
                step.Coordinate == primaryBeyond.Coordinate), Is.False);
            Assert.That(steps.Any(step =>
                step.Coordinate == branchBeyond.Coordinate), Is.False);
            for (var index = 1; index < steps.Count; index++)
            {
                Assert.That(
                    CompareSteps(steps[index - 1], steps[index]),
                    Is.LessThanOrEqualTo(0));
            }
        }

        [Test]
        public void Choices_FilterCandidatesInSequenceAndKeepBranchState()
        {
            var start = CreateTile("Start", 0, 0);
            var first = CreateTile("First", 1, 0);
            var other = CreateTile("Other", 0, 1);
            var straight = CreateTile("Straight", 2, 0);
            var detour = CreateTile("Detour", 1, 1);
            var topology = CreateTopology(
                new[] { start, first, other, straight, detour },
                CreateGate(start, first),
                CreateGate(start, other),
                CreateGate(first, straight),
                CreateGate(first, detour),
                CreateGate(straight, start),
                CreateGate(detour, start),
                CreateGate(other, start));
            var preview = new BoardTravelRoutePreview(topology, start, 2);
            var afterFirstChoice = new[]
            {
                new BoardRouteChoice(start.Coordinate, first.Coordinate)
            };
            var afterSecondChoice = new[]
            {
                afterFirstChoice[0],
                new BoardRouteChoice(first.Coordinate, detour.Coordinate)
            };

            Assert.That(preview.RouteCount, Is.EqualTo(3));
            Assert.That(
                preview.GetCompatibleRouteCount(afterFirstChoice),
                Is.EqualTo(2));
            Assert.That(
                preview.GetCompatibleRouteCount(afterSecondChoice),
                Is.EqualTo(1));

            var steps = preview.GetVisibleSteps(afterSecondChoice);
            Assert.That(steps.Count, Is.EqualTo(3));
            AssertStep(steps, start.Coordinate, 0, false);
            AssertStep(steps, first.Coordinate, 1, false);
            AssertStep(steps, detour.Coordinate, 2, true);
            Assert.That(steps.Any(step => step.Coordinate == straight.Coordinate), Is.False);
            Assert.That(steps.Any(step => step.Coordinate == other.Coordinate), Is.False);
        }

        [Test]
        public void CyclesAndRepeatedForkChoices_AreKeptInTraversalOrder()
        {
            var start = CreateTile("Start", 0, 0);
            var primary = CreateTile("Primary", 1, 0);
            var branch = CreateTile("Branch", 0, 1);
            var topology = CreateTopology(
                new[] { start, primary, branch },
                CreateGate(start, primary),
                CreateGate(start, branch),
                CreateGate(primary, start),
                CreateGate(branch, start));
            var preview = new BoardTravelRoutePreview(topology, start, 4);
            var choices = new[]
            {
                new BoardRouteChoice(start.Coordinate, branch.Coordinate),
                new BoardRouteChoice(start.Coordinate, primary.Coordinate)
            };

            Assert.That(preview.RouteCount, Is.EqualTo(4));
            Assert.That(preview.GetCompatibleRouteCount(choices), Is.EqualTo(1));
            var steps = preview.GetVisibleSteps(choices);
            AssertStep(steps, start.Coordinate, 0, false);
            AssertStep(steps, branch.Coordinate, 1, true);
            AssertStep(steps, start.Coordinate, 2, true);
            AssertStep(steps, primary.Coordinate, 3, true);
            AssertStep(steps, start.Coordinate, 4, true);
        }

        [Test]
        public void RelocatedPreview_PreservesConsumedStepAndOnlyShowsRemainingRoll()
        {
            var relocated = CreateTile("Relocated", 0, 0);
            var next = CreateTile("Next", 1, 0);
            var final = CreateTile("Final", 2, 0);
            var beyond = CreateTile("Beyond", 3, 0);
            var topology = CreateTopology(
                new[] { relocated, next, final, beyond },
                CreateGate(relocated, next),
                CreateGate(next, final),
                CreateGate(final, beyond));

            var startingStep =
                BoardTravelRouteStatePolicy.GetStartingStepAfterRelocation(
                    5,
                    2);
            var preview = new BoardTravelRoutePreview(
                topology,
                relocated,
                startingStep,
                5);
            var steps = preview.GetVisibleSteps();

            Assert.That(preview.StartingStep, Is.EqualTo(3));
            AssertStep(steps, relocated.Coordinate, 3, false);
            AssertStep(steps, next.Coordinate, 4, false);
            AssertStep(steps, final.Coordinate, 5, false);
            Assert.That(steps.Any(step => step.Step < 3), Is.False);
            Assert.That(steps.Any(step => step.Coordinate == beyond.Coordinate),
                Is.False);
        }

        [Test]
        public void TurnVisibility_RequiresActionPhaseResultAndValidOrigin()
        {
            Assert.That(BoardTravelRoutePreview.ShouldDisplayForTurn(
                true, 5, true), Is.True);
            Assert.That(BoardTravelRoutePreview.ShouldDisplayForTurn(
                true, 0, true), Is.False,
                "No route numbers are shown before the dice result.");
            Assert.That(BoardTravelRoutePreview.ShouldDisplayForTurn(
                false, 5, true), Is.False,
                "No route numbers remain after the action turn.");
            Assert.That(BoardTravelRoutePreview.ShouldDisplayForTurn(
                true, 5, false), Is.False,
                "A missing authoritative roll origin must fail closed.");
        }

        [Test]
        public void ReconnectPolicy_PreservesOffsetAndOrderedRepeatedForkChoices()
        {
            var start = CreateTile("Start", 0, 0);
            var primary = CreateTile("Primary", 1, 0);
            var branch = CreateTile("Branch", 0, 1);
            var topology = CreateTopology(
                new[] { start, primary, branch },
                CreateGate(start, primary),
                CreateGate(start, branch),
                CreateGate(primary, start),
                CreateGate(branch, start));
            var choices = new[]
            {
                new BoardRouteChoice(start.Coordinate, primary.Coordinate),
                new BoardRouteChoice(start.Coordinate, branch.Coordinate)
            };

            var restored = BoardTravelRouteStatePolicy.Restore(
                topology,
                6,
                4,
                true,
                start.Coordinate,
                2,
                choices);

            Assert.That(restored.Active, Is.True);
            Assert.That(restored.Origin, Is.EqualTo(start.Coordinate));
            Assert.That(restored.StartingStep, Is.EqualTo(2));
            Assert.That(restored.Choices, Is.EqualTo(choices));
        }

        [Test]
        public void ReconnectPolicy_InvalidStateFailsClosed()
        {
            var start = CreateTile("Start", 0, 0);
            var primary = CreateTile("Primary", 1, 0);
            var branch = CreateTile("Branch", 0, 1);
            var topology = CreateTopology(
                new[] { start, primary, branch },
                CreateGate(start, primary),
                CreateGate(start, branch));
            var incompatibleChoices = new[]
            {
                new BoardRouteChoice(start.Coordinate, primary.Coordinate),
                new BoardRouteChoice(start.Coordinate, branch.Coordinate)
            };
            var invalidStates = new[]
            {
                BoardTravelRouteStatePolicy.Restore(
                    topology, 0, 0, true, start.Coordinate, 0, null),
                BoardTravelRouteStatePolicy.Restore(
                    topology, 6, 6, false, start.Coordinate, 0, null),
                BoardTravelRouteStatePolicy.Restore(
                    topology,
                    6,
                    6,
                    true,
                    new Vector2Int(99, 99),
                    0,
                    null),
                BoardTravelRouteStatePolicy.Restore(
                    topology,
                    1,
                    1,
                    true,
                    start.Coordinate,
                    0,
                    incompatibleChoices),
                BoardTravelRouteStatePolicy.Restore(
                    topology,
                    6,
                    5,
                    true,
                    start.Coordinate,
                    4,
                    null),
                BoardTravelRouteStatePolicy.Restore(
                    topology,
                    6,
                    6,
                    true,
                    start.Coordinate,
                    0,
                    new[]
                    {
                        new BoardRouteChoice(
                            start.Coordinate,
                            new Vector2Int(99, 99))
                    })
            };

            foreach (var restored in invalidStates)
            {
                Assert.That(restored.Active, Is.False);
                Assert.That(restored.StartingStep, Is.Zero);
                Assert.That(restored.Choices, Is.Empty);
            }
        }

        private static void AssertStep(
            IReadOnlyList<BoardTravelRouteStep> steps,
            Vector2Int coordinate,
            int step,
            bool isBranch)
        {
            Assert.That(
                steps.Any(candidate =>
                    candidate.Coordinate == coordinate &&
                    candidate.Step == step &&
                    candidate.IsBranch == isBranch),
                Is.True,
                $"Expected ({coordinate.x}, {coordinate.y}) step {step}, branch={isBranch}.");
        }

        private static int CompareSteps(
            BoardTravelRouteStep left,
            BoardTravelRouteStep right)
        {
            var x = left.Coordinate.x.CompareTo(right.Coordinate.x);
            if (x != 0)
                return x;
            var y = left.Coordinate.y.CompareTo(right.Coordinate.y);
            return y != 0 ? y : left.Step.CompareTo(right.Step);
        }

        private BoardTopology CreateTopology(
            BoardTile[] tiles,
            params BoardGate[] gates)
        {
            var topology = CreateObject("Topology").AddComponent<BoardTopology>();
            topology.Configure(tiles, gates);
            return topology;
        }

        private BoardTile CreateTile(string name, int x, int y)
        {
            var tile = CreateObject(name).AddComponent<BoardTile>();
            tile.Configure(new Vector2Int(x, y), BoardTileType.Normal);
            return tile;
        }

        private BoardGate CreateGate(BoardTile source, BoardTile destination)
        {
            var gate = CreateObject(source.name + " -> " + destination.name)
                .AddComponent<BoardGate>();
            gate.Configure(source, destination);
            return gate;
        }

        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name);
            _objects.Add(gameObject);
            return gameObject;
        }
    }
}
