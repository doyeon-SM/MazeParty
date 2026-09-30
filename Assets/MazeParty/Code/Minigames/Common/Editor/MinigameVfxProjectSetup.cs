using System;
using System.Linq;
using MazeParty.Multiplayer;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    /// <summary>
    /// Authors the shared production minigame VFX references. Toolkit content stays in
    /// Assets/Ignore while the tracked aura is an unpacked, presentation-only copy.
    /// </summary>
    public static class MinigameVfxProjectSetup
    {
        public const string HitSparkPrefabPath =
            "Assets/MazeParty/Prefabs/Common/VFX/HitSpark.prefab";
        public const string CartoonExplosionPrefabPath =
            "Assets/MazeParty/Prefabs/Common/VFX/CartoonExplosion.prefab";
        public const string TaggerAuraPrefabPath =
            "Assets/MazeParty/Prefabs/Minigames/Common/VFX/TaggerAura.prefab";

        private const string TaggerAuraSourcePath =
            "Assets/Ignore/AllIn1VfxToolkit/Demo & Assets/Demo/Prefabs/Evil Aura.prefab";
        private const string GrabPassShaderPath =
            "Assets/Ignore/AllIn1VfxToolkit/Shaders/" +
            "AllIn1VfxGrabPass.shader";
        private const string PrefabFolder =
            "Assets/MazeParty/Prefabs/Minigames/Common/VFX";

        private static readonly string[] ScenePaths =
        {
            ArenaCombatProjectSetup.ScenePath,
            BalloonBlowProjectSetup.BalloonBlowScenePath,
            BouncingBallsProjectSetup.ScenePath,
            CliffBarrageProjectSetup.ScenePath,
            GiftGrabProjectSetup.GiftGrabScenePath,
            MinefieldProjectSetup.MinefieldScenePath,
            RaceProjectSetup.RaceScenePath,
            RedLightGreenLightProjectSetup.RedLightGreenLightScenePath,
            SequenceMemoryProjectSetup.ScenePath,
            SnowySpinProjectSetup.ScenePath,
            StableFootingProjectSetup.StableFootingScenePath,
            TagChaseProjectSetup.TagChaseScenePath,
            TerritoryPaintProjectSetup.TerritoryPaintScenePath,
            WrongWayProjectSetup.WrongWayScenePath
        };

        [MenuItem("MazeParty/VFX/Install Minigame VFX")]
        public static void InstallMinigameVfx()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("Minigame VFX install canceled; scene edits preserved.");
                return;
            }

            EnsureAssets();
            for (var index = 0; index < ScenePaths.Length; index++)
            {
                InstallScene(ScenePaths[index]);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Minigame VFX prefabs and scene bindings are ready.");
        }

        [MenuItem("MazeParty/VFX/Install Minigame VFX", true)]
        private static bool CanInstallMinigameVfx() =>
            !EditorApplication.isPlayingOrWillChangePlaymode;

        public static void EnsureAssets()
        {
            EnsureFolder(PrefabFolder);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(
                    TaggerAuraPrefabPath) == null)
            {
                CreateToolkitOwnedPrefab(
                    TaggerAuraPrefabPath,
                    TaggerAuraSourcePath,
                    "TaggerAura");
            }
            SanitizeTaggerAuraPrefab();
        }

        public static void InstallScene(string scenePath)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                return;
            }

            EnsureAssets();
            var previousActive = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedForSetup = !scene.IsValid() || !scene.isLoaded;
            if (openedForSetup)
            {
                scene = EditorSceneManager.OpenScene(
                    scenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                BindScene(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, scenePath);
            }
            finally
            {
                if (openedForSetup && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
                if (previousActive.IsValid() && previousActive.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActive);
                }
            }
        }

        private static void BindScene(Scene scene)
        {
            var hitSpark = AssetDatabase.LoadAssetAtPath<GameObject>(
                HitSparkPrefabPath);
            var explosion = AssetDatabase.LoadAssetAtPath<GameObject>(
                CartoonExplosionPrefabPath);
            var aura = AssetDatabase.LoadAssetAtPath<GameObject>(
                TaggerAuraPrefabPath);

            if (hitSpark == null || explosion == null || aura == null)
            {
                throw new InvalidOperationException(
                    "Required shared VFX prefabs are missing. Run " +
                    "MazeParty/VFX/Build Shared VFX Wrappers before " +
                    "installing minigame VFX. No scene bindings were changed.");
            }

            var roots = scene.GetRootGameObjects();
            for (var index = 0; index < roots.Length; index++)
            {
                var root = roots[index];
                var arena = root.GetComponentInChildren<
                    ArenaCombatNetworkView>(true);
                arena?.ConfigureVfx(hitSpark);

                var bouncing = root.GetComponentInChildren<
                    BouncingBallsNetworkView>(true);
                bouncing?.ConfigureVfx(hitSpark);

                var balloon = root.GetComponentInChildren<
                    BalloonBlowNetworkView>(true);
                balloon?.ConfigureVfx(explosion);

                var cliff = root.GetComponentInChildren<
                    CliffBarrageNetworkView>(true);
                cliff?.ConfigureVfx(hitSpark);

                var minefield = root.GetComponentInChildren<
                    MinefieldNetworkView>(true);
                minefield?.ConfigureVfx(explosion, hitSpark);

                var gift = root.GetComponentInChildren<
                    GiftGrabNetworkView>(true);
                gift?.ConfigureVfx(hitSpark);

                var race = root.GetComponentInChildren<
                    RaceNetworkView>(true);
                race?.ConfigureVfx(hitSpark, explosion);

                var redLight = root.GetComponentInChildren<
                    RedLightGreenLightNetworkView>(true);
                redLight?.ConfigureVfx(hitSpark);

                var sequence = root.GetComponentInChildren<
                    SequenceMemoryNetworkView>(true);
                sequence?.ConfigureVfx(hitSpark);

                var snowy = root.GetComponentInChildren<
                    SnowySpinNetworkView>(true);
                snowy?.ConfigureVfx(hitSpark);

                var stable = root.GetComponentInChildren<
                    StableFootingNetworkView>(true);
                stable?.ConfigureVfx(hitSpark);

                var tag = root.GetComponentInChildren<
                    TagChaseNetworkView>(true);
                tag?.ConfigureVfx(hitSpark, aura);

                var territory = root.GetComponentInChildren<
                    TerritoryPaintNetworkView>(true);
                territory?.ConfigureVfx(hitSpark);

                var wrongWay = root.GetComponentInChildren<
                    WrongWayNetworkView>(true);
                wrongWay?.ConfigureVfx(hitSpark);

                if (arena != null || balloon != null || bouncing != null ||
                    cliff != null || gift != null || minefield != null ||
                    race != null || redLight != null || sequence != null ||
                    snowy != null || stable != null || tag != null ||
                    territory != null || wrongWay != null)
                {
                    EditorUtility.SetDirty(root);
                }
            }
        }

        private static void CreateToolkitOwnedPrefab(
            string targetPath,
            string sourcePath,
            string rootName)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    "Required AllIn1 VFX Toolkit prefab is missing: " +
                    sourcePath);
            }

            var root = new GameObject(rootName);
            try
            {
                var nested = PrefabUtility.InstantiatePrefab(
                    source,
                    root.transform) as GameObject;
                if (nested == null)
                {
                    throw new InvalidOperationException(
                        "Could not nest toolkit prefab: " + sourcePath);
                }
                nested.name = "AllIn1 " + source.name;
                nested.transform.SetLocalPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                nested.transform.localScale = Vector3.one;
                PrefabUtility.UnpackPrefabInstance(
                    nested,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                StripUnsupportedVendorContent(root);
                ValidateTaggerAuraHierarchy(root, sourcePath);

                PrefabUtility.SaveAsPrefabAsset(root, targetPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void SanitizeTaggerAuraPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(
                TaggerAuraPrefabPath);
            try
            {
                var changed = UnpackNestedPrefabInstances(root);
                changed |= StripUnsupportedVendorContent(root);
                ValidateTaggerAuraHierarchy(root, TaggerAuraPrefabPath);

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(
                        root,
                        TaggerAuraPrefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            ValidateTaggerAuraAsset();
        }

        private static bool UnpackNestedPrefabInstances(GameObject root)
        {
            var changed = false;
            var instanceRoots = root.GetComponentsInChildren<Transform>(true)
                .Where(item => item != root.transform &&
                               PrefabUtility.IsAnyPrefabInstanceRoot(
                                   item.gameObject))
                .OrderBy(GetDepth)
                .Select(item => item.gameObject)
                .ToArray();
            foreach (var instanceRoot in instanceRoots)
            {
                if (instanceRoot == null ||
                    !PrefabUtility.IsAnyPrefabInstanceRoot(instanceRoot))
                {
                    continue;
                }

                PrefabUtility.UnpackPrefabInstance(
                    instanceRoot,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                changed = true;
            }
            return changed;
        }

        private static bool StripUnsupportedVendorContent(GameObject root)
        {
            var changed = false;
            var unsupported = root.GetComponentsInChildren<Transform>(true)
                .Where(item => item != root.transform &&
                               HasUnsupportedPresentationName(item.name))
                .OrderByDescending(GetDepth)
                .ToArray();
            foreach (var item in unsupported)
            {
                if (item != null)
                {
                    UnityEngine.Object.DestroyImmediate(item.gameObject);
                    changed = true;
                }
            }

            foreach (var renderer in root
                         .GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var supported = materials
                    .Where(material => !UsesUnsupportedShader(material))
                    .ToArray();
                if (supported.Length == materials.Length)
                {
                    continue;
                }

                renderer.sharedMaterials = supported;
                changed = true;
            }

            foreach (var networkObject in root
                         .GetComponentsInChildren<NetworkObject>(true)
                         .Where(item => item != null)
                         .ToArray())
            {
                UnityEngine.Object.DestroyImmediate(networkObject);
                changed = true;
            }
            foreach (var behaviour in root
                         .GetComponentsInChildren<MonoBehaviour>(true)
                         .Where(item => item != null)
                         .ToArray())
            {
                UnityEngine.Object.DestroyImmediate(behaviour);
                changed = true;
            }
            foreach (var collider in root
                         .GetComponentsInChildren<Collider>(true)
                         .ToArray())
            {
                UnityEngine.Object.DestroyImmediate(collider);
                changed = true;
            }
            foreach (var collider in root
                         .GetComponentsInChildren<Collider2D>(true)
                         .ToArray())
            {
                UnityEngine.Object.DestroyImmediate(collider);
                changed = true;
            }

            foreach (var item in root.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                        item.gameObject) <= 0)
                {
                    continue;
                }

                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(
                    item.gameObject);
                changed = true;
            }
            return changed;
        }

        private static bool HasUnsupportedPresentationName(string name) =>
            name.IndexOf("Distort", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("GrabPass", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool UsesUnsupportedShader(Material material)
        {
            if (material == null || material.shader == null)
            {
                return false;
            }

            var shaderPath = AssetDatabase.GetAssetPath(material.shader)
                .Replace('\\', '/');
            return HasUnsupportedPresentationName(material.shader.name) ||
                   string.Equals(
                       shaderPath,
                       GrabPassShaderPath,
                       StringComparison.OrdinalIgnoreCase);
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

        private static void ValidateTaggerAuraHierarchy(
            GameObject root,
            string context)
        {
            if (root == null ||
                root.GetComponentsInChildren<ParticleSystem>(true).Length == 0 ||
                root.GetComponentsInChildren<MonoBehaviour>(true).Length > 0 ||
                root.GetComponentsInChildren<Collider>(true).Length > 0 ||
                root.GetComponentsInChildren<Collider2D>(true).Length > 0 ||
                root.GetComponentsInChildren<NetworkObject>(true).Length > 0 ||
                root.GetComponentsInChildren<Transform>(true)
                    .Any(item => HasUnsupportedPresentationName(item.name)) ||
                root.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(item => item.sharedMaterials)
                    .Any(UsesUnsupportedShader))
            {
                throw new InvalidOperationException(
                    "Tagger aura must be an unpacked, presentation-only VFX " +
                    "without distortion or GrabPass content: " + context);
            }
        }

        private static void ValidateTaggerAuraAsset()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                TaggerAuraPrefabPath);
            ValidateTaggerAuraHierarchy(prefab, TaggerAuraPrefabPath);

            if (AssetDatabase.GetDependencies(TaggerAuraPrefabPath, true)
                .Any(path => string.Equals(
                    path.Replace('\\', '/'),
                    GrabPassShaderPath,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "Tagger aura still depends on the unsupported GrabPass " +
                    "shader: " + TaggerAuraPrefabPath);
            }
        }

        private static void EnsureFolder(string folderPath)
        {
            var segments = folderPath.Split('/');
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
