using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardTileFootprintTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (var i = _objects.Count - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(_objects[i]);
            }

            _objects.Clear();
        }

        [Test]
        public void LegacyTile_PreservesSquareContainmentClampAndRecovery()
        {
            var tile = CreateTile(
                "Legacy",
                Vector2Int.zero,
                BoardTileType.Start,
                new Vector3(3f, 2f, 5f));
            tile.transform.rotation = Quaternion.Euler(0f, 37f, 0f);
            var right = tile.transform.right.normalized;
            var forward = tile.transform.forward.normalized;
            var up = tile.transform.up.normalized;

            Assert.That(tile.HasCustomFootprint, Is.False);
            Assert.That(
                tile.ContainsHorizontalPoint(
                    tile.WorldCenter + right * BoardTile.HalfRoomSize -
                    forward * BoardTile.HalfRoomSize),
                Is.True);
            Assert.That(
                tile.ContainsHorizontalPoint(
                    tile.WorldCenter + right * (BoardTile.HalfRoomSize + 0.01f)),
                Is.False);

            var outside = tile.WorldCenter + right * 10f - forward * 9f + up * 1.25f;
            var clamped = tile.GetClosestPointInside(outside, 0.5f);
            var offset = clamped - tile.WorldCenter;
            Assert.That(Vector3.Dot(offset, right), Is.EqualTo(3.5f).Within(0.0001f));
            Assert.That(Vector3.Dot(offset, forward), Is.EqualTo(-3.5f).Within(0.0001f));
            Assert.That(Vector3.Dot(offset, up), Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(
                Vector3.Distance(
                    tile.GetRecoveryCenter(0.75f),
                    tile.WorldCenter + up * 0.75f),
                Is.LessThan(0.0001f));
        }

        [Test]
        public void AuthoredTriangle_ContainsClampsAndRecoversWithinRotatedPolygon()
        {
            var tile = CreateTile(
                "Triangle",
                Vector2Int.zero,
                BoardTileType.Start,
                new Vector3(10f, 1f, -3f));
            tile.transform.rotation = Quaternion.Euler(0f, 31f, 0f);
            var footprint = tile.gameObject.AddComponent<BoardTileFootprint>();
            footprint.Configure(new[]
            {
                new Vector2(1f, -1f),
                new Vector2(4f, -1f),
                new Vector2(1f, 2f)
            });

            var inside = tile.transform.TransformPoint(new Vector3(1.5f, 0f, 0f));
            var outsideOnSurface =
                tile.transform.TransformPoint(new Vector3(3.5f, 0f, 1.5f));
            var outside = tile.transform.TransformPoint(new Vector3(3.5f, 1.25f, 1.5f));
            Assert.That(tile.HasCustomFootprint, Is.True);
            Assert.That(tile.ContainsHorizontalPoint(inside), Is.True);
            Assert.That(tile.ContainsHorizontalPoint(outsideOnSurface), Is.False);

            var topology = CreateObject("Triangle Topology").AddComponent<BoardTopology>();
            topology.Configure(new[] { tile }, System.Array.Empty<BoardGate>());
            Assert.That(
                BoardItemLifecycleRules.TryGetMinePlacementPosition(
                    topology,
                    inside,
                    tile.transform.up,
                    out _),
                Is.True);
            Assert.That(
                BoardItemLifecycleRules.TryGetMinePlacementPosition(
                    topology,
                    outsideOnSurface,
                    tile.transform.up,
                    out _),
                Is.False,
                "Mine placement follows the authored footprint instead of the legacy square.");

            var clamped = tile.GetClosestPointInside(outside, 0.2f);
            Assert.That(tile.ContainsHorizontalPoint(clamped), Is.True);
            Assert.That(
                Vector3.Dot(clamped - tile.transform.position, tile.transform.up.normalized),
                Is.EqualTo(1.25f).Within(0.0001f));

            var recovery = tile.GetRecoveryCenter(0.75f);
            Assert.That(tile.ContainsHorizontalPoint(recovery), Is.True);
            Assert.That(
                Vector3.Dot(recovery - tile.transform.position, tile.transform.up.normalized),
                Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(
                Vector3.Distance(
                    recovery,
                    tile.transform.TransformPoint(new Vector3(2f, 0.75f, 0f))),
                Is.LessThan(0.0001f),
                "Recovery uses the authored polygon centroid instead of the transform origin.");
        }

        [Test]
        public void FootprintValidation_AcceptsStrictConvexThreeToFiveSidesOnly()
        {
            var footprint = CreateTile(
                    "Shapes",
                    Vector2Int.zero,
                    BoardTileType.Start,
                    Vector3.zero)
                .gameObject.AddComponent<BoardTileFootprint>();
            var validShapes = new[]
            {
                new[]
                {
                    new Vector2(0f, 2f),
                    new Vector2(-2f, -1f),
                    new Vector2(2f, -1f)
                },
                new[]
                {
                    new Vector2(-2f, -2f),
                    new Vector2(2f, -2f),
                    new Vector2(2f, 2f),
                    new Vector2(-2f, 2f)
                },
                CreateRegularPolygon(5, 2f)
            };

            foreach (var shape in validShapes)
            {
                footprint.Configure(shape);
                Assert.That(
                    footprint.TryValidate(out var message),
                    Is.True,
                    message);
            }

            var invalidShapes = new[]
            {
                CreateRegularPolygon(6, 2f),
                new[]
                {
                    new Vector2(-2f, -2f),
                    new Vector2(2f, -2f),
                    Vector2.zero,
                    new Vector2(2f, 2f),
                    new Vector2(-2f, 2f)
                },
                new[]
                {
                    new Vector2(-2f, -2f),
                    new Vector2(2f, 2f),
                    new Vector2(-2f, 2f),
                    new Vector2(2f, -2f)
                }
            };

            foreach (var shape in invalidShapes)
            {
                footprint.Configure(shape);
                Assert.That(footprint.TryValidate(out _), Is.False);
            }
        }

        [Test]
        public void InsetCapacity_DetectsWhenARequiredDiscCannotFit()
        {
            var tile = CreateTile(
                "Small Footprint",
                Vector2Int.zero,
                BoardTileType.Normal,
                Vector3.zero);
            var footprint = tile.gameObject.AddComponent<BoardTileFootprint>();
            footprint.Configure(new[]
            {
                new Vector2(-1f, -1f),
                new Vector2(1f, -1f),
                new Vector2(1f, 1f),
                new Vector2(-1f, 1f)
            });

            Assert.That(footprint.CanContainInset(0.75f), Is.True);
            Assert.That(footprint.CanContainInset(1.01f), Is.False);
            Assert.That(footprint.CanContainInset(float.PositiveInfinity), Is.False);

            var fallback = footprint.GetClosestPointInside(
                new Vector3(5f, 0.6f, -4f),
                1.01f);
            Assert.That(footprint.ContainsHorizontalPoint(fallback), Is.True);
            Assert.That(fallback.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(fallback.y, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(fallback.z, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void TopologyValidation_AllowsFreeformCoordinatesAndReportsInvalidFootprint()
        {
            var source = CreateTile(
                "Source",
                Vector2Int.zero,
                BoardTileType.Start,
                Vector3.zero);
            var destination = CreateTile(
                "Destination",
                new Vector2Int(20, -7),
                BoardTileType.Normal,
                Vector3.right * BoardTile.RoomSize);
            var gateObject = CreateObject("Freeform Gate");
            gateObject.transform.position = Vector3.right * BoardTile.HalfRoomSize;
            gateObject.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            var gate = gateObject.AddComponent<BoardGate>();
            gate.Configure(source, destination);

            var valid = BoardTopologyValidator.Validate(
                new[] { source, destination },
                new[] { gate });
            Assert.That(valid.IsValid, Is.True);
            Assert.That(
                valid.Issues.Any(issue =>
                    issue.Code == BoardTopologyIssueCode.GateTilesNotAdjacent),
                Is.False);

            destination.gameObject.AddComponent<BoardTileFootprint>().Configure(
                CreateRegularPolygon(6, 2f));
            var invalid = BoardTopologyValidator.Validate(
                new[] { source, destination },
                new[] { gate });
            Assert.That(
                invalid.Issues.Select(issue => issue.Code),
                Does.Contain(BoardTopologyIssueCode.InvalidTileFootprint));
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

        private GameObject CreateObject(string name)
        {
            var instance = new GameObject(name);
            _objects.Add(instance);
            return instance;
        }

        private static Vector2[] CreateRegularPolygon(int sides, float radius)
        {
            var vertices = new Vector2[sides];
            for (var i = 0; i < sides; i++)
            {
                var angle = Mathf.PI * 2f * i / sides;
                vertices[i] = new Vector2(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius);
            }

            return vertices;
        }
    }
}
