using System;
using System.Linq;
using MazeParty.Gameplay;
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
        public const string ArrivalFireworksPrefabPath =
            "Assets/MazeParty/Prefabs/Minigames/Common/VFX/" +
            "ArrivalFireworks.prefab";
        public const string LightningStrikePrefabPath =
            SharedVfxProjectSetup.LightningStrikePrefabPath;

        private const string TaggerAuraSourcePath =
            "Assets/Ignore/AllIn1VfxToolkit/Demo & Assets/Demo/Prefabs/Evil Aura.prefab";
        private const string ArrivalFireworksSourcePath =
            "Assets/Ignore/AllIn1VfxToolkit/Demo & Assets/Demo/Prefabs/" +
            "Explosion Galaxy.prefab";
        private const string GrabPassShaderPath =
            "Assets/Ignore/AllIn1VfxToolkit/Shaders/" +
            "AllIn1VfxGrabPass.shader";
        private const string PrefabFolder =
            "Assets/MazeParty/Prefabs/Minigames/Common/VFX";

        private static readonly Color ArrivalGreen =
            new Color(0.18f, 1f, 0.28f, 1f);
        private static readonly Color ArrivalYellow =
            new Color(1f, 0.82f, 0.12f, 1f);

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

        [MenuItem("MazeParty/VFX/Install Balloon And Red Light Feedback")]
        public static void InstallBalloonAndRedLightFeedback()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(
                    "Minigame feedback install requires Edit Mode.");
                return;
            }

            EnsureAssets();
            InstallScene(BalloonBlowProjectSetup.BalloonBlowScenePath);
            InstallScene(
                RedLightGreenLightProjectSetup.RedLightGreenLightScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "Balloon and Red Light feedback VFX bindings are ready.");
        }

        [MenuItem("MazeParty/VFX/Install Minigame VFX", true)]
        private static bool CanInstallMinigameVfx() =>
            !EditorApplication.isPlayingOrWillChangePlaymode;

        public static void EnsureAssets()
        {
            SharedVfxProjectSetup.EnsureLightningStrikePrefab();
            EnsureFolder(PrefabFolder);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(
                    TaggerAuraPrefabPath) == null)
            {
                CreateToolkitOwnedPrefab(
                    TaggerAuraPrefabPath,
                    TaggerAuraSourcePath,
                    "TaggerAura");
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(
                    ArrivalFireworksPrefabPath) == null)
            {
                CreateArrivalFireworksPrefab();
            }
            SanitizeTaggerAuraPrefab();
            SanitizeArrivalFireworksPrefab();
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
            var arrivalFireworks = AssetDatabase.LoadAssetAtPath<GameObject>(
                ArrivalFireworksPrefabPath);
            var lightningStrike = AssetDatabase.LoadAssetAtPath<GameObject>(
                LightningStrikePrefabPath);

            if (hitSpark == null || explosion == null || aura == null ||
                arrivalFireworks == null || lightningStrike == null)
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
                balloon?.ConfigureVfx(arrivalFireworks);

                var cliff = root.GetComponentInChildren<
                    CliffBarrageNetworkView>(true);
                cliff?.ConfigureVfx(hitSpark);

                var minefield = root.GetComponentInChildren<
                    MinefieldNetworkView>(true);
                minefield?.ConfigureVfx(
                    explosion,
                    hitSpark,
                    arrivalFireworks);

                var gift = root.GetComponentInChildren<
                    GiftGrabNetworkView>(true);
                gift?.ConfigureVfx(hitSpark);

                var race = root.GetComponentInChildren<
                    RaceNetworkView>(true);
                race?.ConfigureVfx(hitSpark, arrivalFireworks);

                var redLight = root.GetComponentInChildren<
                    RedLightGreenLightNetworkView>(true);
                redLight?.ConfigureVfx(
                    hitSpark,
                    arrivalFireworks,
                    lightningStrike);

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
                wrongWay?.ConfigureVfx(hitSpark, arrivalFireworks);

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

        private static void CreateArrivalFireworksPrefab()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(
                ArrivalFireworksSourcePath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    "Required AllIn1 VFX Toolkit prefab is missing: " +
                    ArrivalFireworksSourcePath);
            }

            var root = new GameObject("ArrivalFireworks");
            try
            {
                CreateArrivalFireworkBurst(
                    root.transform,
                    source,
                    "Green Firework Burst",
                    ArrivalGreen,
                    new Vector3(-0.55f, 0.65f, 0f),
                    Quaternion.Euler(0f, -18f, 0f),
                    0.78f);
                CreateArrivalFireworkBurst(
                    root.transform,
                    source,
                    "Yellow Firework Burst",
                    ArrivalYellow,
                    new Vector3(0.55f, 0.95f, 0f),
                    Quaternion.Euler(0f, 22f, 0f),
                    0.86f);

                CreateArrivalFlash(
                    root.transform,
                    "Green Firework Flash",
                    ArrivalGreen,
                    new Vector3(-0.55f, 1.05f, 0f));
                CreateArrivalFlash(
                    root.transform,
                    "Yellow Firework Flash",
                    ArrivalYellow,
                    new Vector3(0.55f, 1.3f, 0f));

                var pooled = root.AddComponent<PooledOneShotVfx>();
                pooled.Configure(
                    root.GetComponentsInChildren<ParticleSystem>(true),
                    root.GetComponentsInChildren<Light>(true),
                    3f,
                    0.2f);
                ValidateArrivalFireworksHierarchy(
                    root,
                    ArrivalFireworksSourcePath);

                PrefabUtility.SaveAsPrefabAsset(
                    root,
                    ArrivalFireworksPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void CreateArrivalFireworkBurst(
            Transform parent,
            GameObject source,
            string name,
            Color color,
            Vector3 localPosition,
            Quaternion localRotation,
            float localScale)
        {
            var burst = PrefabUtility.InstantiatePrefab(
                source,
                parent) as GameObject;
            if (burst == null)
            {
                throw new InvalidOperationException(
                    "Could not instantiate arrival VFX source prefab: " +
                    ArrivalFireworksSourcePath);
            }

            burst.name = name;
            burst.transform.SetLocalPositionAndRotation(
                localPosition,
                localRotation);
            burst.transform.localScale = Vector3.one * localScale;
            PrefabUtility.UnpackPrefabInstance(
                burst,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);

            var discarded = burst.GetComponentsInChildren<Transform>(true)
                .Where(item => item.parent == burst.transform &&
                               !IsArrivalFireworkParticle(item.name))
                .ToArray();
            foreach (var item in discarded)
            {
                UnityEngine.Object.DestroyImmediate(item.gameObject);
            }

            var sourceRenderer = burst.GetComponent<ParticleSystemRenderer>();
            if (sourceRenderer != null)
            {
                UnityEngine.Object.DestroyImmediate(sourceRenderer);
            }
            var sourceParticles = burst.GetComponent<ParticleSystem>();
            if (sourceParticles != null)
            {
                UnityEngine.Object.DestroyImmediate(sourceParticles);
            }

            StripUnsupportedVendorContent(burst);
            foreach (var particles in burst
                         .GetComponentsInChildren<ParticleSystem>(true))
            {
                ConfigureArrivalParticles(particles, color);
            }
        }

        private static bool IsArrivalFireworkParticle(string name) =>
            string.Equals(name, "SparksExplosion", StringComparison.Ordinal) ||
            string.Equals(name, "Stars", StringComparison.Ordinal) ||
            string.Equals(name, "LateEmbers", StringComparison.Ordinal);

        private static void ConfigureArrivalParticles(
            ParticleSystem particles,
            Color color)
        {
            var main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.useUnscaledTime = true;
            main.stopAction = ParticleSystemStopAction.None;
            main.startColor = new ParticleSystem.MinMaxGradient(color);
        }

        private static void CreateArrivalFlash(
            Transform parent,
            string name,
            Color color,
            Vector3 localPosition)
        {
            var flashObject = new GameObject(name);
            flashObject.transform.SetParent(parent, false);
            flashObject.transform.localPosition = localPosition;
            var flash = flashObject.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = color;
            flash.range = 4f;
            flash.intensity = 3f;
            flash.shadows = LightShadows.None;
            flash.enabled = false;
        }

        private static void SanitizeArrivalFireworksPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(
                ArrivalFireworksPrefabPath);
            try
            {
                UnpackNestedPrefabInstances(root);
                StripUnsupportedArrivalFireworksContent(root);

                var particles = root.GetComponentsInChildren<ParticleSystem>(
                    true);
                foreach (var system in particles)
                {
                    var main = system.main;
                    main.loop = false;
                    main.playOnAwake = false;
                    main.useUnscaledTime = true;
                    main.stopAction = ParticleSystemStopAction.None;
                }

                var lights = root.GetComponentsInChildren<Light>(true);
                foreach (var light in lights)
                {
                    light.enabled = false;
                }

                var pooled = root.GetComponent<PooledOneShotVfx>();
                if (pooled == null)
                {
                    throw new InvalidOperationException(
                        "Arrival fireworks is missing its pooled one-shot " +
                        "contract: " + ArrivalFireworksPrefabPath);
                }
                pooled.Configure(particles, lights, 3f, 0.2f);
                ValidateArrivalFireworksHierarchy(
                    root,
                    ArrivalFireworksPrefabPath);
                PrefabUtility.SaveAsPrefabAsset(
                    root,
                    ArrivalFireworksPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            ValidateArrivalFireworksAsset();
        }

        private static void ValidateArrivalFireworksHierarchy(
            GameObject root,
            string context)
        {
            if (root == null)
            {
                throw new InvalidOperationException(
                    "Arrival fireworks prefab is missing: " + context);
            }

            var pooled = root.GetComponent<PooledOneShotVfx>();
            var behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var toolkitMaterialCount = renderers
                .SelectMany(item => item.sharedMaterials)
                .Where(item => item != null)
                .Count(item => AssetDatabase.GetAssetPath(item)
                    .Replace('\\', '/')
                    .StartsWith(
                        "Assets/Ignore/AllIn1VfxToolkit/",
                        StringComparison.OrdinalIgnoreCase));

            if (pooled == null ||
                pooled.ParticleSystems.Length == 0 ||
                pooled.ParticleSystems.Any(item => item == null) ||
                pooled.FlashLights.Length < 2 ||
                pooled.FlashLights.Any(item => item == null) ||
                behaviours.Any(item => item == null || item != pooled) ||
                root.GetComponentsInChildren<Collider>(true).Length > 0 ||
                root.GetComponentsInChildren<Collider2D>(true).Length > 0 ||
                root.GetComponentsInChildren<NetworkObject>(true).Length > 0 ||
                root.GetComponentsInChildren<Transform>(true)
                    .Any(item => HasUnsupportedPresentationName(item.name)) ||
                renderers.SelectMany(item => item.sharedMaterials)
                    .Any(UsesUnsupportedShader) ||
                toolkitMaterialCount == 0)
            {
                throw new InvalidOperationException(
                    "Arrival fireworks must remain a pooled, " +
                    "presentation-only AllIn1 VFX without distortion or " +
                    "GrabPass content: " + context);
            }
        }

        private static void ValidateArrivalFireworksAsset()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ArrivalFireworksPrefabPath);
            ValidateArrivalFireworksHierarchy(
                prefab,
                ArrivalFireworksPrefabPath);

            if (AssetDatabase.GetDependencies(
                    ArrivalFireworksPrefabPath,
                    true)
                .Any(path => string.Equals(
                    path.Replace('\\', '/'),
                    GrabPassShaderPath,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "Arrival fireworks still depends on the unsupported " +
                    "GrabPass shader: " + ArrivalFireworksPrefabPath);
            }
        }

        private static bool StripUnsupportedArrivalFireworksContent(
            GameObject root)
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
                         .Where(item => item != null &&
                                        item is not PooledOneShotVfx)
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
