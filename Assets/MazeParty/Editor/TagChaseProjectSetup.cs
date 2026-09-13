using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.TagChase;
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
    /// Builds the bounded Tag Chase arena and its prefab-owned timer-only HUD.
    /// Existing prefab styling and material assets are never overwritten.
    /// </summary>
    public static class TagChaseProjectSetup
    {
        private const string MenuPath =
            "MazeParty/Minigames/Rebuild Tag Chase";
        private const string ProjectRoot =
            "Assets/MazeParty";
        private const string ScenesFolder =
            ProjectRoot + "/Scenes";
        private const string UiPrefabFolder =
            ProjectRoot + "/UI/Prefabs";
        private const string MaterialFolder =
            ProjectRoot +
            "/Art/Minigames/TagChase/Materials";

        public const string TagChaseScenePath =
            ScenesFolder + "/TagChase.unity";
        public const string HudPrefabPath =
            UiPrefabFolder + "/TagChaseHud.prefab";

        [MenuItem(MenuPath)]
        public static void RebuildTagChase()
        {
            if (!EditorSceneManager
                    .SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log(
                    "Tag Chase rebuild canceled; open scene changes " +
                    "were left untouched.");
                return;
            }

            BuildTagChaseAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(
                TagChaseScenePath,
                OpenSceneMode.Single);
            Debug.Log(
                "Tag Chase rebuilt: bounded obstacle arena, runner " +
                "group camera, tagger first-person camera and " +
                "timer-only prefab HUD.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRebuildTagChase()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static void BuildTagChaseAssets()
        {
            EnsureFolders();
            MinigameTimerDialProjectSetup.EnsurePrefabExists();
            var floorMaterial =
                CreateOrLoadMaterial(
                    "TagChaseFloor",
                    new Color(0.08f, 0.12f, 0.16f),
                    0.15f);
            var wallMaterial =
                CreateOrLoadMaterial(
                    "TagChaseWall",
                    new Color(0.22f, 0.28f, 0.34f),
                    0.25f);
            var obstacleMaterial =
                CreateOrLoadMaterial(
                    "TagChaseObstacle",
                    new Color(0.58f, 0.13f, 0.18f),
                    0.2f);
            var hudPrefab =
                LoadOrCreateHudPrefab();
            BuildScene(
                floorMaterial,
                wallMaterial,
                obstacleMaterial,
                hudPrefab);
            AssetDatabase.SaveAssets();
        }

        private static void BuildScene(
            Material floorMaterial,
            Material wallMaterial,
            Material obstacleMaterial,
            GameObject hudPrefab)
        {
            var previousActive =
                SceneManager.GetActiveScene();
            var previousPath =
                previousActive.path;
            var loaded =
                SceneManager.GetSceneByPath(
                    TagChaseScenePath);
            var replaceSingleOpenScene =
                SceneManager.sceneCount == 1 &&
                ((loaded.IsValid() && loaded.isLoaded) ||
                 string.IsNullOrEmpty(previousPath));

            if (loaded.IsValid() && loaded.isLoaded)
            {
                if (loaded.isDirty)
                {
                    throw new InvalidOperationException(
                        "TagChase.unity has unsaved changes. Save or " +
                        "discard them before rebuilding.");
                }
                if (!replaceSingleOpenScene)
                {
                    EditorSceneManager.CloseScene(
                        loaded,
                        true);
                }
            }

            var scene =
                EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    replaceSingleOpenScene
                        ? NewSceneMode.Single
                        : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            var root =
                new GameObject(
                    "Tag Chase Network State");
            var arenaPresentation =
                new GameObject(
                    "Arena Presentation");
            arenaPresentation.transform.SetParent(
                root.transform,
                false);
            CreateArena(
                arenaPresentation.transform,
                floorMaterial,
                wallMaterial,
                obstacleMaterial);
            CreateLighting(
                arenaPresentation.transform);
            var sharedCamera =
                CreateCamera(
                    "CM_TagChaseRunners",
                    root.transform,
                    TagChaseNetworkView
                        .InitialSharedCameraPosition,
                    TagChaseNetworkView
                        .InitialSharedCameraRotation,
                    TagChaseNetworkView
                        .SharedFieldOfView);
            var taggerCamera =
                CreateCamera(
                    "CM_TagChaseTagger",
                    root.transform,
                    new Vector3(
                        NetworkTagChaseState.ArenaCenterX,
                        TagChaseNetworkView
                            .FirstPersonEyeHeight,
                        0f),
                    Quaternion.identity,
                    TagChaseNetworkView
                        .TaggerFieldOfView);
            var playerRoot =
                new GameObject("Runtime Players");
            playerRoot.transform.SetParent(
                root.transform,
                false);
            CreateReplacementAnchors(
                root.transform);

            EditorSceneManager.SaveScene(
                scene,
                TagChaseScenePath);
            root.AddComponent<NetworkObject>();
            var state =
                root.AddComponent<
                    NetworkTagChaseState>();
            var view =
                root.AddComponent<
                    TagChaseNetworkView>();

            var hudObject =
                PrefabUtility.InstantiatePrefab(
                    hudPrefab,
                    root.transform) as GameObject;
            var hud =
                hudObject != null
                    ? hudObject.GetComponent<
                        TagChaseHudBindings>()
                    : null;
            if (hud == null ||
                !hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "TagChaseHud.prefab has invalid serialized " +
                    "bindings.");
            }

            view.Configure(
                state,
                sharedCamera,
                taggerCamera,
                playerRoot.transform,
                arenaPresentation,
                hud);

            ValidateSceneContract(root);
            EditorSceneManager.SaveScene(
                scene,
                TagChaseScenePath);
            EnsureInBuildSettings();

            if (previousPath == TagChaseScenePath)
            {
                SceneManager.SetActiveScene(scene);
            }
            else if (previousActive.IsValid() &&
                     previousActive.isLoaded &&
                     previousActive.path !=
                     TagChaseScenePath)
            {
                SceneManager.SetActiveScene(
                    previousActive);
                EditorSceneManager.CloseScene(
                    scene,
                    true);
            }
        }

        private static void CreateArena(
            Transform parent,
            Material floorMaterial,
            Material wallMaterial,
            Material obstacleMaterial)
        {
            var width =
                NetworkTagChaseState.ArenaHalfWidth * 2f;
            var depth =
                NetworkTagChaseState.ArenaHalfDepth * 2f;
            CreatePrimitive(
                "Arena Floor",
                PrimitiveType.Cube,
                parent,
                new Vector3(
                    NetworkTagChaseState.ArenaCenterX,
                    -0.3f,
                    0f),
                new Vector3(width, 0.6f, depth),
                floorMaterial,
                true);

            const float wallThickness = 0.45f;
            const float wallHeight = 2.4f;
            CreatePrimitive(
                "North Boundary",
                PrimitiveType.Cube,
                parent,
                new Vector3(
                    NetworkTagChaseState.ArenaCenterX,
                    wallHeight * 0.5f,
                    NetworkTagChaseState.ArenaHalfDepth +
                    wallThickness * 0.5f),
                new Vector3(
                    width + wallThickness * 2f,
                    wallHeight,
                    wallThickness),
                wallMaterial,
                true);
            CreatePrimitive(
                "South Boundary",
                PrimitiveType.Cube,
                parent,
                new Vector3(
                    NetworkTagChaseState.ArenaCenterX,
                    wallHeight * 0.5f,
                    -NetworkTagChaseState.ArenaHalfDepth -
                    wallThickness * 0.5f),
                new Vector3(
                    width + wallThickness * 2f,
                    wallHeight,
                    wallThickness),
                wallMaterial,
                true);
            CreatePrimitive(
                "West Boundary",
                PrimitiveType.Cube,
                parent,
                new Vector3(
                    NetworkTagChaseState.ArenaCenterX -
                    NetworkTagChaseState.ArenaHalfWidth -
                    wallThickness * 0.5f,
                    wallHeight * 0.5f,
                    0f),
                new Vector3(
                    wallThickness,
                    wallHeight,
                    depth),
                wallMaterial,
                true);
            CreatePrimitive(
                "East Boundary",
                PrimitiveType.Cube,
                parent,
                new Vector3(
                    NetworkTagChaseState.ArenaCenterX +
                    NetworkTagChaseState.ArenaHalfWidth +
                    wallThickness * 0.5f,
                    wallHeight * 0.5f,
                    0f),
                new Vector3(
                    wallThickness,
                    wallHeight,
                    depth),
                wallMaterial,
                true);

            for (var index = 0; index < NetworkTagChaseState.ObstacleCount; index++)
            {
                var obstacle =
                    NetworkTagChaseState
                        .GetObstacleRect(index);
                CreatePrimitive(
                    "Sight Blocker " + (index + 1),
                    PrimitiveType.Cube,
                    parent,
                    new Vector3(
                        obstacle.center.x,
                        1.65f,
                        obstacle.center.y),
                    new Vector3(
                        obstacle.width,
                        3.3f,
                        obstacle.height),
                    obstacleMaterial,
                    true);
            }

            CreateSpawnMarker(
                "Tagger Start",
                parent,
                new Vector2(
                    NetworkTagChaseState.ArenaCenterX,
                    0f),
                new Color(0.95f, 0.12f, 0.16f));
            for (var runner = 0; runner < 3; runner++)
            {
                CreateSpawnMarker(
                    "Runner Start " + (runner + 1),
                    parent,
                    NetworkTagChaseState
                        .GetRoundStartPosition(
                            0,
                            runner + 1),
                    new Color(0.2f, 0.68f, 0.95f));
            }
        }

        private static void CreateSpawnMarker(
            string name,
            Transform parent,
            Vector2 position,
            Color color)
        {
            var material =
                CreateOrLoadMaterial(
                    name.Replace(" ", string.Empty),
                    color,
                    0.15f);
            CreatePrimitive(
                name,
                PrimitiveType.Cylinder,
                parent,
                new Vector3(
                    position.x,
                    0.02f,
                    position.y),
                new Vector3(0.75f, 0.025f, 0.75f),
                material,
                false);
        }

        private static void CreateLighting(Transform parent)
        {
            var lightObject =
                new GameObject(
                    "Tag Chase Directional Light");
            lightObject.transform.SetParent(
                parent,
                false);
            lightObject.transform.rotation =
                Quaternion.Euler(52f, -34f, 0f);
            var light =
                lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color =
                new Color(1f, 0.95f, 0.9f);
        }

        private static CinemachineCamera CreateCamera(
            string name,
            Transform parent,
            Vector3 position,
            Quaternion rotation,
            float fieldOfView)
        {
            var cameraObject =
                new GameObject(name);
            cameraObject.transform.SetParent(
                parent,
                false);
            cameraObject.transform.SetPositionAndRotation(
                position,
                rotation);
            var camera =
                cameraObject.AddComponent<
                    CinemachineCamera>();
            camera.Priority = 0;
            var lens = camera.Lens;
            lens.ModeOverride =
                LensSettings.OverrideModes.Perspective;
            lens.FieldOfView = fieldOfView;
            lens.NearClipPlane = 0.08f;
            lens.FarClipPlane = 100f;
            camera.Lens = lens;
            return camera;
        }

        private static GameObject LoadOrCreateHudPrefab()
        {
            var prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    HudPrefabPath);
            if (prefab == null)
            {
                var template =
                    CreateHudTemplate();
                try
                {
                    prefab =
                        PrefabUtility.SaveAsPrefabAsset(
                            template,
                            HudPrefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(
                        template);
                }
            }

            if (prefab != null &&
                prefab.transform.localScale != Vector3.one)
            {
                var contents =
                    PrefabUtility.LoadPrefabContents(
                        HudPrefabPath);
                try
                {
                    contents.transform.localScale = Vector3.one;
                    PrefabUtility.SaveAsPrefabAsset(
                        contents,
                        HudPrefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }

                prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(
                        HudPrefabPath);
            }

            var binding =
                prefab != null
                    ? prefab.GetComponent<
                        TagChaseHudBindings>()
                    : null;
            if (binding == null ||
                !binding.HasRequiredReferences ||
                prefab.transform.localScale !=
                Vector3.one)
            {
                throw new InvalidOperationException(
                    "TagChaseHud.prefab must contain only its " +
                    "serialized timer binding and use unit root scale.");
            }

            return prefab;
        }

        private static GameObject CreateHudTemplate()
        {
            var root =
                new GameObject(
                    "TagChaseHud",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(TagChaseHudBindings));
            root.transform.localScale = Vector3.one;
            root.layer = LayerMask.NameToLayer("UI");
            var canvas =
                root.GetComponent<Canvas>();
            canvas.renderMode =
                RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 45;
            var scaler =
                root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode
                    .ScaleWithScreenSize;
            scaler.referenceResolution =
                new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var timer =
                MinigameTimerDialProjectSetup
                    .InstantiateTimer(root.transform);
            root.GetComponent<TagChaseHudBindings>()
                .Configure(canvas, timer);
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
            var gameObject =
                GameObject.CreatePrimitive(primitive);
            gameObject.name = name;
            gameObject.transform.SetParent(
                parent,
                false);
            gameObject.transform.position = position;
            gameObject.transform.localScale = scale;
            gameObject.GetComponent<Renderer>()
                .sharedMaterial = material;
            if (!keepCollider)
            {
                var collider =
                    gameObject.GetComponent<Collider>();
                if (collider != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        collider);
                }
            }

            return gameObject;
        }

        private static Material CreateOrLoadMaterial(
            string name,
            Color color,
            float smoothness)
        {
            var path =
                MaterialFolder + "/" + name + ".mat";
            var existing =
                AssetDatabase.LoadAssetAtPath<Material>(
                    path);
            if (existing != null)
            {
                return existing;
            }

            var shader =
                Shader.Find(
                    "Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Universal Render Pipeline/Lit is required " +
                    "for Tag Chase prototype materials.");
            }

            var material =
                new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor(
                    "_BaseColor",
                    color);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor(
                    "_Color",
                    color);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat(
                    "_Smoothness",
                    smoothness);
            }
            AssetDatabase.CreateAsset(
                material,
                path);
            return material;
        }

        private static void CreateReplacementAnchors(
            Transform parent)
        {
            var root =
                new GameObject(
                    "Art Replacement Anchors").transform;
            root.SetParent(parent, false);
            new GameObject("Arena Art Anchor")
                .transform.SetParent(root, false);
            new GameObject("Obstacle Art Anchor")
                .transform.SetParent(root, false);
            new GameObject("Catch VFX Anchor")
                .transform.SetParent(root, false);
        }

        private static void ValidateSceneContract(
            GameObject root)
        {
            var state =
                root.GetComponent<
                    NetworkTagChaseState>();
            var view =
                root.GetComponent<
                    TagChaseNetworkView>();
            var networkObject =
                root.GetComponent<NetworkObject>();
            var hud =
                root.GetComponentInChildren<
                    TagChaseHudBindings>(true);
            var cameras =
                root.GetComponentsInChildren<
                    CinemachineCamera>(true);
            if (state == null ||
                view == null ||
                networkObject == null ||
                hud == null ||
                !hud.HasRequiredReferences ||
                cameras.Length != 2 ||
                FindDescendant(
                    root.transform,
                    "Arena Floor") == null ||
                FindDescendant(
                    root.transform,
                    "Runtime Players") == null ||
                FindDescendant(
                    root.transform,
                    "Art Replacement Anchors") == null ||
                root.GetComponentInChildren<Light>(true) == null)
            {
                throw new InvalidOperationException(
                    "Generated Tag Chase scene is missing its network " +
                    "state, arena, two cameras, light, art anchors or " +
                    "timer-only prefab HUD.");
            }

            for (var index = 0; index < 4; index++)
            {
                if (FindDescendant(
                        root.transform,
                        "Sight Blocker " + (index + 1)) == null)
                {
                    throw new InvalidOperationException(
                        "Generated Tag Chase scene is missing a " +
                        "logical sight blocker.");
                }
            }

            var sourcePath =
                PrefabUtility
                    .GetPrefabAssetPathOfNearestInstanceRoot(
                        hud.gameObject);
            if (sourcePath != HudPrefabPath)
            {
                throw new InvalidOperationException(
                    "Tag Chase Canvas must be instantiated from " +
                    "TagChaseHud.prefab.");
            }
        }

        private static Transform FindDescendant(
            Transform root,
            string name)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == name)
            {
                return root;
            }
            for (var index = 0;
                 index < root.childCount;
                 index++)
            {
                var found =
                    FindDescendant(
                        root.GetChild(index),
                        name);
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
            EnsureFolder(
                ProjectRoot + "/Art/Minigames");
            EnsureFolder(
                ProjectRoot +
                "/Art/Minigames/TagChase");
            EnsureFolder(MaterialFolder);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var separator =
                path.LastIndexOf('/');
            if (separator <= 0)
            {
                throw new InvalidOperationException(
                    "Invalid asset folder: " + path);
            }

            var parent =
                path.Substring(0, separator);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(
                parent,
                path.Substring(separator + 1));
        }

        private static void EnsureInBuildSettings()
        {
            var scenes =
                new List<EditorBuildSettingsScene>(
                    EditorBuildSettings.scenes);
            for (var index = 0;
                 index < scenes.Count;
                 index++)
            {
                if (scenes[index].path !=
                    TagChaseScenePath)
                {
                    continue;
                }

                if (!scenes[index].enabled)
                {
                    scenes[index] =
                        new EditorBuildSettingsScene(
                            TagChaseScenePath,
                            true);
                    EditorBuildSettings.scenes =
                        scenes.ToArray();
                }
                return;
            }

            scenes.Add(
                new EditorBuildSettingsScene(
                    TagChaseScenePath,
                    true));
            EditorBuildSettings.scenes =
                scenes.ToArray();
        }
    }
}
