using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.StableFooting;
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
    /// Builds the additive Stable Footing arena and its replaceable prototype
    /// presentation. The production scene owns a fixed shared Cinemachine view,
    /// while Board continues to own the output Camera and AudioListener.
    /// </summary>
    public static class StableFootingProjectSetup
    {
        private const string MenuPath =
            "MazeParty/Minigames/Rebuild Stable Footing";
        private const string ProjectRoot = "Assets/MazeParty";
        private const string ScenesFolder = ProjectRoot + "/Scenes";
        private const string UiFolder = ProjectRoot + "/UI";
        private const string UiPrefabFolder = UiFolder + "/Prefabs";
        private const string ArtFolder = ProjectRoot + "/Art";
        private const string MinigameArtFolder = ArtFolder + "/Minigames";
        private const string StableFootingArtFolder =
            MinigameArtFolder + "/StableFooting";
        private const string MaterialFolder =
            StableFootingArtFolder + "/Materials";
        private const string RedLightGreenLightScenePath =
            ScenesFolder + "/RedLightGreenLight.unity";

        public const string StableFootingScenePath =
            ScenesFolder + "/StableFooting.unity";
        public const string HudPrefabPath =
            UiPrefabFolder + "/StableFootingHud.prefab";

        private const float TileSurfaceSize = 2.16f;
        private const float TileSurfaceHeight = 0.34f;
        private const float MarkHeight = 0.035f;

        [MenuItem(MenuPath)]
        public static void RebuildStableFooting()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log(
                    "Stable Footing rebuild canceled; open scene changes " +
                    "were left untouched.");
                return;
            }

            BuildStableFootingAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var scene = SceneManager.GetSceneByPath(
                StableFootingScenePath);
            if (scene.IsValid() && scene.isLoaded)
            {
                SceneManager.SetActiveScene(scene);
            }
            else
            {
                EditorSceneManager.OpenScene(
                    StableFootingScenePath,
                    OpenSceneMode.Single);
            }

            Debug.Log(
                "Stable Footing rebuilt: 6 x 8 replaceable platform arena, " +
                "three symbols, fixed shared camera, player/tile/art/audio " +
                "anchors, network state and prefab-only HUD.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRebuildStableFooting()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static void BuildStableFootingAssets()
        {
            EnsureFolders();
            BuildStableFootingScene(
                CreateMaterials(),
                LoadOrCreateHudPrefab());
            AssetDatabase.SaveAssets();
        }

        private static void BuildStableFootingScene(
            StableFootingMaterials materials,
            GameObject hudPrefab)
        {
            var previousActive = SceneManager.GetActiveScene();
            var previousActivePath = previousActive.path;
            var scratchScene = default(Scene);
            var loadedScene = SceneManager.GetSceneByPath(
                StableFootingScenePath);

            if (loadedScene.IsValid() && loadedScene.isLoaded)
            {
                if (loadedScene.isDirty)
                {
                    throw new InvalidOperationException(
                        "StableFooting.unity has unsaved changes. Save or " +
                        "discard them before rebuilding the generated scene.");
                }

                if (SceneManager.sceneCount == 1)
                {
                    scratchScene = EditorSceneManager.NewScene(
                        NewSceneSetup.EmptyScene,
                        NewSceneMode.Additive);
                    SceneManager.SetActiveScene(scratchScene);
                }

                EditorSceneManager.CloseScene(loadedScene, true);
            }

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            var root = new GameObject("Stable Footing Network State");
            var arenaPresentation = new GameObject("Arena Presentation");
            arenaPresentation.transform.SetParent(root.transform, false);

            var arena = CreateArena(
                arenaPresentation.transform,
                materials);
            CreateLighting(arenaPresentation.transform);
            var sharedCamera = CreateSharedCamera(root.transform);

            var runtimeRunners = new GameObject("Runtime Runners");
            runtimeRunners.transform.SetParent(root.transform, false);

            var audioAnchor = new GameObject("Audio Replacement Anchor");
            audioAnchor.transform.SetParent(root.transform, false);
            audioAnchor.transform.position = new Vector3(
                NetworkStableFootingState.ArenaCenterX,
                2f,
                -StableFootingRules.BoardHeight *
                NetworkStableFootingState.TileSize * 0.5f - 1.5f);
            var cueAudioSource = audioAnchor.AddComponent<AudioSource>();
            cueAudioSource.playOnAwake = false;
            cueAudioSource.loop = false;
            cueAudioSource.spatialBlend = 0f;

            CreateArtReplacementAnchors(root.transform);

            // Save first so NGO can give the subsequently added NetworkObject
            // a stable in-scene GlobalObjectIdHash.
            EditorSceneManager.SaveScene(scene, StableFootingScenePath);

            root.AddComponent<NetworkObject>();
            var state = root.AddComponent<NetworkStableFootingState>();
            var view = root.AddComponent<StableFootingNetworkView>();

            var hudObject = PrefabUtility.InstantiatePrefab(
                hudPrefab,
                root.transform) as GameObject;
            if (hudObject != null)
            {
                hudObject.transform.localScale =
                    hudPrefab.transform.localScale;
            }
            var hud = hudObject != null
                ? hudObject.GetComponent<StableFootingHudBindings>()
                : null;
            if (hud == null || !hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "StableFootingHud.prefab could not be instantiated with " +
                    "its required serialized bindings.");
            }

            view.Configure(
                state,
                sharedCamera,
                runtimeRunners.transform,
                arena.TileRoot,
                arenaPresentation,
                arena.SafeSymbolCrossRenderer,
                arena.SafeSymbolCircleRenderer,
                arena.SafeSymbolSquareRenderer,
                cueAudioSource,
                hud);

            ValidateSceneContract(root);
            EditorSceneManager.SaveScene(scene, StableFootingScenePath);
            EnsureStableFootingInBuildSettings();

            if (previousActivePath == StableFootingScenePath)
            {
                SceneManager.SetActiveScene(scene);
            }
            else if (previousActive.IsValid() &&
                     previousActive.isLoaded &&
                     previousActive.path != StableFootingScenePath)
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

        private static ArenaReferences CreateArena(
            Transform parent,
            StableFootingMaterials materials)
        {
            var width = StableFootingRules.BoardWidth *
                        NetworkStableFootingState.TileSize;
            var length = StableFootingRules.BoardHeight *
                         NetworkStableFootingState.TileSize;

            CreatePrimitive(
                "Arena Understructure",
                PrimitiveType.Cube,
                parent,
                new Vector3(
                    NetworkStableFootingState.ArenaCenterX,
                    -1.25f,
                    0f),
                Quaternion.identity,
                new Vector3(width + 2f, 1.2f, length + 2f),
                materials.Understructure,
                true);

            var tileRoot = new GameObject("Tile Anchors").transform;
            tileRoot.SetParent(parent, false);
            for (var index = 0;
                 index < StableFootingRules.TileCount;
                 index++)
            {
                CreateTileAnchor(
                    tileRoot,
                    index,
                    materials);
            }

            var playerAnchorRoot =
                new GameObject("Player Anchors").transform;
            playerAnchorRoot.SetParent(parent, false);
            for (var slot = 0;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                var anchor = new GameObject(
                    "Player Anchor " + (slot + 1)).transform;
                anchor.SetParent(playerAnchorRoot, false);
                anchor.position = NetworkStableFootingState.GetTileCenter(
                    NetworkStableFootingState.GetStartTileIndex(slot));
                anchor.position += Vector3.up *
                                   StableFootingNetworkView
                                       .RunnerPresentationHeight;
            }

            var safeDisplay = CreateSafeSymbolDisplay(
                parent,
                length,
                materials);

            return new ArenaReferences
            {
                TileRoot = tileRoot,
                SafeSymbolCrossRenderer = safeDisplay.CrossRenderer,
                SafeSymbolCircleRenderer = safeDisplay.CircleRenderer,
                SafeSymbolSquareRenderer = safeDisplay.SquareRenderer
            };
        }

        private static void CreateTileAnchor(
            Transform tileRoot,
            int tileIndex,
            StableFootingMaterials materials)
        {
            var anchor = new GameObject(
                "Tile Anchor " + tileIndex.ToString("00")).transform;
            anchor.SetParent(tileRoot, false);
            anchor.position = NetworkStableFootingState.GetTileCenter(
                tileIndex);

            CreatePrimitive(
                "Tile Surface",
                PrimitiveType.Cube,
                anchor,
                Vector3.zero,
                Quaternion.identity,
                new Vector3(
                    TileSurfaceSize,
                    TileSurfaceHeight,
                    TileSurfaceSize),
                materials.Tile,
                true);

            var markY = TileSurfaceHeight * 0.5f + MarkHeight;
            var cross = CreateCrossMark(
                "Cross Mark",
                anchor,
                new Vector3(0f, markY, 0f),
                materials.Cross,
                0.72f);
            var circle = CreatePrimitive(
                "Circle Mark",
                PrimitiveType.Cylinder,
                anchor,
                new Vector3(0f, markY, 0f),
                Quaternion.identity,
                new Vector3(0.63f, MarkHeight, 0.63f),
                materials.Circle,
                false);
            var square = CreatePrimitive(
                "Square Mark",
                PrimitiveType.Cube,
                anchor,
                new Vector3(0f, markY, 0f),
                Quaternion.Euler(0f, 45f, 0f),
                new Vector3(0.9f, MarkHeight * 2f, 0.9f),
                materials.Square,
                false);

            var visibleSymbol = tileIndex % StableFootingRules.SymbolCount;
            cross.SetActive(visibleSymbol == 0);
            circle.SetActive(visibleSymbol == 1);
            square.SetActive(visibleSymbol == 2);
        }

        private static SafeDisplayReferences CreateSafeSymbolDisplay(
            Transform parent,
            float arenaLength,
            StableFootingMaterials materials)
        {
            var display = new GameObject("Safe Symbol Display").transform;
            display.SetParent(parent, false);
            display.position = new Vector3(
                NetworkStableFootingState.ArenaCenterX,
                0.3f,
                arenaLength * 0.5f + 0.6f);

            CreatePrimitive(
                "Display Backing",
                PrimitiveType.Cube,
                display,
                Vector3.zero,
                Quaternion.identity,
                new Vector3(7.8f, 0.35f, 2.4f),
                materials.Wall,
                false);

            var cross = CreateCrossMark(
                "Cross Mark",
                display,
                new Vector3(-2.25f, 0.23f, 0f),
                materials.Cross,
                1.35f);
            var circle = CreatePrimitive(
                "Circle Mark",
                PrimitiveType.Cylinder,
                display,
                new Vector3(0f, 0.23f, 0f),
                Quaternion.identity,
                new Vector3(1.15f, 0.08f, 1.15f),
                materials.Circle,
                false);
            var square = CreatePrimitive(
                "Square Mark",
                PrimitiveType.Cube,
                display,
                new Vector3(2.25f, 0.23f, 0f),
                Quaternion.Euler(0f, 45f, 0f),
                new Vector3(1.45f, 0.08f, 1.45f),
                materials.Square,
                false);

            return new SafeDisplayReferences
            {
                CrossRenderer = cross.GetComponentInChildren<Renderer>(),
                CircleRenderer = circle.GetComponent<Renderer>(),
                SquareRenderer = square.GetComponent<Renderer>()
            };
        }

        private static GameObject CreateCrossMark(
            string name,
            Transform parent,
            Vector3 localPosition,
            Material material,
            float size)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;

            CreatePrimitive(
                "Cross Stroke A",
                PrimitiveType.Cube,
                root.transform,
                Vector3.zero,
                Quaternion.Euler(0f, 45f, 0f),
                new Vector3(size * 1.7f, MarkHeight * 2f, size * 0.38f),
                material,
                false);
            CreatePrimitive(
                "Cross Stroke B",
                PrimitiveType.Cube,
                root.transform,
                Vector3.zero,
                Quaternion.Euler(0f, -45f, 0f),
                new Vector3(size * 1.7f, MarkHeight * 2f, size * 0.38f),
                material,
                false);
            return root;
        }

        private static void CreateArtReplacementAnchors(Transform parent)
        {
            var root = new GameObject("Art Replacement Anchors").transform;
            root.SetParent(parent, false);

            var arena = new GameObject("Arena Art Anchor").transform;
            arena.SetParent(root, false);
            arena.position = new Vector3(
                NetworkStableFootingState.ArenaCenterX,
                0f,
                0f);

            var environment =
                new GameObject("Environment Art Anchor").transform;
            environment.SetParent(root, false);
            environment.position = arena.position;

            var effects = new GameObject("VFX Anchor").transform;
            effects.SetParent(root, false);
            effects.position = arena.position;
        }

        private static void CreateLighting(Transform parent)
        {
            var lightObject = new GameObject(
                "Stable Footing Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation =
                Quaternion.Euler(52f, -28f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.color = new Color(1f, 0.95f, 0.88f);
        }

        private static CinemachineCamera CreateSharedCamera(
            Transform parent)
        {
            var cameraObject = new GameObject("CM_StableFootingShared");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.SetPositionAndRotation(
                StableFootingNetworkView.SharedCameraPosition,
                StableFootingNetworkView.SharedCameraRotation);

            var camera = cameraObject.AddComponent<CinemachineCamera>();
            camera.Priority = 0;
            var lens = camera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize =
                StableFootingNetworkView.SharedCameraOrthographicSize;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 160f;
            camera.Lens = lens;
            return camera;
        }

        private static GameObject LoadOrCreateHudPrefab()
        {
            EnsureFolder(UiPrefabFolder);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                HudPrefabPath);
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

            var bindings = prefab != null
                ? prefab.GetComponent<StableFootingHudBindings>()
                : null;
            if (bindings == null || !bindings.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "StableFootingHud.prefab is missing its serialized UI " +
                    "binding contract. Repair the prefab instead of allowing " +
                    "runtime UI generation.");
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
                    "Unity built-in LegacyRuntime.ttf font could not be loaded.");
            }

            var root = new GameObject(
                "StableFootingHud",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(StableFootingHudBindings));
            root.transform.localScale = Vector3.one;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 45;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = CreatePanel(
                "HudPanel",
                root.transform,
                new Vector2(0f, 1f),
                new Vector2(24f, -24f),
                new Vector2(500f, 190f),
                new Color(0.025f, 0.035f, 0.055f, 0.84f));

            var phase = CreateHudText(
                "Phase",
                panel.transform,
                font,
                new Vector2(0f, -14f),
                new Vector2(450f, 38f),
                24,
                TextAnchor.MiddleCenter,
                FontStyle.Bold,
                "STABLE FOOTING  ·  MOVE");
            var timer = CreateHudText(
                "Timer",
                panel.transform,
                font,
                new Vector2(0f, -54f),
                new Vector2(450f, 52f),
                36,
                TextAnchor.MiddleCenter,
                FontStyle.Bold,
                "01:00");

            var statusPanel = CreatePanel(
                "StatusPanel",
                root.transform,
                new Vector2(1f, 1f),
                new Vector2(-24f, -24f),
                new Vector2(500f, 190f),
                new Color(0.025f, 0.035f, 0.055f, 0.84f));
            var round = CreateHudText(
                "Round",
                statusPanel.transform,
                font,
                new Vector2(0f, -14f),
                new Vector2(450f, 38f),
                24,
                TextAnchor.MiddleCenter,
                FontStyle.Bold,
                "ROUND 1 / 3");
            var instructions = CreateHudText(
                "Instructions",
                panel.transform,
                font,
                new Vector2(0f, -118f),
                new Vector2(450f, 50f),
                16,
                TextAnchor.MiddleCenter,
                FontStyle.Normal,
                "WASD MOVE  ·  LEFT CLICK PUSH  ·  STAND ON THE SAFE SYMBOL");

            var rows = new Text[StableFootingRules.PlayerCount];
            var playerColors = new[]
            {
                new Color(0.16f, 0.48f, 0.95f),
                new Color(0.92f, 0.2f, 0.16f),
                new Color(0.18f, 0.78f, 0.32f),
                new Color(0.7f, 0.26f, 0.9f)
            };
            for (var slot = 0; slot < rows.Length; slot++)
            {
                rows[slot] = CreateHudText(
                    "Player" + (slot + 1) + "Row",
                    statusPanel.transform,
                    font,
                    new Vector2(
                        slot % 2 == 0 ? -120f : 120f,
                        slot < 2 ? -58f : -116f),
                    new Vector2(220f, 52f),
                    16,
                    TextAnchor.MiddleCenter,
                    FontStyle.Bold,
                    "PLAYER " + (slot + 1) + "\nALIVE");
                rows[slot].color = playerColors[slot];
            }

            var controlsPanel = CreatePanel(
                "ControlsPanel",
                root.transform,
                new Vector2(0f, 0f),
                new Vector2(24f, 24f),
                new Vector2(460f, 90f),
                new Color(0.025f, 0.035f, 0.055f, 0.82f));
            CreateHudText(
                "Controls",
                controlsPanel.transform,
                font,
                Vector2.zero,
                new Vector2(420f, 70f),
                18,
                TextAnchor.MiddleCenter,
                FontStyle.Bold,
                "WASD  MOVE\nLEFT CLICK  PUSH");

            var pausePanel = CreatePanel(
                "PausePanel",
                root.transform,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(720f, 180f),
                new Color(0.02f, 0.025f, 0.04f, 0.94f));
            CreateHudText(
                "PauseMessage",
                pausePanel.transform,
                font,
                Vector2.zero,
                new Vector2(680f, 140f),
                28,
                TextAnchor.MiddleCenter,
                FontStyle.Bold,
                "PLAYER DISCONNECTED\nMATCH PAUSED");
            pausePanel.SetActive(false);

            var resultPanel = CreatePanel(
                "ResultPanel",
                root.transform,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(760f, 220f),
                new Color(0.02f, 0.025f, 0.04f, 0.94f));
            CreateHudText(
                "ResultMessage",
                resultPanel.transform,
                font,
                Vector2.zero,
                new Vector2(720f, 180f),
                30,
                TextAnchor.MiddleCenter,
                FontStyle.Bold,
                "ROUND RESULTS");
            resultPanel.SetActive(false);

            root.GetComponent<StableFootingHudBindings>().Configure(
                canvas,
                phase,
                timer,
                round,
                instructions,
                rows,
                pausePanel,
                controlsPanel,
                resultPanel);
            return root;
        }

        private static GameObject CreatePanel(
            string name,
            Transform parent,
            Vector2 anchor,
            Vector2 anchoredPosition,
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
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            panel.GetComponent<Image>().color = color;
            return panel;
        }

        private static Text CreateHudText(
            string name,
            Transform parent,
            Font font,
            Vector2 anchoredPosition,
            Vector2 size,
            int fontSize,
            TextAnchor alignment,
            FontStyle style,
            string sampleText)
        {
            var textObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            textObject.transform.SetParent(parent, false);
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var text = textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = new Color(0.94f, 0.97f, 1f);
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = sampleText;
            return text;
        }

        private static GameObject CreatePrimitive(
            string name,
            PrimitiveType primitiveType,
            Transform parent,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale,
            Material material,
            bool keepCollider)
        {
            var gameObject = GameObject.CreatePrimitive(primitiveType);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPosition;
            gameObject.transform.localRotation = localRotation;
            gameObject.transform.localScale = localScale;
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

        private static StableFootingMaterials CreateMaterials()
        {
            return new StableFootingMaterials
            {
                Understructure = CreateOrUpdateMaterial(
                    "StableFootingUnderstructure",
                    new Color(0.055f, 0.07f, 0.1f)),
                Tile = CreateOrUpdateMaterial(
                    "StableFootingTile",
                    new Color(0.2f, 0.24f, 0.3f)),
                Wall = CreateOrUpdateMaterial(
                    "StableFootingWall",
                    new Color(0.08f, 0.105f, 0.15f)),
                Cross = CreateOrUpdateMaterial(
                    "StableFootingSymbolCross",
                    new Color(1f, 0.24f, 0.18f),
                    new Color(0.4f, 0.035f, 0.02f)),
                Circle = CreateOrUpdateMaterial(
                    "StableFootingSymbolCircle",
                    new Color(0.12f, 0.72f, 1f),
                    new Color(0.01f, 0.2f, 0.45f)),
                Square = CreateOrUpdateMaterial(
                    "StableFootingSymbolSquare",
                    new Color(1f, 0.78f, 0.1f),
                    new Color(0.42f, 0.2f, 0.01f))
            };
        }

        private static Material CreateOrUpdateMaterial(
            string name,
            Color color,
            Color? emission = null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Universal Render Pipeline/Lit is required for Stable " +
                    "Footing materials.");
            }

            var path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0f);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.2f);
            }
            if (emission.HasValue && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ValidateSceneContract(GameObject root)
        {
            var tileRoot = FindDescendant(root.transform, "Tile Anchors");
            var playerAnchors = FindDescendant(
                root.transform,
                "Player Anchors");
            var hud = root.GetComponentInChildren<
                StableFootingHudBindings>(true);

            if (root.GetComponent<NetworkObject>() == null ||
                root.GetComponent<NetworkStableFootingState>() == null ||
                root.GetComponent<StableFootingNetworkView>() == null ||
                tileRoot == null ||
                tileRoot.transform.childCount !=
                    StableFootingRules.TileCount ||
                playerAnchors == null ||
                playerAnchors.transform.childCount !=
                    StableFootingRules.PlayerCount ||
                FindDescendant(
                    root.transform,
                    "Safe Symbol Display") == null ||
                FindDescendant(
                    root.transform,
                    "Audio Replacement Anchor") == null ||
                FindDescendant(
                    root.transform,
                    "Art Replacement Anchors") == null ||
                root.GetComponentInChildren<CinemachineCamera>(true) == null ||
                root.GetComponentInChildren<AudioSource>(true) == null ||
                hud == null)
            {
                throw new InvalidOperationException(
                    "Generated Stable Footing scene is missing its network, " +
                    "arena, symbols, anchors, camera, audio or HUD contract.");
            }

            if (!hud.HasRequiredReferences ||
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    hud.gameObject) != HudPrefabPath)
            {
                throw new InvalidOperationException(
                    "Stable Footing HUD must remain a configured " +
                    "StableFootingHud.prefab instance.");
            }

            if (root.GetComponentInChildren<Camera>(true) != null ||
                root.GetComponentInChildren<AudioListener>(true) != null)
            {
                throw new InvalidOperationException(
                    "Stable Footing must not contain a Unity Camera or " +
                    "AudioListener; the additive Board scene owns output.");
            }
        }

        private static GameObject FindDescendant(
            Transform root,
            string childName)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == childName)
            {
                return root.gameObject;
            }
            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindDescendant(
                    root.GetChild(index),
                    childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static void EnsureFolders()
        {
            EnsureFolder(ScenesFolder);
            EnsureFolder(UiFolder);
            EnsureFolder(UiPrefabFolder);
            EnsureFolder(ArtFolder);
            EnsureFolder(MinigameArtFolder);
            EnsureFolder(StableFootingArtFolder);
            EnsureFolder(MaterialFolder);
        }

        private static void EnsureStableFootingInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            for (var index = scenes.Count - 1; index >= 0; index--)
            {
                if (scenes[index].path == StableFootingScenePath)
                {
                    scenes.RemoveAt(index);
                }
            }

            var insertAfter = -1;
            for (var index = 0; index < scenes.Count; index++)
            {
                if (scenes[index].path == RedLightGreenLightScenePath)
                {
                    insertAfter = index;
                    break;
                }
            }

            scenes.Insert(
                insertAfter >= 0 ? insertAfter + 1 : scenes.Count,
                new EditorBuildSettingsScene(
                    StableFootingScenePath,
                    true));
            EditorBuildSettings.scenes = scenes.ToArray();
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
                    "Cannot create project folder '" + path + "'.");
            }
            var parent = path.Substring(0, slash);
            var child = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, child);
        }

        private sealed class StableFootingMaterials
        {
            public Material Understructure { get; set; }
            public Material Tile { get; set; }
            public Material Wall { get; set; }
            public Material Cross { get; set; }
            public Material Circle { get; set; }
            public Material Square { get; set; }
        }

        private sealed class ArenaReferences
        {
            public Transform TileRoot { get; set; }
            public Renderer SafeSymbolCrossRenderer { get; set; }
            public Renderer SafeSymbolCircleRenderer { get; set; }
            public Renderer SafeSymbolSquareRenderer { get; set; }
        }

        private sealed class SafeDisplayReferences
        {
            public Renderer CrossRenderer { get; set; }
            public Renderer CircleRenderer { get; set; }
            public Renderer SquareRenderer { get; set; }
        }
    }
}
