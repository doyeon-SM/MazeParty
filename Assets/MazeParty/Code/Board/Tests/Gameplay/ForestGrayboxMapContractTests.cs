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
        private const string TerrainDataPath =
            "Assets/MazeParty/Art/Board/Forest/ForestGroundTerrain.asset";
        private const string GrassLayerPath =
            "Assets/Ignore/Polytope Studio/Lowpoly_Demos/Environment_Free/" +
            "Helpers/Ground_Layer_02.terrainlayer";
        private const string DirtLayerPath =
            "Assets/Ignore/Polytope Studio/Lowpoly_Demos/Environment_Free/" +
            "Helpers/Ground_Layer_01.terrainlayer";

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
            Assert.That(definition.DisplayName, Is.EqualTo("Forest"));
            Assert.That(definition.ContentVersion, Is.EqualTo(4));
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
        public void ForestGraybox_HasFortyTwoValidReachableTilesAndCounterclockwiseOneWayConnections()
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
            Assert.That(tiles.Count, Is.EqualTo(42));
            Assert.That(gates.Count, Is.EqualTo(49));
            Assert.That(tiles.Select(tile => tile.Coordinate).Distinct().Count(),
                Is.EqualTo(tiles.Count));
            Assert.That(tiles.Count(tile => tile.TileType == BoardTileType.Start),
                Is.EqualTo(1));
            Assert.That(tiles.Count(tile => tile.TileType == BoardTileType.Respawn),
                Is.EqualTo(2));
            Assert.That(mapRoot.StartTile.TileType, Is.EqualTo(BoardTileType.Start));
            Assert.That(mapRoot.StartTile.Coordinate, Is.EqualTo(new Vector2Int(3, 0)));
            Assert.That(
                tiles.Where(tile => tile.TileType == BoardTileType.Respawn)
                    .Select(tile => tile.Coordinate.x),
                Is.EquivalentTo(new[] { 2, 13 }));

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

            Assert.That(polygonSides, Is.EquivalentTo(new[] { 4, 5 }));

            var edgeSet = gates
                .Where(gate => gate != null)
                .Select(gate => (gate.Source, gate.Destination))
                .ToHashSet();
            Assert.That(gates.All(gate =>
                !edgeSet.Contains((gate.Destination, gate.Source))), Is.True,
                "Every forest connection must be one-way.");
            Assert.That(tiles.All(tile =>
                topology.GetOutgoingGates(tile).Count > 0), Is.True);
            Assert.That(tiles.All(tile =>
                topology.GetIncomingGates(tile).Count > 0), Is.True);

            var outerLoop = Enumerable.Range(0, 23)
                .Select(index => tiles.Single(tile =>
                    tile.Coordinate == new Vector2Int(index, 0)))
                .ToArray();
            var twiceSignedArea = 0f;
            for (var index = 0; index < outerLoop.Length; index++)
            {
                var source = outerLoop[index];
                var destination = outerLoop[(index + 1) % outerLoop.Length];
                Assert.That(edgeSet.Contains((source, destination)), Is.True,
                    $"Outer route {index} must advance counterclockwise.");
                twiceSignedArea +=
                    source.WorldCenter.x * destination.WorldCenter.z -
                    destination.WorldCenter.x * source.WorldCenter.z;
            }

            Assert.That(twiceSignedArea, Is.GreaterThan(0f),
                "The authored outer loop must be counterclockwise in XZ space.");
            AssertDirectedPath(edgeSet, tiles, 1, 3);
            AssertDirectedPath(edgeSet, tiles, 12, 14);
            AssertDirectedPath(edgeSet, tiles, 4, 23, 24, 25, 26, 27, 28, 12);
            AssertDirectedPath(edgeSet, tiles, 8, 29, 30, 31, 26);
            AssertDirectedPath(edgeSet, tiles, 26, 32, 33, 34, 17);
            AssertDirectedPath(edgeSet, tiles, 20, 35, 36, 37, 38, 24);
            AssertDirectedPath(edgeSet, tiles, 24, 39, 40, 41, 7);
            Assert.That(tiles.Where(tile =>
                    topology.GetOutgoingGates(tile)
                        .Select(gate => gate.Destination)
                        .Distinct()
                        .Count() == 2)
                .Select(tile => tile.Coordinate.x),
                Is.EquivalentTo(new[] { 1, 4, 8, 12, 20, 24, 26 }));
            Assert.That(tiles.All(tile => CanReachAll(topology, tile)), Is.True,
                "The directed forest graph must remain strongly connected.");

            Assert.That(mapRoot.PlayerStartTiles.Count, Is.EqualTo(4));
            Assert.That(mapRoot.PlayerStartTiles[0], Is.Null);
            Assert.That(mapRoot.PlayerStartTiles.Skip(1).All(tile => tile != null), Is.True);
            Assert.That(
                Enumerable.Range(0, 4)
                    .Select(slot => mapRoot.GetStartTile(slot).Coordinate)
                    .ToArray(),
                Is.EqualTo(new[]
                {
                    new Vector2Int(3, 0),
                    new Vector2Int(9, 0),
                    new Vector2Int(14, 0),
                    new Vector2Int(19, 0)
                }));
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

        [Test]
        public void ForestGraybox_GroundUsesPolytopeLayersAndPaintsEveryRoute()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);
            var mapRoot = prefab.GetComponent<BoardMapRoot>();
            Assert.That(mapRoot, Is.Not.Null);

            var generatedGround = mapRoot.EnvironmentRoot.Find("Generated Ground");
            Assert.That(generatedGround, Is.Not.Null);
            Assert.That(generatedGround.parent, Is.SameAs(mapRoot.EnvironmentRoot));

            var terrain = generatedGround.GetComponentInChildren<Terrain>(true);
            Assert.That(terrain, Is.Not.Null);
            Assert.That(terrain.name, Is.EqualTo("Forest Ground"));
            Assert.That(terrain.materialTemplate, Is.Not.Null,
                "The terrain needs an explicit URP material to render in prefab stage and builds.");
            Assert.That(
                generatedGround.GetComponentsInChildren<TerrainCollider>(true),
                Is.Empty,
                "Generated ground is visual-only and must not alter board physics.");
            Assert.That(AssetDatabase.GetAssetPath(terrain.terrainData),
                Is.EqualTo(TerrainDataPath));

            var layers = terrain.terrainData.terrainLayers;
            Assert.That(layers, Has.Length.EqualTo(2));
            Assert.That(AssetDatabase.GetAssetPath(layers[0]),
                Is.EqualTo(GrassLayerPath));
            Assert.That(AssetDatabase.GetAssetPath(layers[1]),
                Is.EqualTo(DirtLayerPath));

            var terrainOrigin = mapRoot.EnvironmentRoot.InverseTransformPoint(
                terrain.transform.position);
            var terrainSize = terrain.terrainData.size;
            foreach (var tile in mapRoot.Topology.Tiles)
            {
                var local = mapRoot.EnvironmentRoot.InverseTransformPoint(
                    tile.WorldCenter);
                Assert.That(local.x,
                    Is.InRange(terrainOrigin.x, terrainOrigin.x + terrainSize.x),
                    tile.name);
                Assert.That(local.z,
                    Is.InRange(terrainOrigin.z, terrainOrigin.z + terrainSize.z),
                    tile.name);
            }

            var visited = new HashSet<string>();
            foreach (var gate in mapRoot.Topology.Gates.Where(gate => gate != null))
            {
                var sourceCoordinate = gate.Source.Coordinate;
                var destinationCoordinate = gate.Destination.Coordinate;
                var source = sourceCoordinate.x + "," + sourceCoordinate.y;
                var destination =
                    destinationCoordinate.x + "," + destinationCoordinate.y;
                var key = string.CompareOrdinal(source, destination) < 0
                    ? source + ":" + destination
                    : destination + ":" + source;
                if (!visited.Add(key))
                {
                    continue;
                }

                var midpoint = (gate.Source.WorldCenter + gate.Destination.WorldCenter) * 0.5f;
                var local = mapRoot.EnvironmentRoot.InverseTransformPoint(midpoint);
                var normalizedX = Mathf.InverseLerp(
                    terrainOrigin.x,
                    terrainOrigin.x + terrainSize.x,
                    local.x);
                var normalizedZ = Mathf.InverseLerp(
                    terrainOrigin.z,
                    terrainOrigin.z + terrainSize.z,
                    local.z);
                var alphaX = Mathf.Clamp(
                    Mathf.RoundToInt(normalizedX *
                                     (terrain.terrainData.alphamapWidth - 1)),
                    0,
                    terrain.terrainData.alphamapWidth - 1);
                var alphaZ = Mathf.Clamp(
                    Mathf.RoundToInt(normalizedZ *
                                     (terrain.terrainData.alphamapHeight - 1)),
                    0,
                    terrain.terrainData.alphamapHeight - 1);
                var weights = terrain.terrainData.GetAlphamaps(
                    alphaX,
                    alphaZ,
                    1,
                    1);
                Assert.That(weights[0, 0, 1],
                    Is.GreaterThan(weights[0, 0, 0]),
                    "Dirt is missing from route " + key);
            }

            Assert.That(visited, Has.Count.EqualTo(49));
        }

        private static void AssertDirectedPath(
            ISet<(BoardTile Source, BoardTile Destination)> edges,
            IReadOnlyList<BoardTile> tiles,
            params int[] coordinates)
        {
            for (var index = 0; index < coordinates.Length - 1; index++)
            {
                var source = tiles.Single(tile =>
                    tile.Coordinate == new Vector2Int(coordinates[index], 0));
                var destination = tiles.Single(tile =>
                    tile.Coordinate == new Vector2Int(coordinates[index + 1], 0));
                Assert.That(edges.Contains((source, destination)), Is.True,
                    $"Expected directed route {coordinates[index]} -> " +
                    $"{coordinates[index + 1]}.");
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
