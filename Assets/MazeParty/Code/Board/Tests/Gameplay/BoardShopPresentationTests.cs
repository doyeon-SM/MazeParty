using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardShopPresentationTests
    {
        private static void AssertPeripheral(BoardTile tile, Vector3 position)
        {
            Assert.That(tile.ContainsHorizontalDisc(position, 1.45f), Is.True,
                "The shop footprint must remain inside its authoritative tile.");
            var distance = Vector3.Distance(tile.GetRecoveryCenter(), position);
            Assert.That(distance, Is.GreaterThan(1.5f));
            Assert.That(distance, Is.LessThan(ItemShopRules.InteractionDistance));
        }

        [Test]
        public void ShopPrefabs_SeparatePhysicalAndInteractionColliders()
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
                    Assert.That(
                        visual.HasRequiredReferences,
                        Is.True,
                        path);
                    var interactionColliders = prefab
                        .GetComponentsInChildren<Collider>(true)
                        .Where(item =>
                            item.GetComponent<KeyShopWorldTarget>() != null ||
                            item.GetComponent<ItemShopWorldTarget>() != null)
                        .ToArray();
                    Assert.That(
                        interactionColliders,
                        Is.Not.Empty,
                        path);
                    Assert.That(
                        interactionColliders.All(item => item.isTrigger),
                        Is.True,
                        path +
                        " interaction volumes must remain query-only triggers.");
                    Assert.That(
                        visual.InteractionColliders,
                        Is.EquivalentTo(interactionColliders),
                        path);

                    var physicalColliders = visual.PhysicalColliders;
                    Assert.That(physicalColliders, Is.Not.Null, path);
                    Assert.That(physicalColliders, Is.Not.Empty, path);
                    Assert.That(
                        physicalColliders.All(item => item != null &&
                            !item.isTrigger &&
                            item.GetComponent<KeyShopWorldTarget>() == null &&
                            item.GetComponent<ItemShopWorldTarget>() == null),
                        Is.True,
                        path +
                        " must separate solid occupancy from interaction targets.");
                    Assert.That(
                        physicalColliders.Intersect(interactionColliders),
                        Is.Empty,
                        path);

                    var visualRoot = prefab.transform.Find("Visuals");
                    Assert.That(visualRoot, Is.Not.Null, path);
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
                        path +
                        " physical occupancy must reuse the authored model mesh.");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefab);
                }
            }
        }

        [Test]
        public void ShopMarkers_ReuseInstancesAndRejectStaleLocations()
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
                AssertPeripheral(first, marker.transform.position);
                Assert.That(marker.GetComponent<BoardShopVisual>().HasRequiredReferences, Is.True);
                Assert.That(key.ApplyReplicatedState(KeyShopLifecycleState.Active, true, second.Coordinate, topology, 3), Is.True);
                Assert.That(key.MarkerObject, Is.SameAs(marker));
                Assert.That(key.ApplyReplicatedState(KeyShopLifecycleState.Active, true, first.Coordinate, topology, 2), Is.False);
                Assert.That(marker.activeSelf, Is.True);
                AssertPeripheral(second, marker.transform.position);
                key.ApplyReplicatedState(KeyShopLifecycleState.Inactive, false, first.Coordinate, topology, 4);
                Assert.That(marker.activeSelf, Is.False);

                var items = root.AddComponent<ItemShopWorldMarker>();
                for (var index = 0; index < ItemShopRules.ShopCount; index++)
                {
                    Assert.That(items.ApplyReplicatedState(index, true, first.Coordinate, false, topology, 2), Is.True);
                    var item = items.GetMarkerObject(index);
                    AssertPeripheral(first, item.transform.position);
                    Assert.That(item.GetComponent<BoardShopVisual>().HasRequiredReferences, Is.True);
                    foreach (var target in item.GetComponentsInChildren<ItemShopWorldTarget>(true))
                        Assert.That(target.ShopIndex, Is.EqualTo(index));
                    Assert.That(items.ApplyReplicatedState(index, true, second.Coordinate, true, topology, 3), Is.True);
                    Assert.That(items.GetMarkerObject(index), Is.SameAs(item));
                    Assert.That(items.ApplyReplicatedState(index, false, first.Coordinate, false, topology, 2), Is.False);
                    Assert.That(item.activeSelf, Is.True);
                    AssertPeripheral(second, item.transform.position);
                    items.ApplyReplicatedState(index, false, second.Coordinate, true, topology, 4);
                    Assert.That(item.activeSelf, Is.False);
                }
            }
            finally { Object.DestroyImmediate(root); }
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
