using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardSceneContractTests
    {
        private const string BoardScenePath = "Assets/MazeParty/Scenes/Board/Board.unity";

        [Test]
        public void GeneratedBoardScene_MatchesPrototypeTopologyContract()
        {
            var scene = SceneManager.GetSceneByPath(BoardScenePath);
            var wasAlreadyLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasAlreadyLoaded)
            {
                scene = EditorSceneManager.OpenScene(BoardScenePath, OpenSceneMode.Additive);
            }

            try
            {
                var topology = FindTopology(scene);
                Assert.That(topology, Is.Not.Null, "Board scene must contain one BoardTopology.");
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
                var backdrop = scene.GetRootGameObjects().Single(root => root.name == "Board Backdrop (No Gameplay Collision)");
                Assert.That(PrefabUtility.IsPartOfPrefabInstance(backdrop), Is.True);
                Assert.That(backdrop.GetComponentsInChildren<Collider>(true), Is.Empty);
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
                    Assert.That(tile.GetComponent<BoxCollider>(), Is.Not.Null);
                    Assert.That(new SerializedObject(tile).FindProperty("landingEffectRenderer").objectReferenceValue, Is.Not.Null);
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
                if (!wasAlreadyLoaded && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
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
