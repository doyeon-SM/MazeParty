using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.RedLightGreenLight;
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
    /// Generates the additive Red Light / Green Light scene and its replaceable
    /// prototype presentation. Existing build scenes are preserved and the new
    /// scene is inserted immediately after WrongWay.
    /// </summary>
    public static class RedLightGreenLightProjectSetup
    {
        private const string MenuPath =
            "MazeParty/Minigames/Rebuild Red Light Green Light";
        private const string ProjectRoot = "Assets/MazeParty";
        private const string ScenesFolder = ProjectRoot + "/Scenes";
        private const string UiFolder = ProjectRoot + "/UI";
        private const string UiPrefabFolder = UiFolder + "/Prefabs";
        public const string HudPrefabPath =
            UiPrefabFolder + "/RedLightGreenLightHud.prefab";
        private const string ArtFolder = ProjectRoot + "/Art";
        private const string MinigameArtFolder = ArtFolder + "/Minigames";
        private const string RedLightGreenLightArtFolder =
            MinigameArtFolder + "/RedLightGreenLight";
        private const string MaterialFolder =
            RedLightGreenLightArtFolder + "/Materials";
        private const string BoardScenePath = ScenesFolder + "/Board.unity";
        private const string MinefieldScenePath =
            ScenesFolder + "/Minefield.unity";
        private const string WrongWayScenePath =
            ScenesFolder + "/WrongWay.unity";

        public const string RedLightGreenLightScenePath =
            "Assets/MazeParty/Scenes/RedLightGreenLight.unity";

        [MenuItem(MenuPath)]
        public static void RebuildRedLightGreenLight()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log(
                    "Red Light / Green Light rebuild canceled; open scene " +
                    "changes were left untouched.");
                return;
            }

            BuildRedLightGreenLightAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var minigameScene = SceneManager.GetSceneByPath(
                RedLightGreenLightScenePath);
            if (minigameScene.IsValid() && minigameScene.isLoaded)
            {
                SceneManager.SetActiveScene(minigameScene);
            }
            else
            {
                EditorSceneManager.OpenScene(
                    RedLightGreenLightScenePath,
                    OpenSceneMode.Single);
            }

            Debug.Log(
                "Red Light / Green Light rebuilt: 20 x 44 shared arena, " +
                "replaceable observer and signal anchors, network state, " +
                "and additive-safe personal top-view camera.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRebuildRedLightGreenLight()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static void BuildRedLightGreenLightAssets()
        {
            EnsureFolders();
            BuildRedLightGreenLightScene(
                CreateMaterials(),
                LoadOrCreateHudPrefab());
            AssetDatabase.SaveAssets();
        }

        private static void BuildRedLightGreenLightScene(
            RedLightGreenLightMaterials materials,
            GameObject hudPrefab)
        {
            var previousActive = SceneManager.GetActiveScene();
            var previousActivePath = previousActive.path;
            var scratchScene = default(Scene);
            var loadedMinigame = SceneManager.GetSceneByPath(
                RedLightGreenLightScenePath);

            if (loadedMinigame.IsValid() && loadedMinigame.isLoaded)
            {
                if (loadedMinigame.isDirty)
                {
                    throw new InvalidOperationException(
                        "RedLightGreenLight.unity has unsaved changes. Save or " +
                        "discard them before rebuilding the generated scene.");
                }

                if (SceneManager.sceneCount == 1)
                {
                    scratchScene = EditorSceneManager.OpenScene(
                        BoardScenePath,
                        OpenSceneMode.Additive);
                    SceneManager.SetActiveScene(scratchScene);
                }

                EditorSceneManager.CloseScene(loadedMinigame, true);
            }

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            var root = new GameObject(
                "Red Light Green Light Network State");
            var arenaPresentation = new GameObject("Arena Presentation");
            arenaPresentation.transform.SetParent(root.transform, false);

            var presentation = CreateArena(
                arenaPresentation.transform,
                materials);
            CreateLighting(arenaPresentation.transform);
            var topDownCamera = CreateTopDownCamera(root.transform);

            var runtimeRunners = new GameObject("Runtime Runners");
            runtimeRunners.transform.SetParent(root.transform, false);

            var cueAnchor = new GameObject("Signal Audio Anchor");
            cueAnchor.transform.SetParent(root.transform, false);
            cueAnchor.transform.position = new Vector3(
                NetworkRedLightGreenLightState.ArenaCenterX,
                2f,
                NetworkRedLightGreenLightState.ArenaMaxZ - 1f);
            var cueAudioSource = cueAnchor.AddComponent<AudioSource>();
            cueAudioSource.playOnAwake = false;
            cueAudioSource.loop = false;
            cueAudioSource.spatialBlend = 0f;

            // Save and register the scene before adding the NetworkObject so NGO
            // can assign a stable in-scene GlobalObjectIdHash.
            EditorSceneManager.SaveScene(
                scene,
                RedLightGreenLightScenePath);
            EnsureRedLightGreenLightInBuildSettings();

            root.AddComponent<NetworkObject>();
            var networkState =
                root.AddComponent<NetworkRedLightGreenLightState>();
            var networkView =
                root.AddComponent<RedLightGreenLightNetworkView>();
            var hudObject = PrefabUtility.InstantiatePrefab(
                hudPrefab,
                root.transform) as GameObject;
            if (hudObject != null)
            {
                hudObject.transform.localScale =
                    hudPrefab.transform.localScale;
            }
            var hud = hudObject != null
                ? hudObject.GetComponent<RedLightGreenLightHudBindings>()
                : null;
            if (hud == null || !hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "RedLightGreenLightHud.prefab could not be instantiated " +
                    "with its required bindings.");
            }
            ConfigureNetworkView(
                networkView,
                networkState,
                topDownCamera,
                runtimeRunners.transform,
                arenaPresentation,
                presentation,
                cueAudioSource,
                hud);

            ValidateSceneContract(root);
            EditorSceneManager.SaveScene(
                scene,
                RedLightGreenLightScenePath);

            if (previousActivePath == RedLightGreenLightScenePath)
            {
                SceneManager.SetActiveScene(scene);
            }
            else if (previousActive.IsValid() &&
                     previousActive.isLoaded &&
                     previousActive.path != RedLightGreenLightScenePath)
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

        private static PresentationReferences CreateArena(
            Transform parent,
            RedLightGreenLightMaterials materials)
        {
            var centerZ =
                (NetworkRedLightGreenLightState.ArenaMinZ +
                 NetworkRedLightGreenLightState.ArenaMaxZ) * 0.5f;
            var width =
                NetworkRedLightGreenLightState.ArenaMaxX -
                NetworkRedLightGreenLightState.ArenaMinX;
            var length =
                NetworkRedLightGreenLightState.ArenaMaxZ -
                NetworkRedLightGreenLightState.ArenaMinZ;

            CreatePrimitive(
                "Arena Floor",
                PrimitiveType.Cube,
                parent,
                new Vector3(
                    NetworkRedLightGreenLightState.ArenaCenterX,
                    -0.1f,
                    centerZ),
                Quaternion.identity,
                new Vector3(width, 0.2f, length),
                materials.Floor,
                true);

            const float wallThickness = 0.5f;
            const float wallHeight = 2.5f;
            CreatePrimitive(
                "West Wall",
                PrimitiveType.Cube,
                parent,
                new Vector3(
                    NetworkRedLightGreenLightState.ArenaMinX -
                    wallThickness * 0.5f,
                    wallHeight * 0.5f,
                    centerZ),
                Quaternion.identity,
                new Vector3(wallThickness, wallHeight, length),
                materials.Wall,
                true);
            CreatePrimitive(
                "East Wall",
                PrimitiveType.Cube,
                parent,
                new Vector3(
                    NetworkRedLightGreenLightState.ArenaMaxX +
                    wallThickness * 0.5f,
                    wallHeight * 0.5f,
                    centerZ),
                Quaternion.identity,
                new Vector3(wallThickness, wallHeight, length),
                materials.Wall,
                true);
            CreatePrimitive(
                "Start Wall",
                PrimitiveType.Cube,
                parent,
                new Vector3(
                    NetworkRedLightGreenLightState.ArenaCenterX,
                    wallHeight * 0.5f,
                    NetworkRedLightGreenLightState.ArenaMinZ -
                    wallThickness * 0.5f),
                Quaternion.identity,
                new Vector3(
                    width + wallThickness * 2f,
                    wallHeight,
                    wallThickness),
                materials.Wall,
                true);
            CreatePrimitive(
                "Finish Wall",
                PrimitiveType.Cube,
                parent,
                new Vector3(
                    NetworkRedLightGreenLightState.ArenaCenterX,
                    wallHeight * 0.5f,
                    NetworkRedLightGreenLightState.ArenaMaxZ +
                    wallThickness * 0.5f),
                Quaternion.identity,
                new Vector3(
                    width + wallThickness * 2f,
                    wallHeight,
                    wallThickness),
                materials.Wall,
                true);

            CreateLine(
                "Start Line",
                parent,
                NetworkRedLightGreenLightState.ArenaMinZ + 0.75f,
                width,
                materials.Start);
            CreateLine(
                "Finish Line",
                parent,
                NetworkRedLightGreenLightState.ArenaMaxZ - 0.5f,
                width,
                materials.Finish);
            CreateCourseGuides(parent, width, length, materials.Guide);

            var observerHead = CreateObserver(parent, materials);
            CreateSignalTower(
                parent,
                materials,
                out var greenSignalRenderer,
                out var redSignalRenderer,
                out var greenSignalLight,
                out var redSignalLight);

            return new PresentationReferences
            {
                ObserverHead = observerHead,
                GreenSignalRenderer = greenSignalRenderer,
                RedSignalRenderer = redSignalRenderer,
                GreenSignalLight = greenSignalLight,
                RedSignalLight = redSignalLight
            };
        }

        private static void CreateLine(
            string name,
            Transform parent,
            float worldZ,
            float width,
            Material material)
        {
            CreatePrimitive(
                name,
                PrimitiveType.Cube,
                parent,
                new Vector3(
                    NetworkRedLightGreenLightState.ArenaCenterX,
                    0.025f,
                    worldZ),
                Quaternion.identity,
                new Vector3(width, 0.05f, 0.4f),
                material,
                false);
        }

        private static void CreateCourseGuides(
            Transform parent,
            float width,
            float length,
            Material material)
        {
            var guideRoot = new GameObject("Course Guides");
            guideRoot.transform.SetParent(parent, false);

            for (var index = 1; index <= 3; index++)
            {
                var x = NetworkRedLightGreenLightState.ArenaMinX +
                        width * index / 4f;
                CreatePrimitive(
                    "Guide " + index,
                    PrimitiveType.Cube,
                    guideRoot.transform,
                    new Vector3(x, 0.0125f, 0f),
                    Quaternion.identity,
                    new Vector3(0.045f, 0.025f, length),
                    material,
                    false);
            }
        }

        private static Transform CreateObserver(
            Transform parent,
            RedLightGreenLightMaterials materials)
        {
            var observer = new GameObject("Observer Placeholder");
            observer.transform.SetParent(parent, false);
            observer.transform.position = new Vector3(
                NetworkRedLightGreenLightState.ArenaCenterX,
                0f,
                NetworkRedLightGreenLightState.ArenaMaxZ - 1.6f);

            CreatePrimitive(
                "Observer Plinth",
                PrimitiveType.Cylinder,
                observer.transform,
                new Vector3(0f, 0.3f, 0f),
                Quaternion.identity,
                new Vector3(1.8f, 0.3f, 1.8f),
                materials.ObserverAccent,
                true);
            CreatePrimitive(
                "Observer Body",
                PrimitiveType.Capsule,
                observer.transform,
                new Vector3(0f, 1.9f, 0f),
                Quaternion.identity,
                new Vector3(1.25f, 1.35f, 1.1f),
                materials.ObserverBody,
                false);
            CreatePrimitive(
                "Observer Left Arm",
                PrimitiveType.Capsule,
                observer.transform,
                new Vector3(-0.95f, 2f, 0f),
                Quaternion.Euler(0f, 0f, -12f),
                new Vector3(0.42f, 1f, 0.42f),
                materials.ObserverBody,
                false);
            CreatePrimitive(
                "Observer Right Arm",
                PrimitiveType.Capsule,
                observer.transform,
                new Vector3(0.95f, 2f, 0f),
                Quaternion.Euler(0f, 0f, 12f),
                new Vector3(0.42f, 1f, 0.42f),
                materials.ObserverBody,
                false);

            var head = CreatePrimitive(
                "Observer Head",
                PrimitiveType.Sphere,
                observer.transform,
                new Vector3(0f, 3.75f, 0f),
                Quaternion.Euler(0f, 180f, 0f),
                new Vector3(1.35f, 1.35f, 1.35f),
                materials.ObserverHead,
                false);
            CreatePrimitive(
                "Observer Face Direction",
                PrimitiveType.Cube,
                head.transform,
                new Vector3(0f, 0f, -0.48f),
                Quaternion.identity,
                new Vector3(0.62f, 0.2f, 0.18f),
                materials.ObserverAccent,
                false);
            return head.transform;
        }

        private static void CreateSignalTower(
            Transform parent,
            RedLightGreenLightMaterials materials,
            out Renderer greenRenderer,
            out Renderer redRenderer,
            out Light greenLight,
            out Light redLight)
        {
            var tower = new GameObject("Signal Tower Placeholder");
            tower.transform.SetParent(parent, false);
            tower.transform.position = new Vector3(
                NetworkRedLightGreenLightState.ArenaMaxX - 2.1f,
                0f,
                NetworkRedLightGreenLightState.ArenaMaxZ - 1.8f);

            CreatePrimitive(
                "Signal Stand",
                PrimitiveType.Cylinder,
                tower.transform,
                new Vector3(0f, 1.35f, 0f),
                Quaternion.identity,
                new Vector3(0.35f, 1.35f, 0.35f),
                materials.SignalHousing,
                true);
            CreatePrimitive(
                "Signal Housing",
                PrimitiveType.Cube,
                tower.transform,
                new Vector3(0f, 3.05f, 0f),
                Quaternion.identity,
                new Vector3(1.3f, 2.15f, 0.8f),
                materials.SignalHousing,
                false);

            var greenSignal = CreatePrimitive(
                "Green Signal",
                PrimitiveType.Sphere,
                tower.transform,
                new Vector3(0f, 3.52f, -0.43f),
                Quaternion.identity,
                Vector3.one * 0.72f,
                materials.GreenSignal,
                false);
            greenRenderer = greenSignal.GetComponent<Renderer>();
            greenLight = CreateSignalLight(
                "Green Signal Light",
                greenSignal.transform,
                new Color(0.12f, 1f, 0.25f),
                true);

            var redSignal = CreatePrimitive(
                "Red Signal",
                PrimitiveType.Sphere,
                tower.transform,
                new Vector3(0f, 2.62f, -0.43f),
                Quaternion.identity,
                Vector3.one * 0.72f,
                materials.RedSignal,
                false);
            redRenderer = redSignal.GetComponent<Renderer>();
            redLight = CreateSignalLight(
                "Red Signal Light",
                redSignal.transform,
                new Color(1f, 0.08f, 0.04f),
                false);
        }

        private static Light CreateSignalLight(
            string name,
            Transform parent,
            Color color,
            bool enabled)
        {
            var lightObject = new GameObject(name);
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.localPosition =
                new Vector3(0f, 0f, -0.35f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = 9f;
            light.intensity = 4f;
            light.shadows = LightShadows.None;
            light.enabled = enabled;
            return light;
        }

        private static void CreateLighting(Transform parent)
        {
            var lightObject = new GameObject(
                "Red Light Green Light Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation =
                Quaternion.Euler(50f, -32f, 0f);

            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.95f, 0.88f);
        }

        private static CinemachineCamera CreateTopDownCamera(
            Transform parent)
        {
            var cameraObject = new GameObject(
                "CM_RedLightGreenLightTopDown");
            cameraObject.transform.SetParent(parent, false);

            var focus = new Vector3(
                NetworkRedLightGreenLightState.ArenaCenterX,
                0f,
                NetworkRedLightGreenLightState.ArenaMinZ + 0.75f);
            cameraObject.transform.SetPositionAndRotation(
                RedLightGreenLightNetworkView
                    .CalculatePlayerCameraPosition(focus),
                RedLightGreenLightNetworkView.PlayerCameraRotation);

            var camera = cameraObject.AddComponent<CinemachineCamera>();
            camera.Priority = 0;
            var lens = camera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize =
                RedLightGreenLightNetworkView
                    .PlayerCameraOrthographicSize;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 150f;
            camera.Lens = lens;
            return camera;
        }

        private static void ConfigureNetworkView(
            RedLightGreenLightNetworkView view,
            NetworkRedLightGreenLightState state,
            CinemachineCamera camera,
            Transform runnerRoot,
            GameObject arenaPresentation,
            PresentationReferences presentation,
            AudioSource cueAudioSource,
            RedLightGreenLightHudBindings hud)
        {
            var serializedView = new SerializedObject(view);
            SetObjectReference(serializedView, "state", state);
            SetObjectReference(serializedView, "topDownCamera", camera);
            SetObjectReference(serializedView, "runnerRoot", runnerRoot);
            SetObjectReference(
                serializedView,
                "arenaPresentation",
                arenaPresentation);
            SetObjectReference(
                serializedView,
                "observerHead",
                presentation.ObserverHead);
            SetObjectReference(
                serializedView,
                "greenSignalRenderer",
                presentation.GreenSignalRenderer);
            SetObjectReference(
                serializedView,
                "redSignalRenderer",
                presentation.RedSignalRenderer);
            SetObjectReference(
                serializedView,
                "greenSignalLight",
                presentation.GreenSignalLight);
            SetObjectReference(
                serializedView,
                "redSignalLight",
                presentation.RedSignalLight);
            SetObjectReference(
                serializedView,
                "cueAudioSource",
                cueAudioSource);
            SetObjectReference(serializedView, "hud", hud);
            serializedView.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(view);
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
                ? prefab.GetComponent<RedLightGreenLightHudBindings>()
                : null;
            if (bindings == null || !bindings.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "RedLightGreenLightHud.prefab is missing its serialized " +
                    "UI binding contract. Repair the prefab instead of " +
                    "allowing runtime UI generation.");
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
                "RedLightGreenLightHud",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(RedLightGreenLightHudBindings));
            root.transform.localScale = Vector3.one;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 45;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = new GameObject(
                "HudPanel",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            panel.transform.SetParent(root.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 1f);
            panelRect.anchorMax = new Vector2(0.5f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.anchoredPosition = new Vector2(0f, -22f);
            panelRect.sizeDelta = new Vector2(1120f, 335f);
            panel.GetComponent<Image>().color =
                new Color(0.025f, 0.035f, 0.055f, 0.88f);

            var phase = CreateHudText(
                "Phase",
                panel.transform,
                font,
                new Vector2(0f, -12f),
                new Vector2(1060f, 42f),
                28,
                TextAnchor.MiddleCenter,
                FontStyle.Bold,
                "RED LIGHT, GREEN LIGHT  ·  ROUND 1 / 3");
            var signal = CreateHudText(
                "Signal",
                panel.transform,
                font,
                new Vector2(0f, -52f),
                new Vector2(1060f, 72f),
                48,
                TextAnchor.MiddleCenter,
                FontStyle.Bold,
                "GREEN LIGHT  ·  MOVE");
            var instructions = CreateHudText(
                "Instructions",
                panel.transform,
                font,
                new Vector2(0f, -124f),
                new Vector2(1060f, 36f),
                18,
                TextAnchor.MiddleCenter,
                FontStyle.Normal,
                "WASD MOVE  ·  FREEZE ON RED  ·  FIRST CATCH: SLOWED  ·  SECOND: OUT");

            var rows = new Text[
                RedLightGreenLightRules.PlayerCount];
            for (var slot = 0; slot < rows.Length; slot++)
            {
                rows[slot] = CreateHudText(
                    "Player" + (slot + 1) + "Row",
                    panel.transform,
                    font,
                    new Vector2(-405f + slot * 270f, -174f),
                    new Vector2(255f, 145f),
                    18,
                    TextAnchor.UpperCenter,
                    FontStyle.Bold,
                    "PLAYER " + (slot + 1));
                rows[slot].color = new[]
                {
                    new Color(0.16f, 0.48f, 0.95f),
                    new Color(0.92f, 0.2f, 0.16f),
                    new Color(0.18f, 0.78f, 0.32f),
                    new Color(0.7f, 0.26f, 0.9f)
                }[slot];
            }

            root.GetComponent<RedLightGreenLightHudBindings>().Configure(
                canvas,
                phase,
                signal,
                instructions,
                rows,
                Color.white,
                new Color(0.22f, 1f, 0.35f, 1f),
                new Color(1f, 0.74f, 0.12f, 1f),
                new Color(1f, 0.16f, 0.1f, 1f));
            return root;
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

        private static void SetObjectReference(
            SerializedObject target,
            string propertyName,
            UnityEngine.Object value)
        {
            var property = target.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    "RedLightGreenLightNetworkView is missing serialized " +
                    "property '" + propertyName + "'.");
            }
            property.objectReferenceValue = value;
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

        private static RedLightGreenLightMaterials CreateMaterials()
        {
            return new RedLightGreenLightMaterials
            {
                Floor = CreateOrUpdateMaterial(
                    "RedLightGreenLightFloor",
                    new Color(0.12f, 0.18f, 0.15f)),
                Guide = CreateOrUpdateMaterial(
                    "RedLightGreenLightGuide",
                    new Color(0.3f, 0.4f, 0.32f)),
                Wall = CreateOrUpdateMaterial(
                    "RedLightGreenLightWall",
                    new Color(0.16f, 0.22f, 0.18f)),
                Start = CreateOrUpdateMaterial(
                    "RedLightGreenLightStart",
                    new Color(0.92f, 0.92f, 0.92f)),
                Finish = CreateOrUpdateMaterial(
                    "RedLightGreenLightFinish",
                    new Color(1f, 0.74f, 0.1f)),
                ObserverBody = CreateOrUpdateMaterial(
                    "RedLightGreenLightObserverBody",
                    new Color(0.78f, 0.17f, 0.2f)),
                ObserverHead = CreateOrUpdateMaterial(
                    "RedLightGreenLightObserverHead",
                    new Color(0.92f, 0.72f, 0.55f)),
                ObserverAccent = CreateOrUpdateMaterial(
                    "RedLightGreenLightObserverAccent",
                    new Color(0.1f, 0.07f, 0.09f)),
                SignalHousing = CreateOrUpdateMaterial(
                    "RedLightGreenLightSignalHousing",
                    new Color(0.08f, 0.09f, 0.1f)),
                GreenSignal = CreateOrUpdateMaterial(
                    "RedLightGreenLightGreenSignal",
                    new Color(0.12f, 1f, 0.25f),
                    new Color(0.04f, 0.65f, 0.12f)),
                RedSignal = CreateOrUpdateMaterial(
                    "RedLightGreenLightRedSignal",
                    new Color(1f, 0.08f, 0.04f),
                    new Color(0.7f, 0.025f, 0.01f))
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
                    "Universal Render Pipeline/Lit is required for Red Light " +
                    "/ Green Light materials.");
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
                material.SetFloat("_Smoothness", 0.18f);
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
            if (root.GetComponent<NetworkObject>() == null ||
                root.GetComponent<NetworkRedLightGreenLightState>() == null ||
                root.GetComponent<RedLightGreenLightNetworkView>() == null ||
                root.GetComponentInChildren<
                    RedLightGreenLightHudBindings>(true) == null ||
                FindDescendant(root.transform, "Arena Presentation") == null ||
                FindDescendant(root.transform, "Arena Floor") == null ||
                FindDescendant(root.transform, "Start Line") == null ||
                FindDescendant(root.transform, "Finish Line") == null ||
                FindDescendant(root.transform, "Observer Placeholder") == null ||
                FindDescendant(root.transform, "Observer Head") == null ||
                FindDescendant(root.transform, "Green Signal") == null ||
                FindDescendant(root.transform, "Red Signal") == null ||
                root.GetComponentInChildren<CinemachineCamera>(true) == null ||
                root.GetComponentInChildren<AudioSource>(true) == null)
            {
                throw new InvalidOperationException(
                    "Generated Red Light / Green Light scene is missing its " +
                    "network, arena, signal, observer, camera or audio contract.");
            }

            var hud = root.GetComponentInChildren<
                RedLightGreenLightHudBindings>(true);
            if (!hud.HasRequiredReferences ||
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    hud.gameObject) != HudPrefabPath)
            {
                throw new InvalidOperationException(
                    "Red Light / Green Light HUD must remain a configured " +
                    "RedLightGreenLightHud.prefab instance.");
            }

            if (root.GetComponentInChildren<Camera>(true) != null ||
                root.GetComponentInChildren<AudioListener>(true) != null)
            {
                throw new InvalidOperationException(
                    "Red Light / Green Light must not contain a Unity Camera " +
                    "or AudioListener; the additive Board scene owns output.");
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
                var result = FindDescendant(
                    root.GetChild(index),
                    childName);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        private static void EnsureRedLightGreenLightInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            for (var index = scenes.Count - 1; index >= 0; index--)
            {
                if (scenes[index].path == RedLightGreenLightScenePath)
                {
                    scenes.RemoveAt(index);
                }
            }

            var insertAfter = FindSceneIndex(scenes, WrongWayScenePath);
            if (insertAfter < 0)
            {
                insertAfter = FindSceneIndex(scenes, MinefieldScenePath);
            }
            if (insertAfter < 0)
            {
                insertAfter = FindSceneIndex(scenes, BoardScenePath);
            }

            var targetIndex = insertAfter >= 0
                ? insertAfter + 1
                : scenes.Count;
            scenes.Insert(
                targetIndex,
                new EditorBuildSettingsScene(
                    RedLightGreenLightScenePath,
                    true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static int FindSceneIndex(
            IReadOnlyList<EditorBuildSettingsScene> scenes,
            string path)
        {
            for (var index = 0; index < scenes.Count; index++)
            {
                if (scenes[index].path == path)
                {
                    return index;
                }
            }
            return -1;
        }

        private static void EnsureFolders()
        {
            EnsureFolder(ProjectRoot);
            EnsureFolder(ScenesFolder);
            EnsureFolder(UiFolder);
            EnsureFolder(UiPrefabFolder);
            EnsureFolder(ArtFolder);
            EnsureFolder(MinigameArtFolder);
            EnsureFolder(RedLightGreenLightArtFolder);
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
            AssetDatabase.CreateFolder(
                parent,
                path.Substring(separator + 1));
        }

        private sealed class PresentationReferences
        {
            public Transform ObserverHead;
            public Renderer GreenSignalRenderer;
            public Renderer RedSignalRenderer;
            public Light GreenSignalLight;
            public Light RedSignalLight;
        }

        private sealed class RedLightGreenLightMaterials
        {
            public Material Floor;
            public Material Guide;
            public Material Wall;
            public Material Start;
            public Material Finish;
            public Material ObserverBody;
            public Material ObserverHead;
            public Material ObserverAccent;
            public Material SignalHousing;
            public Material GreenSignal;
            public Material RedSignal;
        }
    }
}
