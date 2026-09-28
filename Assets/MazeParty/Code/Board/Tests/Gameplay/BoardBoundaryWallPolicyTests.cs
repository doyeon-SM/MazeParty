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

            Assert.That(first.ActivePortalCount, Is.EqualTo(2));
            Assert.That(first.WallCount, Is.EqualTo(PlayerBoardBoundaryWalls.WallsPerSlot));

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
            first.SetPresentationVisible(false);
            Assert.That(westWall.enabled, Is.True, "Hiding a remote wall must keep its owner collision active.");
            Assert.That(westWall.GetComponentsInChildren<Renderer>(true),
                Is.All.Matches<Renderer>(renderer => !renderer.enabled));
            first.Refresh(source, 0);
            Assert.That(eastWall.enabled, Is.True);
            Assert.That(Physics.GetIgnoreCollision(eastWall, secondController), Is.True);
            first.Hide();
            Assert.That(westWall.gameObject.activeInHierarchy, Is.False);
        }

        [Test]
        public void PortalPolicy_MergesReciprocalFreeformGatesAndPreservesDirectionality()
        {
            var source = CreateTile("Source", Vector2Int.zero, Vector3.zero);
            var destination = CreateTile(
                "Destination",
                new Vector2Int(20, -7),
                new Vector3(6f, 0f, 4f));
            var direction = (destination.WorldCenter - source.WorldCenter).normalized;
            var planePoint = new Vector3(2.5f, 0f, 1.25f);
            var outgoing = CreateGateAt(
                source,
                destination,
                planePoint,
                direction,
                2.5f);
            var reciprocal = CreateGateAt(
                destination,
                source,
                planePoint,
                -direction,
                3.25f);
            var topology = CreateTopology(
                source,
                destination,
                outgoing,
                reciprocal);
            var portals = new List<BoardBoundaryPortal>();

            BoardBoundaryWallPolicy.EvaluatePortals(
                topology,
                source,
                2,
                portals);
            Assert.That(portals, Has.Count.EqualTo(1));
            Assert.That(portals[0].ConnectedTile, Is.SameAs(destination));
            Assert.That(portals[0].HasOutgoingGate, Is.True);
            Assert.That(portals[0].IsPassable, Is.True);
            Assert.That(portals[0].Width, Is.EqualTo(3.25f).Within(0.0001f));

            BoardBoundaryWallPolicy.EvaluatePortals(
                topology,
                source,
                0,
                portals);
            Assert.That(portals, Has.Count.EqualTo(1));
            Assert.That(portals[0].IsPhysicallyBlocked, Is.True);

            topology.Configure(
                new[] { source, destination },
                new[] { reciprocal });
            BoardBoundaryWallPolicy.EvaluatePortals(
                topology,
                source,
                3,
                portals);
            Assert.That(portals, Has.Count.EqualTo(1));
            Assert.That(portals[0].HasOutgoingGate, Is.False);
            Assert.That(portals[0].IsPhysicallyBlocked, Is.True);
        }

        [Test]
        public void PortalWallPool_GrowsPastFourAndUsesGatePlanes()
        {
            var source = CreateTile("Source", Vector2Int.zero, Vector3.zero);
            var members = new List<Object> { source };
            BoardGate firstOutgoing = null;
            BoardTile firstDestination = null;
            for (var index = 0; index < 5; index++)
            {
                var angle = index * Mathf.PI * 2f / 5f;
                var direction = new Vector3(
                    Mathf.Cos(angle),
                    0f,
                    Mathf.Sin(angle));
                var destination = CreateTile(
                    "Destination " + index,
                    new Vector2Int(10 + index, -10 - index),
                    direction * BoardTile.RoomSize);
                var gate = CreateGateAt(
                    source,
                    destination,
                    direction * BoardTile.HalfRoomSize,
                    direction,
                    2f + index * 0.2f);
                members.Add(destination);
                members.Add(gate);
                if (index == 0)
                {
                    firstOutgoing = gate;
                    firstDestination = destination;
                }
            }

            members.Add(CreateGateAt(
                firstDestination,
                source,
                firstOutgoing.PlanePoint,
                -firstOutgoing.ForwardNormal,
                firstOutgoing.GateWidth));
            var topology = CreateTopology(members.ToArray());
            var controller = CreateController("P1");
            var boundaries =
                controller.gameObject.AddComponent<PlayerBoardBoundaryWalls>();
            boundaries.Configure(0, controller, topology);
            boundaries.Refresh(source, 2);

            Assert.That(boundaries.ActivePortalCount, Is.EqualTo(5));
            Assert.That(boundaries.WallCount, Is.GreaterThanOrEqualTo(5));
            for (var index = 0; index < boundaries.ActivePortalCount; index++)
            {
                Assert.That(
                    boundaries.TryGetPortalWall(
                        index,
                        out var wall,
                        out var wallCollider),
                    Is.True);
                Assert.That(wall.activeInHierarchy, Is.True);
                Assert.That(wallCollider.enabled, Is.False);
                Assert.That(
                    Vector3.Dot(
                        wall.transform.forward,
                        boundaries.CurrentPortals[index].OutwardNormal),
                    Is.GreaterThan(0.999f));
            }

            boundaries.Refresh(source, 0);
            for (var index = 0; index < boundaries.ActivePortalCount; index++)
            {
                Assert.That(
                    boundaries.TryGetPortalWall(index, out _, out var wallCollider),
                    Is.True);
                Assert.That(wallCollider.enabled, Is.True);
            }
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
            return CreateGateAt(
                source,
                destination,
                (source.WorldCenter + destination.WorldCenter) * 0.5f,
                (destination.WorldCenter - source.WorldCenter).normalized,
                2.5f);
        }

        private BoardGate CreateGateAt(
            BoardTile source,
            BoardTile destination,
            Vector3 planePoint,
            Vector3 forward,
            float width)
        {
            var gameObject = CreateObject(source.name + " -> " + destination.name);
            gameObject.transform.position = planePoint;
            gameObject.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            var gate = gameObject.AddComponent<BoardGate>();
            gate.Configure(source, destination, width);
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
