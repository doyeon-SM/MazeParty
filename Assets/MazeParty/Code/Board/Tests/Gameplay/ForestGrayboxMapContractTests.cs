using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class ForestGrayboxMapContractTests
    {
        private const string MapFolder =
            "Assets/MazeParty/Resources/MazeParty/Board/Maps";
        private const string DefinitionPath = MapFolder + "/ForestGrayboxMap.asset";
        private const string CatalogPath = MapFolder + "/BoardMapCatalog.asset";
        private const string PrefabPath = MapFolder + "/ForestGrayboxMapRoot.prefab";

        [Test]
        public void ForestGraybox_AssetsAreRuntimeDiscoverableAndCrossLinked()
        {
            var definition = AssetDatabase.LoadAssetAtPath<BoardMapDefinition>(DefinitionPath);
            var catalog = AssetDatabase.LoadAssetAtPath<BoardMapCatalog>(CatalogPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            Assert.That(definition, Is.Not.Null, DefinitionPath);
            Assert.That(catalog, Is.Not.Null, CatalogPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);

            Assert.That(definition.MapId, Is.EqualTo("forest-graybox"));
            Assert.That(definition.DisplayName, Is.EqualTo("Forest Graybox"));
            Assert.That(definition.ContentVersion, Is.EqualTo(1));
            Assert.That(
                AssetDatabase.GetAssetPath(definition.MapRootPrefab),
                Is.EqualTo(PrefabPath));
            Assert.That(definition.HasValidIdentity, Is.True);
            Assert.That(definition.HasValidPrefab, Is.True);
            Assert.That(catalog.HasUniqueValidIds(), Is.True);
            Assert.That(catalog.TryGetMap("forest-graybox", out var catalogEntry), Is.True);
            Assert.That(
                AssetDatabase.GetAssetPath(catalogEntry),
                Is.EqualTo(DefinitionPath));

            var mapRoot = prefab.GetComponent<BoardMapRoot>();
            Assert.That(mapRoot, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(mapRoot.Definition),
                Is.EqualTo(DefinitionPath));
            Assert.That(mapRoot.HasAuthoringRoots, Is.True);
            Assert.That(mapRoot.EnvironmentRoot.parent, Is.SameAs(mapRoot.transform));
        }

        [Test]
        public void ForestGraybox_HasFortyValidReachableTilesAndMixedConnections()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);
            var mapRoot = prefab.GetComponent<BoardMapRoot>();
            Assert.That(mapRoot, Is.Not.Null);

            var topology = mapRoot.Topology;
            topology.RebuildIndex();
            var tiles = topology.Tiles;
            var gates = topology.Gates;
            var validation = topology.ValidateTopology();
            Assert.That(
                validation.IsValid,
                Is.True,
                string.Join("\n", validation.Issues.Select(issue => issue.Message)));
            Assert.That(tiles.Count, Is.EqualTo(40));
            Assert.That(gates.Count, Is.EqualTo(87));
            Assert.That(tiles.Select(tile => tile.Coordinate).Distinct().Count(),
                Is.EqualTo(tiles.Count));
            Assert.That(tiles.Count(tile => tile.TileType == BoardTileType.Start),
                Is.EqualTo(1));
            Assert.That(tiles.Count(tile => tile.TileType == BoardTileType.Respawn),
                Is.EqualTo(1));
            Assert.That(mapRoot.StartTile.TileType, Is.EqualTo(BoardTileType.Start));

            var polygonSides = new HashSet<int>();
            for (var index = 0; index < tiles.Count; index++)
            {
                var tile = tiles[index];
                Assert.That(tile.transform.IsChildOf(mapRoot.TilesRoot), Is.True);
                Assert.That(tile.Footprint, Is.Not.Null, tile.name);
                Assert.That(
                    tile.Footprint.TryValidate(out var footprintError),
                    Is.True,
                    $"{tile.name}: {footprintError}");
                Assert.That(tile.Footprint.CanContainInset(0.52f), Is.True, tile.name);
                polygonSides.Add(tile.Footprint.VertexCount);
            }

            Assert.That(polygonSides, Is.EquivalentTo(new[] { 3, 4, 5 }));

            var edgeSet = gates
                .Where(gate => gate != null)
                .Select(gate => (gate.Source, gate.Destination))
                .ToHashSet();
            Assert.That(gates.Any(gate =>
                edgeSet.Contains((gate.Destination, gate.Source))), Is.True);
            Assert.That(gates.Count(gate =>
                !edgeSet.Contains((gate.Destination, gate.Source))), Is.EqualTo(1));
            Assert.That(tiles.Count(tile =>
                topology.GetOutgoingGates(tile)
                    .Select(gate => gate.Destination)
                    .Distinct()
                    .Count() >= 3), Is.GreaterThanOrEqualTo(4));

            Assert.That(mapRoot.PlayerStartTiles.Count, Is.EqualTo(4));
            Assert.That(mapRoot.PlayerStartTiles[0], Is.Null);
            Assert.That(mapRoot.PlayerStartTiles.Skip(1).All(tile => tile != null), Is.True);
            Assert.That(mapRoot.PlayerSpawnAnchors.Count, Is.EqualTo(4));
            for (var slot = 0; slot < 4; slot++)
            {
                var start = mapRoot.GetStartTile(slot);
                var anchor = mapRoot.GetSpawnAnchor(slot);
                Assert.That(anchor, Is.Not.Null, $"Player {slot + 1}");
                Assert.That(start.ContainsHorizontalPoint(anchor.position), Is.True,
                    $"Player {slot + 1}");
                Assert.That(
                    Vector3.Dot(
                        anchor.position - start.WorldCenter,
                        start.transform.up.normalized),
                    Is.EqualTo(1f).Within(0.001f),
                    $"Player {slot + 1}");
                Assert.That(CanReachAll(topology, start), Is.True,
                    $"Player {slot + 1}");
            }
        }

        private static bool CanReachAll(BoardTopology topology, BoardTile start)
        {
            var visited = new HashSet<BoardTile> { start };
            var pending = new Queue<BoardTile>();
            pending.Enqueue(start);
            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                var outgoing = topology.GetOutgoingGates(current);
                for (var index = 0; index < outgoing.Count; index++)
                {
                    var destination = outgoing[index]?.Destination;
                    if (destination != null && visited.Add(destination))
                    {
                        pending.Enqueue(destination);
                    }
                }
            }

            return topology.Tiles.All(tile => tile != null && visited.Contains(tile));
        }
    }
}
