using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardShopPresentationTests
    {
        private const string WaterShieldPath =
            "Assets/MazeParty/Prefabs/Common/VFX/WaterShield.prefab";
        private const string SandShieldPath =
            "Assets/MazeParty/Prefabs/Common/VFX/SandShield.prefab";

        [Test]
        public void ShopPrefabs_AuthorSharedShieldForBothHighlightRequests()
        {
            foreach (var shop in new[]
                     {
                         (Path: "Assets/MazeParty/Prefabs/Board/World/KeyShop.prefab",
                             ShieldPath: SandShieldPath),
                         (Path: "Assets/MazeParty/Prefabs/Board/World/ItemShop1.prefab",
                             ShieldPath: WaterShieldPath),
                         (Path: "Assets/MazeParty/Prefabs/Board/World/ItemShop2.prefab",
                             ShieldPath: WaterShieldPath)
                     })
            {
                var prefab = PrefabUtility.LoadPrefabContents(shop.Path);
                try
                {
                    Assert.That(prefab, Is.Not.Null, shop.Path);
                    var visual = prefab.GetComponent<BoardShopVisual>();
                    Assert.That(visual, Is.Not.Null, shop.Path);
                    Assert.That(
                        visual.HasRequiredReferences,
                        Is.True,
                        shop.Path);
                    Assert.That(
                        visual.TopViewHighlight,
                        Is.SameAs(visual.LocationHighlightVfx),
                        shop.Path +
                        " must combine top-view and location requests on one shield.");
                    AssertLocationHighlightBinding(
                        visual,
                        shop.ShieldPath,
                        shop.Path);
                    Assert.That(visual.LocationHighlightVfx.activeSelf,
                        Is.False, shop.Path);
                    var interactionColliders = prefab
                        .GetComponentsInChildren<Collider>(true)
                        .Where(item =>
                            item.GetComponent<KeyShopWorldTarget>() != null ||
                            item.GetComponent<ItemShopWorldTarget>() != null)
                        .ToArray();
                    Assert.That(
                        interactionColliders,
                        Is.Not.Empty,
                        shop.Path);
                    Assert.That(
                        interactionColliders.All(item => item.isTrigger),
                        Is.True,
                        shop.Path +
                        " interaction volumes must remain query-only triggers.");
                    Assert.That(
                        visual.InteractionColliders,
                        Is.EquivalentTo(interactionColliders),
                        shop.Path);

                    var physicalColliders = visual.PhysicalColliders;
                    Assert.That(physicalColliders, Is.Not.Null, shop.Path);
                    Assert.That(physicalColliders, Is.Not.Empty, shop.Path);
                    Assert.That(
                        physicalColliders.All(item => item != null &&
                            !item.isTrigger &&
                            item.GetComponent<KeyShopWorldTarget>() == null &&
                            item.GetComponent<ItemShopWorldTarget>() == null),
                        Is.True,
                        shop.Path +
                        " must separate solid occupancy from interaction targets.");
                    Assert.That(
                        physicalColliders.Intersect(interactionColliders),
                        Is.Empty,
                        shop.Path);

                    var visualRoot = prefab.transform.Find("Visuals");
                    Assert.That(visualRoot, Is.Not.Null, shop.Path);
                    var authoredMeshes = visualRoot
                        .GetComponentsInChildren<MeshFilter>(true);
                    Assert.That(
                        physicalColliders.All(item =>
                            item is MeshCollider meshCollider &&
                            meshCollider.sharedMesh != null &&
                            !meshCollider.transform.IsChildOf(visualRoot) &&
                            authoredMeshes.Any(authoredMesh =>
                                HasMatchingMeshTransform(
                                    meshCollider,
                                    authoredMesh))),
                        Is.True,
                        shop.Path +
                        " physical occupancy must reuse the authored model mesh.");
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
                keyVisual.StopLocationHighlight();
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
                    itemVisual.StopLocationHighlight();
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

        [Test]
        public void ShopHighlightRequests_DoNotCancelEachOther()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MazeParty/Prefabs/Board/World/ItemShop1.prefab");
            var instance = Object.Instantiate(source);
            try
            {
                var visual = instance.GetComponent<BoardShopVisual>();
                Assert.That(visual, Is.Not.Null);

                visual.SetTopViewHighlightVisible(true);
                visual.PlayLocationHighlight();
                visual.StopLocationHighlight();
                Assert.That(
                    visual.LocationHighlightVfx.activeSelf,
                    Is.True,
                    "Ending the timed request must preserve top-view visibility.");

                visual.SetTopViewHighlightVisible(false);
                Assert.That(visual.LocationHighlightVfx.activeSelf, Is.False);

                visual.PlayLocationHighlight();
                visual.SetTopViewHighlightVisible(true);
                visual.SetTopViewHighlightVisible(false);
                Assert.That(
                    visual.LocationHighlightVfx.activeSelf,
                    Is.True,
                    "Ending top-view mode must preserve the timed request.");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void AssertLocationHighlightBinding(
            BoardShopVisual visual,
            string shieldPath,
            string context)
        {
            Assert.That(visual, Is.Not.Null, context);
            Assert.That(visual.LocationHighlightVfx, Is.Not.Null, context);
            Assert.That(
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    visual.LocationHighlightVfx),
                Is.EqualTo(shieldPath),
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

        private static bool HasMatchingMeshTransform(
            MeshCollider physical,
            MeshFilter authored)
        {
            return authored != null &&
                   authored.sharedMesh == physical.sharedMesh &&
                   Vector3.SqrMagnitude(
                       authored.transform.position -
                       physical.transform.position) <= 0.000001f &&
                   Quaternion.Angle(
                       authored.transform.rotation,
                       physical.transform.rotation) <= 0.001f &&
                   Vector3.SqrMagnitude(
                       authored.transform.lossyScale -
                       physical.transform.lossyScale) <= 0.000001f;
        }

    }
}
