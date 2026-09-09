using System.Collections.Generic;
using MazeParty.Gameplay;
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
            for (var i = _objects.Count - 1; i >= 0; i--)
            {
                if (_objects[i] != null)
                    Object.DestroyImmediate(_objects[i]);
            }
            _objects.Clear();
        }

        [Test]
        public void PositiveMoves_OnlyDirectedCardinalExitsAreBlueAndPassable()
        {
            var destinations = new[]
            {
                Vector2Int.up,
                Vector2Int.right,
                new Vector2Int(1, 1),
                new Vector2Int(0, 2)
            };

            var layout = BoardBoundaryWallPolicy.Evaluate(Vector2Int.zero, destinations, 3);

            Assert.That(layout.HasExit(BoardBoundarySide.North), Is.True);
            Assert.That(layout.HasExit(BoardBoundarySide.East), Is.True);
            Assert.That(layout.IsPassable(BoardBoundarySide.North), Is.True);
            Assert.That(layout.IsPassable(BoardBoundarySide.East), Is.True);
            Assert.That(layout.IsPhysicallyBlocked(BoardBoundarySide.South), Is.True);
            Assert.That(layout.IsPhysicallyBlocked(BoardBoundarySide.West), Is.True);
        }

        [Test]
        public void ZeroMoves_KeepsTopologyKnowledgeButPhysicallyBlocksEverySide()
        {
            var layout = BoardBoundaryWallPolicy.Evaluate(
                new Vector2Int(4, 7),
                new[] { new Vector2Int(5, 7), new Vector2Int(4, 8) },
                0);

            Assert.That(layout.HasExit(BoardBoundarySide.East), Is.True);
            Assert.That(layout.HasExit(BoardBoundarySide.North), Is.True);
            Assert.That(layout.PassableMask, Is.Zero);
            for (var i = 0; i < BoardBoundaryWallPolicy.SideCount; i++)
                Assert.That(layout.IsPhysicallyBlocked((BoardBoundarySide)i), Is.True);
        }

        [Test]
        public void IncomingOnlyConnection_RemainsVisibleAndBlocked()
        {
            var layout = BoardBoundaryWallPolicy.Evaluate(
                Vector2Int.zero,
                new[] { Vector2Int.left },
                new Vector2Int[0],
                3);

            Assert.That(layout.HasExit(BoardBoundarySide.West), Is.True);
            Assert.That(layout.IsPassable(BoardBoundarySide.West), Is.False);
            Assert.That(layout.IsPhysicallyBlocked(BoardBoundarySide.West), Is.True);
            Assert.That(layout.HasExit(BoardBoundarySide.North), Is.False);
        }

        [TestCase(0, 1, BoardBoundarySide.North)]
        [TestCase(1, 0, BoardBoundarySide.East)]
        [TestCase(0, -1, BoardBoundarySide.South)]
        [TestCase(-1, 0, BoardBoundarySide.West)]
        public void CardinalNeighbor_MapsToExpectedSide(int x, int y, BoardBoundarySide expected)
        {
            Assert.That(
                BoardBoundaryWallPolicy.TryGetSide(Vector2Int.zero, new Vector2Int(x, y), out var actual),
                Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Refresh_ReusesFourWallsAndTogglesCollidersWithoutRecreation()
        {
            var source = CreateTile("Source", Vector2Int.zero, Vector3.zero);
            var destination = CreateTile(
                "Destination",
                Vector2Int.right,
                Vector3.right * BoardTile.RoomSize);
            var gate = CreateGate(source, destination);
            var topology = CreateTopology(source, destination, gate);
            var controller = CreateController("P1");
            var walls = controller.gameObject.AddComponent<PlayerBoardBoundaryWalls>();
            walls.Configure(0, controller, topology);

            walls.Refresh(source, 2);
            Assert.That(walls.WallCount, Is.EqualTo(4));
            Assert.That(walls.TryGetWall(BoardBoundarySide.East, out var eastBefore, out var eastCollider), Is.True);
            Assert.That(eastCollider.enabled, Is.False);
            Assert.That(ReadWallColor(eastBefore).b, Is.GreaterThan(ReadWallColor(eastBefore).r));
            Assert.That(walls.TryGetWall(BoardBoundarySide.North, out var northWall, out var northCollider), Is.True);
            Assert.That(northWall.activeSelf, Is.False);
            Assert.That(northCollider.enabled, Is.False);

            walls.Refresh(source, 0);
            Assert.That(walls.TryGetWall(BoardBoundarySide.East, out var eastAfter, out eastCollider), Is.True);
            Assert.That(eastAfter, Is.SameAs(eastBefore));
            Assert.That(eastCollider.enabled, Is.True);
        }

        [Test]
        public void Refresh_DisablesVeilsWhereTheTileHasNoConnectedPath()
        {
            var source = CreateTile("Source", Vector2Int.zero, Vector3.zero);
            var destination = CreateTile(
                "Destination",
                Vector2Int.right,
                Vector3.right * BoardTile.RoomSize);
            var gate = CreateGate(source, destination);
            var topology = CreateTopology(source, destination, gate);
            var controller = CreateController("P1");
            var walls = controller.gameObject.AddComponent<PlayerBoardBoundaryWalls>();
            walls.Configure(0, controller, topology);

            walls.Refresh(source, 0);

            Assert.That(walls.TryGetWall(BoardBoundarySide.East, out var east, out var eastCollider), Is.True);
            Assert.That(east.activeSelf, Is.True);
            Assert.That(eastCollider.enabled, Is.True);
            Assert.That(walls.TryGetWall(BoardBoundarySide.North, out var north, out var northCollider), Is.True);
            Assert.That(north.activeSelf, Is.False);
            Assert.That(northCollider.enabled, Is.False);
        }

        [Test]
        public void Refresh_IncomingOnlyPathKeepsBlackBlockingVeilActive()
        {
            var source = CreateTile("Source", Vector2Int.zero, Vector3.zero);
            var west = CreateTile(
                "West",
                Vector2Int.left,
                Vector3.left * BoardTile.RoomSize);
            var incomingGate = CreateGate(west, source);
            var topology = CreateTopology(source, west, incomingGate);
            var controller = CreateController("P1");
            var walls = controller.gameObject.AddComponent<PlayerBoardBoundaryWalls>();
            walls.Configure(0, controller, topology);

            walls.Refresh(source, 3);

            Assert.That(walls.TryGetWall(
                BoardBoundarySide.West,
                out var westWall,
                out var westCollider), Is.True);
            Assert.That(westWall.activeSelf, Is.True);
            Assert.That(westCollider.enabled, Is.True);
            var color = ReadWallColor(westWall);
            Assert.That(color.r, Is.LessThan(0.05f));
            Assert.That(color.g, Is.LessThan(0.05f));
            Assert.That(color.b, Is.LessThan(0.05f));

            Assert.That(walls.TryGetWall(
                BoardBoundarySide.North,
                out var northWall,
                out var northCollider), Is.True);
            Assert.That(northWall.activeSelf, Is.False);
            Assert.That(northCollider.enabled, Is.False);
        }

        [Test]
        public void SlotSpecificWalls_IgnoreOtherPlayersButStillCollideWithTheirOwner()
        {
            var tile = CreateTile("Tile", Vector2Int.zero, Vector3.zero);
            var topology = CreateTopology(tile);
            var firstController = CreateController("P1");
            var secondController = CreateController("P2");
            var first = firstController.gameObject.AddComponent<PlayerBoardBoundaryWalls>();
            var second = secondController.gameObject.AddComponent<PlayerBoardBoundaryWalls>();
            first.Configure(0, firstController, topology);
            second.Configure(1, secondController, topology);
            first.Refresh(tile, 0);
            second.Refresh(tile, 0);

            Assert.That(first.TryGetWall(BoardBoundarySide.North, out _, out var firstWall), Is.True);
            Assert.That(Physics.GetIgnoreCollision(firstWall, secondController), Is.True);
            Assert.That(Physics.GetIgnoreCollision(firstWall, firstController), Is.False);
            Assert.That(second.TryGetWall(BoardBoundarySide.South, out _, out var secondWall), Is.True);
            Assert.That(Physics.GetIgnoreCollision(secondWall, firstController), Is.True);
            Assert.That(Physics.GetIgnoreCollision(secondWall, secondController), Is.False);
        }

        [Test]
        public void FourPlayerSlots_CreateExactlySixteenReusableWallObjects()
        {
            var tile = CreateTile("Tile", Vector2Int.zero, Vector3.zero);
            var topology = CreateTopology(tile);
            var total = 0;
            for (var slot = 0; slot < PlayerBoardBoundaryWalls.MaxPlayerSlots; slot++)
            {
                var controller = CreateController("P" + (slot + 1));
                var walls = controller.gameObject.AddComponent<PlayerBoardBoundaryWalls>();
                walls.Configure(slot, controller, topology);
                walls.Refresh(tile, 0);
                total += walls.WallCount;
            }

            Assert.That(total, Is.EqualTo(16));
        }

        [Test]
        public void PresentationVisibility_PersistsAcrossRefreshAndHideWithoutChangingCollision()
        {
            var tile = CreateTile("Tile", Vector2Int.zero, Vector3.zero);
            var destination = CreateTile(
                "North",
                Vector2Int.up,
                Vector3.forward * BoardTile.RoomSize);
            var gate = CreateGate(tile, destination);
            var topology = CreateTopology(tile, destination, gate);
            var controller = CreateController("P1");
            var walls = controller.gameObject.AddComponent<PlayerBoardBoundaryWalls>();
            walls.Configure(0, controller, topology);
            walls.Refresh(tile, 0);

            walls.SetPresentationVisible(false);
            Assert.That(walls.PresentationVisible, Is.False);
            Assert.That(walls.TryGetWall(
                BoardBoundarySide.North,
                out var northWall,
                out var northCollider), Is.True);
            Assert.That(northWall.GetComponent<Renderer>().enabled, Is.False);
            Assert.That(northCollider.enabled, Is.True);

            walls.Hide();
            walls.Refresh(tile, 0);
            Assert.That(northWall.GetComponent<Renderer>().enabled, Is.False);
            Assert.That(northCollider.enabled, Is.True);

            walls.SetPresentationVisible(true);
            Assert.That(walls.PresentationVisible, Is.True);
            Assert.That(northWall.GetComponent<Renderer>().enabled, Is.True);
            Assert.That(northCollider.enabled, Is.True);
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
            for (var i = 0; i < members.Length; i++)
            {
                if (members[i] is BoardTile tile)
                    tiles.Add(tile);
                else if (members[i] is BoardGate gate)
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

        private static Color ReadWallColor(GameObject wall)
        {
            var properties = new MaterialPropertyBlock();
            wall.GetComponent<Renderer>().GetPropertyBlock(properties);
            return properties.GetColor("_BaseColor");
        }

        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name);
            _objects.Add(gameObject);
            return gameObject;
        }
    }
}
