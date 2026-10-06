using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MazeParty.Editor
{
    /// <summary>
    /// Installs collider-free Pandazole vegetation that is safe for the project's
    /// Universal Render Pipeline, then paints a deterministic forest layout.
    /// </summary>
    internal static class ForestFoliageAuthoring
    {
        internal const float TerrainVerticalSize = 10f;
        internal const string TreePrototypePath =
            "Assets/MazeParty/Prefabs/Board/World/Forest/Vegetation/Tree_24_Spring.prefab";
        internal const string Grass25PrototypePath =
            "Assets/MazeParty/Prefabs/Board/World/Forest/Vegetation/Grass_25.prefab";
        internal const string Grass24PrototypePath =
            "Assets/MazeParty/Prefabs/Board/World/Forest/Vegetation/Grass_24.prefab";
        internal const string Grass20PrototypePath =
            "Assets/MazeParty/Prefabs/Board/World/Forest/Vegetation/Grass_20.prefab";
        internal const string Grass19PrototypePath =
            "Assets/MazeParty/Prefabs/Board/World/Forest/Vegetation/Grass_19.prefab";

        private const string SourceRoot =
            "Assets/Ignore/Pandazole_Ultimate_Pack/Pandazole Nature Environment Pack";
        private const string SourceTreePath = SourceRoot + "/Prefabs/Tree_24_Spring.prefab";
        private const string SourceGrass25Path = SourceRoot + "/Prefabs/Grass_25.prefab";
        private const string SourceGrass24Path = SourceRoot + "/Prefabs/Grass_24.prefab";
        private const string SourceGrass20Path = SourceRoot + "/Prefabs/Grass_20.prefab";
        private const string SourceGrass19Path = SourceRoot + "/Prefabs/Grass_19.prefab";
        private const string SourceGrass25ModelPath = SourceRoot + "/Models/Grass_25.fbx";
        private const string SourceGrass24ModelPath = SourceRoot + "/Models/Grass_24.fbx";
        private const string SourceGrass20ModelPath = SourceRoot + "/Models/Grass_20.fbx";
        private const string SourceGrass19ModelPath = SourceRoot + "/Models/Grass_19.fbx";
        private const string SourceTexturePath = SourceRoot + "/Textures/PandaMat.png";
        private const string VegetationPrefabFolder =
            "Assets/MazeParty/Prefabs/Board/World/Forest/Vegetation";
        private const string FoliageArtFolder =
            "Assets/MazeParty/Art/Board/Forest/Foliage";
        private const string FoliageMaterialPath =
            FoliageArtFolder + "/PandazoleNatureURP.mat";
        private const int DetailResolution = 256;
        private const int DetailResolutionPerPatch = 16;
        private const int TreeCount = 48;
        private const float TreeEdgeMargin = 1.5f;
        private const float TreeSpacing = 4.5f;

        private static readonly string[] SourceGrassModelPaths =
        {
            SourceGrass25ModelPath,
            SourceGrass24ModelPath,
            SourceGrass20ModelPath,
            SourceGrass19ModelPath
        };

        private static readonly string[] SourceGrassPrefabPaths =
        {
            SourceGrass25Path,
            SourceGrass24Path,
            SourceGrass20Path,
            SourceGrass19Path
        };

        private static readonly string[] GrassPrototypePaths =
        {
            Grass25PrototypePath,
            Grass24PrototypePath,
            Grass20PrototypePath,
            Grass19PrototypePath
        };

        private static readonly int[] GrassSeeds = { 25, 24, 20, 19 };

        [MenuItem("MazeParty/Board/Install Forest Foliage")]
        internal static void InstallFromMenu()
        {
            try
            {
                var terrainData = AssetDatabase.LoadAssetAtPath<TerrainData>(
                    ForestGroundAuthoring.TerrainDataPath);
                if (terrainData == null)
                {
                    throw new InvalidOperationException(
                        "Create the Forest ground before installing foliage.");
                }

                ResizeTerrainPreservingSurface(
                    terrainData,
                    new Vector3(
                        terrainData.size.x,
                        TerrainVerticalSize,
                        terrainData.size.z));
                ConfigureTerrainData(terrainData, true);
                ConfigureForestPrefab(terrainData);
                EditorUtility.SetDirty(terrainData);
                AssetDatabase.SaveAssets();
                Debug.Log(
                    "Installed collider-free Tree_24_Spring and a mixed " +
                    "Grass_25/24/20/19 detail layer. Existing terrain heights " +
                    "were preserved while the vertical range was expanded to 10 metres.",
                    terrainData);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        internal static void ResizeTerrainPreservingSurface(
            TerrainData terrainData,
            Vector3 newSize)
        {
            if (terrainData == null)
            {
                throw new ArgumentNullException(nameof(terrainData));
            }

            var resolution = terrainData.heightmapResolution;
            var heights = terrainData.GetHeights(0, 0, resolution, resolution);
            var existingTrees = terrainData.treeInstances;
            var oldHeight = terrainData.size.y;
            var heightScale = oldHeight > 0.0001f && newSize.y > 0.0001f
                ? oldHeight / newSize.y
                : 1f;

            if (!Mathf.Approximately(heightScale, 1f))
            {
                for (var z = 0; z < resolution; z++)
                {
                    for (var x = 0; x < resolution; x++)
                    {
                        heights[z, x] = Mathf.Clamp01(heights[z, x] * heightScale);
                    }
                }
            }

            terrainData.size = newSize;
            terrainData.SetHeights(0, 0, heights);
            if (existingTrees.Length > 0)
            {
                terrainData.SetTreeInstances(existingTrees, true);
            }

            EditorUtility.SetDirty(terrainData);
        }

        internal static void ConfigureTerrainData(
            TerrainData terrainData,
            bool repaint)
        {
            EnsureAssetFolder(VegetationPrefabFolder);
            EnsureAssetFolder(FoliageArtFolder);
            EnsurePaintDetailAssetsReadable();

            var material = CreateOrUpdateUrpMaterial();
            var treePrefab = CreateOrUpdateCollisionFreePrefab(
                SourceTreePath,
                TreePrototypePath,
                material,
                true);
            var grassPrefabs = new GameObject[SourceGrassPrefabPaths.Length];
            for (var index = 0; index < SourceGrassPrefabPaths.Length; index++)
            {
                grassPrefabs[index] = CreateOrUpdateCollisionFreePrefab(
                    SourceGrassPrefabPaths[index],
                    GrassPrototypePaths[index],
                    material,
                    false);
            }

            var treeSetupChanged =
                terrainData.treePrototypes.Length != 1 ||
                terrainData.treePrototypes[0].prefab != treePrefab;
            if (treeSetupChanged)
            {
                terrainData.treePrototypes = new[]
                {
                    new TreePrototype
                    {
                        prefab = treePrefab,
                        bendFactor = 0f,
                        navMeshLod = 0
                    }
                };
            }

            var detailSetupChanged = !DetailPrototypesMatch(
                terrainData.detailPrototypes,
                grassPrefabs);
            if (detailSetupChanged)
            {
                var prototypes = new DetailPrototype[grassPrefabs.Length];
                for (var index = 0; index < grassPrefabs.Length; index++)
                {
                    prototypes[index] = CreateGrassPrototype(
                        grassPrefabs[index],
                        GrassSeeds[index]);
                }

                terrainData.detailPrototypes = prototypes;
            }

            var detailResolutionChanged =
                terrainData.detailWidth != DetailResolution ||
                terrainData.detailHeight != DetailResolution;
            if (detailResolutionChanged)
            {
                terrainData.SetDetailResolution(
                    DetailResolution,
                    DetailResolutionPerPatch);
            }

            if (repaint || detailSetupChanged || detailResolutionChanged ||
                !HasAnyDetails(terrainData))
            {
                PaintMixedGrass(terrainData);
            }

            if (repaint || treeSetupChanged || terrainData.treeInstanceCount == 0)
            {
                PaintTrees(terrainData);
            }

            EditorUtility.SetDirty(terrainData);
        }

        private static void ConfigureForestPrefab(TerrainData terrainData)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null &&
                string.Equals(
                    stage.assetPath,
                    ForestGrayboxMapAuthoring.PrefabPath,
                    StringComparison.Ordinal))
            {
                ApplyTerrainPresentation(stage.prefabContentsRoot, terrainData);
                EditorSceneManager.MarkSceneDirty(stage.scene);
                if (!EditorApplication.ExecuteMenuItem("File/Save"))
                {
                    throw new InvalidOperationException(
                        "Could not save the Forest foliage from Prefab Stage.");
                }
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(
                ForestGrayboxMapAuthoring.PrefabPath);
            try
            {
                ApplyTerrainPresentation(root, terrainData);
                if (PrefabUtility.SaveAsPrefabAsset(
                        root,
                        ForestGrayboxMapAuthoring.PrefabPath) == null)
                {
                    throw new InvalidOperationException(
                        "Could not save the Forest foliage to its map prefab.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ApplyTerrainPresentation(
            GameObject root,
            TerrainData terrainData)
        {
            var terrain = root.GetComponentsInChildren<Terrain>(true)
                .FirstOrDefault(candidate =>
                    candidate.name == ForestGroundAuthoring.TerrainObjectName);
            if (terrain == null)
            {
                throw new InvalidOperationException(
                    "Forest Graybox does not contain its authored Terrain.");
            }

            terrain.terrainData = terrainData;
            terrain.drawTreesAndFoliage = true;
            terrain.treeDistance = 250f;
            terrain.treeBillboardDistance = 250f;
            terrain.treeMaximumFullLODCount = TreeCount;
            terrain.detailObjectDistance = 80f;
            terrain.detailObjectDensity = 1f;

            var generatedGround = terrain.transform.parent;
            if (generatedGround != null)
            {
                foreach (var collider in
                    generatedGround.GetComponentsInChildren<Collider>(true))
                {
                    UnityEngine.Object.DestroyImmediate(collider);
                }
            }

            EditorUtility.SetDirty(terrain);
        }

        private static void EnsurePaintDetailAssetsReadable()
        {
            foreach (var modelPath in SourceGrassModelPaths)
            {
                var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                if (importer == null)
                {
                    throw new InvalidOperationException(
                        "Required Pandazole grass model is missing: '" +
                        modelPath + "'.");
                }

                if (!importer.isReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }
            }

            var textureImporter =
                AssetImporter.GetAtPath(SourceTexturePath) as TextureImporter;
            if (textureImporter == null)
            {
                throw new InvalidOperationException(
                    "Required Pandazole atlas is missing: '" +
                    SourceTexturePath + "'.");
            }

            if (!textureImporter.isReadable)
            {
                textureImporter.isReadable = true;
                textureImporter.SaveAndReimport();
            }
        }

        private static Material CreateOrUpdateUrpMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Universal Render Pipeline/Lit is unavailable.");
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                SourceTexturePath);
            if (texture == null)
            {
                throw new InvalidOperationException(
                    "Could not load the Pandazole texture atlas.");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(
                FoliageMaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "Pandazole Nature URP"
                };
                AssetDatabase.CreateAsset(material, FoliageMaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.SetTexture("_BaseMap", texture);
            material.SetTexture("_MainTex", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetColor("_Color", Color.white);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.enableInstancing = false;
            material.renderQueue = -1;
            EditorUtility.SetDirty(material);
            return material;
        }

private static GameObject CreateOrUpdateCollisionFreePrefab(
            string sourcePath,
            string destinationPath,
            Material material,
            bool castsShadows)
        {
            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                sourcePath);
            if (sourcePrefab == null)
            {
                throw new InvalidOperationException(
                    "Required Pandazole prefab is missing: '" +
                    sourcePath + "'.");
            }

            var root = PrefabUtility.LoadPrefabContents(sourcePath);
            try
            {
                root.name = Path.GetFileNameWithoutExtension(destinationPath);
                foreach (var collider in
                    root.GetComponentsInChildren<Collider>(true))
                {
                    UnityEngine.Object.DestroyImmediate(collider);
                }

                var renderers = root.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in renderers)
                {
                    var materials = renderer.sharedMaterials;
                    for (var index = 0; index < materials.Length; index++)
                    {
                        materials[index] = material;
                    }

                    renderer.sharedMaterials = materials;
                    renderer.shadowCastingMode = castsShadows
                        ? ShadowCastingMode.On
                        : ShadowCastingMode.Off;
                    renderer.receiveShadows = castsShadows;
                }

                if (castsShadows)
                {
                    var lodGroup = root.GetComponent<LODGroup>();
                    if (lodGroup == null)
                    {
                        lodGroup = root.AddComponent<LODGroup>();
                    }

                    lodGroup.fadeMode = LODFadeMode.None;
                    lodGroup.animateCrossFading = false;
                    lodGroup.SetLODs(new[]
                    {
                        new LOD(0.005f, renderers)
                    });
                    lodGroup.RecalculateBounds();
                }

                if (PrefabUtility.SaveAsPrefabAsset(root, destinationPath) == null)
                {
                    throw new InvalidOperationException(
                        "Could not create collision-free foliage prefab: '" +
                        destinationPath + "'.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(destinationPath);
        }

        private static DetailPrototype CreateGrassPrototype(
            GameObject prefab,
            int seed)
        {
            var prototype = new DetailPrototype
            {
                prototype = prefab,
                usePrototypeMesh = true,
                useInstancing = false,
                renderMode = DetailRenderMode.Grass,
                minWidth = 0.85f,
                maxWidth = 1.15f,
                minHeight = 0.85f,
                maxHeight = 1.15f,
                healthyColor = Color.white,
                dryColor = Color.white,
                noiseSpread = 0.1f,
                noiseSeed = seed,
                alignToGround = 0.5f,
                positionJitter = 1f,
                holeEdgePadding = 0.1f,
                useDensityScaling = true,
                density = 1f
            };

            string error;
            if (!prototype.Validate(out error))
            {
                throw new InvalidOperationException(
                    "Invalid grass detail prototype '" +
                    prefab.name + "': " + error);
            }

            return prototype;
        }

        private static bool DetailPrototypesMatch(
            IReadOnlyList<DetailPrototype> existing,
            IReadOnlyList<GameObject> expected)
        {
            if (existing.Count != expected.Count)
            {
                return false;
            }

            for (var index = 0; index < existing.Count; index++)
            {
                if (existing[index] == null ||
                    existing[index].prototype != expected[index] ||
                    !existing[index].usePrototypeMesh)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasAnyDetails(TerrainData terrainData)
        {
            if (terrainData.detailWidth <= 0 || terrainData.detailHeight <= 0)
            {
                return false;
            }

            for (var layer = 0; layer < terrainData.detailPrototypes.Length; layer++)
            {
                var details = terrainData.GetDetailLayer(
                    0,
                    0,
                    terrainData.detailWidth,
                    terrainData.detailHeight,
                    layer);
                foreach (var value in details)
                {
                    if (value > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void PaintMixedGrass(TerrainData terrainData)
        {
            var resolution = terrainData.detailWidth;
            var layers = new int[GrassPrototypePaths.Length][,];
            for (var index = 0; index < layers.Length; index++)
            {
                layers[index] = new int[resolution, resolution];
            }

            for (var z = 0; z < resolution; z++)
            {
                var normalizedZ = (z + 0.5f) / resolution;
                for (var x = 0; x < resolution; x++)
                {
                    var normalizedX = (x + 0.5f) / resolution;
                    var grass = SampleGrassWeight(
                        terrainData,
                        normalizedX,
                        normalizedZ);
                    var coverage = Mathf.InverseLerp(0.6f, 1f, grass) * 0.58f;
                    if (Hash01(x, z, 731) >= coverage)
                    {
                        continue;
                    }

                    var variant = Mathf.Min(
                        layers.Length - 1,
                        Mathf.FloorToInt(Hash01(x, z, 1931) * layers.Length));
                    layers[variant][x, z] = 1;
                }
            }

            for (var layer = 0; layer < layers.Length; layer++)
            {
                terrainData.SetDetailLayer(0, 0, layer, layers[layer]);
            }
        }

        private static void PaintTrees(TerrainData terrainData)
        {
            var random = new System.Random(240419);
            var accepted = new List<Vector2>(TreeCount);
            var instances = new List<TreeInstance>(TreeCount);
            var edgeX = TreeEdgeMargin / terrainData.size.x;
            var edgeZ = TreeEdgeMargin / terrainData.size.z;

            for (var attempt = 0;
                 attempt < 12000 && instances.Count < TreeCount;
                 attempt++)
            {
                var normalizedX =
                    Mathf.Lerp(edgeX, 1f - edgeX, (float)random.NextDouble());
                var normalizedZ =
                    Mathf.Lerp(edgeZ, 1f - edgeZ, (float)random.NextDouble());
                if (!IsTreeLocationClear(
                        terrainData,
                        normalizedX,
                        normalizedZ))
                {
                    continue;
                }

                var worldXZ = new Vector2(
                    normalizedX * terrainData.size.x,
                    normalizedZ * terrainData.size.z);
                var tooClose = false;
                for (var index = 0; index < accepted.Count; index++)
                {
                    if ((accepted[index] - worldXZ).sqrMagnitude <
                        TreeSpacing * TreeSpacing)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (tooClose)
                {
                    continue;
                }

                accepted.Add(worldXZ);
                var scale = Mathf.Lerp(
                    0.9f,
                    1.1f,
                    (float)random.NextDouble());
                instances.Add(new TreeInstance
                {
                    position = new Vector3(normalizedX, 0f, normalizedZ),
                    widthScale = scale,
                    heightScale = scale,
                    rotation = (float)random.NextDouble() * Mathf.PI * 2f,
                    color = Color.white,
                    lightmapColor = Color.white,
                    prototypeIndex = 0
                });
            }

            if (instances.Count < TreeCount)
            {
                Debug.LogWarning(
                    "Only " + instances.Count + " of " + TreeCount +
                    " requested forest trees fit the grass/path clearance rules.");
            }

            terrainData.SetTreeInstances(instances.ToArray(), true);
        }

        private static bool IsTreeLocationClear(
            TerrainData terrainData,
            float normalizedX,
            float normalizedZ)
        {
            var radiusX = 2.25f / terrainData.size.x;
            var radiusZ = 2.25f / terrainData.size.z;
            var offsets = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(radiusX, 0f),
                new Vector2(-radiusX, 0f),
                new Vector2(0f, radiusZ),
                new Vector2(0f, -radiusZ),
                new Vector2(radiusX, radiusZ),
                new Vector2(radiusX, -radiusZ),
                new Vector2(-radiusX, radiusZ),
                new Vector2(-radiusX, -radiusZ)
            };

            foreach (var offset in offsets)
            {
                var x = normalizedX + offset.x;
                var z = normalizedZ + offset.y;
                if (x <= 0f || x >= 1f || z <= 0f || z >= 1f ||
                    SampleGrassWeight(terrainData, x, z) < 0.9f)
                {
                    return false;
                }
            }

            return true;
        }

        private static float SampleGrassWeight(
            TerrainData terrainData,
            float normalizedX,
            float normalizedZ)
        {
            if (terrainData.alphamapLayers == 0)
            {
                return 0f;
            }

            var x = Mathf.Clamp(
                Mathf.RoundToInt(normalizedX *
                    (terrainData.alphamapWidth - 1)),
                0,
                terrainData.alphamapWidth - 1);
            var z = Mathf.Clamp(
                Mathf.RoundToInt(normalizedZ *
                    (terrainData.alphamapHeight - 1)),
                0,
                terrainData.alphamapHeight - 1);
            return terrainData.GetAlphamaps(x, z, 1, 1)[0, 0, 0];
        }

        private static float Hash01(int x, int z, int seed)
        {
            unchecked
            {
                var hash = (uint)(x * 374761393 + z * 668265263 + seed * 1442695041);
                hash = (hash ^ (hash >> 13)) * 1274126177u;
                hash ^= hash >> 16;
                return (hash & 0x00ffffffu) / 16777215f;
            }
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
    }
}
