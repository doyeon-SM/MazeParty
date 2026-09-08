using System;
using System.Collections.Generic;
using System.Linq;
using MazeParty.Gameplay;
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
            for (var i = 0; i < _traversals.Count; i++)
                _traversals[i].Dispose();
            _traversals.Clear();

            for (var i = _objects.Count - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        [Test]
        public void Tile_UsesEightByEightLogicalRoom_AndDefinesRequiredTypes()
        {
            var tile = CreateTile("Tile", Vector2Int.zero, BoardTileType.Normal, Vector3.zero);

            Assert.That(BoardTile.RoomSize, Is.EqualTo(8f));
            Assert.That(tile.ContainsHorizontalPoint(new Vector3(3.999f, 0f, 3.999f)), Is.True);
            Assert.That(tile.ContainsHorizontalPoint(new Vector3(4.01f, 0f, 0f)), Is.False);
            Assert.That(
                Enum.GetValues(typeof(BoardTileType)),
                Is.EquivalentTo(new[]
                {
                    BoardTileType.Normal,
                    BoardTileType.Start,
                    BoardTileType.KeyShop,
                    BoardTileType.Respawn
                }));
        }

        [Test]
        public void Gate_PartialCapsuleOverlap_RemainsOnPreviousTile()
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

            var outcome = gate.TryTraverse(traversal, controller);

            Assert.That(gate.GetCapsuleRelation(controller), Is.EqualTo(BoardGateCapsuleRelation.Straddling));
            Assert.That(outcome, Is.EqualTo(BoardGateTraversalOutcome.PartialCrossing));
            Assert.That(traversal.CurrentTile, Is.SameAs(source));
            Assert.That(traversal.LastValidTile, Is.SameAs(source));
            Assert.That(traversal.RemainingMoves, Is.EqualTo(2));
        }

        [Test]
        public void Gate_FullyClearedByCapsule_CommitsAndConsumesExactlyOneMove()
        {
            var source = CreateTile("Source", Vector2Int.zero, BoardTileType.Start, Vector3.zero);
            var destination = CreateTile(
                "Destination",
                Vector2Int.right,
                BoardTileType.KeyShop,
                Vector3.right * BoardTile.RoomSize);
            var gate = CreateGate(source, destination);
            var controller = CreateController(new Vector3(4.51f, 0f, 0f));
            var traversal = CreateTraversal(source, 2);

            var outcome = gate.TryTraverse(traversal, controller);

            Assert.That(outcome, Is.EqualTo(BoardGateTraversalOutcome.Committed));
            Assert.That(traversal.CurrentTile, Is.SameAs(destination));
            Assert.That(traversal.LastValidTile, Is.SameAs(destination));
            Assert.That(traversal.RemainingMoves, Is.EqualTo(1));
            Assert.That(source.OccupancyCount, Is.Zero);
            Assert.That(destination.IsOccupiedBy(traversal), Is.True);
        }

        [Test]
        public void Gate_CrossingCommit_DoesNotDependOnWhetherMovementWasDirectOrPush()
        {
            var source = CreateTile("Source", Vector2Int.zero, BoardTileType.Start, Vector3.zero);
            var destination = CreateTile(
                "Destination",
                Vector2Int.right,
                BoardTileType.Normal,
                Vector3.right * BoardTile.RoomSize);
            var gate = CreateGate(source, destination);
            var controller = CreateController(Vector3.zero);
            var traversal = CreateTraversal(source, 1);

            controller.transform.position = new Vector3(4.75f, 0f, 0f);
            var outcome = gate.TryTraverse(traversal, controller);

            Assert.That(outcome, Is.EqualTo(BoardGateTraversalOutcome.Committed));
            Assert.That(traversal.CurrentTile, Is.SameAs(destination));
            Assert.That(traversal.RemainingMoves, Is.Zero);
        }

        [Test]
        public void Gate_IsOneWay_AndRejectsTraversalFromDestination()
        {
            var source = CreateTile("Source", Vector2Int.zero, BoardTileType.Start, Vector3.zero);
            var destination = CreateTile(
                "Destination",
                Vector2Int.right,
                BoardTileType.Normal,
                Vector3.right * BoardTile.RoomSize);
            var gate = CreateGate(source, destination);
            var controller = CreateController(new Vector3(3f, 0f, 0f));
            var traversal = CreateTraversal(destination, 2);

            var outcome = gate.TryTraverse(traversal, controller);

            Assert.That(outcome, Is.EqualTo(BoardGateTraversalOutcome.NotFromSource));
            Assert.That(traversal.CurrentTile, Is.SameAs(destination));
            Assert.That(traversal.RemainingMoves, Is.EqualTo(2));
        }

        [Test]
        public void ZeroMoves_BlocksNextGate_AndCanRecoverToLastValidCenter()
        {
            var source = CreateTile(
                "Source",
                Vector2Int.zero,
                BoardTileType.Respawn,
                new Vector3(2f, 0f, 3f));
            var destination = CreateTile(
                "Destination",
                Vector2Int.right,
                BoardTileType.Normal,
                new Vector3(10f, 0f, 3f));
            var gate = CreateGate(source, destination);
            var topology = CreateTopology(source, destination, gate);
            var controller = CreateController(new Vector3(6.75f, 0f, 3f));
            var traversal = CreateTraversal(source, 0);

            var outcome = topology.ResolveGate(
                gate,
                traversal,
                controller,
                true,
                0.25f);

            Assert.That(outcome, Is.EqualTo(BoardGateTraversalOutcome.BlockedNoMoves));
            Assert.That(traversal.CurrentTile, Is.SameAs(source));
            Assert.That(traversal.RemainingMoves, Is.Zero);
            Assert.That(controller.transform.position, Is.EqualTo(new Vector3(2f, 0.25f, 3f)));
        }

        [Test]
        public void Tile_AllowsMultipleOccupantsWithoutCapacityRejection()
        {
            var tile = CreateTile("Shared", Vector2Int.zero, BoardTileType.Start, Vector3.zero);
            var first = CreateTraversal(tile, 1);
            var second = CreateTraversal(tile, 3);

            Assert.That(tile.OccupancyCount, Is.EqualTo(2));
            Assert.That(tile.IsOccupiedBy(first), Is.True);
            Assert.That(tile.IsOccupiedBy(second), Is.True);
        }

        [Test]
        public void OutOfBounds_RecoversToCenterOfLastCommittedTile()
        {
            var source = CreateTile("Source", Vector2Int.zero, BoardTileType.Start, Vector3.zero);
            var destination = CreateTile(
                "Destination",
                Vector2Int.right,
                BoardTileType.Respawn,
                Vector3.right * BoardTile.RoomSize);
            var gate = CreateGate(source, destination);
            var topology = CreateTopology(source, destination, gate);
            var controller = CreateController(new Vector3(4.75f, 0f, 0f));
            var traversal = CreateTraversal(source, 1);
            Assert.That(gate.TryTraverse(traversal, controller), Is.EqualTo(BoardGateTraversalOutcome.Committed));

            controller.transform.position = new Vector3(100f, -20f, 100f);
            var recovered = topology.TryRecoverOutOfBounds(controller, traversal, 0.5f);

            Assert.That(recovered, Is.True);
            Assert.That(controller.transform.position, Is.EqualTo(new Vector3(8f, 0.5f, 0f)));
            Assert.That(traversal.CurrentTile, Is.SameAs(destination));
        }

        [Test]
        public void ValidTopology_IndexesDirectedExitAndPassesValidation()
        {
            var source = CreateTile("Start", Vector2Int.zero, BoardTileType.Start, Vector3.zero);
            var destination = CreateTile(
                "Shop",
                Vector2Int.right,
                BoardTileType.KeyShop,
                Vector3.right * BoardTile.RoomSize);
            var gate = CreateGate(source, destination);
            var topology = CreateTopology(source, destination, gate);

            var validation = topology.ValidateTopology();

            Assert.That(validation.IsValid, Is.True);
            Assert.That(topology.TryGetTile(Vector2Int.right, out var found), Is.True);
            Assert.That(found, Is.SameAs(destination));
            Assert.That(topology.GetOutgoingGates(source), Has.Count.EqualTo(1));
            Assert.That(topology.GetOutgoingGates(destination), Is.Empty);
        }

        [Test]
        public void Validator_ReportsDuplicateCoordinatesAndMisdirectedGate()
        {
            var source = CreateTile("Start", Vector2Int.zero, BoardTileType.Start, Vector3.zero);
            var duplicate = CreateTile("Duplicate", Vector2Int.zero, BoardTileType.Normal, Vector3.forward * 8f);
            var destination = CreateTile(
                "Destination",
                Vector2Int.right,
                BoardTileType.Normal,
                Vector3.right * BoardTile.RoomSize);
            var gate = CreateGate(source, destination);
            gate.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);

            var result = BoardTopologyValidator.Validate(
                new[] { source, duplicate, destination },
                new[] { gate });

            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.Issues.Select(issue => issue.Code),
                Does.Contain(BoardTopologyIssueCode.DuplicateCoordinate));
            Assert.That(
                result.Issues.Select(issue => issue.Code),
                Does.Contain(BoardTopologyIssueCode.GateDirectionMismatch));
        }

        private BoardTile CreateTile(
            string name,
            Vector2Int coordinate,
            BoardTileType type,
            Vector3 position)
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
            var direction = destination.WorldCenter - source.WorldCenter;
            gameObject.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
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

        private BoardTopology CreateTopology(
            BoardTile source,
            BoardTile destination,
            BoardGate gate)
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
