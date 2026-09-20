using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardBoundaryWallPolicyTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (var index = _objects.Count - 1; index >= 0; index--)
            {
                if (_objects[index] != null)
                    Object.DestroyImmediate(_objects[index]);
            }

            _objects.Clear();
        }

        [Test]
        public void DirectedExitsAndRemainingMoves_ControlOwnerSpecificBoundaries()
        {
            var source = CreateTile("Source", Vector2Int.zero, Vector3.zero);
            var east = CreateTile(
                "East",
                Vector2Int.right,
                Vector3.right * BoardTile.RoomSize);
            var west = CreateTile(
                "West",
                Vector2Int.left,
                Vector3.left * BoardTile.RoomSize);
            var outgoing = CreateGate(source, east);
            var incoming = CreateGate(west, source);
            var topology = CreateTopology(source, east, west, outgoing, incoming);

            var open = BoardBoundaryWallPolicy.Evaluate(
                source.Coordinate,
                new[] { east.Coordinate, west.Coordinate },
                new[] { east.Coordinate },
                2);
            Assert.That(open.IsPassable(BoardBoundarySide.East), Is.True);
            Assert.That(open.HasExit(BoardBoundarySide.West), Is.True);
            Assert.That(open.IsPhysicallyBlocked(BoardBoundarySide.West), Is.True);
            Assert.That(open.HasExit(BoardBoundarySide.North), Is.False);

            var exhausted = BoardBoundaryWallPolicy.Evaluate(
                source.Coordinate,
                new[] { east.Coordinate, west.Coordinate },
                new[] { east.Coordinate },
                0);
            Assert.That(exhausted.PassableMask, Is.Zero);

            var firstController = CreateController("P1");
            var secondController = CreateController("P2");
            var first = firstController.gameObject.AddComponent<PlayerBoardBoundaryWalls>();
            var second = secondController.gameObject.AddComponent<PlayerBoardBoundaryWalls>();
            first.Configure(0, firstController, topology);
            second.Configure(1, secondController, topology);
            first.Refresh(source, 2);
            second.Refresh(source, 0);

            Assert.That(
                first.TryGetWall(BoardBoundarySide.East, out _, out var eastWall),
                Is.True);
            Assert.That(eastWall.enabled, Is.False);
            Assert.That(
                first.TryGetWall(BoardBoundarySide.West, out _, out var westWall),
                Is.True);
            Assert.That(westWall.enabled, Is.True);
            Assert.That(Physics.GetIgnoreCollision(westWall, secondController), Is.True);
            Assert.That(Physics.GetIgnoreCollision(westWall, firstController), Is.False);
        }

        private BoardTile CreateTile(string name, Vector2Int coordinate, Vector3 position)
        {
            var gameObject = CreateObject(name);
            gameObject.transform.position = position;
            var tile = gameObject.AddComponent<BoardTile>();
            tile.Configure(coordinate, BoardTileType.Normal);
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

        private BoardTopology CreateTopology(params Object[] members)
        {
            var tiles = new List<BoardTile>();
            var gates = new List<BoardGate>();
            foreach (var member in members)
            {
                if (member is BoardTile tile)
                    tiles.Add(tile);
                else if (member is BoardGate gate)
                    gates.Add(gate);
            }

            var topology = CreateObject("Topology").AddComponent<BoardTopology>();
            topology.Configure(tiles.ToArray(), gates.ToArray());
            return topology;
        }

        private CharacterController CreateController(string name)
        {
            var controller = CreateObject(name).AddComponent<CharacterController>();
            controller.center = Vector3.up;
            controller.height = 2f;
            controller.radius = 0.5f;
            return controller;
        }

        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name);
            _objects.Add(gameObject);
            return gameObject;
        }
    }
}
