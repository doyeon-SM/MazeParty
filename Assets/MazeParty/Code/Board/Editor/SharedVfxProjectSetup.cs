using System;
using System.Linq;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    /// <summary>
    /// Creates project-owned wrappers around the git-ignored All In 1 VFX
    /// Toolkit prefabs. Existing wrappers are validated, never regenerated.
    /// </summary>
    public static class SharedVfxProjectSetup
    {
        public const string VfxFolder =
            "Assets/MazeParty/Prefabs/Common/VFX";
        public const string CartoonExplosionPrefabPath =
            VfxFolder + "/CartoonExplosion.prefab";
        public const string HitSparkPrefabPath =
            VfxFolder + "/HitSpark.prefab";

        private const string ToolkitPrefabFolder =
            "Assets/Ignore/AllIn1VfxToolkit/Demo & Assets/Demo/Prefabs";
        private const string GrabPassShaderPath =
            "Assets/Ignore/AllIn1VfxToolkit/Shaders/" +
            "AllIn1VfxGrabPass.shader";
        private const string ToonExplosionSourcePath =
            ToolkitPrefabFolder + "/Toon Explosion.prefab";
        private const string HitSparkSourcePath =
            ToolkitPrefabFolder + "/Blue Impact.prefab";
        private const string BombPassingScenePath =
            "Assets/MazeParty/Scenes/Minigames/BombPassing/" +
            "BombPassing.unity";

        [MenuItem("MazeParty/VFX/Build Shared VFX Wrappers")]
        public static void BuildSharedVfxWrappers()
        {
            EnsureFolder(VfxFolder);
            var explosion = EnsureCartoonExplosionPrefab();
            var impact = EnsureHitSparkPrefab();
            EnsureBoardItemBindings(explosion, impact);
            EnsureBombPassingBinding(explosion);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("MazeParty shared pooled VFX wrappers are ready.");
        }

        public static GameObject EnsureCartoonExplosionPrefab()
        {
            return EnsureWrapper(
                CartoonExplosionPrefabPath,
                ToonExplosionSourcePath,
                "Cartoon Explosion",
                new Color(1f, 0.28f, 0.05f),
                4.5f,
                0.2f,
                4f,
                7f,
                1f);
        }

        public static GameObject EnsureHitSparkPrefab()
        {
            return EnsureWrapper(
                HitSparkPrefabPath,
                HitSparkSourcePath,
                "Hit Spark",
                new Color(0.3f, 0.75f, 1f),
                3.5f,
                0.08f,
                1.2f,
                3f,
                0.32f);
        }

        public static void EnsureBoardItemBindings(
            GameObject explosion,
            GameObject impact)
        {
            foreach (var guid in AssetDatabase.FindAssets(
                         "t:BoardItemDefinition",
                         new[] { BoardItemProjectSetup.DataFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<BoardItemDefinition>(
                    path);
                if (item == null)
                {
                    continue;
                }

                var dirty = false;
                var explosive = item.Id == PrototypeItemId.Grenade ||
                                item.Id == PrototypeItemId.Mine;
                if (explosive && item.ExplosionPrefab != explosion)
                {
                    item.ExplosionPrefab = explosion;
                    dirty = true;
                }
                else if (!explosive && item.ExplosionPrefab != null)
                {
                    item.ExplosionPrefab = null;
                    dirty = true;
                }

                var firearm = item.Id == PrototypeItemId.Pistol ||
                              item.Id == PrototypeItemId.Sniper;
                if (firearm && item.ImpactPrefab != impact)
                {
                    item.ImpactPrefab = impact;
                    dirty = true;
                }
                else if (!firearm && item.ImpactPrefab != null)
                {
                    item.ImpactPrefab = null;
                    dirty = true;
                }

                if (dirty)
                {
                    EditorUtility.SetDirty(item);
                }
            }
        }

        private static void EnsureBombPassingBinding(GameObject explosion)
        {
            var scene = SceneManager.GetSceneByPath(BombPassingScenePath);
            var openedForSetup = !scene.IsValid() || !scene.isLoaded;
            if (openedForSetup)
            {
                scene = EditorSceneManager.OpenScene(
                    BombPassingScenePath,
                    OpenSceneMode.Additive);
            }
            else if (scene.isDirty)
            {
                throw new InvalidOperationException(
                    "BombPassing.unity has unsaved edits. Save or discard " +
                    "them before building shared VFX wrappers.");
            }

            try
            {
                var view = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<
                        BombPassingNetworkView>(true))
                    .SingleOrDefault();
                if (view == null)
                {
                    throw new InvalidOperationException(
                        "BombPassing.unity must contain one " +
                        "BombPassingNetworkView.");
                }

                var serialized = new SerializedObject(view);
                var property = serialized.FindProperty(
                    "explosionVfxPrefab");
                if (property == null)
                {
                    throw new InvalidOperationException(
                        "BombPassingNetworkView explosion VFX binding is " +
                        "missing. Wait for scripts to compile and retry.");
                }
                if (property.objectReferenceValue != explosion)
                {
                    property.objectReferenceValue = explosion;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(view);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally
            {
                if (openedForSetup && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static GameObject EnsureWrapper(
            string wrapperPath,
            string sourcePath,
            string objectName,
            Color flashColor,
            float lifetime,
            float flashSeconds,
            float flashIntensity,
            float flashRange,
            float sourceScale)
        {
            EnsureFolder(VfxFolder);
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(wrapperPath);
            if (existing != null)
            {
                SanitizeExistingWrapper(
                    wrapperPath,
                    lifetime,
                    flashSeconds);
                existing = AssetDatabase.LoadAssetAtPath<GameObject>(
                    wrapperPath);
                ValidateWrapper(existing, wrapperPath);
                return existing;
            }

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    "Required All In 1 VFX Toolkit prefab is missing: " +
                    sourcePath + ". Keep toolkit v2.32 installed under " +
                    "Assets/Ignore before running VFX setup.");
            }

            var root = new GameObject(objectName);
            try
            {
                var vendor = PrefabUtility.InstantiatePrefab(
                    source,
                    root.transform) as GameObject;
                if (vendor == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate VFX source prefab: " + sourcePath);
                }

                vendor.name = "AllIn1 " + source.name;
                vendor.transform.localPosition = Vector3.zero;
                vendor.transform.localRotation = Quaternion.identity;
                vendor.transform.localScale = Vector3.one * sourceScale;
                PrefabUtility.UnpackPrefabInstance(
                    vendor,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                StripUnsupportedVendorContent(vendor);

                var particles = root.GetComponentsInChildren<ParticleSystem>(
                    true);
                foreach (var particle in particles)
                {
                    var main = particle.main;
                    main.playOnAwake = false;
                    main.useUnscaledTime = true;
                }

                var flashObject = new GameObject("Accessibility Flash Light");
                flashObject.transform.SetParent(root.transform, false);
                flashObject.transform.localPosition = Vector3.up * 0.25f;
                var flash = flashObject.AddComponent<Light>();
                flash.type = LightType.Point;
                flash.color = flashColor;
                flash.range = flashRange;
                flash.intensity = flashIntensity;
                flash.shadows = LightShadows.None;
                flash.enabled = false;

                var pooled = root.AddComponent<PooledOneShotVfx>();
                pooled.Configure(
                    particles,
                    new[] { flash },
                    lifetime,
                    flashSeconds);

                var result = PrefabUtility.SaveAsPrefabAsset(root, wrapperPath);
                ValidateWrapper(result, wrapperPath);
                return result;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void SanitizeExistingWrapper(
            string wrapperPath,
            float lifetime,
            float flashSeconds)
        {
            var root = PrefabUtility.LoadPrefabContents(wrapperPath);
            try
            {
                var vendorRoots = Enumerable.Range(
                        0,
                        root.transform.childCount)
                    .Select(index => root.transform.GetChild(index).gameObject)
                    .Where(IsToolkitPresentationRoot)
                    .ToArray();
                foreach (var vendor in vendorRoots)
                {
                    if (PrefabUtility.IsAnyPrefabInstanceRoot(vendor))
                    {
                        PrefabUtility.UnpackPrefabInstance(
                            vendor,
                            PrefabUnpackMode.Completely,
                            InteractionMode.AutomatedAction);
                    }

                    StripUnsupportedVendorContent(vendor);
                }

                var particles = root.GetComponentsInChildren<ParticleSystem>(
                    true);
                foreach (var particle in particles)
                {
                    var main = particle.main;
                    main.playOnAwake = false;
                    main.useUnscaledTime = true;
                }

                var pooled = root.GetComponent<PooledOneShotVfx>();
                if (pooled == null)
                {
                    throw new InvalidOperationException(
                        "Shared VFX wrapper is missing its pool contract: " +
                        wrapperPath);
                }
                pooled.Configure(
                    particles,
                    root.GetComponentsInChildren<Light>(true),
                    lifetime,
                    flashSeconds);

                PrefabUtility.SaveAsPrefabAsset(root, wrapperPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool IsToolkitPresentationRoot(GameObject candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            if (candidate.name.StartsWith(
                    "AllIn1 ",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!PrefabUtility.IsAnyPrefabInstanceRoot(candidate))
            {
                return false;
            }

            var sourcePath = PrefabUtility
                .GetPrefabAssetPathOfNearestInstanceRoot(candidate)
                ?.Replace('\\', '/');
            return !string.IsNullOrEmpty(sourcePath) &&
                   sourcePath.StartsWith(
                       ToolkitPrefabFolder + "/",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static void StripUnsupportedVendorContent(GameObject vendor)
        {
            var unsupported = vendor.GetComponentsInChildren<Transform>(true)
                .Where(item => item != vendor.transform &&
                               (item.name.IndexOf(
                                    "Distort",
                                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                                item.name.IndexOf(
                                    "GrabPass",
                                    StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderByDescending(item => GetDepth(item))
                .ToArray();
            foreach (var item in unsupported)
            {
                if (item != null)
                {
                    UnityEngine.Object.DestroyImmediate(item.gameObject);
                }
            }

            foreach (var behaviour in vendor
                         .GetComponentsInChildren<MonoBehaviour>(true)
                         .Where(item => item != null)
                         .ToArray())
            {
                UnityEngine.Object.DestroyImmediate(behaviour);
            }
            foreach (var collider in vendor
                         .GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
        }

        private static int GetDepth(Transform item)
        {
            var depth = 0;
            for (var current = item; current != null; current = current.parent)
            {
                depth++;
            }
            return depth;
        }

        private static void ValidateWrapper(GameObject prefab, string path)
        {
            var pooled = prefab != null
                ? prefab.GetComponent<PooledOneShotVfx>()
                : null;
            if (pooled == null || pooled.ParticleSystems.Length == 0 ||
                pooled.ParticleSystems.Any(item => item == null) ||
                prefab.GetComponentsInChildren<Collider>(true).Length > 0 ||
                prefab.GetComponentsInChildren<Transform>(true).Any(item =>
                    item.name.IndexOf(
                        "Distort",
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.name.IndexOf(
                        "GrabPass",
                        StringComparison.OrdinalIgnoreCase) >= 0) ||
                prefab.GetComponentsInChildren<Renderer>(false)
                    .SelectMany(item => item.sharedMaterials)
                    .Where(item => item != null && item.shader != null)
                    .Any(item => item.shader.name.IndexOf(
                        "GrabPass",
                        StringComparison.OrdinalIgnoreCase) >= 0) ||
                AssetDatabase.GetDependencies(path, true).Any(item =>
                    string.Equals(
                        item.Replace('\\', '/'),
                        GrabPassShaderPath,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "Shared VFX wrapper contract is invalid: " + path);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var slash = path.LastIndexOf('/');
            if (slash <= 0)
            {
                throw new InvalidOperationException(
                    "Invalid asset folder: " + path);
            }
            var parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
