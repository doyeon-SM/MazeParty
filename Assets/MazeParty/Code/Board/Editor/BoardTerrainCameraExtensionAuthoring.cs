using System;
using System.Linq;
using MazeParty.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    /// <summary>Adds authored terrain around the untouched sculpted map ground.</summary>
    public static class BoardTerrainCameraExtensionAuthoring
    {
        private const string BoardScene = "Assets/MazeParty/Scenes/Board/Board.unity";
        private const string ExtensionName = "Camera Ground Extension";
        private static readonly float[] Aspects = { 4f / 3f, 16f / 10f, 16f / 9f, 21f / 9f, 32f / 9f };

        [MenuItem("MazeParty/Board/Extend Terrain to Top View")]
        public static void Apply()
        {
            var scene = SceneManager.GetSceneByPath(BoardScene);
            var opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(BoardScene, OpenSceneMode.Additive);
            try
            {
                var framing = scene.GetRootGameObjects().SelectMany(root =>
                    root.GetComponentsInChildren<BoardCameraFramingAnchor>(true)).Single();
                Extend("Forest", framing);
                Extend("Maze", framing);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void Extend(string mapName, BoardCameraFramingAnchor framing)
        {
            var path = "Assets/MazeParty/Resources/MazeParty/Board/Maps/" + mapName + "GrayboxMapRoot.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var map = root.GetComponent<BoardMapRoot>();
                // Existing outer ground is authored content too; repeat setup preserves it.
                if (map.EnvironmentRoot.Find(ExtensionName) != null) return;
                var core = map.EnvironmentRoot.GetComponentsInChildren<Terrain>(true).Single();
                if (core.transform.lossyScale != Vector3.one || core.transform.rotation != Quaternion.identity)
                    throw new InvalidOperationException("Terrain extension requires unscaled, unrotated map ground.");
                var data = core.terrainData;
                var origin = core.transform.position;
                var size = data.size;
                var extent = new Bounds(origin + size * .5f, size);
                map.Topology.RebuildIndex();
                var maxHeight = data.GetHeights(0, 0, data.heightmapResolution,
                    data.heightmapResolution).Cast<float>().Max() * size.y;
                var eyeHeight = maxHeight + PlayerAvatarVisual.StandingControllerHeight * .5f +
                    PlayerAvatarVisual.StandingEyeHeight + .5f;
                foreach (var aspect in Aspects)
                {
                    var pose = framing.EvaluateForMap(map, aspect);
                    IncludeView(ref extent, pose.Position, pose, aspect, origin.y);
                    foreach (var tile in map.Topology.Tiles.Where(tile => tile != null))
                    {
                        // Player-wide view follows the eye; include all legal room corners.
                        foreach (var x in new[] { -1f, 1f })
                        foreach (var z in new[] { -1f, 1f })
                        {
                            var eye = tile.WorldCenter + new Vector3(x * BoardTile.HalfRoomSize,
                                eyeHeight, z * BoardTile.HalfRoomSize);
                            IncludeView(ref extent, eye + Vector3.up * pose.Height, pose, aspect, origin.y);
                        }
                    }
                }
                const float margin = 8f;
                var xs = new[] { Mathf.Floor((extent.min.x - margin) / 5f) * 5f,
                    origin.x, origin.x + size.x, Mathf.Ceil((extent.max.x + margin) / 5f) * 5f };
                var zs = new[] { Mathf.Floor((extent.min.z - margin) / 5f) * 5f,
                    origin.z, origin.z + size.z, Mathf.Ceil((extent.max.z + margin) / 5f) * 5f };
                var parent = new GameObject(ExtensionName);
                parent.transform.SetParent(map.EnvironmentRoot, false);
                var folder = "Assets/MazeParty/Art/Board/" + mapName + "/CameraGround";
                if (!AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.CreateFolder("Assets/MazeParty/Art/Board/" + mapName, "CameraGround");
                var alpha = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
                for (var z = 0; z < 3; z++)
                for (var x = 0; x < 3; x++)
                {
                    if (x == 1 && z == 1) continue;
                    var name = mapName + "Ground_" + x + "_" + z;
                    var tileData = CreateOuterData(data, alpha, origin,
                        new Vector3(xs[x], origin.y, zs[z]),
                        new Vector3(xs[x + 1] - xs[x], size.y, zs[z + 1] - zs[z]), name);
                    var assetPath = folder + "/" + name + ".asset";
                    if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                        throw new InvalidOperationException("Refusing to overwrite existing terrain: " + assetPath);
                    AssetDatabase.CreateAsset(tileData, assetPath);
                    var terrainObject = Terrain.CreateTerrainGameObject(tileData);
                    terrainObject.name = name;
                    terrainObject.layer = core.gameObject.layer;
                    terrainObject.transform.SetParent(parent.transform, false);
                    terrainObject.transform.position = new Vector3(xs[x], origin.y, zs[z]);
                    var terrain = terrainObject.GetComponent<Terrain>();
                    terrain.materialTemplate = core.materialTemplate;
                    terrain.drawInstanced = core.drawInstanced;
                    terrain.drawTreesAndFoliage = false;
                    terrain.heightmapPixelError = 1f;
                    terrain.basemapDistance = core.basemapDistance;
                    terrain.shadowCastingMode = core.shadowCastingMode;
                    terrain.allowAutoConnect = false;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log(mapName + " terrain extended to X " + xs[0] + ".." + xs[3] +
                    ", Z " + zs[0] + ".." + zs[3] + "; original terrain data preserved.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void IncludeView(ref Bounds bounds, Vector3 camera,
            BoardCameraPose pose, float aspect, float groundY)
        {
            var halfHeight = (camera.y - groundY) * Mathf.Tan(pose.FieldOfView * .5f * Mathf.Deg2Rad);
            var right = pose.Rotation * Vector3.right;
            var up = pose.Rotation * Vector3.up;
            foreach (var x in new[] { -1f, 1f })
            foreach (var y in new[] { -1f, 1f })
                bounds.Encapsulate(new Vector3(camera.x, groundY, camera.z) +
                    right * (x * halfHeight * aspect) + up * (y * halfHeight));
        }

        private static TerrainData CreateOuterData(TerrainData core, float[,,] alpha,
            Vector3 coreOrigin, Vector3 origin, Vector3 size, string name)
        {
            var result = new TerrainData
            {
                name = name,
                heightmapResolution = core.heightmapResolution,
                alphamapResolution = 128,
                baseMapResolution = 128,
                size = size,
                terrainLayers = core.terrainLayers
            };
            var resolution = result.heightmapResolution;
            var heights = new float[resolution, resolution];
            for (var z = 0; z < resolution; z++)
            for (var x = 0; x < resolution; x++)
            {
                var offset = origin - coreOrigin + new Vector3(x * size.x / (resolution - 1f),
                    0f, z * size.z / (resolution - 1f));
                var u = Mathf.Clamp01(offset.x / core.size.x);
                var v = Mathf.Clamp01(offset.z / core.size.z);
                var distance = Vector2.Distance(new Vector2(offset.x, offset.z),
                    new Vector2(u * core.size.x, v * core.size.z));
                heights[z, x] = core.GetInterpolatedHeight(u, v) / core.size.y *
                    (1f - Mathf.SmoothStep(0f, 1f, distance / 20f));
            }
            result.SetHeights(0, 0, heights);
            var weights = new float[128, 128, core.alphamapLayers];
            for (var z = 0; z < 128; z++)
            for (var x = 0; x < 128; x++)
            {
                var offset = origin - coreOrigin + new Vector3(x * size.x / 127f, 0f, z * size.z / 127f);
                var u = Mathf.Clamp01(offset.x / core.size.x);
                var v = Mathf.Clamp01(offset.z / core.size.z);
                var px = u * (core.alphamapWidth - 1);
                var pz = v * (core.alphamapHeight - 1);
                var x0 = Mathf.FloorToInt(px);
                var z0 = Mathf.FloorToInt(pz);
                var x1 = Mathf.Min(x0 + 1, core.alphamapWidth - 1);
                var z1 = Mathf.Min(z0 + 1, core.alphamapHeight - 1);
                var distance = Vector2.Distance(new Vector2(offset.x, offset.z),
                    new Vector2(u * core.size.x, v * core.size.z));
                var fade = Mathf.SmoothStep(0f, 1f, distance / 8f);
                for (var layer = 0; layer < core.alphamapLayers; layer++)
                {
                    var weight = Mathf.Lerp(Mathf.Lerp(alpha[z0, x0, layer], alpha[z0, x1, layer], px - x0),
                        Mathf.Lerp(alpha[z1, x0, layer], alpha[z1, x1, layer], px - x0), pz - z0);
                    weights[z, x, layer] = Mathf.Lerp(weight, layer == 0 ? 1f : 0f, fade);
                }
            }
            result.SetAlphamaps(0, 0, weights);
            return result;
        }
    }
}
