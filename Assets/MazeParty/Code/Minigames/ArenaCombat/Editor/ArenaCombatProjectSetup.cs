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
    /// Creates the isolated, wall-bounded four-player fight arena once.
    /// Re-running setup never overwrites a scene or material edited by hand.
    /// </summary>
    public static class ArenaCombatProjectSetup
    {
        private const string MenuPath =
            "MazeParty/Minigames/Create Arena Combat";
        private const string SceneFolder =
            "Assets/MazeParty/Scenes/Minigames/ArenaCombat";
        private const string CorePrefabFolder =
            "Assets/MazeParty/Prefabs/Minigames/ArenaCombat";
        private const string MaterialFolder =
            "Assets/MazeParty/Art/Minigames/ArenaCombat/Materials";
        public const string ScenePath =
            "Assets/MazeParty/Scenes/Minigames/ArenaCombat/ArenaCombat.unity";

        private const float CenterX = ArenaCombatNetworkView.ArenaCenterX;
        private const float HalfWidth = ArenaCombatNetworkView.ArenaHalfWidth;
        private const float HalfDepth = ArenaCombatNetworkView.ArenaHalfDepth;

        private static readonly Vector3[] SpawnOffsets =
        {
            new Vector3(-4f, 1f, -4f),
            new Vector3(4f, 1f, -4f),
            new Vector3(-4f, 1f, 4f),
            new Vector3(4f, 1f, 4f)
        };

        private static readonly Color[] PlayerColors =
        {
            new Color(0.18f, 0.57f, 0.98f),
            new Color(0.95f, 0.22f, 0.2f),
            new Color(0.18f, 0.8f, 0.38f),
            new Color(0.68f, 0.3f, 0.93f)
        };

        [MenuItem(MenuPath)]
        public static void CreateArenaCombat()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("Arena Combat setup canceled; scene edits preserved.");
                return;
            }

            BuildAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("Arena Combat scene and spectator cameras are ready.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanCreateArenaCombat() =>
            !EditorApplication.isPlayingOrWillChangePlaymode;

        public static void BuildAssets()
        {
            EnsureFolder(SceneFolder);
            EnsureFolder(CorePrefabFolder);
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
                var root = new GameObject("Arena Combat Network State");
                var arena = new GameObject("Arena Presentation");
                arena.transform.SetParent(root.transform, false);
                var structure = new GameObject("Arena Structure");
                structure.transform.SetParent(arena.transform, false);
                CreateArena(structure.transform, materials);
                MinigameCorePrefabUtility.Connect(structure,
                    CorePrefabFolder + "/ArenaStructure.prefab");
                CreateLighting(arena.transform);
                var spawns = CreateSpawnMarkers(
                    arena.transform,
                    materials.SpawnMaterials);
                var firstPersonCamera = CreateCamera(
                    "CM_ArenaCombatFirstPerson",
                    root.transform,
                    new Vector3(CenterX, 1.75f, -4f),
                    Quaternion.identity,
                    ArenaCombatNetworkView.FirstPersonFieldOfView);
                var spectatorPosition = new Vector3(
                    CenterX, 12f, -14f);
                var spectatorCamera = CreateCamera(
                    "CM_ArenaCombatSpectator",
                    root.transform,
                    spectatorPosition,
                    Quaternion.LookRotation(
                        new Vector3(CenterX, 0.8f, 0f) -
                        spectatorPosition,
                        Vector3.up),
                    ArenaCombatNetworkView.SpectatorFieldOfView);
                CreateReplacementAnchors(root.transform);

                // NGO assigns an in-scene identity after a scene path exists.
                EditorSceneManager.SaveScene(scene, ScenePath);
                root.AddComponent<NetworkObject>();
                var state = root.AddComponent<NetworkArenaCombatState>();
                var view = root.AddComponent<ArenaCombatNetworkView>();
                view.Configure(state, firstPersonCamera,
                    spectatorCamera, arena, spawns);

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

        private static void CreateArena(
            Transform parent,
            Materials materials)
        {
            const float wallThickness = 0.65f;
            const float wallHeight = 3.2f;
            CreatePrimitive("Arena Floor", PrimitiveType.Cube,
                parent, new Vector3(CenterX, -0.3f, 0f),
                new Vector3(HalfWidth * 2f, 0.6f,
                    HalfDepth * 2f), materials.Floor, true);
            CreatePrimitive("North Boundary", PrimitiveType.Cube,
                parent,
                new Vector3(CenterX, wallHeight * 0.5f,
                    HalfDepth + wallThickness * 0.5f),
                new Vector3(HalfWidth * 2f + wallThickness * 2f,
                    wallHeight, wallThickness),
                materials.Wall, true);
            CreatePrimitive("South Boundary", PrimitiveType.Cube,
                parent,
                new Vector3(CenterX, wallHeight * 0.5f,
                    -HalfDepth - wallThickness * 0.5f),
                new Vector3(HalfWidth * 2f + wallThickness * 2f,
                    wallHeight, wallThickness),
                materials.Wall, true);
            CreatePrimitive("West Boundary", PrimitiveType.Cube,
                parent,
                new Vector3(CenterX - HalfWidth -
                    wallThickness * 0.5f,
                    wallHeight * 0.5f, 0f),
                new Vector3(wallThickness, wallHeight,
                    HalfDepth * 2f),
                materials.Wall, true);
            CreatePrimitive("East Boundary", PrimitiveType.Cube,
                parent,
                new Vector3(CenterX + HalfWidth +
                    wallThickness * 0.5f,
                    wallHeight * 0.5f, 0f),
                new Vector3(wallThickness, wallHeight,
                    HalfDepth * 2f),
                materials.Wall, true);

            // Thin visual markings carry no collision; punches and locomotion
            // are adjudicated by the existing authoritative avatar system.
            CreatePrimitive("Center Mark", PrimitiveType.Cylinder,
                parent, new Vector3(CenterX, 0.012f, 0f),
                new Vector3(1.25f, 0.012f, 1.25f),
                materials.Center, false);
            for (var index = -1; index <= 1; index += 2)
            {
                CreatePrimitive(
                    index < 0 ? "West Lane" : "East Lane",
                    PrimitiveType.Cube,
                    parent,
                    new Vector3(CenterX + index * 6.3f,
                        0.012f, 0f),
                    new Vector3(0.05f, 0.012f, 14f),
                    materials.Line,
                    false);
                CreatePrimitive(
                    index < 0 ? "South Lane" : "North Lane",
                    PrimitiveType.Cube,
                    parent,
                    new Vector3(CenterX, 0.012f,
                        index * 6.3f),
                    new Vector3(14f, 0.012f, 0.05f),
                    materials.Line,
                    false);
            }
        }

        private static Transform[] CreateSpawnMarkers(
            Transform parent,
            Material[] materials)
        {
            var spawns = new Transform[4];
            var center = new Vector3(CenterX, 1f, 0f);
            for (var slot = 0; slot < 4; slot++)
            {
                var marker = new GameObject(
                    "Player Spawn " + (slot + 1));
                marker.transform.SetParent(parent, false);
                marker.transform.position =
                    new Vector3(CenterX, 0f, 0f) +
                    SpawnOffsets[slot];
                marker.transform.rotation = Quaternion.LookRotation(
                    center - marker.transform.position,
                    Vector3.up);
                CreatePrimitive("Spawn Ring", PrimitiveType.Cylinder,
                    marker.transform,
                    new Vector3(marker.transform.position.x,
                        0.017f,
                        marker.transform.position.z),
                    new Vector3(0.95f, 0.016f, 0.95f),
                    materials[slot], false);
                marker = MinigameCorePrefabUtility.Connect(marker,
                    CorePrefabFolder + "/PlayerSpawn" + (slot + 1) + ".prefab");
                spawns[slot] = marker.transform;
            }
            return spawns;
        }

        private static CinemachineCamera CreateCamera(
            string name,
            Transform parent,
            Vector3 position,
            Quaternion rotation,
            float fieldOfView)
        {
            var cameraObject = new GameObject(name);
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.SetPositionAndRotation(
                position, rotation);
            var camera = cameraObject.AddComponent<CinemachineCamera>();
            camera.Priority = 0;
            var lens = camera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            lens.FieldOfView = fieldOfView;
            lens.NearClipPlane = 0.08f;
            lens.FarClipPlane = 120f;
            camera.Lens = lens;
            return camera;
        }

        private static GameObject CreatePrimitive(
            string name,
            PrimitiveType primitiveType,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Material material,
            bool keepCollider)
        {
            var primitive = GameObject.CreatePrimitive(primitiveType);
            primitive.name = name;
            primitive.transform.SetParent(parent, false);
            primitive.transform.position = position;
            primitive.transform.localScale = scale;
            primitive.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider)
            {
                UnityEngine.Object.DestroyImmediate(
                    primitive.GetComponent<Collider>());
            }
            return primitive;
        }

        private static void CreateLighting(Transform parent)
        {
            var lightObject = new GameObject("Arena Combat Key Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation =
                Quaternion.Euler(53f, -35f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.color = new Color(1f, 0.95f, 0.9f);
        }

        private static void CreateReplacementAnchors(
            Transform parent)
        {
            var root = new GameObject(
                "Art Replacement Anchors").transform;
            root.SetParent(parent, false);
            new GameObject("Arena Art Anchor")
                .transform.SetParent(root, false);
            new GameObject("Hit VFX Anchor")
                .transform.SetParent(root, false);
        }

        private static Materials CreateMaterials()
        {
            var materials = new Materials
            {
                Floor = CreateOrLoadMaterial("Floor",
                    new Color(0.12f, 0.15f, 0.2f), 0.3f),
                Wall = CreateOrLoadMaterial("Wall",
                    new Color(0.25f, 0.3f, 0.37f), 0.2f),
                Center = CreateOrLoadMaterial("Center",
                    new Color(0.95f, 0.76f, 0.27f), 0.36f),
                Line = CreateOrLoadMaterial("Line",
                    new Color(0.45f, 0.75f, 0.88f), 0.25f),
                SpawnMaterials = new Material[4]
            };
            for (var slot = 0; slot < 4; slot++)
            {
                materials.SpawnMaterials[slot] = CreateOrLoadMaterial(
                    "Spawn" + (slot + 1),
                    PlayerColors[slot],
                    0.3f);
            }
            return materials;
        }

        private static Material CreateOrLoadMaterial(
            string suffix,
            Color color,
            float smoothness)
        {
            var path = MaterialFolder + "/ArenaCombat" + suffix + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "URP Lit shader is required for Arena Combat materials.");
            }
            material = new Material(shader)
            {
                name = "ArenaCombat" + suffix
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
            GameObject root,
            ArenaCombatNetworkView view)
        {
            if (root.GetComponent<NetworkObject>() == null ||
                root.GetComponent<NetworkArenaCombatState>() == null ||
                view == null || view.ArenaPresentation == null ||
                view.FirstPersonCamera == null ||
                view.SpectatorCamera == null ||
                root.GetComponentsInChildren<CinemachineCamera>(true)
                    .Length != 2 ||
                root.GetComponentsInChildren<Camera>(true).Length != 0 ||
                root.GetComponentsInChildren<AudioListener>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "Arena Combat scene failed its network/camera contract.");
            }
            for (var slot = 0; slot < 4; slot++)
            {
                if (view.GetSpawnMarker(slot) == null)
                {
                    throw new InvalidOperationException(
                        "Arena Combat spawn marker is missing: " + slot);
                }
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
            var index = scenes.FindIndex(scene =>
                scene.path == ScenePath);
            if (index >= 0)
            {
                if (!scenes[index].enabled)
                {
                    scenes[index] = new EditorBuildSettingsScene(
                        ScenePath, true);
                    EditorBuildSettings.scenes = scenes.ToArray();
                }
                return;
            }

            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private sealed class Materials
        {
            public Material Floor;
            public Material Wall;
            public Material Center;
            public Material Line;
            public Material[] SpawnMaterials;
        }
    }
}
