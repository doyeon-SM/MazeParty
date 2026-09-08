using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class KeyShopWorldMarkerTests
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
        public void Preparing_IsHiddenAndRaisesPresentationHook()
        {
            var topology = CreateTopology(CreateTile(Vector2Int.zero));
            var marker = CreateMarker();
            var preparingCount = 0;
            marker.Preparing += value =>
            {
                preparingCount++;
                Assert.That(value.State, Is.EqualTo(KeyShopLifecycleState.Preparing));
                Assert.That(value.Revision, Is.EqualTo(1));
            };

            var applied = marker.ApplyReplicatedState(
                KeyShopLifecycleState.Preparing,
                false,
                default,
                topology,
                1);

            Assert.That(applied, Is.True);
            Assert.That(marker.IsVisible, Is.False);
            Assert.That(marker.MarkerObject, Is.Null);
            Assert.That(preparingCount, Is.EqualTo(1));
        }

        [Test]
        public void Appearing_CreatesVisibleEnglishMarkerAtResolvedTile()
        {
            var tile = CreateTile(new Vector2Int(3, 4));
            var topology = CreateTopology(tile);
            var marker = CreateMarker();
            KeyShopWorldMarkerEvent observed = default;
            marker.Appearing += value => observed = value;

            var applied = marker.ApplyReplicatedState(
                KeyShopLifecycleState.Appearing,
                true,
                tile.Coordinate,
                topology,
                2);

            Assert.That(applied, Is.True);
            Assert.That(marker.IsVisible, Is.True);
            Assert.That(marker.MarkerObject, Is.Not.Null);
            Assert.That(marker.WorldTextMesh, Is.Not.Null);
            Assert.That(marker.WorldTextMesh.text, Does.StartWith("KEY SHOP"));
            Assert.That(marker.WorldTextMesh.text, Does.Contain("20 GOLD"));
            Assert.That(
                marker.MarkerObject.transform.position,
                Is.EqualTo(tile.WorldCenter + Vector3.up * KeyShopWorldMarker.DefaultVerticalOffset));
            Assert.That(observed.Tile, Is.SameAs(tile));
            Assert.That(observed.Marker, Is.SameAs(marker.MarkerObject.transform));
        }

        [Test]
        public void ActiveAndRelocation_ReuseTheSameMarkerObject()
        {
            var first = CreateTile(Vector2Int.zero);
            var second = CreateTile(Vector2Int.right);
            var topology = CreateTopology(first, second);
            var marker = CreateMarker();
            var activeCount = 0;
            marker.Activated += _ => activeCount++;

            Assert.That(
                marker.ApplyReplicatedState(
                    KeyShopLifecycleState.Appearing,
                    true,
                    first.Coordinate,
                    topology,
                    1),
                Is.True);
            var originalMarker = marker.MarkerObject;
            Assert.That(
                marker.ApplyReplicatedState(
                    KeyShopLifecycleState.Active,
                    true,
                    first.Coordinate,
                    topology,
                    1),
                Is.True);
            Assert.That(activeCount, Is.EqualTo(1));

            Assert.That(
                marker.ApplyReplicatedState(
                    KeyShopLifecycleState.Preparing,
                    false,
                    default,
                    topology,
                    2),
                Is.True);
            Assert.That(marker.IsVisible, Is.False);
            Assert.That(marker.MarkerObject, Is.SameAs(originalMarker));

            Assert.That(
                marker.ApplyReplicatedState(
                    KeyShopLifecycleState.Appearing,
                    true,
                    second.Coordinate,
                    topology,
                    2),
                Is.True);
            Assert.That(marker.MarkerObject, Is.SameAs(originalMarker));
            Assert.That(marker.IsVisible, Is.True);
            Assert.That(
                marker.MarkerObject.transform.position,
                Is.EqualTo(second.WorldCenter + Vector3.up * KeyShopWorldMarker.DefaultVerticalOffset));
        }

        [Test]
        public void OlderRevision_CannotMoveOrHideCurrentMarker()
        {
            var first = CreateTile(Vector2Int.zero);
            var second = CreateTile(Vector2Int.right);
            var topology = CreateTopology(first, second);
            var marker = CreateMarker();
            Assert.That(
                marker.ApplyReplicatedState(
                    KeyShopLifecycleState.Active,
                    true,
                    second.Coordinate,
                    topology,
                    3),
                Is.True);
            var expectedPosition = marker.MarkerObject.transform.position;

            var staleApplied = marker.ApplyReplicatedState(
                KeyShopLifecycleState.Inactive,
                false,
                first.Coordinate,
                topology,
                2);

            Assert.That(staleApplied, Is.False);
            Assert.That(marker.LastAppliedRevision, Is.EqualTo(3));
            Assert.That(marker.AppliedState, Is.EqualTo(KeyShopLifecycleState.Active));
            Assert.That(marker.IsVisible, Is.True);
            Assert.That(marker.MarkerObject.transform.position, Is.EqualTo(expectedPosition));
        }

        [Test]
        public void MissingTopology_DefersVisibleHookAndSameRevisionCanRetry()
        {
            var tile = CreateTile(new Vector2Int(2, 2));
            var topology = CreateTopology(tile);
            var marker = CreateMarker();
            var appearingCount = 0;
            marker.Appearing += _ => appearingCount++;

            Assert.That(
                marker.ApplyReplicatedState(
                    KeyShopLifecycleState.Appearing,
                    true,
                    tile.Coordinate,
                    null,
                    4),
                Is.False);
            Assert.That(marker.IsVisible, Is.False);
            Assert.That(appearingCount, Is.Zero);

            Assert.That(
                marker.ApplyReplicatedState(
                    KeyShopLifecycleState.Appearing,
                    true,
                    tile.Coordinate,
                    topology,
                    4),
                Is.True);
            Assert.That(marker.IsVisible, Is.True);
            Assert.That(appearingCount, Is.EqualTo(1));
        }

        private BoardTile CreateTile(Vector2Int coordinate)
        {
            var gameObject = CreateObject("Tile " + coordinate);
            gameObject.transform.position = new Vector3(
                coordinate.x * BoardTile.RoomSize,
                0f,
                coordinate.y * BoardTile.RoomSize);
            var tile = gameObject.AddComponent<BoardTile>();
            tile.Configure(coordinate, BoardTileType.Normal);
            return tile;
        }

        private BoardTopology CreateTopology(params BoardTile[] tiles)
        {
            var topology = CreateObject("Topology").AddComponent<BoardTopology>();
            topology.Configure(tiles, new BoardGate[0]);
            return topology;
        }

        private KeyShopWorldMarker CreateMarker()
        {
            return CreateObject("Marker Host").AddComponent<KeyShopWorldMarker>();
        }

        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name);
            _objects.Add(gameObject);
            return gameObject;
        }
    }
}
