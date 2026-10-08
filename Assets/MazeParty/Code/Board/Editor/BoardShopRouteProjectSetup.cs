using System;
using System.Linq;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    public static class BoardShopRouteProjectSetup
    {
        public const string DotPath = "Assets/MazeParty/Prefabs/Board/World/KeyShopRouteHemisphere.prefab";
        private const string MeshPath = "Assets/MazeParty/Board/Materials/KeyShopRouteHemisphere.asset";
        private const string MaterialPath = "Assets/MazeParty/Board/Materials/KeyShopRouteYellow.mat";
        private const string ToolkitSparkMaterialPath =
            "Assets/Ignore/AllIn1VfxToolkit/Demo & Assets/Demo/Materials/OrbSparkGlow.mat";
        private const string ScenePath = "Assets/MazeParty/Scenes/Board/Board.unity";

        [MenuItem("MazeParty/Board/Install Local Key Shop Route")]
        public static void Install()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    var topology = root.GetComponentInChildren<BoardTopology>(true);
                    if (topology == null) continue;
                    EnsureView(topology.gameObject);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    AssetDatabase.SaveAssets();
                    return;
                }
                throw new System.InvalidOperationException("Board topology is missing.");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        internal static void EnsureView(GameObject root)
        {
            var view = root.GetComponent<BoardShopRouteView>();
            if (view == null) view = root.AddComponent<BoardShopRouteView>();
            if (!view.HasRequiredReferences) view.Configure(EnsureDotPrefab());
            EditorUtility.SetDirty(view);
        }

        private static GameObject EnsureDotPrefab()
        {
            var sparkMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                ToolkitSparkMaterialPath);
            if (sparkMaterial == null || sparkMaterial.shader == null)
            {
                throw new InvalidOperationException(
                    "The key-shop route requires the All In 1 VFX Toolkit " +
                    "OrbSparkGlow material at " + ToolkitSparkMaterialPath + ".");
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DotPath);
            if (prefab != null)
            {
                if (IsRouteGlowPrefab(prefab))
                {
                    return prefab;
                }
                if (!IsLegacyHemisphere(prefab))
                {
                    throw new InvalidOperationException(
                        "The authored key-shop route prefab is incompatible. " +
                        "Fix it in place instead of letting setup replace its design: " +
                        DotPath + ".");
                }
            }
            else if (AssetDatabase.LoadMainAssetAtPath(DotPath) != null)
            {
                throw new InvalidOperationException(
                    "An incompatible asset exists at " + DotPath + ".");
            }

            return BuildRouteGlowPrefab(sparkMaterial, prefab != null);
        }

        private static GameObject BuildRouteGlowPrefab(
            Material sparkMaterial,
            bool replaceLegacyPrefab)
        {
            var root = replaceLegacyPrefab
                ? PrefabUtility.LoadPrefabContents(DotPath)
                : new GameObject("Key Shop Route Glow");
            try
            {
                for (var index = root.transform.childCount - 1;
                     index >= 0;
                     index--)
                {
                    UnityEngine.Object.DestroyImmediate(
                        root.transform.GetChild(index).gameObject);
                }
                foreach (var component in root.GetComponents<Component>()
                             .Where(item => item is not Transform)
                             .ToArray())
                {
                    UnityEngine.Object.DestroyImmediate(component);
                }

                root.name = "Key Shop Route Glow";
                root.SetActive(true);
                root.transform.SetLocalPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                root.transform.localScale = Vector3.one;

                var particles = root.AddComponent<ParticleSystem>();
                ConfigureRouteGlow(particles, sparkMaterial);

                var result = PrefabUtility.SaveAsPrefabAsset(root, DotPath);
                if (!IsRouteGlowPrefab(result))
                {
                    throw new InvalidOperationException(
                        "The generated key-shop route glow is invalid: " +
                        DotPath + ".");
                }
                return result;
            }
            finally
            {
                if (replaceLegacyPrefab)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private static void ConfigureRouteGlow(
            ParticleSystem particles,
            Material sparkMaterial)
        {
            var main = particles.main;
            main.duration = 1f;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.useUnscaledTime = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.65f, .9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, .003f);
            main.startSize = new ParticleSystem.MinMaxCurve(.09f, .15f);
            main.startRotation = new ParticleSystem.MinMaxCurve(
                0f,
                Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, .68f, .03f, .4f),
                new Color(1f, .96f, .42f, .9f));
            main.maxParticles = 4;
            main.stopAction = ParticleSystemStopAction.None;

            var emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 3f;
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());

            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = .006f;
            shape.radiusThickness = 1f;
            shape.position = Vector3.up * .075f;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, .72f, .05f), 0f),
                    new GradientColorKey(new Color(1f, .98f, .48f), .45f),
                    new GradientColorKey(new Color(1f, .72f, .05f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, .18f),
                    new GradientAlphaKey(.72f, .72f),
                    new GradientAlphaKey(0f, 1f)
                });
            var colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(
                gradient);

            var sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, .45f),
                    new Keyframe(.22f, 1f),
                    new Keyframe(.7f, .72f),
                    new Keyframe(1f, .2f)));

            var velocityOverLifetime = particles.velocityOverLifetime;
            velocityOverLifetime.enabled = false;
            var limitVelocityOverLifetime = particles.limitVelocityOverLifetime;
            limitVelocityOverLifetime.enabled = false;
            var inheritVelocity = particles.inheritVelocity;
            inheritVelocity.enabled = false;
            var forceOverLifetime = particles.forceOverLifetime;
            forceOverLifetime.enabled = false;
            var colorBySpeed = particles.colorBySpeed;
            colorBySpeed.enabled = false;
            var sizeBySpeed = particles.sizeBySpeed;
            sizeBySpeed.enabled = false;
            var rotationOverLifetime = particles.rotationOverLifetime;
            rotationOverLifetime.enabled = false;
            var rotationBySpeed = particles.rotationBySpeed;
            rotationBySpeed.enabled = false;
            var externalForces = particles.externalForces;
            externalForces.enabled = false;
            var noise = particles.noise;
            noise.enabled = false;
            var collision = particles.collision;
            collision.enabled = false;
            var trigger = particles.trigger;
            trigger.enabled = false;
            var subEmitters = particles.subEmitters;
            subEmitters.enabled = false;
            var textureSheetAnimation = particles.textureSheetAnimation;
            textureSheetAnimation.enabled = false;
            var lights = particles.lights;
            lights.enabled = false;
            var trails = particles.trails;
            trails.enabled = false;
            var customData = particles.customData;
            customData.enabled = false;

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = sparkMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sortingOrder = 1;
        }

        private static bool IsRouteGlowPrefab(GameObject prefab)
        {
            if (prefab == null ||
                prefab.GetComponentsInChildren<Collider>(true).Length > 0 ||
                prefab.GetComponentsInChildren<Collider2D>(true).Length > 0 ||
                prefab.GetComponentsInChildren<NetworkObject>(true).Length > 0 ||
                prefab.GetComponentsInChildren<Light>(true).Length > 0 ||
                prefab.GetComponentsInChildren<MonoBehaviour>(true).Length > 0 ||
                prefab.GetComponentsInChildren<Transform>(true).Any(item =>
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                        item.gameObject) > 0))
            {
                return false;
            }

            var particleSystems = prefab.GetComponentsInChildren<
                ParticleSystem>(true);
            if (particleSystems.Length != 1 ||
                particleSystems[0].transform != prefab.transform)
            {
                return false;
            }

            var main = particleSystems[0].main;
            var renderer = particleSystems[0]
                .GetComponent<ParticleSystemRenderer>();
            if (!main.loop || !main.playOnAwake || renderer == null ||
                renderer.sharedMaterial == null ||
                renderer.sharedMaterial.shader == null)
            {
                return false;
            }

            var materialPath = AssetDatabase.GetAssetPath(
                    renderer.sharedMaterial)
                .Replace('\\', '/');
            return materialPath.StartsWith(
                       "Assets/Ignore/AllIn1VfxToolkit/",
                       StringComparison.OrdinalIgnoreCase) &&
                   renderer.sharedMaterial.shader.name.IndexOf(
                       "GrabPass",
                       StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool IsLegacyHemisphere(GameObject prefab)
        {
            if (prefab == null || prefab.transform.childCount != 0)
            {
                return false;
            }

            var filter = prefab.GetComponent<MeshFilter>();
            var renderer = prefab.GetComponent<MeshRenderer>();
            return filter != null && renderer != null &&
                   AssetDatabase.GetAssetPath(filter.sharedMesh) == MeshPath &&
                   AssetDatabase.GetAssetPath(renderer.sharedMaterial) ==
                   MaterialPath;
        }
    }
}
