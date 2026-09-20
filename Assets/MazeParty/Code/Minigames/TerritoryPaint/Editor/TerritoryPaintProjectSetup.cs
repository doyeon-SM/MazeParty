using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.TerritoryPaint;
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
    /// Builds the generated Territory Paint arena and instantiates its
    /// prefab-owned HUD. Existing prefab styling and material assets are never
    /// overwritten.
    /// </summary>
    public static class TerritoryPaintProjectSetup
    {
        private const string MenuPath =
            "MazeParty/Minigames/Rebuild Territory Paint";
        private const string ProjectRoot = "Assets/MazeParty";
        private const string ScenesFolder = "Assets/MazeParty/Scenes/Minigames/TerritoryPaint";
        private const string UiPrefabFolder =
            "Assets/MazeParty/Prefabs/Minigames/TerritoryPaint/UI";
        private const string MaterialFolder =
            ProjectRoot +
            "/Art/Minigames/TerritoryPaint/Materials";
        private const string PaintSurfacePrefabPath =
            ProjectRoot +
            "/Prefabs/Minigames/TerritoryPaint/PaintSurface.prefab";

        public const string TerritoryPaintScenePath =
            "Assets/MazeParty/Scenes/Minigames/TerritoryPaint/TerritoryPaint.unity";
        public const string HudPrefabPath =
            "Assets/MazeParty/Prefabs/Minigames/TerritoryPaint/UI/TerritoryPaintHud.prefab";

        private static readonly Color[] PlayerColors =
        {
            new Color(0.18f, 0.48f, 0.95f),
            new Color(0.93f, 0.22f, 0.18f),
            new Color(0.18f, 0.78f, 0.32f),
            new Color(0.7f, 0.27f, 0.91f)
        };

        [MenuItem(MenuPath)]
        public static void RebuildTerritoryPaint()
        {
            if (!EditorSceneManager
                    .SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log(
                    "Territory Paint rebuild canceled; open scene " +
                    "changes were left untouched.");
                return;
            }

            BuildTerritoryPaintAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(
                TerritoryPaintScenePath,
                OpenSceneMode.Single);
            Debug.Log(
                "Territory Paint rebuilt: shared camera, bounded arena, " +
                "network paint surface and prefab HUD.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRebuildTerritoryPaint()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static void BuildTerritoryPaintAssets()
        {
            EnsureFolders();
            var surfaceMaterial = CreateOrLoadMaterial(
                "TerritoryPaintSurface",
                Color.white,
                0.08f);
            var understructureMaterial = CreateOrLoadMaterial(
                "TerritoryPaintUnderstructure",
                new Color(0.035f, 0.055f, 0.075f),
                0.35f);
            var boundaryMaterial = CreateOrLoadMaterial(
                "TerritoryPaintBoundary",
                new Color(0.08f, 0.72f, 0.66f),
                0.22f);
            var hudPrefab = LoadOrCreateHudPrefab();
            BuildScene(
                surfaceMaterial,
                understructureMaterial,
                boundaryMaterial,
                hudPrefab);
            AssetDatabase.SaveAssets();
        }

        private static void BuildScene(
            Material surfaceMaterial,
            Material understructureMaterial,
            Material boundaryMaterial,
            GameObject hudPrefab)
        {
            var previousActive = SceneManager.GetActiveScene();
            var previousPath = previousActive.path;
            var loaded =
                SceneManager.GetSceneByPath(TerritoryPaintScenePath);
            var replaceSingleOpenScene =
                SceneManager.sceneCount == 1 &&
                ((loaded.IsValid() && loaded.isLoaded) ||
                 string.IsNullOrEmpty(previousPath));

            if (loaded.IsValid() && loaded.isLoaded)
            {
                if (loaded.isDirty)
                {
                    throw new InvalidOperationException(
                        "TerritoryPaint.unity has unsaved changes. Save or " +
                        "discard them before rebuilding.");
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

            var root =
                new GameObject("Territory Paint Network State");
            var arenaPresentation =
                new GameObject("Arena Presentation");
            arenaPresentation.transform.SetParent(
                root.transform,
                false);
            var paintRenderer = CreateArena(
                arenaPresentation.transform,
                surfaceMaterial,
                understructureMaterial,
                boundaryMaterial);
            CreateLighting(arenaPresentation.transform);
            var sharedCamera = CreateSharedCamera(root.transform);

            var runnerRoot = new GameObject("Runtime Runners");
            runnerRoot.transform.SetParent(root.transform, false);
            CreateReplacementAnchors(root.transform);

            // Saving first gives the in-scene NetworkObject a stable identity.
            EditorSceneManager.SaveScene(
                scene,
                TerritoryPaintScenePath);
            root.AddComponent<NetworkObject>();
            var state =
                root.AddComponent<NetworkTerritoryPaintState>();
            var view =
                root.AddComponent<TerritoryPaintNetworkView>();

            var hudObject = PrefabUtility.InstantiatePrefab(
                hudPrefab,
                root.transform) as GameObject;
            var hud = hudObject != null
                ? hudObject.GetComponent<TerritoryPaintHudBindings>()
                : null;
            if (hud == null || !hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "TerritoryPaintHud.prefab has invalid serialized " +
                    "bindings.");
            }

            view.Configure(
                state,
                sharedCamera,
                runnerRoot.transform,
                arenaPresentation,
                paintRenderer,
                hud);

            ValidateSceneContract(root);
            EditorSceneManager.SaveScene(
                scene,
                TerritoryPaintScenePath);
            EnsureInBuildSettings();

            if (previousPath == TerritoryPaintScenePath)
            {
                SceneManager.SetActiveScene(scene);
            }
            else if (previousActive.IsValid() &&
                     previousActive.isLoaded &&
                     previousActive.path != TerritoryPaintScenePath)
            {
                SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static Renderer CreateArena(
            Transform parent,
            Material surfaceMaterial,
            Material understructureMaterial,
            Material boundaryMaterial)
        {
            var centerX =
                NetworkTerritoryPaintState.ArenaCenterX;
            var size =
                NetworkTerritoryPaintState.ArenaHalfExtent * 2f;

            CreatePrimitive(
                "Arena Understructure",
                PrimitiveType.Cube,
                parent,
                new Vector3(centerX, -0.34f, 0f),
                Quaternion.identity,
                new Vector3(size, 0.65f, size),
                understructureMaterial,
                true);
            var paintSurface = CreatePrimitive(
                "Paint Surface",
                PrimitiveType.Plane,
                parent,
                new Vector3(centerX, 0.001f, 0f),
                Quaternion.identity,
                new Vector3(size / 10f, 1f, size / 10f),
                surfaceMaterial,
                false);
            paintSurface = MinigameCorePrefabUtility.Connect(
                paintSurface,
                PaintSurfacePrefabPath);
            if (paintSurface.GetComponent<Renderer>() == null)
            {
                throw new InvalidOperationException(
                    "PaintSurface.prefab must keep a root Renderer for " +
                    "the territory display contract.");
            }

            const float wallThickness = 0.42f;
            const float wallHeight = 0.9f;
            var wallOffset =
                NetworkTerritoryPaintState.ArenaHalfExtent +
                wallThickness * 0.5f;
            CreatePrimitive(
                "North Boundary",
                PrimitiveType.Cube,
                parent,
                new Vector3(centerX, wallHeight * 0.5f, wallOffset),
                Quaternion.identity,
                new Vector3(
                    size + wallThickness * 2f,
                    wallHeight,
                    wallThickness),
                boundaryMaterial,
                true);
            CreatePrimitive(
                "South Boundary",
                PrimitiveType.Cube,
                parent,
                new Vector3(centerX, wallHeight * 0.5f, -wallOffset),
                Quaternion.identity,
                new Vector3(
                    size + wallThickness * 2f,
                    wallHeight,
                    wallThickness),
                boundaryMaterial,
                true);
            CreatePrimitive(
                "West Boundary",
                PrimitiveType.Cube,
                parent,
                new Vector3(
                    centerX - wallOffset,
                    wallHeight * 0.5f,
                    0f),
                Quaternion.identity,
                new Vector3(
                    wallThickness,
                    wallHeight,
                    size),
                boundaryMaterial,
                true);
            CreatePrimitive(
                "East Boundary",
                PrimitiveType.Cube,
                parent,
                new Vector3(
                    centerX + wallOffset,
                    wallHeight * 0.5f,
                    0f),
                Quaternion.identity,
                new Vector3(
                    wallThickness,
                    wallHeight,
                    size),
                boundaryMaterial,
                true);

            for (var slot = 0;
                 slot < TerritoryPaintRules.PlayerCount;
                 slot++)
            {
                var marker = CreatePrimitive(
                    "Start Marker " + (slot + 1),
                    PrimitiveType.Cylinder,
                    parent,
                    Vector3.zero,
                    Quaternion.identity,
                    new Vector3(0.32f, 0.025f, 0.32f),
                    CreateOrLoadMaterial(
                        "TerritoryPaintPlayer" + (slot + 1),
                        PlayerColors[slot],
                        0.18f),
                    false);
                var start =
                    NetworkTerritoryPaintState.GetPlayerStartPosition(
                        slot);
                marker.transform.position =
                    new Vector3(start.x, 0.018f, start.y);
            }

            return paintSurface.GetComponent<Renderer>();
        }

        private static void CreateLighting(Transform parent)
        {
            var lightObject =
                new GameObject("Territory Paint Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation =
                Quaternion.Euler(52f, -34f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.96f, 0.9f);
        }

        private static CinemachineCamera CreateSharedCamera(
            Transform parent)
        {
            var cameraObject =
                new GameObject("CM_TerritoryPaintShared");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.SetPositionAndRotation(
                TerritoryPaintNetworkView.SharedCameraPosition,
                TerritoryPaintNetworkView.SharedCameraRotation);
            var camera =
                cameraObject.AddComponent<CinemachineCamera>();
            camera.Priority = 0;
            var lens = camera.Lens;
            lens.ModeOverride =
                LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize =
                TerritoryPaintNetworkView
                    .SharedCameraOrthographicSize;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 80f;
            camera.Lens = lens;
            return camera;
        }

        private static void CreateReplacementAnchors(
            Transform parent)
        {
            var root =
                new GameObject("Art Replacement Anchors").transform;
            root.SetParent(parent, false);
            new GameObject("Arena Art Anchor").transform.SetParent(
                root,
                false);
            new GameObject("Boundary Art Anchor").transform.SetParent(
                root,
                false);
            new GameObject("Paint VFX Anchor").transform.SetParent(
                root,
                false);
        }

        private static GameObject LoadOrCreateHudPrefab()
        {
            var prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    HudPrefabPath);
            var createdDefaultPrefab = prefab == null;
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

            if (createdDefaultPrefab && prefab != null &&
                prefab.transform.localScale != Vector3.one)
            {
                var contents =
                    PrefabUtility.LoadPrefabContents(HudPrefabPath);
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
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    HudPrefabPath);
            }

            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
            var binding = prefab != null
                ? prefab.GetComponent<TerritoryPaintHudBindings>()
                : null;
            if (binding == null ||
                !binding.HasRequiredReferences ||
                prefab.transform.localScale != Vector3.one)
            {
                throw new InvalidOperationException(
                    "TerritoryPaintHud.prefab must have complete " +
                    "serialized bindings and unit root scale. Repair the " +
                    "prefab directly; setup will not overwrite it.");
            }

            return prefab;
        }

        private static GameObject CreateHudTemplate()
        {
            var font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            if (font == null)
            {
                throw new InvalidOperationException(
                    "Unity built-in LegacyRuntime.ttf is required.");
            }

            var root = new GameObject(
                "TerritoryPaintHud",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(TerritoryPaintHudBindings));
            root.transform.localScale = Vector3.one;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 45;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var scorePanel = CreatePanel(
                "Score Panel",
                root.transform,
                new Vector2(0f, 1f),
                new Vector2(22f, -22f),
                new Vector2(390f, 250f),
                new Color(0.025f, 0.03f, 0.045f, 0.9f));
            var rows =
                new Text[TerritoryPaintRules.PlayerCount];
            for (var slot = 0; slot < rows.Length; slot++)
            {
                rows[slot] = CreateHudText(
                    "Player " + (slot + 1) + " Row",
                    scorePanel.transform,
                    font,
                    new Vector2(0f, -18f - slot * 55f),
                    new Vector2(350f, 46f),
                    22,
                    "PLAYER " + (slot + 1) + "   0");
                rows[slot].alignment =
                    TextAnchor.MiddleLeft;
                rows[slot].color = PlayerColors[slot];
            }

            root.GetComponent<TerritoryPaintHudBindings>()
                .Configure(canvas, rows);
            root.transform.localScale = Vector3.one;
            return root;
        }

        private static GameObject CreatePanel(
            string name,
            Transform parent,
            Vector2 anchor,
            Vector2 position,
            Vector2 size,
            Color color)
        {
            var panel = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            panel.transform.SetParent(parent, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = panel.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return panel;
        }

        private static Text CreateHudText(
            string name,
            Transform parent,
            Font font,
            Vector2 position,
            Vector2 size,
            int fontSize,
            string sample)
        {
            var textObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            textObject.transform.SetParent(parent, false);
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax =
                new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var text = textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow =
                HorizontalWrapMode.Overflow;
            text.verticalOverflow =
                VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = sample;
            return text;
        }

        private static GameObject CreatePrimitive(
            string name,
            PrimitiveType primitive,
            Transform parent,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            Material material,
            bool keepCollider)
        {
            var gameObject =
                GameObject.CreatePrimitive(primitive);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = position;
            gameObject.transform.rotation = rotation;
            gameObject.transform.localScale = scale;
            gameObject.GetComponent<Renderer>().sharedMaterial =
                material;
            if (!keepCollider)
            {
                var collider =
                    gameObject.GetComponent<Collider>();
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
            var existing =
                AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            var shader =
                Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Universal Render Pipeline/Lit is required for " +
                    "Territory Paint prototype materials.");
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

        private static void ValidateSceneContract(GameObject root)
        {
            var state =
                root.GetComponent<NetworkTerritoryPaintState>();
            var view =
                root.GetComponent<TerritoryPaintNetworkView>();
            var networkObject =
                root.GetComponent<NetworkObject>();
            var hud =
                root.GetComponentInChildren<
                    TerritoryPaintHudBindings>(true);
            var paint = FindDescendant(
                root.transform,
                "Paint Surface");
            var runners = FindDescendant(
                root.transform,
                "Runtime Runners");
            var art = FindDescendant(
                root.transform,
                "Art Replacement Anchors");
            var camera =
                root.GetComponentInChildren<CinemachineCamera>(true);
            var light =
                root.GetComponentInChildren<Light>(true);
            if (state == null || view == null ||
                networkObject == null || hud == null ||
                !hud.HasRequiredReferences ||
                paint == null ||
                paint.GetComponent<Renderer>() == null ||
                runners == null || art == null ||
                camera == null || light == null)
            {
                throw new InvalidOperationException(
                    "Generated Territory Paint scene is missing its " +
                    "network state, paint surface, shared camera, light, " +
                    "art anchors or prefab HUD contract.");
            }

            var sourcePath =
                PrefabUtility
                    .GetPrefabAssetPathOfNearestInstanceRoot(
                        hud.gameObject);
            if (sourcePath != HudPrefabPath)
            {
                throw new InvalidOperationException(
                    "Territory Paint Canvas must be instantiated from " +
                    "TerritoryPaintHud.prefab.");
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
            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindDescendant(
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
            EnsureFolder(
                ProjectRoot + "/Art");
            EnsureFolder(
                ProjectRoot + "/Art/Minigames");
            EnsureFolder(
                ProjectRoot + "/Art/Minigames/TerritoryPaint");
            EnsureFolder(MaterialFolder);
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
                    "Invalid asset folder path: " + path);
            }

            var parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(
                parent,
                path.Substring(slash + 1));
        }

        private static void EnsureInBuildSettings()
        {
            var scenes =
                new List<EditorBuildSettingsScene>(
                    EditorBuildSettings.scenes);
            for (var index = 0; index < scenes.Count; index++)
            {
                if (scenes[index].path ==
                    TerritoryPaintScenePath)
                {
                    if (!scenes[index].enabled)
                    {
                        scenes[index] =
                            new EditorBuildSettingsScene(
                                TerritoryPaintScenePath,
                                true);
                        EditorBuildSettings.scenes =
                            scenes.ToArray();
                    }
                    return;
                }
            }

            scenes.Add(
                new EditorBuildSettingsScene(
                    TerritoryPaintScenePath,
                    true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
