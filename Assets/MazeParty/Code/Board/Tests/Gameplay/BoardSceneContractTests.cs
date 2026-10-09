using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardSceneContractTests
    {
        private const string BoardScenePath = "Assets/MazeParty/Scenes/Board/Board.unity";
        private const string BoardSkyboxPath =
            "Assets/Ignore/Fantasy Skybox FREE/Cubemaps/Classic/FS000_Night_01.mat";
        private const string ForestTerrainDataPath =
            "Assets/MazeParty/Art/Board/Forest/ForestGroundTerrain.asset";
        private const string BoundaryBlueFirePath =
            "Assets/Ignore/AllIn1VfxToolkit/Demo & Assets/Demo/Prefabs/Blue Fire.prefab";
        private const string BoundaryRedFirePath =
            "Assets/Ignore/AllIn1VfxToolkit/Demo & Assets/Demo/Prefabs/Real Fire.prefab";

        [Test]
        public void AuthoredTilePrefabs_KeepWorldRenderersOffAndCollidersOn()
        {
            var paths = new[]
            {
                "Assets/MazeParty/Prefabs/Board/World/TileNormalA.prefab",
                "Assets/MazeParty/Prefabs/Board/World/TileNormalB.prefab",
                "Assets/MazeParty/Prefabs/Board/World/TileStart.prefab",
                "Assets/MazeParty/Prefabs/Board/World/TileRespawn.prefab"
            };

            foreach (var path in paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path);
                Assert.That(prefab.GetComponent<BoardTile>(), Is.Not.Null, path);
                var collider = prefab.GetComponent<BoxCollider>();
                Assert.That(collider, Is.Not.Null, path);
                Assert.That(collider.enabled, Is.True, path);
                Assert.That(
                    prefab.GetComponentsInChildren<Renderer>(true),
                    Is.All.Matches<Renderer>(renderer => !renderer.enabled),
                    path);
            }
        }

        [Test]
        public void ShippedMapTiles_HideWorldBlocksButKeepGameplayAndMapData()
        {
            var catalog = Resources.Load<BoardMapCatalog>(
                BoardMapRuntimeLoader.CatalogResourcesPath);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Maps, Is.Not.Empty);

            foreach (var definition in catalog.Maps)
            {
                Assert.That(definition, Is.Not.Null);
                Assert.That(definition.MapRootPrefab, Is.Not.Null,
                    definition.MapId);
                var instance = UnityEngine.Object.Instantiate(
                    definition.MapRootPrefab);
                try
                {
                    var tiles = instance.GetComponentsInChildren<BoardTile>(true);
                    Assert.That(tiles, Is.Not.Empty, definition.MapId);
                    foreach (var tile in tiles)
                    {
                        tile.ApplyLandingEffectPresentation(
                            BoardLandingEffectType.SpecialEvent);
                        Assert.That(
                            tile.LandingEffect,
                            Is.EqualTo(tile.TileType == BoardTileType.Respawn
                                ? BoardLandingEffectType.None
                                : BoardLandingEffectType.SpecialEvent),
                            definition.MapId + " / " + tile.name);
                        Assert.That(
                            tile.GetComponentsInChildren<Renderer>(true),
                            Is.All.Matches<Renderer>(renderer =>
                                !renderer.enabled),
                            definition.MapId + " / " + tile.name);
                        var collider = tile.GetComponent<BoxCollider>();
                        Assert.That(collider, Is.Not.Null,
                            definition.MapId + " / " + tile.name);
                        Assert.That(collider.enabled, Is.True,
                            definition.MapId + " / " + tile.name);
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void GeneratedBoardScene_MatchesPrototypeTopologyContract()
        {
            var previousActiveScene = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(BoardScenePath);
            var wasAlreadyLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasAlreadyLoaded)
            {
                scene = EditorSceneManager.OpenScene(BoardScenePath, OpenSceneMode.Additive);
            }
            SceneManager.SetActiveScene(scene);

            try
            {
                var expectedSkybox = AssetDatabase.LoadAssetAtPath<Material>(
                    BoardSkyboxPath);
                Assert.That(expectedSkybox, Is.Not.Null, BoardSkyboxPath);
                Assert.That(RenderSettings.skybox, Is.SameAs(expectedSkybox));
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Skybox));
                var mainCamera = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                    .Single(camera => camera.name == "Main Camera");
                Assert.That(mainCamera.clearFlags, Is.EqualTo(CameraClearFlags.Skybox));

                var topology = FindTopology(scene);
                Assert.That(topology, Is.Not.Null, "Board scene must contain one BoardTopology.");
                var mapLoader = scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<BoardMapRuntimeLoader>(true))
                    .Single();
                Assert.That(mapLoader.HasRequiredReferences, Is.True);
                Assert.That(mapLoader.RuntimeTopology, Is.SameAs(topology));
                Assert.That(
                    Resources.Load<BoardMapCatalog>(
                        BoardMapRuntimeLoader.CatalogResourcesPath),
                    Is.Not.Null,
                    "The production runtime map catalog must stay at its stable Resources path.");
                var tombstoneView = topology.GetComponents<MonoBehaviour>()
                    .SingleOrDefault(component => component != null &&
                        component.GetType().FullName ==
                            "MazeParty.Multiplayer.BoardTombstoneWorldView");
                Assert.That(tombstoneView, Is.Not.Null);
                var hasReferences = tombstoneView.GetType()
                    .GetProperty("HasRequiredReferences")?.GetValue(tombstoneView);
                Assert.That(hasReferences, Is.EqualTo(true),
                    "Board tombstones must use the authored world prefab.");
                var tombstonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/MazeParty/Prefabs/Board/World/BoardTombstone.prefab");
                Assert.That(tombstonePrefab, Is.Not.Null);
                Assert.That(tombstonePrefab.GetComponent<BoxCollider>(), Is.Not.Null);
                Assert.That(tombstonePrefab.GetComponents<MonoBehaviour>()
                    .Any(component => component != null &&
                        component.GetType().FullName ==
                            "MazeParty.Multiplayer.BoardTombstoneMarker"), Is.True);
                var routeView = topology.GetComponents<MonoBehaviour>().SingleOrDefault(component =>
                    component != null && component.GetType().FullName == "MazeParty.Multiplayer.BoardShopRouteView");
                Assert.That(routeView, Is.Not.Null, "Board scene must have exactly one local shop guide.");
                Assert.That(routeView.GetType().GetProperty("HasRequiredReferences")?.GetValue(routeView), Is.EqualTo(true));
                var worldAssets = BoardWorldPrefabs.LoadRequired();
                Assert.That(worldAssets.HasRequiredReferences, Is.True);
                foreach (var marker in new Component[] { topology.GetComponent<KeyShopWorldMarker>(), topology.GetComponent<ItemShopWorldMarker>() })
                {
                    Assert.That(marker, Is.Not.Null);
                    Assert.That(new SerializedObject(marker).FindProperty("worldPrefabs").objectReferenceValue, Is.SameAs(worldAssets));
                }
                Assert.That(worldAssets.KeyShop.GetComponentsInChildren<KeyShopWorldTarget>(true), Is.Not.Empty);
                for (var shop = 0; shop < 2; shop++)
                {
                    var targets = worldAssets.ItemShop(shop).GetComponentsInChildren<ItemShopWorldTarget>(true);
                    Assert.That(targets, Is.Not.Empty, "Authored shop targets must survive prefab serialization.");
                    Assert.That(targets.All(target => target.ShopIndex == shop), Is.True);
                }
                var wall = worldAssets.BoundaryWall;
                Assert.That(wall.GetComponentsInChildren<Collider>(true), Has.Length.EqualTo(1),
                    "Decorative wall children must not bypass owner-only collision isolation.");
                Assert.That(wall.BlockingCollider.isTrigger, Is.False);
                Assert.That(wall.HasRequiredReferences, Is.True);
                Assert.That(
                    AssetDatabase.GetAssetPath(
                        PrefabUtility.GetCorrespondingObjectFromSource(
                            wall.PassableFlameRoot)),
                    Is.EqualTo(BoundaryBlueFirePath));
                Assert.That(
                    AssetDatabase.GetAssetPath(
                        PrefabUtility.GetCorrespondingObjectFromSource(
                            wall.BlockedFlameRoot)),
                    Is.EqualTo(BoundaryRedFirePath));
                var backdrop = scene.GetRootGameObjects().Single(root => root.name == "Board Backdrop (No Gameplay Collision)");
                Assert.That(PrefabUtility.IsPartOfPrefabInstance(backdrop), Is.True);
                Assert.That(backdrop.GetComponentsInChildren<Collider>(true), Is.Empty);
                var terrainPlaceholder = scene.GetRootGameObjects().Single(root =>
                    root.name == "Runtime Terrain Resources (Build Placeholder)");
                var placeholderTerrain = terrainPlaceholder.GetComponent<Terrain>();
                Assert.That(terrainPlaceholder.activeSelf, Is.False,
                    "The runtime placeholder must stay hidden in the Board scene.");
                Assert.That(placeholderTerrain, Is.Not.Null);
                Assert.That(placeholderTerrain.enabled, Is.True,
                    "Unity discovers an enabled Terrain component on the inactive placeholder.");
                Assert.That(placeholderTerrain.drawInstanced, Is.False,
                    "The placeholder must match the runtime Terrain rendering path.");
                Assert.That(placeholderTerrain.materialTemplate, Is.Not.Null);
                Assert.That(
                    AssetDatabase.GetAssetPath(placeholderTerrain.terrainData),
                    Is.EqualTo(ForestTerrainDataPath));
                Assert.That(
                    terrainPlaceholder.GetComponentsInChildren<TerrainCollider>(true),
                    Is.Empty);
                topology.RebuildIndex();

                var tiles = topology.Tiles.Where(tile => tile != null).ToArray();
                var gates = topology.Gates.Where(gate => gate != null).ToArray();
                var starts = tiles.Where(tile => tile.TileType == BoardTileType.Start).ToArray();

                Assert.That(tiles, Has.Length.EqualTo(32));
                Assert.That(gates, Has.Length.EqualTo(36));
                Assert.That(starts, Has.Length.EqualTo(4));
                Assert.That(
                    starts.Select(tile => tile.Coordinate),
                    Is.EquivalentTo(new[]
                    {
                        new Vector2Int(1, 1),
                        new Vector2Int(5, 1),
                        new Vector2Int(5, 5),
                        new Vector2Int(1, 5)
                    }));
                Assert.That(tiles.Min(tile => tile.Coordinate.x), Is.EqualTo(0));
                Assert.That(tiles.Max(tile => tile.Coordinate.x), Is.EqualTo(6));
                Assert.That(tiles.Min(tile => tile.Coordinate.y), Is.EqualTo(0));
                Assert.That(tiles.Max(tile => tile.Coordinate.y), Is.EqualTo(6));
                Assert.That(
                    gates.All(gate => Mathf.Approximately(gate.GateWidth, BoardTile.RoomSize)),
                    Is.True,
                    "A blue reusable boundary opens the complete side of its room.");
                Assert.That(
                    tiles.Count(tile => tile.TileType == BoardTileType.KeyShop),
                    Is.Zero,
                    "The unique Key Shop is placed dynamically at turn-two overview.");

                var validation = topology.ValidateTopology();
                Assert.That(
                    validation.IsValid,
                    Is.True,
                    string.Join(Environment.NewLine, validation.Issues.Select(issue => issue.Message)));

                foreach (var tile in tiles)
                {
                    Assert.That(PrefabUtility.IsPartOfPrefabInstance(tile), Is.True,
                        "Board rooms must inherit their authored prefab design.");
                    var tileCollider = tile.GetComponent<BoxCollider>();
                    Assert.That(tileCollider, Is.Not.Null);
                    Assert.That(tileCollider.enabled, Is.True,
                        "Hidden tile visuals must not disable board collision.");
                    Assert.That(new SerializedObject(tile).FindProperty("landingEffectRenderer").objectReferenceValue, Is.Not.Null);
                    tile.ApplyLandingEffectPresentation(
                        BoardLandingEffectType.GoldGain);
                    Assert.That(
                        tile.LandingEffect,
                        Is.EqualTo(tile.TileType == BoardTileType.Respawn
                            ? BoardLandingEffectType.None
                            : BoardLandingEffectType.GoldGain),
                        "The hidden world tile must still retain map-readable effect data.");
                    Assert.That(
                        tile.GetComponentsInChildren<Renderer>(true),
                        Is.All.Matches<Renderer>(renderer => !renderer.enabled),
                        "Board tile blocks, labels and effect surfaces stay hidden in world view.");
                    Assert.That(
                        topology.GetOutgoingGates(tile),
                        Is.Not.Empty,
                        "Every room must retain at least one directed exit: " + tile.Coordinate);
                }

                foreach (var start in starts)
                {
                    Assert.That(
                        CountReachableTiles(topology, start),
                        Is.EqualTo(tiles.Length),
                        "Every start must be able to reach the full directed board: " + start.Coordinate);
                    var route = new List<BoardTile>();
                    var target = tiles.Single(tile =>
                        tile.Coordinate == new Vector2Int(3, 0));
                    Assert.That(BoardMapRoute.TryFind(topology, start, target, route),
                        Is.True);
                    Assert.That(route[0], Is.SameAs(start));
                    Assert.That(route[route.Count - 1], Is.SameAs(target));
                    for (var index = 0; index + 1 < route.Count; index++)
                    {
                        Assert.That(topology.GetOutgoingGates(route[index])
                                .Any(gate => gate.Destination == route[index + 1]),
                            Is.True, "Map route must respect directed gates.");
                    }
                }

            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActiveScene);
                }
                if (!wasAlreadyLoaded && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [Test]
        public void AuthoredShopPrefabs_UseAssignedHousesAndKeepVisualsCollisionFree()
        {
            var worldAssets = BoardWorldPrefabs.LoadRequired();

            AssertShopPrefab(
                worldAssets.KeyShop,
                "Assets/Ignore/Fantasy Lowpoly Pack (Demo)/Prefabs/blue-house_001.prefab");
            AssertShopPrefab(
                worldAssets.ItemShop(0),
                "Assets/Ignore/Fantasy Lowpoly Pack (Demo)/Prefabs/house-red_001.prefab");
            AssertShopPrefab(
                worldAssets.ItemShop(1),
                "Assets/Ignore/Fantasy Lowpoly Pack (Demo)/Prefabs/house-red_001.prefab");
        }

        private static void AssertShopPrefab(
            BoardShopVisual shop,
            string expectedModelPath)
        {
            Assert.That(shop, Is.Not.Null);
            Assert.That(shop.HasRequiredReferences, Is.True);

            var visuals = shop.transform.Find("Visuals");
            Assert.That(visuals, Is.Not.Null);
            var house = visuals.Find("House Model");
            Assert.That(house, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(
                    PrefabUtility.GetCorrespondingObjectFromSource(
                        house.gameObject)),
                Is.EqualTo(expectedModelPath));

            Assert.That(
                visuals.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                "Vendor house colliders must not become gameplay collision.");
            var authoredColliders = shop.InteractionColliders
                .Concat(shop.PhysicalColliders)
                .ToArray();
            Assert.That(
                shop.GetComponentsInChildren<Collider>(true),
                Is.EquivalentTo(authoredColliders),
                "Every shop collider must be explicitly bound as interaction " +
                "or physical occupancy.");
            Assert.That(
                shop.InteractionColliders,
                Is.All.Matches<Collider>(collider =>
                    collider != null && collider.isTrigger &&
                    (collider.GetComponent<KeyShopWorldTarget>() != null ||
                     collider.GetComponent<ItemShopWorldTarget>() != null)),
                "Shop interaction targets must remain query-only triggers.");
            Assert.That(
                shop.PhysicalColliders,
                Is.All.Matches<Collider>(collider =>
                    collider != null && !collider.isTrigger &&
                    collider.GetComponent<KeyShopWorldTarget>() == null &&
                    collider.GetComponent<ItemShopWorldTarget>() == null),
                "Shop physical occupancy must remain separate from interaction.");
        }

        private static BoardTopology FindTopology(Scene scene)
        {
            BoardTopology found = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var candidate = root.GetComponentInChildren<BoardTopology>(true);
                if (candidate == null)
                {
                    continue;
                }

                Assert.That(found, Is.Null, "Board scene contains more than one BoardTopology.");
                found = candidate;
            }

            return found;
        }

        private static int CountReachableTiles(BoardTopology topology, BoardTile start)
        {
            var visited = new HashSet<BoardTile> { start };
            var pending = new Queue<BoardTile>();
            pending.Enqueue(start);

            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                var outgoing = topology.GetOutgoingGates(current);
                for (var i = 0; i < outgoing.Count; i++)
                {
                    var destination = outgoing[i].Destination;
                    if (destination != null && visited.Add(destination))
                    {
                        pending.Enqueue(destination);
                    }
                }
            }

            return visited.Count;
        }
    }
}
