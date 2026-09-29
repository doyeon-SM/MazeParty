using System;
using System.Collections.Generic;
using System.Linq;
using MazeParty.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MazeParty.Editor
{
    /// <summary>
    /// Bakes a flat Polytope Studio terrain and paints the authored board route as
    /// dirt. Only Environment/Generated Ground is owned by this tool; sibling
    /// environment objects remain available for manually placed forest props.
    /// </summary>
    internal static class ForestGroundAuthoring
    {
        internal const string GeneratedGroundRootName = "Generated Ground";
        internal const string TerrainObjectName = "Forest Ground";
        internal const string TerrainDataPath =
            "Assets/MazeParty/Art/Board/Forest/ForestGroundTerrain.asset";
        internal const string GrassLayerPath =
            "Assets/Ignore/Polytope Studio/Lowpoly_Demos/Environment_Free/" +
            "Helpers/Ground_Layer_02.terrainlayer";
        internal const string DirtLayerPath =
            "Assets/Ignore/Polytope Studio/Lowpoly_Demos/Environment_Free/" +
            "Helpers/Ground_Layer_01.terrainlayer";

        private const string ArtFolder = "Assets/MazeParty/Art/Board/Forest";
        private const float GroundHeight = -0.12f;
        private const float GroundMargin = 10f;
        private const float MinimumGroundWidth = 80f;
        private const float MinimumGroundDepth = 70f;
        private const float DirtCoreRadius = 1.35f;
        private const float DirtFeatherRadius = 3f;
        private const int HeightmapResolution = 33;
        private const int AlphamapResolution = 256;

        [MenuItem("MazeParty/Board/Create or Refresh Forest Ground")]
        internal static void CreateOrRefreshFromMenu()
        {
            try
            {
                var prefab = CreateOrRefresh();
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
                Debug.Log(
                    "Forest ground refreshed from Polytope Studio terrain layers. " +
                    "Only Environment/Generated Ground was replaced; manual " +
                    "Environment siblings were preserved.",
                    prefab);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        internal static GameObject CreateOrRefresh()
        {
            EnsureAssetFolder(ArtFolder);
            var grassLayer = LoadRequiredLayer(GrassLayerPath);
            var dirtLayer = LoadRequiredLayer(DirtLayerPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ForestGrayboxMapAuthoring.PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Create the Forest Graybox map before authoring its ground.");
            }

            var root = PrefabUtility.LoadPrefabContents(
                ForestGrayboxMapAuthoring.PrefabPath);
            try
            {
                var mapRoot = root.GetComponent<BoardMapRoot>();
                if (mapRoot == null || !mapRoot.HasAuthoringRoots ||
                    mapRoot.EnvironmentRoot == null)
                {
                    throw new InvalidOperationException(
                        "Forest Graybox is missing its authored map roots.");
                }

                mapRoot.Topology.RebuildIndex();
                var tiles = mapRoot.Topology.Tiles
                    .Where(tile => tile != null)
                    .ToArray();
                if (tiles.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Forest Graybox has no tiles to bound the ground.");
                }

                var environment = mapRoot.EnvironmentRoot;
                var tileCenters = tiles
                    .Select(tile => environment.InverseTransformPoint(tile.WorldCenter))
                    .ToArray();
                var segments = BuildUniqueSegments(mapRoot.Topology, environment);
                if (segments.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Forest Graybox has no route connections to paint.");
                }

                var groundBounds = CalculateGroundBounds(tileCenters);
                var terrainData = GetOrCreateTerrainData();
                ConfigureTerrainData(
                    terrainData,
                    groundBounds,
                    segments,
                    grassLayer,
                    dirtLayer);
                ReplaceGeneratedGround(environment, terrainData, groundBounds);

                var savedPrefab = PrefabUtility.SaveAsPrefabAsset(
                    root,
                    ForestGrayboxMapAuthoring.PrefabPath);
                if (savedPrefab == null)
                {
                    throw new InvalidOperationException(
                        "Could not save the Forest Graybox ground to its prefab.");
                }

                EditorUtility.SetDirty(terrainData);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(
                    ForestGrayboxMapAuthoring.PrefabPath,
                    ImportAssetOptions.ForceUpdate);
                return AssetDatabase.LoadAssetAtPath<GameObject>(
                    ForestGrayboxMapAuthoring.PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static TerrainLayer LoadRequiredLayer(string path)
        {
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                throw new InvalidOperationException(
                    $"Required Polytope Studio terrain layer is missing: '{path}'.");
            }

            return layer;
        }

        private static TerrainData GetOrCreateTerrainData()
        {
            var terrainData = AssetDatabase.LoadAssetAtPath<TerrainData>(
                TerrainDataPath);
            if (terrainData != null)
            {
                return terrainData;
            }

            terrainData = new TerrainData
            {
                name = "Forest Ground Terrain"
            };
            AssetDatabase.CreateAsset(terrainData, TerrainDataPath);
            return terrainData;
        }

        private static void ConfigureTerrainData(
            TerrainData terrainData,
            GroundBounds bounds,
            IReadOnlyList<PathSegment> segments,
            TerrainLayer grassLayer,
            TerrainLayer dirtLayer)
        {
            terrainData.heightmapResolution = HeightmapResolution;
            terrainData.size = new Vector3(bounds.Width, 1f, bounds.Depth);
            terrainData.SetHeights(
                0,
                0,
                new float[HeightmapResolution, HeightmapResolution]);
            terrainData.terrainLayers = new[] { grassLayer, dirtLayer };
            terrainData.alphamapResolution = AlphamapResolution;
            terrainData.baseMapResolution = AlphamapResolution;

            var weights = new float[
                AlphamapResolution,
                AlphamapResolution,
                2];
            for (var z = 0; z < AlphamapResolution; z++)
            {
                var normalizedZ = z / (AlphamapResolution - 1f);
                var localZ = bounds.MinZ + normalizedZ * bounds.Depth;
                for (var x = 0; x < AlphamapResolution; x++)
                {
                    var normalizedX = x / (AlphamapResolution - 1f);
                    var localX = bounds.MinX + normalizedX * bounds.Width;
                    var point = new Vector2(localX, localZ);
                    var distance = FindNearestPathDistance(point, segments);
                    var edgeNoise =
                        (Mathf.Sin(localX * 0.71f) +
                         Mathf.Cos(localZ * 0.63f) +
                         Mathf.Sin((localX + localZ) * 0.37f)) * 0.09f;
                    var dirt = 1f - Mathf.InverseLerp(
                        DirtCoreRadius,
                        DirtFeatherRadius,
                        distance + edgeNoise);
                    dirt = Mathf.SmoothStep(0f, 1f, dirt);
                    weights[z, x, 0] = 1f - dirt;
                    weights[z, x, 1] = dirt;
                }
            }

            terrainData.SetAlphamaps(0, 0, weights);
        }

        private static List<PathSegment> BuildUniqueSegments(
            BoardTopology topology,
            Transform environment)
        {
            var segments = new List<PathSegment>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var gates = topology.Gates;
            for (var index = 0; index < gates.Count; index++)
            {
                var gate = gates[index];
                if (gate == null || gate.Source == null || gate.Destination == null)
                {
                    continue;
                }

                var sourceCoordinate = gate.Source.Coordinate;
                var destinationCoordinate = gate.Destination.Coordinate;
                var sourceKey = sourceCoordinate.x + "," + sourceCoordinate.y;
                var destinationKey =
                    destinationCoordinate.x + "," + destinationCoordinate.y;
                var key = string.CompareOrdinal(sourceKey, destinationKey) < 0
                    ? sourceKey + ":" + destinationKey
                    : destinationKey + ":" + sourceKey;
                if (!visited.Add(key))
                {
                    continue;
                }

                var source = environment.InverseTransformPoint(
                    gate.Source.WorldCenter);
                var destination = environment.InverseTransformPoint(
                    gate.Destination.WorldCenter);
                segments.Add(new PathSegment(
                    new Vector2(source.x, source.z),
                    new Vector2(destination.x, destination.z)));
            }

            return segments;
        }

        private static GroundBounds CalculateGroundBounds(
            IReadOnlyList<Vector3> tileCenters)
        {
            var minX = tileCenters.Min(position => position.x);
            var maxX = tileCenters.Max(position => position.x);
            var minZ = tileCenters.Min(position => position.z);
            var maxZ = tileCenters.Max(position => position.z);
            var centerX = (minX + maxX) * 0.5f;
            var centerZ = (minZ + maxZ) * 0.5f;
            var width = Mathf.Max(
                MinimumGroundWidth,
                Mathf.Ceil(maxX - minX + GroundMargin * 2f));
            var depth = Mathf.Max(
                MinimumGroundDepth,
                Mathf.Ceil(maxZ - minZ + GroundMargin * 2f));
            return new GroundBounds(
                centerX - width * 0.5f,
                centerZ - depth * 0.5f,
                width,
                depth);
        }

        private static float FindNearestPathDistance(
            Vector2 point,
            IReadOnlyList<PathSegment> segments)
        {
            var nearestSquared = float.PositiveInfinity;
            for (var index = 0; index < segments.Count; index++)
            {
                var segment = segments[index];
                var direction = segment.End - segment.Start;
                var lengthSquared = direction.sqrMagnitude;
                var t = lengthSquared <= 0.0001f
                    ? 0f
                    : Mathf.Clamp01(
                        Vector2.Dot(point - segment.Start, direction) /
                        lengthSquared);
                var closest = segment.Start + direction * t;
                nearestSquared = Mathf.Min(
                    nearestSquared,
                    (point - closest).sqrMagnitude);
            }

            return Mathf.Sqrt(nearestSquared);
        }

        private static void ReplaceGeneratedGround(
            Transform environment,
            TerrainData terrainData,
            GroundBounds bounds)
        {
            var existing = environment.Find(GeneratedGroundRootName);
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }

            var generatedRoot = new GameObject(GeneratedGroundRootName);
            generatedRoot.transform.SetParent(environment, false);
            generatedRoot.transform.SetAsFirstSibling();

            var terrainObject = new GameObject(TerrainObjectName);
            terrainObject.transform.SetParent(generatedRoot.transform, false);
            terrainObject.transform.localPosition = new Vector3(
                bounds.MinX,
                GroundHeight,
                bounds.MinZ);
            var terrain = terrainObject.AddComponent<Terrain>();
            terrain.terrainData = terrainData;
            RenderPipelineAsset renderPipeline =
                GraphicsSettings.currentRenderPipeline;
            if (renderPipeline == null || renderPipeline.defaultTerrainMaterial == null)
            {
                throw new InvalidOperationException(
                    "The active render pipeline does not provide a default terrain material.");
            }

            terrain.materialTemplate = renderPipeline.defaultTerrainMaterial;
            terrain.drawInstanced = true;
            terrain.drawTreesAndFoliage = false;
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 1000f;
            terrain.allowAutoConnect = false;
            terrain.shadowCastingMode = ShadowCastingMode.Off;
            terrain.reflectionProbeUsage = ReflectionProbeUsage.Off;
            terrain.Flush();
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

        private readonly struct PathSegment
        {
            public PathSegment(Vector2 start, Vector2 end)
            {
                Start = start;
                End = end;
            }

            public Vector2 Start { get; }
            public Vector2 End { get; }
        }

        private readonly struct GroundBounds
        {
            public GroundBounds(float minX, float minZ, float width, float depth)
            {
                MinX = minX;
                MinZ = minZ;
                Width = width;
                Depth = depth;
            }

            public float MinX { get; }
            public float MinZ { get; }
            public float Width { get; }
            public float Depth { get; }
        }
    }
}
