using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardShopPresentationTests
    {
        [Test]
        public void PrefabShops_ReuseInstancesAndRejectStaleLocations()
        {
            var root = new GameObject("Shop state test");
            try
            {
                var first = new GameObject("First").AddComponent<BoardTile>();
                first.transform.SetParent(root.transform);
                first.Configure(Vector2Int.zero, BoardTileType.Normal);
                var second = new GameObject("Second").AddComponent<BoardTile>();
                second.transform.SetParent(root.transform);
                second.transform.position = Vector3.right * BoardTile.RoomSize;
                second.Configure(Vector2Int.right, BoardTileType.Normal);
                var topology = root.AddComponent<BoardTopology>();
                topology.Configure(new[] { first, second }, new BoardGate[0]);
                var key = root.AddComponent<KeyShopWorldMarker>();
                Assert.That(key.ApplyReplicatedState(KeyShopLifecycleState.Active, true, first.Coordinate, topology, 2), Is.True);
                var marker = key.MarkerObject;
                Assert.That(marker.GetComponent<BoardShopVisual>().HasRequiredReferences, Is.True);
                Assert.That(key.ApplyReplicatedState(KeyShopLifecycleState.Active, true, second.Coordinate, topology, 3), Is.True);
                Assert.That(key.MarkerObject, Is.SameAs(marker));
                var location = marker.transform.position;
                Assert.That(key.ApplyReplicatedState(KeyShopLifecycleState.Active, true, first.Coordinate, topology, 2), Is.False);
                Assert.That(marker.transform.position, Is.EqualTo(location));
                key.ApplyReplicatedState(KeyShopLifecycleState.Inactive, false, first.Coordinate, topology, 4);
                Assert.That(marker.activeSelf, Is.False);

                var items = root.AddComponent<ItemShopWorldMarker>();
                for (var index = 0; index < ItemShopRules.ShopCount; index++)
                {
                    Assert.That(items.ApplyReplicatedState(index, true, first.Coordinate, false, topology, 2), Is.True);
                    var item = items.GetMarkerObject(index);
                    foreach (var target in item.GetComponentsInChildren<ItemShopWorldTarget>(true))
                        Assert.That(target.ShopIndex, Is.EqualTo(index));
                    Assert.That(items.ApplyReplicatedState(index, true, second.Coordinate, true, topology, 3), Is.True);
                    Assert.That(items.GetMarkerObject(index), Is.SameAs(item));
                    Assert.That(items.ApplyReplicatedState(index, false, first.Coordinate, false, topology, 2), Is.False);
                    Assert.That(item.activeSelf, Is.True);
                    Assert.That(item.transform.position, Is.EqualTo(second.WorldCenter));
                    items.ApplyReplicatedState(index, false, second.Coordinate, true, topology, 4);
                    Assert.That(item.activeSelf, Is.False);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
