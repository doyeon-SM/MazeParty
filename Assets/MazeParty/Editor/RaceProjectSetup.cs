using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.Race;
using MazeParty.Multiplayer;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    /// <summary>
    /// Builds the four-lane Race arena and its prefab-owned timer-only HUD.
    /// Existing prefab styling and material assets are never overwritten.
    /// </summary>
    public static class RaceProjectSetup
    {
        private const string MenuPath =
            "MazeParty/Minigames/Rebuild Race";
        private const string ProjectRoot = "Assets/MazeParty";
        private const string ScenesFolder = ProjectRoot + "/Scenes";
        private const string UiPrefabFolder = ProjectRoot + "/UI/Prefabs";
        private const string MaterialFolder =
            ProjectRoot + "/Art/Minigames/Race/Materials";

        public const string RaceScenePath = ScenesFolder + "/Race.unity";
        public const string HudPrefabPath = UiPrefabFolder + "/RaceHud.prefab";

        [MenuItem(MenuPath)]
        public static void RebuildRace()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log(
                    "Race rebuild canceled; open scene changes were left " +
                    "untouched.");
                return;
            }

            BuildRaceAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(RaceScenePath, OpenSceneMode.Single);
            Debug.Log(
                "Race rebuilt: four vertical lanes, fixed shared camera " +
                "and timer-only prefab HUD.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRebuildRace()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static void BuildRaceAssets()
        {
            EnsureFolders();
            var floor = CreateOrLoadMaterial(
                "RaceFloor",
                new Color(0.08f, 0.11f, 0.16f),
                0.18f);
            var lane = CreateOrLoadMaterial(
                "RaceLane",
                new Color(0.93f, 0.94f, 0.98f),
                0.1f);
            var boundary = CreateOrLoadMaterial(
                "RaceBoundary",
                new Color(0.95f, 0.48f, 0.08f),
                0.2f);
            var finish = CreateOrLoadMaterial(
                "RaceFinish",
                new Color(0.2f, 0.82f, 0.45f),
                0.1f);
            BuildScene(floor, lane, boundary, finish);
            AssetDatabase.SaveAssets();
        }

        private static void BuildScene(
            Material floorMaterial,
            Material laneMaterial,
            Material boundaryMaterial,
            Material finishMaterial)
        {
            var previousActive = SceneManager.GetActiveScene();
            var previousPath = previousActive.path;
            var loaded = SceneManager.GetSceneByPath(RaceScenePath);
            var replaceSingleOpenScene =
                SceneManager.sceneCount == 1 &&
                ((loaded.IsValid() && loaded.isLoaded) ||
                 string.IsNullOrEmpty(previousPath));

            if (loaded.IsValid() && loaded.isLoaded)
            {
                if (loaded.isDirty)
                {
                    throw new InvalidOperationException(
                        "Race.unity has unsaved changes. Save or discard " +
                        "them before rebuilding.");
                }
                if (!replaceSingleOpenScene)
                {
                    EditorSceneManager.CloseScene(loaded, true);
                }
            }

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                replaceSingleOpenScene
                    ? NewSceneMode.Single
                    : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            var root = new GameObject("Race Network State");
            var arena = new GameObject("Arena Presentation");
            arena.transform.SetParent(root.transform, false);
            CreateArena(
                arena.transform,
                floorMaterial,
                laneMaterial,
                boundaryMaterial,
                finishMaterial);
            CreateLighting(arena.transform);
            var camera = CreateCamera(root.transform);
            var playerRoot = new GameObject("Runtime Players");
            playerRoot.transform.SetParent(root.transform, false);
            CreateReplacementAnchors(root.transform);

            // A saved scene path is required before NGO assigns the in-scene
            // NetworkObject hash.
            EditorSceneManager.SaveScene(scene, RaceScenePath);
            root.AddComponent<NetworkObject>();
            var state = root.AddComponent<NetworkRaceState>();
            var view = root.AddComponent<RaceNetworkView>();

            view.Configure(state, camera, playerRoot.transform, arena);
            ValidateSceneContract(root);
            EditorSceneManager.SaveScene(scene, RaceScenePath);
            EnsureInBuildSettings();

            if (previousPath == RaceScenePath)
            {
                SceneManager.SetActiveScene(scene);
            }
            else if (previousActive.IsValid() && previousActive.isLoaded &&
                     previousActive.path != RaceScenePath)
            {
                SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void CreateArena(
            Transform parent,
            Material floorMaterial,
            Material laneMaterial,
            Material boundaryMaterial,
            Material finishMaterial)
        {
            var trackWidth = RaceRules.PlayerCount *
                             NetworkRaceState.LaneWidth + 1.2f;
            CreatePrimitive(
                "Race Track",
                PrimitiveType.Cube,
                parent,
                new Vector3(NetworkRaceState.TrackCenterX, -0.3f, 0f),
                new Vector3(trackWidth, 0.6f, NetworkRaceState.TrackLength),
                floorMaterial,
                true);

            for (var divider = 1;
                 divider < RaceRules.PlayerCount;
                 divider++)
            {
                var x = NetworkRaceState.GetLaneX(divider - 1) +
                        NetworkRaceState.LaneWidth * 0.5f;
                CreatePrimitive(
                    "Lane Divider " + divider,
                    PrimitiveType.Cube,
                    parent,
                    new Vector3(x, 0.025f, 0f),
                    new Vector3(0.08f, 0.05f, NetworkRaceState.TrackLength),
                    laneMaterial,
                    false);
            }

            CreatePrimitive(
                "Start Line",
                PrimitiveType.Cube,
                parent,
                new Vector3(
                    NetworkRaceState.TrackCenterX,
                    0.03f,
                    NetworkRaceState.TrackStartZ),
                new Vector3(trackWidth, 0.06f, 0.32f),
                laneMaterial,
                false);
            CreatePrimitive(
                "Finish Line",
                PrimitiveType.Cube,
                parent,
                new Vector3(
                    NetworkRaceState.TrackCenterX,
                    0.04f,
                    NetworkRaceState.TrackStartZ +
                    NetworkRaceState.TrackLength),
                new Vector3(trackWidth, 0.08f, 0.55f),
                finishMaterial,
                false);

            const float wallThickness = 0.4f;
            const float wallHeight = 1.5f;
            var sideOffset = trackWidth * 0.5f + wallThickness * 0.5f;
            CreatePrimitive(
                "West Boundary",
                PrimitiveType.Cube,
                parent,
                new Vector3(
                    NetworkRaceState.TrackCenterX - sideOffset,
                    wallHeight * 0.5f,
                    0f),
                new Vector3(
                    wallThickness,
                    wallHeight,
                    NetworkRaceState.TrackLength + wallThickness * 2f),
                boundaryMaterial,
                true);
            CreatePrimitive(
                "East Boundary",
                PrimitiveType.Cube,
                parent,
                new Vector3(
                    NetworkRaceState.TrackCenterX + sideOffset,
                    wallHeight * 0.5f,
                    0f),
                new Vector3(
                    wallThickness,
                    wallHeight,
                    NetworkRaceState.TrackLength + wallThickness * 2f),
                boundaryMaterial,
                true);
            CreatePrimitive(
                "South Boundary",
                PrimitiveType.Cube,
                parent,
                new Vector3(
                    NetworkRaceState.TrackCenterX,
                    wallHeight * 0.5f,
                    NetworkRaceState.TrackStartZ - wallThickness * 0.5f),
                new Vector3(trackWidth, wallHeight, wallThickness),
                boundaryMaterial,
                true);

            for (var slot = 0; slot < RaceRules.PlayerCount; slot++)
            {
                CreatePrimitive(
                    "Start Marker " + (slot + 1),
                    PrimitiveType.Cylinder,
                    parent,
                    new Vector3(
                        NetworkRaceState.GetLaneX(slot),
                        0.02f,
                        NetworkRaceState.TrackStartZ + 0.7f),
                    new Vector3(0.55f, 0.025f, 0.55f),
                    boundaryMaterial,
                    false);
            }
        }

        private static CinemachineCamera CreateCamera(Transform parent)
        {
            var cameraObject = new GameObject("CM_RaceShared");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.SetPositionAndRotation(
                RaceNetworkView.SharedCameraPosition,
                RaceNetworkView.SharedCameraRotation);
            var camera = cameraObject.AddComponent<CinemachineCamera>();
            camera.Priority = 0;
            var lens = camera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize = RaceNetworkView.SharedCameraOrthographicSize;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 100f;
            camera.Lens = lens;
            return camera;
        }

        private static void CreateLighting(Transform parent)
        {
            var lightObject = new GameObject("Race Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(52f, -34f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.95f, 0.9f);
        }

        private static GameObject LoadOrCreateHudPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
            if (prefab == null)
            {
                var template = CreateHudTemplate();
                try
                {
                    prefab = PrefabUtility.SaveAsPrefabAsset(
                        template,
                        HudPrefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(template);
                }
            }

            if (prefab != null && prefab.transform.localScale != Vector3.one)
            {
                var contents = PrefabUtility.LoadPrefabContents(HudPrefabPath);
                try
                {
                    contents.transform.localScale = Vector3.one;
                    PrefabUtility.SaveAsPrefabAsset(contents, HudPrefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
            }

            var binding = prefab != null
                ? prefab.GetComponent<RaceHudBindings>()
                : null;
            if (binding == null || !binding.HasRequiredReferences ||
                prefab.transform.localScale != Vector3.one)
            {
                throw new InvalidOperationException(
                    "RaceHud.prefab must contain a complete timer binding " +
                    "and use unit root scale.");
            }
            return prefab;
        }

        private static GameObject CreateHudTemplate()
        {
            var root = new GameObject(
                "RaceHud",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(RaceHudBindings));
            root.transform.localScale = Vector3.one;
            root.layer = LayerMask.NameToLayer("UI");
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 45;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var timer = MinigameTimerDialProjectSetup.InstantiateTimer(
                root.transform);
            root.GetComponent<RaceHudBindings>().Configure(canvas, timer);
            return root;
        }

        private static GameObject CreatePrimitive(
            string name,
            PrimitiveType primitive,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Material material,
            bool keepCollider)
        {
            var gameObject = GameObject.CreatePrimitive(primitive);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = position;
            gameObject.transform.localScale = scale;
            gameObject.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider)
            {
                var collider = gameObject.GetComponent<Collider>();
                if (collider != null)
                {
                    UnityEngine.Object.DestroyImmediate(collider);
                }
            }
            return gameObject;
        }

        private static Material CreateOrLoadMaterial(
            string name,
            Color color,
            float smoothness)
        {
            var path = MaterialFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Universal Render Pipeline/Lit is required for Race " +
                    "prototype materials.");
            }

            var material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void CreateReplacementAnchors(Transform parent)
        {
            var root = new GameObject("Art Replacement Anchors").transform;
            root.SetParent(parent, false);
            new GameObject("Track Art Anchor").transform.SetParent(root, false);
            new GameObject("Finish VFX Anchor").transform.SetParent(root, false);
        }

        private static void ValidateSceneContract(GameObject root)
        {
            if (root.GetComponent<NetworkObject>() == null ||
                root.GetComponent<NetworkRaceState>() == null ||
                root.GetComponent<RaceNetworkView>() == null ||
                root.GetComponentInChildren<Canvas>(true) != null ||
                root.GetComponentsInChildren<CinemachineCamera>(true).Length != 1 ||
                root.GetComponentsInChildren<Camera>(true).Length != 0 ||
                root.GetComponentsInChildren<AudioListener>(true).Length != 0 ||
                FindDescendant(root.transform, "Race Track") == null ||
                FindDescendant(root.transform, "Start Line") == null ||
                FindDescendant(root.transform, "Finish Line") == null ||
                FindDescendant(root.transform, "Runtime Players") == null ||
                FindDescendant(root.transform, "Art Replacement Anchors") == null ||
                root.GetComponentInChildren<Light>(true) == null)
            {
                throw new InvalidOperationException(
                    "Generated Race scene is missing its network state, " +
                    "track, shared camera, light or art anchors; " +
                    "it must not contain a dedicated Canvas.");
            }

            for (var divider = 1; divider < RaceRules.PlayerCount; divider++)
            {
                if (FindDescendant(
                        root.transform,
                        "Lane Divider " + divider) == null)
                {
                    throw new InvalidOperationException(
                        "Generated Race scene is missing a lane divider.");
                }
            }
            for (var slot = 1; slot <= RaceRules.PlayerCount; slot++)
            {
                if (FindDescendant(
                        root.transform,
                        "Start Marker " + slot) == null)
                {
                    throw new InvalidOperationException(
                        "Generated Race scene is missing a start marker.");
                }
            }

        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }
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

        private static void EnsureFolders()
        {
            EnsureFolder(ProjectRoot);
            EnsureFolder(ScenesFolder);
            EnsureFolder(UiPrefabFolder);
            EnsureFolder(ProjectRoot + "/Art");
            EnsureFolder(ProjectRoot + "/Art/Minigames");
            EnsureFolder(ProjectRoot + "/Art/Minigames/Race");
            EnsureFolder(MaterialFolder);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }
            var separator = path.LastIndexOf('/');
            if (separator <= 0)
            {
                throw new InvalidOperationException(
                    "Invalid asset folder: " + path);
            }
            var parent = path.Substring(0, separator);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
        }

        private static void EnsureInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            for (var index = 0; index < scenes.Count; index++)
            {
                if (scenes[index].path != RaceScenePath)
                {
                    continue;
                }
                if (!scenes[index].enabled)
                {
                    scenes[index] = new EditorBuildSettingsScene(
                        RaceScenePath,
                        true);
                    EditorBuildSettings.scenes = scenes.ToArray();
                }
                return;
            }
            scenes.Add(new EditorBuildSettingsScene(RaceScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
