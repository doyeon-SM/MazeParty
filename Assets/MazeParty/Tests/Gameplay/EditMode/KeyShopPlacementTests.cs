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
            for (var i = _objects.Count - 1; i >= 0; i--)
                Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        [Test]
        public void Policy_TreatsStartMarkerAsNormalAndExcludesSpecialTiles()
        {
            var occupiedNormal = CreateTile(new Vector2Int(0, 0), BoardTileType.Normal);
            var start = CreateTile(new Vector2Int(1, 0), BoardTileType.Start);
            var legacyShop = CreateTile(new Vector2Int(2, 0), BoardTileType.KeyShop);
            var respawn = CreateTile(new Vector2Int(3, 0), BoardTileType.Respawn);
            var firstEligible = CreateTile(new Vector2Int(4, 0), BoardTileType.Normal);
            var secondEligible = CreateTile(new Vector2Int(5, 0), BoardTileType.Normal);
            var random = new FixedRandomSource(1);

            var selected = KeyShopPlacementPolicy.TryChoose(
                new[] { occupiedNormal, start, legacyShop, respawn, firstEligible, secondEligible },
                new[] { occupiedNormal.Coordinate },
                random,
                out var selectedTile);

            Assert.That(selected, Is.True);
            Assert.That(selectedTile, Is.SameAs(firstEligible));
            Assert.That(random.LastExclusiveMaximum, Is.EqualTo(3));
        }

        [Test]
        public void Policy_NoEligibleTile_ReturnsFalseWithoutUsingRandomSource()
        {
            var occupiedNormal = CreateTile(Vector2Int.zero, BoardTileType.Normal);
            var legacyShop = CreateTile(Vector2Int.right, BoardTileType.KeyShop);
            var respawn = CreateTile(Vector2Int.up, BoardTileType.Respawn);
            var random = new FixedRandomSource(0);

            var selected = KeyShopPlacementPolicy.TryChoose(
                new[] { occupiedNormal, legacyShop, respawn },
                new[] { occupiedNormal.Coordinate },
                random,
                out var selectedTile);

            Assert.That(selected, Is.False);
            Assert.That(selectedTile, Is.Null);
            Assert.That(random.CallCount, Is.Zero);
        }

        [Test]
        public void InitialPlacement_OnlyStartsAtTurnTwo()
        {
            var runtime = CreateRuntime();
            var tile = CreateTile(Vector2Int.zero, BoardTileType.Normal);

            Assert.That(
                runtime.TryBeginInitialPlacement(1, new[] { tile }, null, out _),
                Is.False);
            Assert.That(
                runtime.TryBeginInitialPlacement(3, new[] { tile }, null, out _),
                Is.False);
            Assert.That(runtime.State, Is.EqualTo(KeyShopLifecycleState.Inactive));
            Assert.That(runtime.HasLocation, Is.False);
            Assert.That(runtime.PlacementRevision, Is.Zero);
        }

        [Test]
        public void TurnTwoPlacement_ExposesPreparingAppearingAndActiveHooks()
        {
            var runtime = CreateRuntime();
            var first = CreateTile(Vector2Int.zero, BoardTileType.Normal);
            var second = CreateTile(Vector2Int.right, BoardTileType.Normal);
            var observedStates = new List<KeyShopLifecycleState>();
            KeyShopLifecycleEvent preparing = default;
            KeyShopLifecycleEvent appearing = default;
            KeyShopLifecycleEvent activated = default;
            runtime.StateChanged += value => observedStates.Add(value.State);
            runtime.Preparing += value => preparing = value;
            runtime.Appearing += value => appearing = value;
            runtime.Activated += value => activated = value;

            var began = runtime.TryBeginInitialPlacement(
                2,
                new[] { first, second },
                null,
                new FixedRandomSource(1),
                out var selected);

            Assert.That(began, Is.True);
            Assert.That(selected, Is.SameAs(second));
            Assert.That(runtime.State, Is.EqualTo(KeyShopLifecycleState.Appearing));
            Assert.That(preparing.Reason, Is.EqualTo(KeyShopPlacementReason.InitialTurnTwo));
            Assert.That(preparing.HasTargetLocation, Is.False);
            Assert.That(appearing.HasTargetLocation, Is.True);
            Assert.That(appearing.TargetLocation, Is.EqualTo(second.Coordinate));
            Assert.That(appearing.Revision, Is.EqualTo(preparing.Revision));

            Assert.That(runtime.TryCompleteAppearance(), Is.True);

            CollectionAssert.AreEqual(
                new[]
                {
                    KeyShopLifecycleState.Preparing,
                    KeyShopLifecycleState.Appearing,
                    KeyShopLifecycleState.Active
                },
                observedStates);
            Assert.That(runtime.IsActive, Is.True);
            Assert.That(activated.TargetLocation, Is.EqualTo(second.Coordinate));
            Assert.That(activated.Revision, Is.EqualTo(preparing.Revision));
        }

        [Test]
        public void PurchaseRelocation_ReusesPolicyAndExcludesCurrentShopTile()
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

            KeyShopLifecycleEvent preparing = default;
            runtime.Preparing += value => preparing = value;
            var relocated = runtime.TryBeginPurchaseRelocation(
                new[] { first, current, occupied },
                new[] { occupied.Coordinate },
                new FixedRandomSource(0),
                out var selected);

            Assert.That(relocated, Is.True);
            Assert.That(selected, Is.SameAs(first));
            Assert.That(preparing.Reason, Is.EqualTo(KeyShopPlacementReason.PurchaseRelocation));
            Assert.That(preparing.HasPreviousLocation, Is.True);
            Assert.That(preparing.PreviousLocation, Is.EqualTo(current.Coordinate));
            Assert.That(preparing.HasTargetLocation, Is.False);
            Assert.That(runtime.State, Is.EqualTo(KeyShopLifecycleState.Appearing));
        }

        [Test]
        public void FailedPurchaseRelocation_PreservesExistingActiveShop()
        {
            var runtime = CreateRuntime();
            var onlyNormal = CreateTile(Vector2Int.zero, BoardTileType.Normal);
            Assert.That(
                runtime.TryBeginInitialPlacement(
                    2,
                    new[] { onlyNormal },
                    null,
                    new FixedRandomSource(0),
                    out _),
                Is.True);
            Assert.That(runtime.TryCompleteAppearance(), Is.True);

            var relocated = runtime.TryBeginPurchaseRelocation(
                new[] { onlyNormal },
                null,
                new FixedRandomSource(0),
                out var selected);

            Assert.That(relocated, Is.False);
            Assert.That(selected, Is.Null);
            Assert.That(runtime.IsActive, Is.True);
            Assert.That(runtime.Location, Is.EqualTo(onlyNormal.Coordinate));
            Assert.That(runtime.PlacementRevision, Is.EqualTo(1));
        }

        [Test]
        public void InitialPlacement_CannotRunTwice()
        {
            var runtime = CreateRuntime();
            var first = CreateTile(Vector2Int.zero, BoardTileType.Normal);
            var second = CreateTile(Vector2Int.right, BoardTileType.Normal);
            Assert.That(
                runtime.TryBeginInitialPlacement(2, new[] { first, second }, null, out _),
                Is.True);
            Assert.That(runtime.TryCompleteAppearance(), Is.True);

            Assert.That(
                runtime.TryBeginInitialPlacement(2, new[] { first, second }, null, out _),
                Is.False);
            Assert.That(runtime.PlacementRevision, Is.EqualTo(1));
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
            public int LastExclusiveMaximum { get; private set; }

            public int NextIndex(int exclusiveMaximum)
            {
                CallCount++;
                LastExclusiveMaximum = exclusiveMaximum;
                return _index;
            }
        }
    }
}
