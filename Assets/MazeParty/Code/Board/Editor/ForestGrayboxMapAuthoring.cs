using System;
using System.Collections.Generic;
using System.Linq;
using MazeParty.Gameplay;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Editor
{
    /// <summary>
    /// Creates the deterministic first-pass forest board. Refreshing this template
    /// replaces topology-owned objects only; anything below Environment is retained.
    /// </summary>
    internal static class ForestGrayboxMapAuthoring
    {
        internal const string MapId = "forest-graybox";
        internal const string MapFolder =
            "Assets/MazeParty/Resources/MazeParty/Board/Maps";
        internal const string DefinitionPath = MapFolder + "/ForestGrayboxMap.asset";
        internal const string CatalogPath = MapFolder + "/BoardMapCatalog.asset";
        internal const string PrefabPath = MapFolder + "/ForestGrayboxMapRoot.prefab";
        internal const int TileCount = 40;

        private const string TilePrefabFolder = "Assets/MazeParty/Prefabs/Board/World";
        private const float FootprintRadius = 3.6f;
        private const float GateWidth = 2.5f;
        private const float SpawnHeight = 1f;
        private const float RequiredSafeInset = 0.52f;

        private static readonly Vector3[] TilePositions =
        {
            new Vector3(-28f, 0f, -12f),
            new Vector3(-22f, 0f, -18f),
            new Vector3(-14f, 0f, -21f),
            new Vector3(-6f, 0f, -22f),
            new Vector3(2f, 0f, -20f),
            new Vector3(10f, 0f, -22f),
            new Vector3(18f, 0f, -18f),
            new Vector3(25f, 0f, -12f),
            new Vector3(29f, 0f, -4f),
            new Vector3(28f, 0f, 5f),
            new Vector3(24f, 0f, 13f),
            new Vector3(17f, 0f, 19f),
            new Vector3(9f, 0f, 22f),
            new Vector3(1f, 0f, 21f),
            new Vector3(-7f, 0f, 23f),
            new Vector3(-15f, 0f, 20f),
            new Vector3(-22f, 0f, 15f),
            new Vector3(-27f, 0f, 8f),
            new Vector3(-30f, 0f, 0f),
            new Vector3(-29f, 0f, -7f),

            // Interior trail A: lower route to the eastern ridge.
            new Vector3(-9f, 0f, -14f),
            new Vector3(-5f, 0f, -7f),
            new Vector3(0f, 0f, -2f),
            new Vector3(6f, 0f, 2f),
            new Vector3(12f, 0f, 6f),
            new Vector3(18f, 0f, 10f),

            // Interior trail B: eastern fork to the northern ridge.
            new Vector3(19f, 0f, -10f),
            new Vector3(14f, 0f, -5f),
            new Vector3(9f, 0f, 0f),
            new Vector3(4f, 0f, 6f),
            new Vector3(-1f, 0f, 12f),
            new Vector3(-4f, 0f, 17f),

            // Interior trail C: western fork back to the southern ridge.
            new Vector3(-21f, 0f, 8f),
            new Vector3(-15f, 0f, 6f),
            new Vector3(-11f, 0f, 1f),
            new Vector3(-8f, 0f, -5f),
            new Vector3(-3f, 0f, -10f),
            new Vector3(3f, 0f, -12f),
            new Vector3(9f, 0f, -13f),
            new Vector3(14f, 0f, -16f)
        };

        private static readonly ConnectionSpec[] Connections = BuildConnections();

        [MenuItem("MazeParty/Board/Create or Refresh Forest Graybox")]
        internal static void CreateOrRefreshFromMenu()
        {
            try
            {
                var prefab = CreateOrRefresh();
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
                Debug.Log(
                    $"Forest graybox refreshed: {TileCount} tiles at '{PrefabPath}'. " +
                    "Only topology and spawn anchors were rebuilt; Environment contents were preserved.",
                    prefab);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        internal static GameObject CreateOrRefresh()
        {
            ValidateBlueprint();
            EnsureAssetFolder(MapFolder);

            var tilePrefabs = LoadTilePrefabs();
            var definition = GetOrCreateDefinition();
            var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var loadedPrefabContents = existingPrefab != null;
            var root = loadedPrefabContents
                ? PrefabUtility.LoadPrefabContents(PrefabPath)
                : new GameObject("Board Map - Forest Graybox");

            try
            {
                RebuildTemplate(root, definition, tilePrefabs);
                ValidateGeneratedRoot(root);

                var savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (savedPrefab == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save forest graybox prefab at '{PrefabPath}'.");
                }

                definition.Configure(MapId, "Forest Graybox", 1, savedPrefab);
                EditorUtility.SetDirty(definition);
                UpdateCatalog(definition);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
                return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }
            finally
            {
                if (loadedPrefabContents)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private static void RebuildTemplate(
            GameObject root,
            BoardMapDefinition definition,
            IReadOnlyList<GameObject> tilePrefabs)
        {
            root.name = "Board Map - Forest Graybox";
            var mapRoot = root.GetComponent<BoardMapRoot>() ??
                          root.AddComponent<BoardMapRoot>();
            var environment = ResolveEnvironmentRoot(root, mapRoot);
            RemoveGeneratedRoots(root, mapRoot, environment);

            var topologyObject = CreateChild(root.transform, "Topology");
            var topology = topologyObject.AddComponent<BoardTopology>();
            var tilesRoot = CreateChild(topologyObject.transform, "Tiles").transform;
            var connectionsRoot = CreateChild(topologyObject.transform, "Connections").transform;

            var tiles = new BoardTile[TileCount];
            for (var index = 0; index < TileCount; index++)
            {
                var type = index == 0
                    ? BoardTileType.Start
                    : index == 12
                        ? BoardTileType.Respawn
                        : BoardTileType.Normal;
                var prefab = type == BoardTileType.Start
                    ? tilePrefabs[2]
                    : type == BoardTileType.Respawn
                        ? tilePrefabs[3]
                        : tilePrefabs[index % 2];
                var tileObject = PrefabUtility.InstantiatePrefab(prefab, tilesRoot) as GameObject;
                if (tileObject == null)
                {
                    throw new InvalidOperationException(
                        $"Could not instantiate tile prefab '{AssetDatabase.GetAssetPath(prefab)}'.");
                }

                tileObject.name = $"Forest Tile {index:00} - {type}";
                tileObject.transform.localPosition = TilePositions[index];
                tileObject.transform.localRotation = Quaternion.identity;
                tileObject.transform.localScale = Vector3.one;

                var tile = tileObject.GetComponent<BoardTile>();
                tile.Configure(new Vector2Int(index, 0), type);
                var footprint = tileObject.GetComponent<BoardTileFootprint>() ??
                                tileObject.AddComponent<BoardTileFootprint>();
                var sideCount = 3 + ((index + 1) % 3);
                footprint.Configure(BoardMapAuthoringWindow.BuildRegularPolygon(
                    sideCount,
                    FootprintRadius));
                tiles[index] = tile;
            }

            var gates = new List<BoardGate>(Connections.Length * 2);
            for (var index = 0; index < Connections.Length; index++)
            {
                var connection = Connections[index];
                var connectionRoot = CreateChild(
                    connectionsRoot,
                    $"Connection {connection.Source:00} - {connection.Destination:00}" +
                    (connection.Bidirectional ? " (Two Way)" : " (One Way)"));
                gates.Add(CreateGate(
                    connectionRoot.transform,
                    tiles[connection.Source],
                    tiles[connection.Destination]));
                if (connection.Bidirectional)
                {
                    gates.Add(CreateGate(
                        connectionRoot.transform,
                        tiles[connection.Destination],
                        tiles[connection.Source]));
                }
            }

            var playerStartTiles = new BoardTile[PlayerSlotRules.Count];
            playerStartTiles[1] = tiles[1];
            playerStartTiles[2] = tiles[19];
            playerStartTiles[3] = tiles[2];
            var anchors = CreateSpawnAnchors(root.transform, tiles[0], playerStartTiles);

            topology.Configure(tiles, gates.ToArray());
            mapRoot.Configure(
                definition,
                topology,
                tilesRoot,
                connectionsRoot,
                environment,
                tiles[0],
                playerStartTiles,
                anchors);
        }

        private static Transform ResolveEnvironmentRoot(GameObject root, BoardMapRoot mapRoot)
        {
            var environment = mapRoot.EnvironmentRoot;
            if (environment == null)
            {
                environment = root.transform.Find("Environment");
            }

            if (environment == null)
            {
                environment = CreateChild(root.transform, "Environment").transform;
            }
            else if (environment.parent != root.transform)
            {
                environment.SetParent(root.transform, true);
            }

            return environment;
        }

        private static void RemoveGeneratedRoots(
            GameObject root,
            BoardMapRoot mapRoot,
            Transform environment)
        {
            var topologyTransform = mapRoot.Topology != null
                ? mapRoot.Topology.transform
                : root.transform.Find("Topology");
            if (topologyTransform != null &&
                topologyTransform != root.transform &&
                topologyTransform != environment)
            {
                UnityEngine.Object.DestroyImmediate(topologyTransform.gameObject);
            }

            var spawnRoot = root.transform.Find("Player Spawn Anchors");
            if (spawnRoot != null && spawnRoot != environment)
            {
                UnityEngine.Object.DestroyImmediate(spawnRoot.gameObject);
            }
        }

        private static Transform[] CreateSpawnAnchors(
            Transform root,
            BoardTile sharedStart,
            IReadOnlyList<BoardTile> playerStartTiles)
        {
            var anchorRoot = CreateChild(root, "Player Spawn Anchors").transform;
            var anchors = new Transform[PlayerSlotRules.Count];
            for (var slot = 0; slot < anchors.Length; slot++)
            {
                var tile = playerStartTiles[slot] != null
                    ? playerStartTiles[slot]
                    : sharedStart;
                var anchor = CreateChild(anchorRoot, $"Player Spawn {slot + 1}").transform;
                anchor.position = tile.GetRecoveryCenter(SpawnHeight);
                anchor.rotation = tile.transform.rotation;
                anchors[slot] = anchor;
            }

            return anchors;
        }

        private static BoardGate CreateGate(
            Transform parent,
            BoardTile source,
            BoardTile destination)
        {
            var sourcePoint = source.GetClosestPointInside(destination.WorldCenter);
            var destinationPoint = destination.GetClosestPointInside(source.WorldCenter);
            var direction = destination.WorldCenter - source.WorldCenter;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                throw new InvalidOperationException(
                    $"Forest tiles {source.Coordinate.x} and {destination.Coordinate.x} overlap.");
            }

            var gateObject = CreateChild(
                parent,
                $"Gate {source.Coordinate.x:00} to {destination.Coordinate.x:00}");
            gateObject.transform.SetPositionAndRotation(
                (sourcePoint + destinationPoint) * 0.5f + Vector3.up * 0.15f,
                Quaternion.LookRotation(direction.normalized, Vector3.up));
            var gate = gateObject.AddComponent<BoardGate>();
            gate.Configure(source, destination, GateWidth);
            return gate;
        }

        private static void ValidateBlueprint()
        {
            if (TilePositions.Length != TileCount || TileCount < 32 || TileCount > 48)
            {
                throw new InvalidOperationException(
                    $"Forest graybox blueprint must contain 32-48 tiles; found {TilePositions.Length}.");
            }

            var directedEdges = new HashSet<(int Source, int Destination)>();
            for (var index = 0; index < Connections.Length; index++)
            {
                var connection = Connections[index];
                if (connection.Source < 0 || connection.Source >= TileCount ||
                    connection.Destination < 0 || connection.Destination >= TileCount ||
                    connection.Source == connection.Destination ||
                    !directedEdges.Add((connection.Source, connection.Destination)) ||
                    connection.Bidirectional &&
                    !directedEdges.Add((connection.Destination, connection.Source)))
                {
                    throw new InvalidOperationException(
                        $"Forest graybox connection {index} is invalid or duplicated.");
                }
            }
        }

        private static void ValidateGeneratedRoot(GameObject root)
        {
            var problems = new List<string>();
            var mapRoot = root.GetComponent<BoardMapRoot>();
            if (mapRoot == null || !mapRoot.HasAuthoringRoots)
            {
                throw new InvalidOperationException(
                    "Forest graybox root is missing required authoring roots.");
            }

            var tiles = mapRoot.Topology.Tiles;
            var gates = mapRoot.Topology.Gates;
            if (tiles.Count != TileCount)
            {
                problems.Add($"expected {TileCount} tiles, found {tiles.Count}");
            }

            if (tiles.Count(tile => tile != null && tile.TileType == BoardTileType.Start) != 1)
            {
                problems.Add("expected exactly one shared Start tile");
            }

            if (tiles.Count(tile => tile != null && tile.TileType == BoardTileType.Respawn) != 1)
            {
                problems.Add("expected exactly one Respawn tile");
            }

            for (var index = 0; index < tiles.Count; index++)
            {
                var footprint = tiles[index] != null ? tiles[index].Footprint : null;
                var footprintError = "component is missing";
                if (footprint == null ||
                    !footprint.TryValidate(out footprintError) ||
                    !footprint.CanContainInset(RequiredSafeInset))
                {
                    problems.Add(
                        $"tile {index} footprint is invalid or too small: {footprintError}");
                }
            }

            var topologyValidation = mapRoot.Topology.ValidateTopology();
            for (var index = 0; index < topologyValidation.Issues.Count; index++)
            {
                problems.Add(topologyValidation.Issues[index].Message);
            }

            if (mapRoot.PlayerSpawnAnchors.Count != PlayerSlotRules.Count ||
                mapRoot.PlayerSpawnAnchors.Any(anchor => anchor == null))
            {
                problems.Add("expected four player spawn anchors");
            }

            for (var slot = 0; slot < PlayerSlotRules.Count; slot++)
            {
                var start = mapRoot.GetStartTile(slot);
                var anchor = mapRoot.GetSpawnAnchor(slot);
                if (start == null || anchor == null ||
                    !start.ContainsHorizontalPoint(anchor.position) ||
                    !CanReachEveryTile(mapRoot.Topology, start))
                {
                    problems.Add($"player {slot + 1} start assignment is invalid or disconnected");
                }
            }

            var reciprocalEdges = new HashSet<(BoardTile Source, BoardTile Destination)>(
                gates.Where(gate => gate != null)
                    .Select(gate => (gate.Source, gate.Destination)));
            var hasTwoWay = gates.Any(gate =>
                gate != null && reciprocalEdges.Contains((gate.Destination, gate.Source)));
            var hasOneWay = gates.Any(gate =>
                gate != null && !reciprocalEdges.Contains((gate.Destination, gate.Source)));
            if (!hasTwoWay || !hasOneWay)
            {
                problems.Add("expected both bidirectional trails and a directed shortcut");
            }

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Forest graybox validation failed:\n- " + string.Join("\n- ", problems));
            }
        }

        private static bool CanReachEveryTile(BoardTopology topology, BoardTile start)
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

        private static GameObject[] LoadTilePrefabs()
        {
            var paths = new[]
            {
                TilePrefabFolder + "/TileNormalA.prefab",
                TilePrefabFolder + "/TileNormalB.prefab",
                TilePrefabFolder + "/TileStart.prefab",
                TilePrefabFolder + "/TileRespawn.prefab"
            };
            var prefabs = new GameObject[paths.Length];
            for (var index = 0; index < paths.Length; index++)
            {
                prefabs[index] = AssetDatabase.LoadAssetAtPath<GameObject>(paths[index]);
                if (prefabs[index] == null ||
                    prefabs[index].GetComponent<BoardTile>() == null)
                {
                    throw new InvalidOperationException(
                        $"Required production tile prefab is missing or invalid: '{paths[index]}'.");
                }
            }

            return prefabs;
        }

        private static BoardMapDefinition GetOrCreateDefinition()
        {
            var definition = AssetDatabase.LoadAssetAtPath<BoardMapDefinition>(DefinitionPath);
            if (definition != null)
            {
                return definition;
            }

            definition = ScriptableObject.CreateInstance<BoardMapDefinition>();
            definition.Configure(MapId, "Forest Graybox", 1, null);
            AssetDatabase.CreateAsset(definition, DefinitionPath);
            return definition;
        }

        private static void UpdateCatalog(BoardMapDefinition definition)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<BoardMapCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<BoardMapCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var definitions = catalog.Maps
                .Where(candidate => candidate != null && candidate.MapId != MapId)
                .ToList();
            definitions.Insert(0, definition);
            catalog.Configure(definitions.ToArray());
            EditorUtility.SetDirty(catalog);
        }

        private static ConnectionSpec[] BuildConnections()
        {
            var connections = new List<ConnectionSpec>();
            for (var index = 0; index < 20; index++)
            {
                connections.Add(new ConnectionSpec(index, (index + 1) % 20, true));
            }

            AddPath(connections, 3, new[] { 20, 21, 22, 23, 24, 25 }, 10);
            AddPath(connections, 7, new[] { 26, 27, 28, 29, 30, 31 }, 14);
            AddPath(connections, 17, new[] { 32, 33, 34, 35, 36, 37, 38, 39 }, 5);
            connections.Add(new ConnectionSpec(29, 23, false));
            return connections.ToArray();
        }

        private static void AddPath(
            ICollection<ConnectionSpec> connections,
            int start,
            IReadOnlyList<int> interior,
            int destination)
        {
            var previous = start;
            for (var index = 0; index < interior.Count; index++)
            {
                connections.Add(new ConnectionSpec(previous, interior[index], true));
                previous = interior[index];
            }

            connections.Add(new ConnectionSpec(previous, destination, true));
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static void EnsureAssetFolder(string path)
        {
            var segments = path.Split('/');
            var current = segments[0];
            for (var index = 1; index < segments.Length; index++)
            {
                var next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }

                current = next;
            }
        }

        private readonly struct ConnectionSpec
        {
            public ConnectionSpec(int source, int destination, bool bidirectional)
            {
                Source = source;
                Destination = destination;
                Bidirectional = bidirectional;
            }

            public int Source { get; }
            public int Destination { get; }
            public bool Bidirectional { get; }
        }
    }
}
