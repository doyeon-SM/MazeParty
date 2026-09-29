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
        internal const int ContentVersion = 3;
        internal const string MapFolder =
            "Assets/MazeParty/Resources/MazeParty/Board/Maps";
        internal const string DefinitionPath = MapFolder + "/ForestGrayboxMap.asset";
        internal const string CatalogPath = MapFolder + "/BoardMapCatalog.asset";
        internal const string PrefabPath = MapFolder + "/ForestGrayboxMapRoot.prefab";
        internal const int TileCount = 42;

        private const string TilePrefabFolder = "Assets/MazeParty/Prefabs/Board/World";
        private const float GateWidth = 2.5f;
        private const float SpawnHeight = 1f;
        private const float RequiredSafeInset = 0.52f;

        private static readonly Vector3[] TilePositions =
        {
            new Vector3(-28f, 0f, -12f),
            new Vector3(-22f, 0f, -18f),
            new Vector3(-14.5f, 0f, -27.75f),
            new Vector3(-14f, 0f, -21f),
            new Vector3(-6f, 0f, -22f),
            new Vector3(2f, 0f, -20f),
            new Vector3(10f, 0f, -22f),
            new Vector3(18f, 0f, -18f),
            new Vector3(24.75f, 0f, -13.75f),
            new Vector3(27f, 0f, -6.75f),
            new Vector3(29.5f, 0f, -1.75f),
            new Vector3(28f, 0f, 7f),
            new Vector3(24f, 0f, 13f),
            new Vector3(22.5f, 0f, 25f),
            new Vector3(18.5f, 0f, 19f),
            new Vector3(11f, 0f, 21f),
            new Vector3(2.5f, 0f, 22.25f),
            new Vector3(-7f, 0f, 23f),
            new Vector3(-15f, 0f, 20f),
            new Vector3(-22f, 0f, 15f),
            new Vector3(-27f, 0f, 8f),
            new Vector3(-30f, 0f, 0f),
            new Vector3(-29f, 0f, -7f),

            new Vector3(-9f, 0f, -14f),
            new Vector3(-5f, 0f, -7f),
            new Vector3(0f, 0f, -2f),
            new Vector3(6.75f, 0f, 3f),
            new Vector3(12f, 0f, 6f),
            new Vector3(18f, 0f, 10f),
            new Vector3(18.75f, 0f, -8.5f),
            new Vector3(14.25f, 0f, -4.75f),
            new Vector3(10.25f, 0f, -1.75f),
            new Vector3(3f, 0f, 7.25f),
            new Vector3(-1f, 0f, 12f),
            new Vector3(-4f, 0f, 17f),
            new Vector3(-21f, 0f, 8f),
            new Vector3(-15f, 0f, 6f),
            new Vector3(-11f, 0f, 1f),
            new Vector3(-9.5f, 0f, -3f),
            new Vector3(-1f, 0f, -10f),
            new Vector3(4.5f, 0f, -12.25f),
            new Vector3(11f, 0f, -13.75f)
        };

        private static readonly int[] TilePrefabIndices =
        {
            0, 1, 3, 2, 1, 0, 1, 0, 1, 0, 0, 1, 0, 3,
            1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0,
            1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 0, 1, 1, 0
        };

        private static readonly Vector2[][] TileFootprints =
        {
            new[] { new Vector2(3.6f, 3.6f), new Vector2(-2.099999f, 4.350001f), new Vector2(-3.1f, -2.849999f), new Vector2(3.599999f, -3.349999f) },
            new[] { new Vector2(-0.75f, 4.6f), new Vector2(-6.673803f, 0.862461f), new Vector2(-2.116027f, -2.912461f), new Vector2(4.866026f, -4.662462f), new Vector2(4.923803f, 0.862461f) },
            new[] { new Vector2(-2.25f, 4.35f), new Vector2(-3.617691f, -2.299999f), new Vector2(4.117691f, -3.550001f), new Vector2(3.867691f, 2.949999f) },
            new[] { new Vector2(-2.75f, 3.6f), new Vector2(-2.867691f, -2.049999f), new Vector2(3.367691f, -3.550001f), new Vector2(3.367691f, 2.699999f) },
            new[] { new Vector2(3.1f, 3.6f), new Vector2(-4.349999f, 3.6f), new Vector2(-4.35f, -2.599998f), new Vector2(3.849999f, -2.6f) },
            new[] { new Vector2(4.75f, 1.85f), new Vector2(-4.423803f, 1.612461f), new Vector2(-3.616027f, -4.66246f), new Vector2(3.866026f, -4.162462f) },
            new[] { new Vector2(-3f, 3.6f), new Vector2(-3.617691f, -2.049999f), new Vector2(5.117691f, -1.800001f), new Vector2(1.117691f, 4.449999f) },
            new[] { new Vector2(-0.15f, 4.099999f), new Vector2(-6.599999f, 0.849999f), new Vector2(-2.6f, -5.849998f), new Vector2(4.599998f, -0.85f) },
            new[] { new Vector2(-1.75f, 4.85f), new Vector2(-6.673803f, 0.112461f), new Vector2(-1.866028f, -4.91246f), new Vector2(2.366026f, -1.912462f), new Vector2(5.673803f, 3.36246f) },
            new[] { new Vector2(-1.75f, 3.35f), new Vector2(-3.867691f, -1.8f), new Vector2(3.367691f, -3.3f), new Vector2(6.117691f, 2.45f) },
            new[] { new Vector2(-5f, 3.85f), new Vector2(-4.367691f, -1.3f), new Vector2(3.617691f, -2.050001f), new Vector2(3.367691f, 5.199999f) },
            new[] { new Vector2(2.1f, 3.349999f), new Vector2(-5.35f, 1.349999f), new Vector2(-3.6f, -4.849999f), new Vector2(4.849998f, -3.35f) },
            new[] { new Vector2(-0.25f, 5.6f), new Vector2(-4.923803f, 0.862461f), new Vector2(-1.616028f, -4.41246f), new Vector2(6.116026f, -2.412461f), new Vector2(4.173803f, 1.36246f) },
            new[] { new Vector2(-2f, 3.85f), new Vector2(-5.867691f, -1.299999f), new Vector2(0.867691f, -6.050001f), new Vector2(4.617691f, -0.800001f) },
            new[] { new Vector2(-2f, 4.6f), new Vector2(-4.617691f, -2.049999f), new Vector2(0.117691f, -4.800001f), new Vector2(4.867691f, -0.300001f) },
            new[] { new Vector2(5.099998f, 2.6f), new Vector2(-4.099999f, 4.1f), new Vector2(-4.6f, -2.349998f), new Vector2(2.349998f, -3.85f) },
            new[] { new Vector2(4.25f, 2.85f), new Vector2(-2.923803f, 3.112461f), new Vector2(-3.616027f, -2.91246f), new Vector2(3.616026f, -3.412462f) },
            new[] { new Vector2(-4.25f, 2.1f), new Vector2(-3.117691f, -4.049999f), new Vector2(5.617691f, -3.300001f), new Vector2(6.117691f, 2.449999f) },
            new[] { new Vector2(3.349999f, 5.1f), new Vector2(-5.85f, 0.1f), new Vector2(-1.35f, -4.099998f), new Vector2(4.599998f, -1.6f) },
            new[] { new Vector2(0.75f, 4.85f), new Vector2(-5.173803f, -1.137539f), new Vector2(-0.116028f, -3.66246f), new Vector2(5.423803f, 0.862461f) },
            new[] { new Vector2(-0.25f, 5.6f), new Vector2(-4.617691f, -3.8f), new Vector2(1.617691f, -4.550001f), new Vector2(4.867691f, 3.199999f) },
            new[] { new Vector2(4.6f, 3.1f), new Vector2(-1.849998f, 4.099999f), new Vector2(-3.599998f, -3.599999f), new Vector2(3.849998f, -2.35f) },
            new[] { new Vector2(2.75f, 4.6f), new Vector2(-4.423805f, 3.362461f), new Vector2(-3.116028f, -3.41246f), new Vector2(3.616026f, -0.662461f) },
            new[] { new Vector2(-3.75f, 1.1f), new Vector2(-1.367691f, -4.049999f), new Vector2(5.867691f, -4.299999f), new Vector2(4.117691f, 2.200001f), new Vector2(-0.632309f, 5.700001f) },
            new[] { new Vector2(4.849999f, 0.599999f), new Vector2(-0.099999f, 4.349999f), new Vector2(-4.35f, -0.849999f), new Vector2(0.349999f, -4.35f) },
            new[] { new Vector2(0.75f, 4.1f), new Vector2(-4.923803f, -0.387539f), new Vector2(-0.116027f, -4.16246f), new Vector2(5.616026f, 0.337539f) },
            new[] { new Vector2(0.75f, 4.6f), new Vector2(-5.867691f, -0.55f), new Vector2(-1.132309f, -4.550001f), new Vector2(4.867691f, -0.550001f) },
            new[] { new Vector2(1.599999f, 4.849999f), new Vector2(-4.349999f, 1.849999f), new Vector2(0.15f, -3.599999f), new Vector2(5.099998f, -0.6f) },
            new[] { new Vector2(0.75f, 3.85f), new Vector2(-4.173803f, 1.112461f), new Vector2(-0.616028f, -4.41246f), new Vector2(4.423803f, -1.63754f) },
            new[] { new Vector2(0f, 3.6f), new Vector2(-4.867691f, -1.3f), new Vector2(-0.882309f, -4.8f), new Vector2(3.867691f, -0.05f) },
            new[] { new Vector2(1.1f, 4.099999f), new Vector2(-4.849999f, -0.650001f), new Vector2(-0.85f, -4.849999f), new Vector2(4.349998f, 0.15f) },
            new[] { new Vector2(1.75f, 4.1f), new Vector2(-4.423803f, 0.112461f), new Vector2(-1.116028f, -3.41246f), new Vector2(4.923803f, 1.36246f) },
            new[] { new Vector2(1.25f, 3.6f), new Vector2(-5.617691f, -0.05f), new Vector2(-2.632309f, -4.550001f), new Vector2(4.117691f, 0.699999f) },
            new[] { new Vector2(2.35f, 2.849999f), new Vector2(-4.849999f, 1.099999f), new Vector2(-1.6f, -4.599999f), new Vector2(5.099999f, -0.85f) },
            new[] { new Vector2(2.75f, 2.35f), new Vector2(-4.173803f, 1.612461f), new Vector2(-1.866027f, -3.66246f), new Vector2(4.923803f, -1.887539f) },
            new[] { new Vector2(-0.5f, 2.6f), new Vector2(-3.367691f, -4.3f), new Vector2(2.117691f, -4.800001f), new Vector2(4.617691f, 1.949999f) },
            new[] { new Vector2(5.099999f, -0.150001f), new Vector2(-0.85f, 3.849999f), new Vector2(-3.6f, -3.099999f), new Vector2(-0.150002f, -3.85f) },
            new[] { new Vector2(1f, 4.6f), new Vector2(-3.923803f, 1.112461f), new Vector2(-2.116028f, -3.412461f), new Vector2(3.366026f, 1.087538f) },
            new[] { new Vector2(2f, 4.6f), new Vector2(-3.423803f, 0.112461f), new Vector2(-0.366028f, -4.91246f), new Vector2(4.116026f, 0.337539f) },
            new[] { new Vector2(1.25f, 3.35f), new Vector2(-3.173803f, -1.637539f), new Vector2(1.883973f, -4.16246f), new Vector2(5.173803f, 1.61246f) },
            new[] { new Vector2(0f, 3.85f), new Vector2(-3.173803f, -1.887539f), new Vector2(2.883973f, -3.41246f), new Vector2(5.673803f, 2.11246f) },
            new[] { new Vector2(-0.75f, 3.35f), new Vector2(-3.367691f, -1.8f), new Vector2(0.117691f, -3.049999f), new Vector2(6.367691f, -0.05f) }
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

                definition.Configure(
                    MapId,
                    "Forest Graybox",
                    ContentVersion,
                    savedPrefab);
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
                var type = index == 3
                    ? BoardTileType.Start
                    : index == 2 || index == 13
                        ? BoardTileType.Respawn
                        : BoardTileType.Normal;
                var prefab = tilePrefabs[TilePrefabIndices[index]];
                var tileObject = PrefabUtility.InstantiatePrefab(prefab, tilesRoot) as GameObject;
                if (tileObject == null)
                {
                    throw new InvalidOperationException(
                        $"Could not instantiate tile prefab '{AssetDatabase.GetAssetPath(prefab)}'.");
                }

                tileObject.name = $"Forest Tile {index:00} - {type}";
                tileObject.transform.localPosition = TilePositions[index];
                tileObject.transform.localRotation = index == 0
                    ? Quaternion.Euler(0f, 57.83743f, 0f)
                    : Quaternion.identity;
                tileObject.transform.localScale = Vector3.one;

                var tile = tileObject.GetComponent<BoardTile>();
                tile.Configure(new Vector2Int(index, 0), type);
                var footprint = tileObject.GetComponent<BoardTileFootprint>() ??
                                tileObject.AddComponent<BoardTileFootprint>();
                footprint.Configure(TileFootprints[index]);
                tiles[index] = tile;
            }

            var gates = new List<BoardGate>(Connections.Length);
            for (var index = 0; index < Connections.Length; index++)
            {
                var connection = Connections[index];
                var connectionRoot = CreateChild(
                    connectionsRoot,
                    $"Connection {connection.Source:00} - " +
                    $"{connection.Destination:00} (One Way)");
                gates.Add(CreateGate(
                    connectionRoot.transform,
                    tiles[connection.Source],
                    tiles[connection.Destination]));
            }

            var playerStartTiles = new BoardTile[PlayerSlotRules.Count];
            playerStartTiles[1] = tiles[1];
            playerStartTiles[2] = tiles[22];
            playerStartTiles[3] = tiles[3];
            var anchors = CreateSpawnAnchors(root.transform, tiles[3], playerStartTiles);

            topology.Configure(tiles, gates.ToArray());
            mapRoot.Configure(
                definition,
                topology,
                tilesRoot,
                connectionsRoot,
                environment,
                tiles[3],
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
            if (TilePositions.Length != TileCount ||
                TilePrefabIndices.Length != TileCount ||
                TileFootprints.Length != TileCount ||
                TileCount < 32 ||
                TileCount > 48)
            {
                throw new InvalidOperationException(
                    "Forest graybox blueprint arrays must contain the same 32-48 tiles.");
            }

            for (var index = 0; index < TileCount; index++)
            {
                if (TilePrefabIndices[index] < 0 || TilePrefabIndices[index] > 3 ||
                    TileFootprints[index] == null ||
                    TileFootprints[index].Length < BoardTileFootprint.MinVertexCount ||
                    TileFootprints[index].Length > BoardTileFootprint.MaxVertexCount)
                {
                    throw new InvalidOperationException(
                        $"Forest graybox tile {index} blueprint is invalid.");
                }
            }

            var directedEdges = new HashSet<(int Source, int Destination)>();
            for (var index = 0; index < Connections.Length; index++)
            {
                var connection = Connections[index];
                if (connection.Source < 0 || connection.Source >= TileCount ||
                    connection.Destination < 0 || connection.Destination >= TileCount ||
                    connection.Source == connection.Destination ||
                    !directedEdges.Add((connection.Source, connection.Destination)) ||
                    directedEdges.Contains((connection.Destination, connection.Source)))
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

            if (tiles.Count(tile => tile != null && tile.TileType == BoardTileType.Respawn) != 2)
            {
                problems.Add("expected exactly two Respawn tiles");
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

            var directedEdges = new HashSet<(BoardTile Source, BoardTile Destination)>(
                gates.Where(gate => gate != null)
                    .Select(gate => (gate.Source, gate.Destination)));
            if (gates.Count != Connections.Length || gates.Any(gate =>
                    gate == null ||
                    directedEdges.Contains((gate.Destination, gate.Source))))
            {
                problems.Add("expected every forest trail to be one-way");
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
            definition.Configure(MapId, "Forest Graybox", ContentVersion, null);
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
            return new[]
            {
                new ConnectionSpec(0, 1),
                new ConnectionSpec(1, 2),
                new ConnectionSpec(2, 3),
                new ConnectionSpec(3, 4),
                new ConnectionSpec(4, 5),
                new ConnectionSpec(4, 23),
                new ConnectionSpec(5, 6),
                new ConnectionSpec(6, 7),
                new ConnectionSpec(7, 8),
                new ConnectionSpec(8, 9),
                new ConnectionSpec(8, 29),
                new ConnectionSpec(9, 10),
                new ConnectionSpec(10, 11),
                new ConnectionSpec(11, 12),
                new ConnectionSpec(12, 13),
                new ConnectionSpec(13, 14),
                new ConnectionSpec(14, 15),
                new ConnectionSpec(15, 16),
                new ConnectionSpec(16, 17),
                new ConnectionSpec(17, 18),
                new ConnectionSpec(18, 19),
                new ConnectionSpec(19, 20),
                new ConnectionSpec(20, 21),
                new ConnectionSpec(20, 35),
                new ConnectionSpec(21, 22),
                new ConnectionSpec(22, 0),
                new ConnectionSpec(23, 24),
                new ConnectionSpec(24, 25),
                new ConnectionSpec(24, 39),
                new ConnectionSpec(25, 26),
                new ConnectionSpec(26, 27),
                new ConnectionSpec(26, 32),
                new ConnectionSpec(27, 28),
                new ConnectionSpec(28, 12),
                new ConnectionSpec(29, 30),
                new ConnectionSpec(30, 31),
                new ConnectionSpec(31, 26),
                new ConnectionSpec(32, 33),
                new ConnectionSpec(33, 34),
                new ConnectionSpec(34, 17),
                new ConnectionSpec(35, 36),
                new ConnectionSpec(36, 37),
                new ConnectionSpec(37, 38),
                new ConnectionSpec(38, 24),
                new ConnectionSpec(39, 40),
                new ConnectionSpec(40, 41),
                new ConnectionSpec(41, 7)
            };
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
            public ConnectionSpec(int source, int destination)
            {
                Source = source;
                Destination = destination;
            }

            public int Source { get; }
            public int Destination { get; }
        }
    }
}
