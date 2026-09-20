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
    /// Builds the prefab-free, shared-camera Bomb Passing world. The network
    /// view owns the player visuals; this scene authors only the arena and bomb.
    /// Existing material assets are reused when setup is run again.
    /// </summary>
    public static class BombPassingProjectSetup
    {
        private const string MenuPath = "MazeParty/Minigames/Rebuild Bomb Passing";
        private const string ProjectRoot = "Assets/MazeParty";
        private const string SceneFolder = ProjectRoot + "/Scenes/Minigames";
        private const string MaterialFolder =
            ProjectRoot + "/Art/Minigames/BombPassing/Materials";
        public const string ScenePath = SceneFolder + "/BombPassing.unity";
        public const float ArenaCenterX = 1380f;
        public const float WalkBoundary = 8f;
        public const float WallBoundary = 9f;

        private static readonly Vector3[] SpawnPoints =
        {
            new Vector3(-5f, 0f, -5f),
            new Vector3(5f, 0f, -5f),
            new Vector3(5f, 0f, 5f),
            new Vector3(-5f, 0f, 5f)
        };

        private static readonly Color[] PlayerColors =
        {
            new Color(0.18f, 0.58f, 1f),
            new Color(1f, 0.32f, 0.24f),
            new Color(0.25f, 0.86f, 0.48f),
            new Color(0.82f, 0.35f, 1f)
        };

        [MenuItem(MenuPath)]
        public static void RebuildBombPassing()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("Bomb Passing rebuild canceled; scene edits were preserved.");
                return;
            }

            BuildBombPassingAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("Bomb Passing shared-camera arena is ready.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRebuildBombPassing()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static void BuildBombPassingAssets()
        {
            EnsureFolder(SceneFolder);
            EnsureFolder(MaterialFolder);
            var materials = CreateMaterials();
            BuildScene(materials);
            AssetDatabase.SaveAssets();
        }

        private static void BuildScene(Materials materials)
        {
            var previousActive = SceneManager.GetActiveScene();
            var previousPath = previousActive.path;
            var loaded = SceneManager.GetSceneByPath(ScenePath);
            var replaceSingleOpenScene = SceneManager.sceneCount == 1 &&
                ((loaded.IsValid() && loaded.isLoaded) ||
                 string.IsNullOrEmpty(previousPath));

            if (loaded.IsValid() && loaded.isLoaded)
            {
                if (loaded.isDirty)
                {
                    throw new InvalidOperationException(
                        "BombPassing.unity has unsaved edits. Save or discard them first.");
                }
                if (!replaceSingleOpenScene)
                {
                    EditorSceneManager.CloseScene(loaded, true);
                }
            }

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                replaceSingleOpenScene ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            var root = new GameObject("Bomb Passing Network State");
            var arena = new GameObject("Arena Presentation");
            arena.transform.SetParent(root.transform, false);
            arena.transform.position = new Vector3(ArenaCenterX, 0f, 0f);
            var references = CreateArena(arena.transform, materials);
            CreateLighting(arena.transform);
            var sharedCamera = CreateSharedCamera(root.transform);
            CreateArtReplacementAnchors(root.transform);

            // NGO assigns an in-scene NetworkObject hash only after the scene
            // has a saved asset path.
            EditorSceneManager.SaveScene(scene, ScenePath);
            root.AddComponent<NetworkObject>();
            var state = root.AddComponent<NetworkBombPassingState>();
            var view = root.AddComponent<BombPassingNetworkView>();
            view.Configure(state, sharedCamera, references.PlayerRoot,
                arena, references.Bomb, references.BombRenderer,
                references.BombLight);

            ValidateScene(root, view, references);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureInBuildSettings();

            if (previousPath == ScenePath)
            {
                SceneManager.SetActiveScene(scene);
            }
            else if (previousActive.IsValid() && previousActive.isLoaded &&
                     previousActive.path != ScenePath)
            {
                SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static ArenaReferences CreateArena(
            Transform parent, Materials materials)
        {
            CreatePrimitive("Arena Floor", PrimitiveType.Cube, parent,
                new Vector3(0f, -0.45f, 0f), Quaternion.identity,
                new Vector3(18.2f, 0.8f, 18.2f), materials.Floor);
            CreatePrimitive("Inner Floor", PrimitiveType.Cube, parent,
                new Vector3(0f, -0.035f, 0f), Quaternion.identity,
                new Vector3(16.2f, 0.025f, 16.2f), materials.InnerFloor);

            var walls = new GameObject("Boundary Walls").transform;
            walls.SetParent(parent, false);
            CreatePrimitive("North Wall", PrimitiveType.Cube, walls,
                new Vector3(0f, 0.38f, WallBoundary), Quaternion.identity,
                new Vector3(18.5f, 0.75f, 0.36f), materials.Wall);
            CreatePrimitive("South Wall", PrimitiveType.Cube, walls,
                new Vector3(0f, 0.38f, -WallBoundary), Quaternion.identity,
                new Vector3(18.5f, 0.75f, 0.36f), materials.Wall);
            CreatePrimitive("West Wall", PrimitiveType.Cube, walls,
                new Vector3(-WallBoundary, 0.38f, 0f), Quaternion.identity,
                new Vector3(0.36f, 0.75f, 18.2f), materials.Wall);
            CreatePrimitive("East Wall", PrimitiveType.Cube, walls,
                new Vector3(WallBoundary, 0.38f, 0f), Quaternion.identity,
                new Vector3(0.36f, 0.75f, 18.2f), materials.Wall);

            var center = new GameObject("Center Spawn Platform").transform;
            center.SetParent(parent, false);
            CreatePrimitive("Outer Ring", PrimitiveType.Cylinder, center,
                new Vector3(0f, 0.02f, 0f), Quaternion.identity,
                new Vector3(2.6f, 0.035f, 2.6f), materials.Ring);
            CreatePrimitive("Center Disc", PrimitiveType.Cylinder, center,
                new Vector3(0f, 0.055f, 0f), Quaternion.identity,
                new Vector3(1.85f, 0.025f, 1.85f), materials.Center);

            var spawnRoot = new GameObject("Player Spawn Markers").transform;
            spawnRoot.SetParent(parent, false);
            for (var slot = 0; slot < SpawnPoints.Length; slot++)
            {
                var position = SpawnPoints[slot] + Vector3.up * 0.035f;
                CreatePrimitive("Spawn " + (slot + 1),
                    PrimitiveType.Cylinder, spawnRoot, position,
                    Quaternion.identity, new Vector3(1.18f, 0.015f, 1.18f),
                    materials.Spawns[slot]);
            }

            var playerRoot = new GameObject("Player Visuals").transform;
            playerRoot.SetParent(parent, false);

            var bomb = CreatePrimitive("Bomb", PrimitiveType.Sphere, parent,
                new Vector3(0f, 0.76f, 0f), Quaternion.identity,
                Vector3.one * 1.1f, materials.Bomb);
            var fuse = CreatePrimitive("Bomb Fuse", PrimitiveType.Cylinder,
                bomb.transform, new Vector3(0f, 0.55f, 0f),
                Quaternion.identity, new Vector3(0.14f, 0.17f, 0.14f),
                materials.Fuse);
            fuse.transform.localScale = new Vector3(0.14f, 0.17f, 0.14f);

            var lightObject = new GameObject("Bomb Warning Light");
            lightObject.transform.SetParent(bomb.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.52f, 0f);
            var bombLight = lightObject.AddComponent<Light>();
            bombLight.type = LightType.Point;
            bombLight.color = new Color(1f, 0.26f, 0.08f);
            bombLight.range = 5f;
            bombLight.intensity = 0.8f;
            bombLight.shadows = LightShadows.None;

            return new ArenaReferences
            {
                PlayerRoot = playerRoot,
                Bomb = bomb.transform,
                BombRenderer = bomb.GetComponent<Renderer>(),
                BombLight = bombLight
            };
        }

        private static void CreateLighting(Transform parent)
        {
            var lightObject = new GameObject("Bomb Passing Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(0.86f, 0.91f, 1f);
        }

        private static CinemachineCamera CreateSharedCamera(Transform parent)
        {
            var cameraObject = new GameObject("CM_BombPassingShared");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.SetPositionAndRotation(
                new Vector3(ArenaCenterX, 24f, 0f),
                Quaternion.Euler(90f, 0f, 0f));
            var camera = cameraObject.AddComponent<CinemachineCamera>();
            camera.Priority = 0;
            var lens = camera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize = 12f;
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
            new GameObject("Bomb Art Anchor").transform.SetParent(anchors, false);
            new GameObject("Player Art Anchor").transform.SetParent(anchors, false);
            new GameObject("VFX Anchor").transform.SetParent(anchors, false);
            new GameObject("Audio Anchor").transform.SetParent(anchors, false);
        }

        private static GameObject CreatePrimitive(
            string name, PrimitiveType type, Transform parent,
            Vector3 position, Quaternion rotation, Vector3 scale,
            Material material)
        {
            var item = GameObject.CreatePrimitive(type);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localRotation = rotation;
            item.transform.localScale = scale;
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
                Floor = CreateOrLoadMaterial("Floor",
                    new Color(0.035f, 0.075f, 0.12f)),
                InnerFloor = CreateOrLoadMaterial("InnerFloor",
                    new Color(0.075f, 0.15f, 0.19f)),
                Wall = CreateOrLoadMaterial("Wall",
                    new Color(0.2f, 0.3f, 0.35f)),
                Ring = CreateOrLoadMaterial("Ring",
                    new Color(0.78f, 0.42f, 0.13f)),
                Center = CreateOrLoadMaterial("Center",
                    new Color(0.12f, 0.23f, 0.28f)),
                Bomb = CreateOrLoadMaterial("Bomb",
                    new Color(0.9f, 0.24f, 0.1f), true),
                Fuse = CreateOrLoadMaterial("Fuse",
                    new Color(1f, 0.72f, 0.19f)),
                Spawns = new Material[SpawnPoints.Length]
            };
            for (var slot = 0; slot < SpawnPoints.Length; slot++)
            {
                materials.Spawns[slot] = CreateOrLoadMaterial(
                    "Spawn" + (slot + 1),
                    Color.Lerp(PlayerColors[slot], Color.black, 0.35f));
            }
            return materials;
        }

        private static Material CreateOrLoadMaterial(
            string name, Color color, bool emissive = false)
        {
            var path = MaterialFolder + "/BombPassing" + name + ".mat";
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
            material = new Material(shader) { name = "BombPassing" + name };
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.28f);
            }
            if (emissive && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.5f);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void ValidateScene(
            GameObject root, BombPassingNetworkView view,
            ArenaReferences references)
        {
            if (root.GetComponent<NetworkObject>() == null ||
                root.GetComponent<NetworkBombPassingState>() == null ||
                view == null ||
                references.PlayerRoot == null ||
                references.Bomb == null ||
                references.BombRenderer == null ||
                references.BombLight == null ||
                FindDescendant(root.transform, "Boundary Walls")?.childCount != 4 ||
                FindDescendant(root.transform, "Player Spawn Markers")?.childCount != 4 ||
                root.GetComponentsInChildren<CinemachineCamera>(true).Length != 1 ||
                root.GetComponentsInChildren<Camera>(true).Length != 0 ||
                root.GetComponentsInChildren<AudioListener>(true).Length != 0 ||
                root.GetComponentsInChildren<Canvas>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "Generated Bomb Passing scene failed its network, arena, camera, or no-HUD contract.");
            }
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }
            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindDescendant(root.GetChild(index), name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
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
            scenes.RemoveAll(scene => scene.path == ScenePath);
            var after = scenes.FindIndex(scene => scene.path ==
                "Assets/MazeParty/Scenes/Minigames/BouncingBalls.unity");
            scenes.Insert(after >= 0 ? after + 1 : scenes.Count,
                new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private sealed class ArenaReferences
        {
            public Transform PlayerRoot;
            public Transform Bomb;
            public Renderer BombRenderer;
            public Light BombLight;
        }

        private sealed class Materials
        {
            public Material Floor;
            public Material InnerFloor;
            public Material Wall;
            public Material Ring;
            public Material Center;
            public Material Bomb;
            public Material Fuse;
            public Material[] Spawns;
        }
    }
}
