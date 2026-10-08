using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardShopPresentationTests
    {
        private const string WaterShieldPath =
            "Assets/MazeParty/Prefabs/Common/VFX/WaterShield.prefab";

        [Test]
        public void ShopPrefabs_AuthorSharedWaterShieldLocationHighlights()
        {
            foreach (var path in new[]
                     {
                         "Assets/MazeParty/Prefabs/Board/World/KeyShop.prefab",
                         "Assets/MazeParty/Prefabs/Board/World/ItemShop1.prefab",
                         "Assets/MazeParty/Prefabs/Board/World/ItemShop2.prefab"
                     })
            {
                var prefab = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Assert.That(prefab, Is.Not.Null, path);
                    var visual = prefab.GetComponent<BoardShopVisual>();
                    Assert.That(visual, Is.Not.Null, path);
                    Assert.That(visual.HasRequiredReferences, Is.True, path);
                    AssertLocationHighlightBinding(visual, path);
                    Assert.That(visual.LocationHighlightVfx.activeSelf,
                        Is.False, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefab);
                }
            }
        }

        [Test]
        public void ShopHighlights_PlayOncePerRevisionAndRejectStaleLocations()
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
                Assert.That(marker.transform.position, Is.EqualTo(first.WorldCenter));
                var keyVisual = marker.GetComponent<BoardShopVisual>();
                Assert.That(keyVisual.HasRequiredReferences, Is.True);
                AssertRuntimeLocationHighlight(keyVisual, "Key shop marker");
                Assert.That(keyVisual.LocationHighlightVfx.activeSelf, Is.True);
                keyVisual.LocationHighlightVfx.SetActive(false);
                Assert.That(key.ApplyReplicatedState(KeyShopLifecycleState.Active, true, first.Coordinate, topology, 2), Is.True);
                Assert.That(keyVisual.LocationHighlightVfx.activeSelf, Is.False,
                    "A repeated snapshot must not restart the location highlight.");
                Assert.That(key.ApplyReplicatedState(KeyShopLifecycleState.Active, true, second.Coordinate, topology, 3), Is.True);
                Assert.That(key.MarkerObject, Is.SameAs(marker));
                Assert.That(marker.transform.position, Is.EqualTo(second.WorldCenter));
                Assert.That(keyVisual.LocationHighlightVfx.activeSelf, Is.True,
                    "A new key-shop location revision must restart the highlight.");
                Assert.That(key.ApplyReplicatedState(KeyShopLifecycleState.Active, true, first.Coordinate, topology, 2), Is.False);
                Assert.That(marker.activeSelf, Is.True);
                Assert.That(marker.transform.position, Is.EqualTo(second.WorldCenter));
                Assert.That(keyVisual.LocationHighlightVfx.activeSelf, Is.True,
                    "A stale key-shop revision must not replace the active presentation state.");
                key.ApplyReplicatedState(KeyShopLifecycleState.Inactive, false, first.Coordinate, topology, 4);
                Assert.That(marker.activeSelf, Is.False);
                Assert.That(keyVisual.LocationHighlightVfx.activeSelf, Is.False);

                var items = root.AddComponent<ItemShopWorldMarker>();
                for (var index = 0; index < ItemShopRules.ShopCount; index++)
                {
                    Assert.That(items.ApplyReplicatedState(index, true, first.Coordinate, false, topology, 2), Is.True);
                    var item = items.GetMarkerObject(index);
                    Assert.That(item.transform.position, Is.EqualTo(first.WorldCenter));
                    var itemVisual = item.GetComponent<BoardShopVisual>();
                    AssertRuntimeLocationHighlight(
                        itemVisual,
                        "Item shop marker " + index);
                    Assert.That(itemVisual.LocationHighlightVfx.activeSelf,
                        Is.True);
                    foreach (var target in item.GetComponentsInChildren<ItemShopWorldTarget>(true))
                        Assert.That(target.ShopIndex, Is.EqualTo(index));
                    itemVisual.LocationHighlightVfx.SetActive(false);
                    Assert.That(items.ApplyReplicatedState(index, true, first.Coordinate, false, topology, 2), Is.True);
                    Assert.That(itemVisual.LocationHighlightVfx.activeSelf,
                        Is.False,
                        "A repeated item-shop snapshot must not restart the highlight.");
                    Assert.That(items.ApplyReplicatedState(index, true, second.Coordinate, true, topology, 3), Is.True);
                    Assert.That(items.GetMarkerObject(index), Is.SameAs(item));
                    Assert.That(item.transform.position, Is.EqualTo(second.WorldCenter));
                    Assert.That(itemVisual.LocationHighlightVfx.activeSelf,
                        Is.True,
                        "A new item-shop location revision must restart the highlight.");
                    Assert.That(items.ApplyReplicatedState(index, false, first.Coordinate, false, topology, 2), Is.False);
                    Assert.That(item.activeSelf, Is.True);
                    Assert.That(item.transform.position, Is.EqualTo(second.WorldCenter));
                    Assert.That(itemVisual.LocationHighlightVfx.activeSelf,
                        Is.True,
                        "A stale item-shop revision must not replace the active presentation state.");
                    items.ApplyReplicatedState(index, false, second.Coordinate, true, topology, 4);
                    Assert.That(item.activeSelf, Is.False);
                    Assert.That(itemVisual.LocationHighlightVfx.activeSelf,
                        Is.False);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void AssertLocationHighlightBinding(
            BoardShopVisual visual,
            string context)
        {
            Assert.That(visual, Is.Not.Null, context);
            Assert.That(visual.LocationHighlightVfx, Is.Not.Null, context);
            Assert.That(
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    visual.LocationHighlightVfx),
                Is.EqualTo(WaterShieldPath),
                context);
            Assert.That(
                visual.LocationHighlightVfx.GetComponentsInChildren<Collider>(
                    true),
                Is.Empty,
                context + " must remain presentation-only.");
        }

        private static void AssertRuntimeLocationHighlight(
            BoardShopVisual visual,
            string context)
        {
            Assert.That(visual, Is.Not.Null, context);
            Assert.That(visual.LocationHighlightVfx, Is.Not.Null, context);
            Assert.That(
                visual.LocationHighlightVfx.GetComponentsInChildren<Collider>(
                    true),
                Is.Empty,
                context + " must remain presentation-only.");
        }

    }
}
