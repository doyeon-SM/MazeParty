using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardTopologyTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<BoardTraversalState> _traversals = new List<BoardTraversalState>();

        [TearDown]
        public void TearDown()
        {
            foreach (var traversal in _traversals)
                traversal.Dispose();
            _traversals.Clear();

            for (var index = _objects.Count - 1; index >= 0; index--)
                Object.DestroyImmediate(_objects[index]);
            _objects.Clear();
        }

        [Test]
        public void Gate_CommitsOnlyAfterCapsuleFullyClearsBoundary()
        {
            var source = CreateTile("Source", Vector2Int.zero, BoardTileType.Start, Vector3.zero);
            var destination = CreateTile(
                "Destination",
                Vector2Int.right,
                BoardTileType.Normal,
                Vector3.right * BoardTile.RoomSize);
            var gate = CreateGate(source, destination);
            var controller = CreateController(new Vector3(4.25f, 0f, 0f));
            var traversal = CreateTraversal(source, 2);

            Assert.That(
                gate.TryTraverse(traversal, controller),
                Is.EqualTo(BoardGateTraversalOutcome.PartialCrossing));
            Assert.That(traversal.CurrentTile, Is.SameAs(source));
            Assert.That(traversal.RemainingMoves, Is.EqualTo(2));

            controller.transform.position = new Vector3(4.51f, 0f, 0f);
            Assert.That(
                gate.TryTraverse(traversal, controller),
                Is.EqualTo(BoardGateTraversalOutcome.Committed));
            Assert.That(traversal.CurrentTile, Is.SameAs(destination));
            Assert.That(traversal.RemainingMoves, Is.EqualTo(1));
        }

        [Test]
        public void TraversalHistory_RetreatsInReverseArrivalOrder()
        {
            var start = CreateTile("Start", Vector2Int.zero, BoardTileType.Start, Vector3.zero);
            var middle = CreateTile(
                "Middle",
                Vector2Int.right,
                BoardTileType.Normal,
                Vector3.right * BoardTile.RoomSize);
            var end = CreateTile(
                "End",
                Vector2Int.right * 2,
                BoardTileType.Normal,
                Vector3.right * BoardTile.RoomSize * 2f);
            var firstGate = CreateGate(start, middle);
            var secondGate = CreateGate(middle, end);
            var controller = CreateController(new Vector3(4.51f, 0f, 0f));
            var traversal = CreateTraversal(start, 2);

            Assert.That(firstGate.TryTraverse(traversal, controller), Is.EqualTo(BoardGateTraversalOutcome.Committed));
            controller.transform.position = new Vector3(12.51f, 0f, 0f);
            Assert.That(secondGate.TryTraverse(traversal, controller), Is.EqualTo(BoardGateTraversalOutcome.Committed));

            Assert.That(traversal.Retreat(2), Is.EqualTo(new[] { middle, start }));
            Assert.That(traversal.CurrentTile, Is.SameAs(start));
            Assert.That(start.IsOccupiedBy(traversal), Is.True);
        }

        [Test]
        public void WrongWayAndExhaustedTraversal_AreBlockedAtLastValidTile()
        {
            var source = CreateTile("Source", Vector2Int.zero, BoardTileType.Start, new Vector3(2f, 0f, 3f));
            var destination = CreateTile("Destination", Vector2Int.right, BoardTileType.Normal, new Vector3(10f, 0f, 3f));
            var gate = CreateGate(source, destination);
            var topology = CreateTopology(source, destination, gate);

            var reverse = CreateTraversal(destination, 2);
            var reverseController = CreateController(new Vector3(5f, 0f, 3f));
            Assert.That(gate.TryTraverse(reverse, reverseController), Is.EqualTo(BoardGateTraversalOutcome.NotFromSource));

            var exhausted = CreateTraversal(source, 0);
            var exhaustedController = CreateController(new Vector3(6.75f, 0f, 3f));
            Assert.That(
                topology.ResolveGate(gate, exhausted, exhaustedController, true, 0.25f),
                Is.EqualTo(BoardGateTraversalOutcome.BlockedNoMoves));
            Assert.That(exhausted.CurrentTile, Is.SameAs(source));
            Assert.That(exhaustedController.transform.position, Is.EqualTo(new Vector3(2f, 0.25f, 3f)));
        }

        [Test]
        public void SharedOccupancyAndOutOfBoundsRecovery_PreserveCommittedState()
        {
            var source = CreateTile("Source", Vector2Int.zero, BoardTileType.Start, Vector3.zero);
            var destination = CreateTile(
                "Destination",
                Vector2Int.right,
                BoardTileType.Respawn,
                Vector3.right * BoardTile.RoomSize);
            var gate = CreateGate(source, destination);
            var topology = CreateTopology(source, destination, gate);
            var first = CreateTraversal(source, 1);
            var second = CreateTraversal(source, 3);
            Assert.That(source.OccupancyCount, Is.EqualTo(2));

            var controller = CreateController(new Vector3(4.75f, 0f, 0f));
            Assert.That(gate.TryTraverse(first, controller), Is.EqualTo(BoardGateTraversalOutcome.Committed));
            controller.transform.position = new Vector3(100f, -20f, 100f);
            Assert.That(topology.TryRecoverOutOfBounds(controller, first, 0.5f), Is.True);
            Assert.That(controller.transform.position, Is.EqualTo(new Vector3(8f, 0.5f, 0f)));
            Assert.That(first.CurrentTile, Is.SameAs(destination));
            Assert.That(source.IsOccupiedBy(second), Is.True);
        }

        private BoardTile CreateTile(string name, Vector2Int coordinate, BoardTileType type, Vector3 position)
        {
            var gameObject = CreateObject(name);
            gameObject.transform.position = position;
            var tile = gameObject.AddComponent<BoardTile>();
            tile.Configure(coordinate, type);
            return tile;
        }

        private BoardGate CreateGate(BoardTile source, BoardTile destination)
        {
            var gameObject = CreateObject(source.name + " -> " + destination.name);
            gameObject.transform.position = (source.WorldCenter + destination.WorldCenter) * 0.5f;
            gameObject.transform.rotation = Quaternion.LookRotation(
                (destination.WorldCenter - source.WorldCenter).normalized,
                Vector3.up);
            var gate = gameObject.AddComponent<BoardGate>();
            gate.Configure(source, destination);
            return gate;
        }

        private CharacterController CreateController(Vector3 position)
        {
            var gameObject = CreateObject("Controller");
            gameObject.transform.position = position;
            var controller = gameObject.AddComponent<CharacterController>();
            controller.center = Vector3.up;
            controller.height = 2f;
            controller.radius = 0.5f;
            return controller;
        }

        private BoardTraversalState CreateTraversal(BoardTile start, int remainingMoves)
        {
            var traversal = new BoardTraversalState();
            traversal.Begin(start, remainingMoves);
            _traversals.Add(traversal);
            return traversal;
        }

        private BoardTopology CreateTopology(BoardTile source, BoardTile destination, BoardGate gate)
        {
            var topology = CreateObject("Topology").AddComponent<BoardTopology>();
            topology.Configure(new[] { source, destination }, new[] { gate });
            return topology;
        }

        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name);
            _objects.Add(gameObject);
            return gameObject;
        }
    }
}
