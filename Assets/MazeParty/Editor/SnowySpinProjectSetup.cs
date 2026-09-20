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
    /// Authors the fixed ice disk and four ball presentations for Snowy Spin.
    /// Running setup again preserves an existing scene and its manual edits.
    /// </summary>
    public static class SnowySpinProjectSetup
    {
        private const string MenuPath = "MazeParty/Minigames/Create Snowy Spin";
        private const string ProjectRoot = "Assets/MazeParty";
        private const string SceneFolder = ProjectRoot + "/Scenes/Minigames";
        private const string MaterialFolder =
            ProjectRoot + "/Art/Minigames/SnowySpin/Materials";
        public const string ScenePath = SceneFolder + "/SnowySpin.unity";
        public const float ArenaCenterX = 1500f;
        public const float ArenaRadius = 8f;
        public const float BallRadius = 0.65f;
        public const float SpawnRadius = 4.5f;

        private static readonly Vector3[] SpawnPoints =
        {
            new Vector3(0f, 0f, SpawnRadius),
            new Vector3(SpawnRadius, 0f, 0f),
            new Vector3(0f, 0f, -SpawnRadius),
            new Vector3(-SpawnRadius, 0f, 0f)
        };

        private static readonly Color[] PlayerColors =
        {
            new Color(0.18f, 0.58f, 1f),
            new Color(1f, 0.32f, 0.24f),
            new Color(0.25f, 0.86f, 0.48f),
            new Color(0.82f, 0.35f, 1f)
        };

        [MenuItem(MenuPath)]
        public static void CreateSnowySpin()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("Snowy Spin setup canceled; scene edits were preserved.");
                return;
            }

            BuildSnowySpinAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("Snowy Spin shared-camera ice arena is ready.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanCreateSnowySpin()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static void BuildSnowySpinAssets()
        {
            EnsureFolder(SceneFolder);
            EnsureFolder(MaterialFolder);
            var materials = CreateMaterials();

            // A scene asset may have been hand-edited after first generation.
            // Never replace it implicitly when setup or tests are rerun.
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
                NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            try
            {
                var root = new GameObject("Snowy Spin Network State");
                var arena = new GameObject("Arena Presentation");
                arena.transform.SetParent(root.transform, false);
                arena.transform.position = new Vector3(ArenaCenterX, 0f, 0f);
                var references = CreateArena(arena.transform, materials);
                CreateLighting(arena.transform);
                var sharedCamera = CreateSharedCamera(root.transform);
                CreateArtReplacementAnchors(root.transform);

                // NGO assigns an in-scene NetworkObject identity after the
                // scene has an asset path.
                EditorSceneManager.SaveScene(scene, ScenePath);
                root.AddComponent<NetworkObject>();
                var state = root.AddComponent<NetworkSnowySpinState>();
                var view = root.AddComponent<SnowySpinNetworkView>();
                view.Configure(state, sharedCamera, arena,
                    references.PlayerBalls, references.BallRenderers);

                ValidateScene(root, references);
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

        private static ArenaReferences CreateArena(
            Transform parent, Materials materials)
        {
            CreatePrimitive("Ice Edge", PrimitiveType.Cylinder, parent,
                new Vector3(0f, -0.38f, 0f),
                new Vector3(ArenaRadius * 2f, 0.38f, ArenaRadius * 2f),
                materials.Edge);
            CreatePrimitive("Ice Surface", PrimitiveType.Cylinder, parent,
                new Vector3(0f, -0.34f, 0f),
                new Vector3(15.45f, 0.36f, 15.45f), materials.Ice);
            CreatePrimitive("Center Marker", PrimitiveType.Cylinder, parent,
                new Vector3(0f, 0.022f, 0f),
                new Vector3(1.6f, 0.015f, 1.6f), materials.Center);

            var spawnRoot = new GameObject("Player Spawn Markers").transform;
            spawnRoot.SetParent(parent, false);
            var ballRoot = new GameObject("Player Balls").transform;
            ballRoot.SetParent(parent, false);
            var references = new ArenaReferences
            {
                PlayerBalls = new Transform[SpawnPoints.Length],
                BallRenderers = new Renderer[SpawnPoints.Length]
            };
            for (var slot = 0; slot < SpawnPoints.Length; slot++)
            {
                var spawn = SpawnPoints[slot];
                CreatePrimitive("Spawn " + (slot + 1),
                    PrimitiveType.Cylinder, spawnRoot,
                    spawn + Vector3.up * 0.035f,
                    new Vector3(1.5f, 0.012f, 1.5f),
                    materials.Spawns[slot]);

                var ball = CreatePrimitive("Player Ball " + (slot + 1),
                    PrimitiveType.Sphere, ballRoot,
                    spawn + Vector3.up * BallRadius,
                    Vector3.one * BallRadius * 2f,
                    materials.Balls[slot]);
                // The asymmetric white marks make actual rolling readable
                // from the shared top-down camera without a gameplay HUD.
                CreatePrimitive("Roll Cap", PrimitiveType.Sphere,
                    ball.transform, new Vector3(0f, 0.49f, 0f),
                    new Vector3(0.32f, 0.045f, 0.32f),
                    materials.RollMark);
                CreatePrimitive("Roll Spot", PrimitiveType.Sphere,
                    ball.transform, new Vector3(0.46f, 0.07f, 0f),
                    new Vector3(0.16f, 0.09f, 0.16f),
                    materials.RollMark);
                references.PlayerBalls[slot] = ball.transform;
                references.BallRenderers[slot] = ball.GetComponent<Renderer>();
            }
            return references;
        }

        private static void CreateLighting(Transform parent)
        {
            var lightObject = new GameObject("Snowy Spin Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(60f, -35f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.color = new Color(0.82f, 0.93f, 1f);
        }

        private static CinemachineCamera CreateSharedCamera(Transform parent)
        {
            var cameraObject = new GameObject("CM_SnowySpinShared");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.SetPositionAndRotation(
                new Vector3(ArenaCenterX, 23f, 0f),
                Quaternion.Euler(90f, 0f, 0f));
            var camera = cameraObject.AddComponent<CinemachineCamera>();
            camera.Priority = 0;
            var lens = camera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize = 11f;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 100f;
            camera.Lens = lens;
            return camera;
        }

        private static void CreateArtReplacementAnchors(Transform parent)
        {
            var anchors = new GameObject("Art Replacement Anchors").transform;
            anchors.SetParent(parent, false);
            new GameObject("Arena Art Anchor").transform.SetParent(anchors, false);
            new GameObject("Ball Art Anchor").transform.SetParent(anchors, false);
            new GameObject("VFX Anchor").transform.SetParent(anchors, false);
            new GameObject("Audio Anchor").transform.SetParent(anchors, false);
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
                Edge = CreateOrLoadMaterial("Edge",
                    new Color(0.15f, 0.52f, 0.72f), 0.55f),
                Ice = CreateOrLoadMaterial("Ice",
                    new Color(0.6f, 0.85f, 0.94f), 0.92f),
                Center = CreateOrLoadMaterial("Center",
                    new Color(0.89f, 0.96f, 1f), 0.85f),
                RollMark = CreateOrLoadMaterial("RollMark",
                    new Color(0.98f, 0.99f, 1f), 0.45f),
                Spawns = new Material[SpawnPoints.Length],
                Balls = new Material[SpawnPoints.Length]
            };
            for (var slot = 0; slot < SpawnPoints.Length; slot++)
            {
                materials.Spawns[slot] = CreateOrLoadMaterial(
                    "Spawn" + (slot + 1),
                    Color.Lerp(PlayerColors[slot], Color.white, 0.42f),
                    0.62f);
                materials.Balls[slot] = CreateOrLoadMaterial(
                    "Ball" + (slot + 1), PlayerColors[slot], 0.74f);
            }
            return materials;
        }

        private static Material CreateOrLoadMaterial(
            string name, Color color, float smoothness)
        {
            var path = MaterialFolder + "/SnowySpin" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException("URP Lit shader is required.");
            }
            material = new Material(shader) { name = "SnowySpin" + name };
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void ValidateScene(
            GameObject root, ArenaReferences references)
        {
            if (root.GetComponent<NetworkObject>() == null ||
                root.GetComponent<NetworkSnowySpinState>() == null ||
                root.GetComponent<SnowySpinNetworkView>() == null ||
                references.PlayerBalls.Length != 4 ||
                references.BallRenderers.Length != 4 ||
                root.GetComponentsInChildren<CinemachineCamera>(true).Length != 1 ||
                root.GetComponentsInChildren<Camera>(true).Length != 0 ||
                root.GetComponentsInChildren<AudioListener>(true).Length != 0 ||
                root.GetComponentsInChildren<Canvas>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "Snowy Spin scene failed its network, arena, shared-camera or no-HUD contract.");
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
                throw new InvalidOperationException("Invalid asset folder: " + path);
            }
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash),
                path.Substring(slash + 1));
        }

        private static void EnsureInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            var snowIndex = scenes.FindIndex(scene => scene.path == ScenePath);
            if (snowIndex >= 0)
            {
                if (!scenes[snowIndex].enabled)
                {
                    scenes[snowIndex] = new EditorBuildSettingsScene(ScenePath, true);
                    EditorBuildSettings.scenes = scenes.ToArray();
                }
                return;
            }
            var after = scenes.FindIndex(scene => scene.path ==
                "Assets/MazeParty/Scenes/Minigames/BombPassing.unity");
            scenes.Insert(after >= 0 ? after + 1 : scenes.Count,
                new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private sealed class ArenaReferences
        {
            public Transform[] PlayerBalls;
            public Renderer[] BallRenderers;
        }

        private sealed class Materials
        {
            public Material Edge;
            public Material Ice;
            public Material Center;
            public Material RollMark;
            public Material[] Spawns;
            public Material[] Balls;
        }
    }
}
