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
                    AssertLocationHighlightBinding(prefab, visual, path);
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
        public void PrefabShops_StayAtTileCenterWhenSceneryOverlapsAndRejectStaleLocations()
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
                var blockingScenery = new GameObject("Overlapping authored scenery");
                blockingScenery.transform.SetParent(root.transform);
                blockingScenery.transform.position = second.WorldCenter;
                var blockingCollider = blockingScenery.AddComponent<BoxCollider>();
                blockingCollider.size = Vector3.one * BoardTile.RoomSize;
                var key = root.AddComponent<KeyShopWorldMarker>();
                Assert.That(key.ApplyReplicatedState(KeyShopLifecycleState.Active, true, first.Coordinate, topology, 2), Is.True);
                var marker = key.MarkerObject;
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
                Assert.That(blockingCollider.bounds.Contains(marker.transform.position), Is.True);
                var location = marker.transform.position;
                Assert.That(key.ApplyReplicatedState(KeyShopLifecycleState.Active, true, first.Coordinate, topology, 2), Is.False);
                Assert.That(marker.transform.position, Is.EqualTo(location));
                key.ApplyReplicatedState(KeyShopLifecycleState.Inactive, false, first.Coordinate, topology, 4);
                Assert.That(marker.activeSelf, Is.False);
                Assert.That(keyVisual.LocationHighlightVfx.activeSelf, Is.False);

                var items = root.AddComponent<ItemShopWorldMarker>();
                for (var index = 0; index < ItemShopRules.ShopCount; index++)
                {
                    Assert.That(items.ApplyReplicatedState(index, true, first.Coordinate, false, topology, 2), Is.True);
                    var item = items.GetMarkerObject(index);
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
                    Assert.That(itemVisual.LocationHighlightVfx.activeSelf,
                        Is.True,
                        "A new item-shop location revision must restart the highlight.");
                    Assert.That(items.ApplyReplicatedState(index, false, first.Coordinate, false, topology, 2), Is.False);
                    Assert.That(item.activeSelf, Is.True);
                    Assert.That(item.transform.position, Is.EqualTo(second.WorldCenter));
                    Assert.That(blockingCollider.bounds.Contains(item.transform.position), Is.True);
                    items.ApplyReplicatedState(index, false, second.Coordinate, true, topology, 4);
                    Assert.That(item.activeSelf, Is.False);
                    Assert.That(itemVisual.LocationHighlightVfx.activeSelf,
                        Is.False);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void AssertLocationHighlightBinding(
            GameObject prefab,
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

            var visualRoot = prefab.transform.Find("Visuals");
            Assert.That(visualRoot, Is.Not.Null, context);
            var highlight = visual.LocationHighlightVfx;
            var wasActive = highlight.activeSelf;
            highlight.SetActive(true);
            var shopBounds = CalculateRenderedBounds(
                visualRoot,
                prefab.transform);
            var highlightBounds = CalculateRenderedBounds(
                highlight.transform,
                prefab.transform);
            highlight.SetActive(wasActive);

            Assert.That(
                Vector3.Distance(shopBounds.center, highlightBounds.center),
                Is.LessThan(.01f),
                context + " highlight must be centered on the authored shop renderers.");
            Assert.That(
                Mathf.Abs(MaximumComponent(shopBounds.size) -
                          MaximumComponent(highlightBounds.size)),
                Is.LessThan(.01f),
                context + " highlight must fit the authored shop renderer envelope.");
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

        private static Bounds CalculateRenderedBounds(
            Transform renderedRoot,
            Transform relativeTo)
        {
            var renderers = renderedRoot.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty, renderedRoot.name);

            var initialized = false;
            var localBounds = new Bounds();
            foreach (var renderer in renderers)
            {
                var bounds = renderer.bounds;
                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var corner = bounds.center + Vector3.Scale(
                        bounds.extents,
                        new Vector3(x, y, z));
                    var point = relativeTo.InverseTransformPoint(corner);
                    if (!initialized)
                    {
                        localBounds = new Bounds(point, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(point);
                    }
                }
            }

            return localBounds;
        }

        private static float MaximumComponent(Vector3 value)
        {
            return Mathf.Max(value.x, value.y, value.z);
        }
    }
}
