using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class KeyShopPlacementTests
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
        public void PlacementPolicy_SelectsOnlyEligibleTilesAndFailsWithoutOne()
        {
            var occupied = CreateTile(new Vector2Int(0, 0), BoardTileType.Normal);
            var start = CreateTile(new Vector2Int(1, 0), BoardTileType.Start);
            var legacyShop = CreateTile(new Vector2Int(2, 0), BoardTileType.KeyShop);
            var respawn = CreateTile(new Vector2Int(3, 0), BoardTileType.Respawn);
            var eligible = CreateTile(new Vector2Int(4, 0), BoardTileType.Normal);

            Assert.That(
                KeyShopPlacementPolicy.TryChoose(
                    new[] { occupied, start, legacyShop, respawn, eligible },
                    new[] { occupied.Coordinate },
                    new FixedRandomSource(1),
                    out var selected),
                Is.True);
            Assert.That(selected, Is.SameAs(eligible));

            var unusedRandom = new FixedRandomSource(0);
            Assert.That(
                KeyShopPlacementPolicy.TryChoose(
                    new[] { occupied, legacyShop, respawn },
                    new[] { occupied.Coordinate },
                    unusedRandom,
                    out selected),
                Is.False);
            Assert.That(selected, Is.Null);
            Assert.That(unusedRandom.CallCount, Is.Zero);
        }

        [Test]
        public void InitialPlacement_IsTurnTwoOnlyAndRunsLifecycleOnce()
        {
            var runtime = CreateRuntime();
            var first = CreateTile(Vector2Int.zero, BoardTileType.Normal);
            var second = CreateTile(Vector2Int.right, BoardTileType.Normal);
            var states = new List<KeyShopLifecycleState>();
            runtime.StateChanged += value => states.Add(value.State);

            Assert.That(runtime.TryBeginInitialPlacement(1, new[] { first, second }, null, out _), Is.False);
            Assert.That(
                runtime.TryBeginInitialPlacement(
                    2,
                    new[] { first, second },
                    null,
                    new FixedRandomSource(1),
                    out var selected),
                Is.True);
            Assert.That(selected, Is.SameAs(second));
            Assert.That(runtime.State, Is.EqualTo(KeyShopLifecycleState.Appearing));
            Assert.That(runtime.TryCompleteAppearance(), Is.True);
            CollectionAssert.AreEqual(
                new[]
                {
                    KeyShopLifecycleState.Preparing,
                    KeyShopLifecycleState.Appearing,
                    KeyShopLifecycleState.Active
                },
                states);
            Assert.That(runtime.TryBeginInitialPlacement(2, new[] { first, second }, null, out _), Is.False);
            Assert.That(runtime.PlacementRevision, Is.EqualTo(1));
        }

        [Test]
        public void PurchaseRelocation_ExcludesCurrentAndPreservesActiveShopOnFailure()
        {
            var runtime = CreateRuntime();
            var first = CreateTile(new Vector2Int(0, 0), BoardTileType.Normal);
            var current = CreateTile(new Vector2Int(1, 0), BoardTileType.Normal);
            var occupied = CreateTile(new Vector2Int(2, 0), BoardTileType.Normal);
            Assert.That(
                runtime.TryBeginInitialPlacement(
                    2,
                    new[] { first, current, occupied },
                    null,
                    new FixedRandomSource(1),
                    out _),
                Is.True);
            Assert.That(runtime.TryCompleteAppearance(), Is.True);
            Assert.That(runtime.Location, Is.EqualTo(current.Coordinate));

            Assert.That(
                runtime.TryBeginPurchaseRelocation(
                    new[] { first, current, occupied },
                    new[] { occupied.Coordinate },
                    new FixedRandomSource(0),
                    out var selected),
                Is.True);
            Assert.That(selected, Is.SameAs(first));
            Assert.That(runtime.TryCompleteAppearance(), Is.True);
            Assert.That(runtime.Location, Is.EqualTo(first.Coordinate));

            Assert.That(
                runtime.TryBeginPurchaseRelocation(
                    new[] { first },
                    null,
                    new FixedRandomSource(0),
                    out selected),
                Is.False);
            Assert.That(selected, Is.Null);
            Assert.That(runtime.IsActive, Is.True);
            Assert.That(runtime.Location, Is.EqualTo(first.Coordinate));
        }

        private BoardTile CreateTile(Vector2Int coordinate, BoardTileType type)
        {
            var gameObject = CreateObject("Tile " + coordinate);
            gameObject.transform.position = new Vector3(
                coordinate.x * BoardTile.RoomSize,
                0f,
                coordinate.y * BoardTile.RoomSize);
            var tile = gameObject.AddComponent<BoardTile>();
            tile.Configure(coordinate, type);
            return tile;
        }

        private KeyShopRuntimeState CreateRuntime()
        {
            return CreateObject("Key Shop Runtime").AddComponent<KeyShopRuntimeState>();
        }

        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name);
            _objects.Add(gameObject);
            return gameObject;
        }

        private sealed class FixedRandomSource : IKeyShopRandomSource
        {
            private readonly int _index;

            public FixedRandomSource(int index)
            {
                _index = index;
            }

            public int CallCount { get; private set; }

            public int NextIndex(int exclusiveMaximum)
            {
                CallCount++;
                return _index;
            }
        }
    }
}
