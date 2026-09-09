using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.Minefield;
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
    public static class MinefieldProjectSetup
    {
        private const string MenuPath = "MazeParty/Minigames/Rebuild Minefield";
        private const string Root = "Assets/MazeParty";
        private const string ScenesFolder = Root + "/Scenes";
        private const string UiFolder = Root + "/UI";
        private const string UiPrefabFolder = UiFolder + "/Prefabs";
        private const string ArtFolder = Root + "/Art";
        private const string MinigameArtFolder = ArtFolder + "/Minigames";
        private const string MinefieldArtFolder = MinigameArtFolder + "/Minefield";
        private const string MaterialFolder = MinefieldArtFolder + "/Materials";
        private const string BoardScenePath = ScenesFolder + "/Board.unity";
        private const string BoardCanvasPrefabPath =
            UiPrefabFolder + "/BoardCanvas.prefab";
        private const float CrusherStartOffset = 2.5f;

        internal const string MinefieldScenePath =
            ScenesFolder + "/Minefield.unity";

        [MenuItem(MenuPath)]
        public static void RebuildMinefield()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log(
                    "Minefield rebuild canceled; open scene changes were left untouched.");
                return;
            }

            BuildMinefieldAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var minefield = SceneManager.GetSceneByPath(MinefieldScenePath);
            if (minefield.IsValid() && minefield.isLoaded)
            {
                SceneManager.SetActiveScene(minefield);
            }
            else
            {
                EditorSceneManager.OpenScene(MinefieldScenePath, OpenSceneMode.Single);
            }
            Debug.Log(
                "Minefield rebuilt: additive-safe top-view arena, network state, " +
                "and image-centered board minigame UI.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRebuildMinefield()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static void BuildMinefieldAssets()
        {
            EnsureFolders();
            UpdateBoardCanvasPrefab();
            BuildMinefieldScene(CreateMaterials());
            AssetDatabase.SaveAssets();
        }

        private static void BuildMinefieldScene(MinefieldMaterials materials)
        {
            var previousActive = SceneManager.GetActiveScene();
            var previousActivePath = previousActive.path;
            var scratchScene = default(Scene);
            var loadedMinefield = SceneManager.GetSceneByPath(MinefieldScenePath);
            if (loadedMinefield.IsValid() && loadedMinefield.isLoaded)
            {
                if (loadedMinefield.isDirty)
                {
                    throw new InvalidOperationException(
                        "Minefield.unity has unsaved changes. Save or discard them " +
                        "before rebuilding the generated minigame scene.");
                }

                if (SceneManager.sceneCount == 1)
                {
                    scratchScene = EditorSceneManager.OpenScene(
                        BoardScenePath,
                        OpenSceneMode.Additive);
                    SceneManager.SetActiveScene(scratchScene);
                }

                EditorSceneManager.CloseScene(loadedMinefield, true);
            }

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            var root = new GameObject("Minefield Network State");
            var arenaPresentation = new GameObject("Arena Presentation");
            arenaPresentation.transform.SetParent(root.transform, false);
            CreateArena(arenaPresentation.transform, materials);
            CreateLighting(arenaPresentation.transform);
            CreateTopDownCamera(root.transform);

            // Save and enable the scene before adding its NetworkObject so NGO can
            // assign a stable in-scene GlobalObjectIdHash.
            EditorSceneManager.SaveScene(scene, MinefieldScenePath);
            EnsureMinefieldAfterBoardInBuildSettings();

            root.AddComponent<NetworkObject>();
            root.AddComponent<NetworkMinefieldState>();
            root.AddComponent<MinefieldNetworkView>();
            ValidateSceneContract(root);
            EditorSceneManager.SaveScene(scene, MinefieldScenePath);

            if (previousActivePath == MinefieldScenePath)
            {
                SceneManager.SetActiveScene(scene);
            }
            else if (previousActive.IsValid() &&
                previousActive.isLoaded &&
                previousActive.path != MinefieldScenePath)
            {
                SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(scene, true);
            }

            if (scratchScene.IsValid() && scratchScene.isLoaded)
            {
                if (SceneManager.GetActiveScene() == scratchScene)
                {
                    SceneManager.SetActiveScene(scene);
                }
                EditorSceneManager.CloseScene(scratchScene, true);
            }
        }

        private static void CreateArena(
            Transform parent,
            MinefieldMaterials materials)
        {
            var centerZ =
                (NetworkMinefieldState.ArenaMinZ +
                 NetworkMinefieldState.ArenaMaxZ) * 0.5f;
            var width =
                NetworkMinefieldState.ArenaMaxX -
                NetworkMinefieldState.ArenaMinX;
            var depth =
                NetworkMinefieldState.ArenaMaxZ -
                NetworkMinefieldState.ArenaMinZ;

            CreateCube(
                "Arena Floor",
                parent,
                new Vector3(NetworkMinefieldState.ArenaCenterX, -0.1f, centerZ),
                new Vector3(width, 0.2f, depth),
                materials.Floor);

            const float wallThickness = 0.5f;
            const float wallHeight = 2.5f;
            CreateCube(
                "West Wall",
                parent,
                new Vector3(
                    NetworkMinefieldState.ArenaMinX - wallThickness * 0.5f,
                    wallHeight * 0.5f,
                    centerZ),
                new Vector3(wallThickness, wallHeight, depth),
                materials.Wall);
            CreateCube(
                "East Wall",
                parent,
                new Vector3(
                    NetworkMinefieldState.ArenaMaxX + wallThickness * 0.5f,
                    wallHeight * 0.5f,
                    centerZ),
                new Vector3(wallThickness, wallHeight, depth),
                materials.Wall);

            CreateGrid(parent, width, depth, materials.Grid);

            CreateLine(
                "Start Line",
                parent,
                NetworkMinefieldState.ArenaMinZ + 0.75f,
                width,
                materials.Start);
            CreateLine(
                "Finish Line",
                parent,
                NetworkMinefieldState.ArenaMaxZ - 0.5f,
                width,
                materials.Finish);

            var crusher = CreateCube(
                "Crusher Placeholder",
                parent,
                new Vector3(
                    NetworkMinefieldState.ArenaCenterX,
                    1.25f,
                    NetworkMinefieldState.ArenaMinZ - CrusherStartOffset),
                new Vector3(width, 2.5f, 1f),
                materials.Crusher);
            crusher.AddComponent<MinefieldCrusher>();
        }

        private static void CreateGrid(
            Transform parent,
            float width,
            float depth,
            Material material)
        {
            var cellWidth = width / NetworkMinefieldState.GridWidth;
            var cellDepth = depth / NetworkMinefieldState.GridHeight;
            for (var x = 1; x < NetworkMinefieldState.GridWidth; x++)
            {
                var line = CreateCube(
                    "Grid X " + x,
                    parent,
                    new Vector3(
                        NetworkMinefieldState.ArenaMinX + cellWidth * x,
                        0.015f,
                        0f),
                    new Vector3(0.045f, 0.03f, depth),
                    material);
                UnityEngine.Object.DestroyImmediate(line.GetComponent<Collider>());
            }

            for (var z = 1; z < NetworkMinefieldState.GridHeight; z++)
            {
                var line = CreateCube(
                    "Grid Z " + z,
                    parent,
                    new Vector3(
                        NetworkMinefieldState.ArenaCenterX,
                        0.015f,
                        NetworkMinefieldState.ArenaMinZ + cellDepth * z),
                    new Vector3(width, 0.03f, 0.045f),
                    material);
                UnityEngine.Object.DestroyImmediate(line.GetComponent<Collider>());
            }
        }

        private static void CreateLine(
            string name,
            Transform parent,
            float worldZ,
            float arenaWidth,
            Material material)
        {
            var line = CreateCube(
                name,
                parent,
                new Vector3(NetworkMinefieldState.ArenaCenterX, 0.025f, worldZ),
                new Vector3(arenaWidth, 0.05f, 0.35f),
                material);
            var collider = line.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
        }

        private static GameObject CreateCube(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent);
            cube.transform.SetPositionAndRotation(position, Quaternion.identity);
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }

        private static MinefieldMaterials CreateMaterials()
        {
            return new MinefieldMaterials
            {
                Floor = CreateOrUpdateMaterial(
                    "MinefieldFloor",
                    new Color(0.075f, 0.11f, 0.15f)),
                Grid = CreateOrUpdateMaterial(
                    "MinefieldGrid",
                    new Color(0.18f, 0.28f, 0.34f)),
                Wall = CreateOrUpdateMaterial(
                    "MinefieldWall",
                    new Color(0.12f, 0.19f, 0.25f)),
                Start = CreateOrUpdateMaterial(
                    "MinefieldStart",
                    new Color(0.18f, 0.86f, 0.42f)),
                Finish = CreateOrUpdateMaterial(
                    "MinefieldFinish",
                    new Color(1f, 0.74f, 0.12f)),
                Crusher = CreateOrUpdateMaterial(
                    "MinefieldCrusherPlaceholder",
                    new Color(0.82f, 0.08f, 0.075f))
            };
        }

        private static Material CreateOrUpdateMaterial(string name, Color color)
        {
            var path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ??
                             Shader.Find("Standard");
                if (shader == null)
                {
                    throw new InvalidOperationException(
                        "No supported Lit shader is available for Minefield.");
                }

                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0f);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.2f);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CreateLighting(Transform parent)
        {
            var lightObject = new GameObject("Minefield Directional Light");
            lightObject.transform.SetParent(parent);
            lightObject.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
        }

        private static void CreateTopDownCamera(Transform parent)
        {
            var cameraObject = new GameObject("CM_MinefieldTopDown");
            cameraObject.transform.SetParent(parent);
            cameraObject.transform.SetPositionAndRotation(
                MinefieldNetworkView.CalculatePlayerCameraPosition(
                    new Vector3(NetworkMinefieldState.ArenaCenterX, 0f, 0f)),
                MinefieldNetworkView.PlayerCameraRotation);

            var camera = cameraObject.AddComponent<CinemachineCamera>();
            camera.Priority = 200;
            var lens = camera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize =
                MinefieldNetworkView.PlayerCameraOrthographicSize;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 100f;
            camera.Lens = lens;
        }

        private static void ValidateSceneContract(GameObject root)
        {
            if (root.GetComponent<NetworkObject>() == null ||
                root.GetComponent<NetworkMinefieldState>() == null ||
                root.GetComponent<MinefieldNetworkView>() == null ||
                FindDescendant(root.transform, "Arena Presentation") == null ||
                root.GetComponentInChildren<CinemachineCamera>(true) == null ||
                FindDescendant(root.transform, "Crusher Placeholder") == null)
            {
                throw new InvalidOperationException(
                    "Generated Minefield scene is missing its network or presentation contract.");
            }

            if (root.GetComponentInChildren<Camera>(true) != null ||
                root.GetComponentInChildren<AudioListener>(true) != null)
            {
                throw new InvalidOperationException(
                    "Minefield must not contain a Unity Camera or AudioListener; " +
                    "the additive Board scene owns the output camera.");
            }
        }

        private static void UpdateBoardCanvasPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                BoardCanvasPrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Canonical BoardCanvas.prefab is missing. Rebuild the board flow " +
                    "prototype before rebuilding Minefield.");
            }

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                throw new InvalidOperationException(
                    "Unity built-in LegacyRuntime.ttf font could not be loaded.");
            }

            var contents = PrefabUtility.LoadPrefabContents(BoardCanvasPrefabPath);
            try
            {
                var readyPanel = FindDescendant(
                    contents.transform,
                    "MinigameReadyPanel");
                if (readyPanel == null)
                {
                    throw new InvalidOperationException(
                        "BoardCanvas.prefab is missing MinigameReadyPanel.");
                }

                ConfigureCenteredRect(
                    RequireRect(readyPanel),
                    Vector2.zero,
                    new Vector2(1180f, 840f));

                UpdateExistingText(
                    readyPanel.transform,
                    "Ready Title",
                    "MINEFIELD / READY",
                    new Vector2(0f, 365f),
                    new Vector2(1040f, 50f),
                    32);
                UpdateExistingText(
                    readyPanel.transform,
                    "Ready Note",
                    "Study the top-view field, then ready up with all four players.",
                    new Vector2(0f, 315f),
                    new Vector2(1040f, 36f),
                    18);

                var ruleImageObject = EnsureDirectUiChild(
                    readyPanel.transform,
                    "MinigameRuleImage");
                var ruleImage = GetOrAdd<Image>(ruleImageObject);
                ruleImage.type = Image.Type.Simple;
                ruleImage.preserveAspect = true;
                ruleImage.color = ruleImage.sprite != null
                    ? Color.white
                    : new Color(0.055f, 0.09f, 0.14f, 1f);
                ruleImage.raycastTarget = false;
                ConfigureCenteredRect(
                    RequireRect(ruleImageObject),
                    new Vector2(0f, 25f),
                    new Vector2(1040f, 520f));
                ruleImageObject.transform.SetAsFirstSibling();

                EnsureText(
                    readyPanel.transform,
                    "MinigameRulePlaceholderText",
                    "MINEFIELD RULE IMAGE\nARTWORK PLACEHOLDER",
                    font,
                    26,
                    new Vector2(0f, 25f),
                    new Vector2(900f, 120f));
                EnsureText(
                    readyPanel.transform,
                    "MinigameReadyStatus",
                    "READY 0 / 4",
                    font,
                    22,
                    new Vector2(0f, -270f),
                    new Vector2(900f, 48f));

                var readyButton = FindDescendant(
                    readyPanel.transform,
                    "ReadyButton");
                if (readyButton != null)
                {
                    ConfigureCenteredRect(
                        RequireRect(readyButton),
                        new Vector2(0f, -350f),
                        new Vector2(320f, 64f));
                    var label = readyButton.GetComponentInChildren<Text>(true);
                    if (label != null)
                    {
                        label.text = "READY";
                    }
                }

                UpdateResultPanel(contents.transform, font);
                PrefabUtility.SaveAsPrefabAsset(contents, BoardCanvasPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void UpdateResultPanel(Transform canvas, Font font)
        {
            var resultPanel = FindDescendant(canvas, "SkippedResultPanel");
            if (resultPanel == null)
            {
                throw new InvalidOperationException(
                    "BoardCanvas.prefab is missing SkippedResultPanel.");
            }

            ConfigureCenteredRect(
                RequireRect(resultPanel),
                Vector2.zero,
                new Vector2(760f, 320f));
            UpdateExistingText(
                resultPanel.transform,
                "Result Title",
                "MINEFIELD RESULTS",
                new Vector2(0f, 115f),
                new Vector2(680f, 48f),
                30);
            UpdateExistingText(
                resultPanel.transform,
                "Result Note",
                "Final rank settles after all three rounds.",
                new Vector2(0f, 72f),
                new Vector2(680f, 32f),
                18);
            EnsureText(
                resultPanel.transform,
                "MinefieldResultSummary",
                "1ST  --\n2ND  --\n3RD  --\n4TH  --",
                font,
                22,
                new Vector2(0f, -42f),
                new Vector2(680f, 180f));
        }

        private static void UpdateExistingText(
            Transform parent,
            string name,
            string value,
            Vector2 position,
            Vector2 size,
            int fontSize)
        {
            var child = FindDescendant(parent, name);
            if (child == null)
            {
                return;
            }

            var text = child.GetComponent<Text>();
            if (text != null)
            {
                text.text = value;
                text.fontSize = fontSize;
                text.alignment = TextAnchor.MiddleCenter;
            }

            ConfigureCenteredRect(RequireRect(child), position, size);
        }

        private static Text EnsureText(
            Transform parent,
            string name,
            string value,
            Font font,
            int fontSize,
            Vector2 position,
            Vector2 size)
        {
            var textObject = EnsureDirectUiChild(parent, name);
            var text = GetOrAdd<Text>(textObject);
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.color = new Color(0.92f, 0.96f, 1f, 1f);
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            ConfigureCenteredRect(RequireRect(textObject), position, size);
            textObject.transform.SetAsLastSibling();
            return text;
        }

        private static GameObject EnsureDirectUiChild(
            Transform parent,
            string name)
        {
            var existing = parent.Find(name);
            if (existing != null)
            {
                existing.gameObject.layer = parent.gameObject.layer;
                existing.gameObject.SetActive(true);
                return existing.gameObject;
            }

            var child = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer));
            child.layer = parent.gameObject.layer;
            child.transform.SetParent(parent, false);
            return child;
        }

        private static T GetOrAdd<T>(GameObject gameObject)
            where T : Component
        {
            var component = gameObject.GetComponent<T>();
            return component != null ? component : gameObject.AddComponent<T>();
        }

        private static RectTransform RequireRect(GameObject gameObject)
        {
            var rect = gameObject.GetComponent<RectTransform>();
            if (rect == null)
            {
                throw new InvalidOperationException(
                    "UI anchor '" + gameObject.name + "' must use RectTransform.");
            }

            return rect;
        }

        private static void ConfigureCenteredRect(
            RectTransform rect,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        private static GameObject FindDescendant(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root.gameObject;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var result = FindDescendant(root.GetChild(index), name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static void EnsureMinefieldAfterBoardInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            for (var index = scenes.Count - 1; index >= 0; index--)
            {
                if (scenes[index].path == MinefieldScenePath)
                {
                    scenes.RemoveAt(index);
                }
            }

            var boardIndex = -1;
            for (var index = 0; index < scenes.Count; index++)
            {
                if (scenes[index].path != BoardScenePath)
                {
                    continue;
                }

                scenes[index] = new EditorBuildSettingsScene(BoardScenePath, true);
                boardIndex = index;
                break;
            }

            if (boardIndex < 0)
            {
                scenes.Add(new EditorBuildSettingsScene(BoardScenePath, true));
                boardIndex = scenes.Count - 1;
            }

            scenes.Insert(
                boardIndex + 1,
                new EditorBuildSettingsScene(MinefieldScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolders()
        {
            EnsureFolder(Root);
            EnsureFolder(ScenesFolder);
            EnsureFolder(UiFolder);
            EnsureFolder(UiPrefabFolder);
            EnsureFolder(ArtFolder);
            EnsureFolder(MinigameArtFolder);
            EnsureFolder(MinefieldArtFolder);
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
                    "Cannot create project folder '" + path + "'.");
            }

            var parent = path.Substring(0, separator);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
        }

        private sealed class MinefieldMaterials
        {
            public Material Floor;
            public Material Grid;
            public Material Wall;
            public Material Start;
            public Material Finish;
            public Material Crusher;
        }
    }
}
