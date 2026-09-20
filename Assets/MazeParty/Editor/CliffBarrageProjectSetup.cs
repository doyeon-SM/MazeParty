using System;
using System.Collections.Generic;
using MazeParty.Multiplayer;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    /// <summary>
    /// Creates the authored cliff, camera and reusable hazard presentation.
    /// An existing scene or material is left intact on subsequent setup runs.
    /// </summary>
    public static class CliffBarrageProjectSetup
    {
        private const string MenuPath =
            "MazeParty/Minigames/Create Cliff Barrage";
        private const string ProjectRoot = "Assets/MazeParty";
        private const string SceneFolder = ProjectRoot + "/Scenes/Minigames";
        private const string MaterialFolder =
            ProjectRoot + "/Art/Minigames/CliffBarrage/Materials";
        public const string ScenePath = SceneFolder +
            "/CliffBarrage.unity";

        private static readonly Vector3[] SpawnPoints =
        {
            new Vector3(0f, 0f, 4.5f),
            new Vector3(4.5f, 0f, 0f),
            new Vector3(0f, 0f, -4.5f),
            new Vector3(-4.5f, 0f, 0f)
        };

        [MenuItem(MenuPath)]
        public static void CreateCliffBarrage()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("Cliff Barrage setup canceled; scene edits were preserved.");
                return;
            }

            BuildCliffBarrageAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("Cliff Barrage shared-camera arena is ready.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanCreateCliffBarrage() =>
            !EditorApplication.isPlayingOrWillChangePlaymode;

        public static void BuildCliffBarrageAssets()
        {
            EnsureFolder(SceneFolder);
            EnsureFolder(MaterialFolder);
            var materials = CreateMaterials();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                BuildScene(materials);
            }

            EnsureInBuildSettings();
            AssetDatabase.SaveAssets();
        }

        private static void BuildScene(Materials materials)
        {
            var previousActive = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            try
            {
                var root = new GameObject("Cliff Barrage Network State");
                var arena = new GameObject("Arena Presentation");
                arena.transform.SetParent(root.transform, false);
                arena.transform.position = new Vector3(
                    CliffBarrageNetworkView.ArenaCenterX, 0f, 0f);

                CreateCliff(arena.transform, materials);
                var playerRoot = new GameObject("Player Presentations")
                    .transform;
                playerRoot.SetParent(arena.transform, false);
                CreateSpawnMarkers(arena.transform, materials);
                var projectiles = CreateProjectilePool(
                    arena.transform, materials);
                var lasers = CreateLaserPool(
                    arena.transform, materials);
                CreateLighting(arena.transform);
                CreateArtReplacementAnchors(arena.transform);
                var camera = CreateSharedCamera(root.transform);

                // NGO assigns an in-scene NetworkObject identity after the
                // scene has an asset path.
                EditorSceneManager.SaveScene(scene, ScenePath);
                root.AddComponent<NetworkObject>();
                var state = root.AddComponent<NetworkCliffBarrageState>();
                var view = root.AddComponent<CliffBarrageNetworkView>();
                view.Configure(state, camera, arena, playerRoot,
                    projectiles, lasers.Roots,
                    lasers.WarningBeams, lasers.FiringBeams);

                ValidateScene(root, view);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActive);
                }
                if (scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void CreateCliff(Transform parent, Materials materials)
        {
            CreatePrimitive("Void Below", PrimitiveType.Cube, parent,
                new Vector3(0f, -8f, 0f),
                new Vector3(56f, 0.2f, 56f),
                materials.Void);
            CreatePrimitive("Cliff Platform", PrimitiveType.Cube, parent,
                new Vector3(0f, -0.38f, 0f),
                new Vector3(16f, 0.76f, 16f),
                materials.Platform);
            CreatePrimitive("North Cliff Face", PrimitiveType.Cube, parent,
                new Vector3(0f, -3.5f, 8f),
                new Vector3(16f, 6.25f, 0.7f),
                materials.CliffFace);
            CreatePrimitive("South Cliff Face", PrimitiveType.Cube, parent,
                new Vector3(0f, -3.5f, -8f),
                new Vector3(16f, 6.25f, 0.7f),
                materials.CliffFace);
            CreatePrimitive("East Cliff Face", PrimitiveType.Cube, parent,
                new Vector3(8f, -3.5f, 0f),
                new Vector3(0.7f, 6.25f, 16f),
                materials.CliffFace);
            CreatePrimitive("West Cliff Face", PrimitiveType.Cube, parent,
                new Vector3(-8f, -3.5f, 0f),
                new Vector3(0.7f, 6.25f, 16f),
                materials.CliffFace);

            // Narrow inlaid strips reveal the safe surface edge without
            // forming a physical wall that could prevent a fall.
            CreatePrimitive("North Edge Stripe", PrimitiveType.Cube, parent,
                new Vector3(0f, 0.012f, 7.8f),
                new Vector3(15.7f, 0.025f, 0.11f),
                materials.Edge);
            CreatePrimitive("South Edge Stripe", PrimitiveType.Cube, parent,
                new Vector3(0f, 0.012f, -7.8f),
                new Vector3(15.7f, 0.025f, 0.11f),
                materials.Edge);
            CreatePrimitive("East Edge Stripe", PrimitiveType.Cube, parent,
                new Vector3(7.8f, 0.012f, 0f),
                new Vector3(0.11f, 0.025f, 15.7f),
                materials.Edge);
            CreatePrimitive("West Edge Stripe", PrimitiveType.Cube, parent,
                new Vector3(-7.8f, 0.012f, 0f),
                new Vector3(0.11f, 0.025f, 15.7f),
                materials.Edge);
            CreatePrimitive("Center Aim Mark", PrimitiveType.Cylinder,
                parent, new Vector3(0f, 0.018f, 0f),
                new Vector3(0.8f, 0.012f, 0.8f),
                materials.Center);
        }

        private static void CreateSpawnMarkers(
            Transform parent, Materials materials)
        {
            var markerRoot = new GameObject("Player Spawn Markers")
                .transform;
            markerRoot.SetParent(parent, false);
            for (var slot = 0; slot < SpawnPoints.Length; slot++)
            {
                CreatePrimitive("Spawn " + (slot + 1),
                    PrimitiveType.Cylinder, markerRoot,
                    SpawnPoints[slot] + Vector3.up * 0.015f,
                    new Vector3(1.2f, 0.012f, 1.2f),
                    materials.SpawnMarker);
            }
        }

        private static Transform[] CreateProjectilePool(
            Transform parent, Materials materials)
        {
            var pool = new GameObject("Pooled Projectiles").transform;
            pool.SetParent(parent, false);
            var shells = new Transform[
                NetworkCliffBarrageState.ProjectilePoolSize];
            for (var index = 0; index < shells.Length; index++)
            {
                var shell = CreatePrimitive(
                    "Projectile " + (index + 1),
                    PrimitiveType.Sphere, pool,
                    new Vector3(0f,
                        CliffBarrageNetworkView.ProjectilePresentationHeight,
                        0f),
                    Vector3.one * 0.72f,
                    materials.Projectile);
                CreatePrimitive("Shell Highlight", PrimitiveType.Sphere,
                    shell.transform,
                    new Vector3(0f, 0.32f, 0f),
                    new Vector3(0.65f, 0.13f, 0.65f),
                    materials.ProjectileHighlight);
                shell.SetActive(false);
                shells[index] = shell.transform;
            }
            return shells;
        }

        private static LaserReferences CreateLaserPool(
            Transform parent, Materials materials)
        {
            var pool = new GameObject("Pooled Laser Rigs").transform;
            pool.SetParent(parent, false);
            var count = NetworkCliffBarrageState.LaserPoolSize;
            var result = new LaserReferences
            {
                Roots = new GameObject[count],
                WarningBeams = new Transform[count],
                FiringBeams = new Transform[count]
            };

            for (var index = 0; index < count; index++)
            {
                var root = new GameObject("Laser Rig " + (index + 1));
                root.transform.SetParent(pool, false);
                var warning = CreatePrimitive("One Second Telegraph",
                    PrimitiveType.Cube, root.transform,
                    new Vector3(0f, 0.04f, 0f),
                    new Vector3(0.14f, 0.08f, 1f),
                    materials.LaserWarning);
                var firing = CreatePrimitive("Half Second Beam",
                    PrimitiveType.Cube, root.transform,
                    new Vector3(0f, 0.75f, 0f),
                    new Vector3(0.65f, 1.5f, 1f),
                    materials.LaserFiring);
                warning.SetActive(false);
                firing.SetActive(false);
                root.SetActive(false);
                result.Roots[index] = root;
                result.WarningBeams[index] = warning.transform;
                result.FiringBeams[index] = firing.transform;
            }

            return result;
        }

        private static void CreateLighting(Transform parent)
        {
            var lightObject = new GameObject(
                "Cliff Barrage Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation =
                Quaternion.Euler(58f, -35f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.35f;
            light.color = new Color(0.88f, 0.91f, 1f);
        }

        private static CinemachineCamera CreateSharedCamera(
            Transform parent)
        {
            var cameraObject = new GameObject("CM_CliffBarrageShared");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.SetPositionAndRotation(
                CliffBarrageNetworkView.SharedCameraPosition,
                CliffBarrageNetworkView.SharedCameraRotation);
            var camera = cameraObject.AddComponent<CinemachineCamera>();
            camera.Priority = 0;
            var lens = camera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize =
                CliffBarrageNetworkView.SharedCameraOrthographicSize;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 100f;
            camera.Lens = lens;
            return camera;
        }

        private static void CreateArtReplacementAnchors(Transform parent)
        {
            var anchors = new GameObject("Art Replacement Anchors")
                .transform;
            anchors.SetParent(parent, false);
            new GameObject("Arena Art Anchor")
                .transform.SetParent(anchors, false);
            new GameObject("Projectile Art Anchor")
                .transform.SetParent(anchors, false);
            new GameObject("Laser VFX Anchor")
                .transform.SetParent(anchors, false);
            new GameObject("Audio Anchor")
                .transform.SetParent(anchors, false);
        }

        private static GameObject CreatePrimitive(
            string name, PrimitiveType type, Transform parent,
            Vector3 localPosition, Vector3 localScale, Material material)
        {
            var item = GameObject.CreatePrimitive(type);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = localPosition;
            item.transform.localScale = localScale;
            item.GetComponent<Renderer>().sharedMaterial = material;
            var collider = item.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
            return item;
        }

        private static Materials CreateMaterials()
        {
            var materials = new Materials
            {
                Platform = CreateOrLoadMaterial("Platform",
                    new Color(0.28f, 0.34f, 0.39f), 0.15f),
                CliffFace = CreateOrLoadMaterial("CliffFace",
                    new Color(0.11f, 0.16f, 0.23f), 0.04f),
                Void = CreateOrLoadMaterial("Void",
                    new Color(0.025f, 0.035f, 0.075f), 0f),
                Edge = CreateOrLoadMaterial("Edge",
                    new Color(0.94f, 0.56f, 0.15f), 0.45f),
                Center = CreateOrLoadMaterial("Center",
                    new Color(0.54f, 0.72f, 0.79f), 0.28f),
                SpawnMarker = CreateOrLoadMaterial("SpawnMarker",
                    new Color(0.72f, 0.79f, 0.84f), 0.4f),
                Projectile = CreateOrLoadMaterial("Projectile",
                    new Color(0.95f, 0.31f, 0.11f), 0.7f),
                ProjectileHighlight = CreateOrLoadMaterial(
                    "ProjectileHighlight",
                    new Color(1f, 0.83f, 0.31f), 0.75f),
                LaserWarning = CreateOrLoadMaterial("LaserWarning",
                    new Color(1f, 0.28f, 0.23f), 0.6f),
                LaserFiring = CreateOrLoadMaterial("LaserFiring",
                    new Color(1f, 0.12f, 0.52f), 0.7f)
            };
            return materials;
        }

        private static Material CreateOrLoadMaterial(
            string name, Color color, float smoothness)
        {
            var path = MaterialFolder + "/CliffBarrage" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "URP Lit shader is required.");
            }
            material = new Material(shader)
            {
                name = "CliffBarrage" + name
            };
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void ValidateScene(
            GameObject root, CliffBarrageNetworkView view)
        {
            if (root.GetComponent<NetworkObject>() == null ||
                root.GetComponent<NetworkCliffBarrageState>() == null ||
                view == null ||
                view.AuthoredProjectileCount !=
                    NetworkCliffBarrageState.ProjectilePoolSize ||
                view.AuthoredLaserCount !=
                    NetworkCliffBarrageState.LaserPoolSize ||
                root.GetComponentsInChildren<CinemachineCamera>(true).Length != 1 ||
                root.GetComponentsInChildren<Camera>(true).Length != 0 ||
                root.GetComponentsInChildren<AudioListener>(true).Length != 0 ||
                root.GetComponentsInChildren<Canvas>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "Cliff Barrage scene failed its world, pool, camera or no-runtime-HUD contract.");
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
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash),
                path.Substring(slash + 1));
        }

        private static void EnsureInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            var existingIndex = scenes.FindIndex(
                scene => scene.path == ScenePath);
            if (existingIndex >= 0)
            {
                if (!scenes[existingIndex].enabled)
                {
                    scenes[existingIndex] = new EditorBuildSettingsScene(
                        ScenePath, true);
                    EditorBuildSettings.scenes = scenes.ToArray();
                }
                return;
            }
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private sealed class LaserReferences
        {
            public GameObject[] Roots;
            public Transform[] WarningBeams;
            public Transform[] FiringBeams;
        }

        private sealed class Materials
        {
            public Material Platform;
            public Material CliffFace;
            public Material Void;
            public Material Edge;
            public Material Center;
            public Material Projectile;
            public Material ProjectileHighlight;
            public Material LaserWarning;
            public Material LaserFiring;
            public Material SpawnMarker;
        }
    }
}
