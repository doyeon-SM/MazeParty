using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.BalloonBlow;
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
    /// Builds the additive Balloon Blow arena and its replaceable prototype
    /// presentation. Existing UI prefabs are validated and reused unchanged.
    /// Board continues to own the output Camera and AudioListener.
    /// </summary>
    public static class BalloonBlowProjectSetup
    {
        private const string MenuPath =
            "MazeParty/Minigames/Rebuild Balloon Blow";
        private const string ProjectRoot = "Assets/MazeParty";
        private const string ScenesFolder = ProjectRoot + "/Scenes";
        private const string UiPrefabFolder = ProjectRoot + "/UI/Prefabs";
        private const string MaterialFolder =
            ProjectRoot + "/Art/Minigames/BalloonBlow/Materials";
        private const string StableFootingScenePath =
            ScenesFolder + "/StableFooting.unity";

        public const string BalloonBlowScenePath =
            ScenesFolder + "/BalloonBlow.unity";
        public const string HudPrefabPath =
            UiPrefabFolder + "/BalloonBlowHud.prefab";
        public const string StationLabelPrefabPath =
            UiPrefabFolder + "/BalloonBlowStationLabel.prefab";

        private static readonly Vector3[] PlayerPositions =
        {
            new Vector3(-5.25f, BalloonBlowNetworkView.PlayerPresentationHeight, -2.2f),
            new Vector3(-1.75f, BalloonBlowNetworkView.PlayerPresentationHeight, -2.2f),
            new Vector3(1.75f, BalloonBlowNetworkView.PlayerPresentationHeight, -2.2f),
            new Vector3(5.25f, BalloonBlowNetworkView.PlayerPresentationHeight, -2.2f)
        };

        private static readonly Color[] PlayerColors =
        {
            new Color(0.16f, 0.48f, 0.95f),
            new Color(0.92f, 0.2f, 0.16f),
            new Color(0.18f, 0.78f, 0.32f),
            new Color(0.7f, 0.26f, 0.9f)
        };

        [MenuItem(MenuPath)]
        public static void RebuildBalloonBlow()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log(
                    "Balloon Blow rebuild canceled; open scene changes " +
                    "were left untouched.");
                return;
            }

            BuildBalloonBlowAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var scene = SceneManager.GetSceneByPath(BalloonBlowScenePath);
            if (scene.IsValid() && scene.isLoaded)
            {
                SceneManager.SetActiveScene(scene);
            }
            else
            {
                EditorSceneManager.OpenScene(
                    BalloonBlowScenePath,
                    OpenSceneMode.Single);
            }

            Debug.Log(
                "Balloon Blow rebuilt: four fixed stations, shared camera, " +
                "replaceable balloons and arena art, prefab HUD and prefab " +
                "world-space station labels.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRebuildBalloonBlow()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static void BuildBalloonBlowAssets()
        {
            EnsureFolders();
            var materials = CreateMaterials();
            var hudPrefab = LoadOrCreateHudPrefab();
            var labelPrefab = LoadOrCreateStationLabelPrefab(materials);
            BuildBalloonBlowScene(materials, hudPrefab, labelPrefab);
            AssetDatabase.SaveAssets();
        }

        private static void BuildBalloonBlowScene(
            BalloonBlowMaterials materials,
            GameObject hudPrefab,
            GameObject stationLabelPrefab)
        {
            var previousActive = SceneManager.GetActiveScene();
            var previousActivePath = previousActive.path;
            var loadedScene = SceneManager.GetSceneByPath(
                BalloonBlowScenePath);
            var replaceSingleOpenScene =
                SceneManager.sceneCount == 1 &&
                ((loadedScene.IsValid() && loadedScene.isLoaded) ||
                 string.IsNullOrEmpty(previousActivePath));

            if (loadedScene.IsValid() && loadedScene.isLoaded)
            {
                if (loadedScene.isDirty)
                {
                    throw new InvalidOperationException(
                        "BalloonBlow.unity has unsaved changes. Save or " +
                        "discard them before rebuilding the generated scene.");
                }

                if (!replaceSingleOpenScene)
                {
                    EditorSceneManager.CloseScene(loadedScene, true);
                }
            }

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                replaceSingleOpenScene
                    ? NewSceneMode.Single
                    : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            var root = new GameObject("Balloon Blow Network State");
            var arenaPresentation = new GameObject("Arena Presentation");
            arenaPresentation.transform.SetParent(root.transform, false);

            var arena = CreateArena(
                arenaPresentation.transform,
                materials,
                stationLabelPrefab);
            CreateLighting(arenaPresentation.transform);
            var sharedCamera = CreateSharedCamera(root.transform);

            var runtimePlayers = new GameObject("Runtime Players");
            runtimePlayers.transform.SetParent(root.transform, false);

            var audioAnchor = new GameObject("Audio Replacement Anchor");
            audioAnchor.transform.SetParent(root.transform, false);
            audioAnchor.transform.position = new Vector3(0f, 2f, 2.8f);
            var cueAudioSource = audioAnchor.AddComponent<AudioSource>();
            cueAudioSource.playOnAwake = false;
            cueAudioSource.loop = false;
            cueAudioSource.spatialBlend = 0f;

            CreateArtReplacementAnchors(root.transform);

            // Saving before adding NetworkObject gives the in-scene object a
            // stable GlobalObjectIdHash for Netcode scene replication.
            EditorSceneManager.SaveScene(scene, BalloonBlowScenePath);

            root.AddComponent<NetworkObject>();
            var state = root.AddComponent<NetworkBalloonBlowState>();
            var view = root.AddComponent<BalloonBlowNetworkView>();

            var hudObject = PrefabUtility.InstantiatePrefab(
                hudPrefab,
                root.transform) as GameObject;
            var hud = hudObject != null
                ? hudObject.GetComponent<BalloonBlowHudBindings>()
                : null;
            if (hud == null || !hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "BalloonBlowHud.prefab could not be instantiated with " +
                    "its required serialized bindings.");
            }

            view.Configure(
                state,
                sharedCamera,
                runtimePlayers.transform,
                arena.PlayerAnchors,
                arena.BalloonAnchors,
                arena.StationLabels,
                arenaPresentation,
                cueAudioSource,
                hud);

            ValidateSceneContract(root);
            EditorSceneManager.SaveScene(scene, BalloonBlowScenePath);
            EnsureBalloonBlowInBuildSettings();

            if (previousActivePath == BalloonBlowScenePath)
            {
                SceneManager.SetActiveScene(scene);
            }
            else if (previousActive.IsValid() &&
                     previousActive.isLoaded &&
                     previousActive.path != BalloonBlowScenePath)
            {
                SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(scene, true);
            }

        }

        private static ArenaReferences CreateArena(
            Transform parent,
            BalloonBlowMaterials materials,
            GameObject stationLabelPrefab)
        {
            CreatePrimitive(
                "Stage Floor",
                PrimitiveType.Cube,
                parent,
                new Vector3(0f, -0.45f, 0f),
                Quaternion.identity,
                new Vector3(15.5f, 0.8f, 9f),
                materials.Stage,
                true);
            CreatePrimitive(
                "Backdrop",
                PrimitiveType.Cube,
                parent,
                new Vector3(0f, 2.6f, 4.25f),
                Quaternion.identity,
                new Vector3(15.5f, 6f, 0.35f),
                materials.Backdrop,
                false);
            CreatePrimitive(
                "Front Trim",
                PrimitiveType.Cube,
                parent,
                new Vector3(0f, 0.05f, -4.1f),
                Quaternion.identity,
                new Vector3(15.5f, 0.45f, 0.35f),
                materials.Trim,
                false);

            var playerRoot = new GameObject("Player Anchors").transform;
            playerRoot.SetParent(parent, false);
            var balloonRoot = new GameObject("Balloon Anchors").transform;
            balloonRoot.SetParent(parent, false);
            var labelRoot = new GameObject("Station Label Anchors").transform;
            labelRoot.SetParent(parent, false);

            var playerAnchors =
                new Transform[BalloonBlowRules.PlayerCount];
            var balloonAnchors =
                new Transform[BalloonBlowRules.PlayerCount];
            var stationLabels =
                new BalloonBlowStationLabel[BalloonBlowRules.PlayerCount];
            for (var slot = 0;
                 slot < BalloonBlowRules.PlayerCount;
                 slot++)
            {
                var playerAnchor = new GameObject(
                    "Player Anchor " + (slot + 1)).transform;
                playerAnchor.SetParent(playerRoot, false);
                playerAnchor.position = PlayerPositions[slot];
                playerAnchor.rotation = Quaternion.identity;
                playerAnchors[slot] = playerAnchor;

                CreatePrimitive(
                    "Station " + (slot + 1),
                    PrimitiveType.Cylinder,
                    parent,
                    new Vector3(PlayerPositions[slot].x, -0.02f, -1.8f),
                    Quaternion.identity,
                    new Vector3(1.45f, 0.16f, 1.45f),
                    materials.Station[slot],
                    false);

                var balloonAnchor = new GameObject(
                    "Balloon Anchor " + (slot + 1)).transform;
                balloonAnchor.SetParent(balloonRoot, false);
                balloonAnchor.position = new Vector3(
                    PlayerPositions[slot].x,
                    1.85f,
                    0.35f);
                balloonAnchors[slot] = balloonAnchor;

                CreatePrimitive(
                    "Balloon Body",
                    PrimitiveType.Sphere,
                    balloonAnchor,
                    Vector3.zero,
                    Quaternion.identity,
                    new Vector3(1f, 1.14f, 0.94f),
                    materials.Balloon[slot],
                    false);
                CreatePrimitive(
                    "Balloon Knot",
                    PrimitiveType.Cube,
                    balloonAnchor,
                    new Vector3(0f, -0.58f, 0f),
                    Quaternion.Euler(0f, 0f, 45f),
                    new Vector3(0.18f, 0.18f, 0.14f),
                    materials.Balloon[slot],
                    false);

                var labelObject = PrefabUtility.InstantiatePrefab(
                    stationLabelPrefab,
                    labelRoot) as GameObject;
                if (labelObject == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate BalloonBlowStationLabel.prefab.");
                }
                labelObject.name = "Station Label " + (slot + 1);
                labelObject.transform.position = new Vector3(
                    PlayerPositions[slot].x,
                    4.25f,
                    PlayerPositions[slot].z);
                labelObject.transform.rotation =
                    BalloonBlowNetworkView.SharedCameraRotation;
                stationLabels[slot] = labelObject.GetComponent<
                    BalloonBlowStationLabel>();
                if (stationLabels[slot] == null ||
                    !stationLabels[slot].HasRequiredReferences)
                {
                    throw new InvalidOperationException(
                        "Balloon Blow station-label prefab bindings are invalid.");
                }
            }

            CreatePrimitive(
                "Rules Plaque",
                PrimitiveType.Cube,
                parent,
                new Vector3(0f, 2.35f, 4.02f),
                Quaternion.identity,
                new Vector3(8.6f, 2.8f, 0.18f),
                materials.Plaque,
                false);

            return new ArenaReferences
            {
                PlayerAnchors = playerAnchors,
                BalloonAnchors = balloonAnchors,
                StationLabels = stationLabels
            };
        }

        private static void CreateLighting(Transform parent)
        {
            var lightObject = new GameObject("Balloon Blow Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(48f, -25f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.color = new Color(1f, 0.94f, 0.88f);
        }

        private static CinemachineCamera CreateSharedCamera(Transform parent)
        {
            var cameraObject = new GameObject("CM_BalloonBlowShared");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.SetPositionAndRotation(
                BalloonBlowNetworkView.SharedCameraPosition,
                BalloonBlowNetworkView.SharedCameraRotation);

            var camera = cameraObject.AddComponent<CinemachineCamera>();
            camera.Priority = 0;
            var lens = camera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize =
                BalloonBlowNetworkView.SharedCameraOrthographicSize;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 100f;
            camera.Lens = lens;
            return camera;
        }

        private static void CreateArtReplacementAnchors(Transform parent)
        {
            var root = new GameObject("Art Replacement Anchors").transform;
            root.SetParent(parent, false);
            new GameObject("Arena Art Anchor").transform.SetParent(root, false);
            new GameObject("Balloon Art Anchor").transform.SetParent(root, false);
            new GameObject("VFX Anchor").transform.SetParent(root, false);
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

            var bindings = prefab != null
                ? prefab.GetComponent<BalloonBlowHudBindings>()
                : null;
            if (bindings == null || !bindings.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "BalloonBlowHud.prefab is missing its serialized binding " +
                    "contract. Repair the prefab instead of allowing setup or " +
                    "runtime code to replace designer changes.");
            }
            return prefab;
        }

        private static GameObject LoadOrCreateStationLabelPrefab(
            BalloonBlowMaterials materials)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                StationLabelPrefabPath);
            if (prefab == null)
            {
                var template = CreateStationLabelTemplate(materials);
                try
                {
                    prefab = PrefabUtility.SaveAsPrefabAsset(
                        template,
                        StationLabelPrefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(template);
                }
            }

            var bindings = prefab != null
                ? prefab.GetComponent<BalloonBlowStationLabel>()
                : null;
            if (bindings == null || !bindings.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "BalloonBlowStationLabel.prefab is missing its serialized " +
                    "binding contract. Repair the prefab without rebuilding it.");
            }
            return prefab;
        }

        private static GameObject CreateHudTemplate()
        {
            var font = RequireBuiltinFont();
            var root = new GameObject(
                "BalloonBlowHud",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(BalloonBlowHudBindings));
            root.transform.localScale = Vector3.one;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 45;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var headerPanel = CreatePanel(
                "Header Panel",
                root.transform,
                new Vector2(0.5f, 1f),
                new Vector2(0f, -20f),
                new Vector2(760f, 164f),
                new Color(0.035f, 0.025f, 0.07f, 0.9f));
            var phase = CreateHudText(
                "Phase",
                headerPanel.transform,
                font,
                new Vector2(0f, -12f),
                new Vector2(710f, 34f),
                22,
                FontStyle.Bold,
                "BALLOON BLOW · GET READY");
            var timer = CreateHudText(
                "Timer",
                headerPanel.transform,
                font,
                new Vector2(0f, -45f),
                new Vector2(220f, 48f),
                36,
                FontStyle.Bold,
                "00:30");
            var round = CreateHudText(
                "Round",
                headerPanel.transform,
                font,
                new Vector2(250f, -52f),
                new Vector2(200f, 34f),
                18,
                FontStyle.Bold,
                "ROUND 1 / 3");
            var instructions = CreateHudText(
                "Instructions",
                headerPanel.transform,
                font,
                new Vector2(0f, -103f),
                new Vector2(710f, 42f),
                16,
                FontStyle.Normal,
                "HOLD LEFT CLICK TO INFLATE · RELEASE BEFORE 2.0 SECONDS");

            var rows = new Text[BalloonBlowRules.PlayerCount];
            var fills = new Image[BalloonBlowRules.PlayerCount];
            for (var slot = 0; slot < BalloonBlowRules.PlayerCount; slot++)
            {
                var x = -570f + slot * 380f;
                var card = CreatePanel(
                    "Player " + (slot + 1) + " Card",
                    root.transform,
                    new Vector2(0.5f, 0f),
                    new Vector2(x, 22f),
                    new Vector2(350f, 104f),
                    new Color(0.025f, 0.032f, 0.052f, 0.9f));
                rows[slot] = CreateHudText(
                    "Player " + (slot + 1) + " Row",
                    card.transform,
                    font,
                    new Vector2(0f, -12f),
                    new Vector2(320f, 34f),
                    15,
                    FontStyle.Bold,
                    "PLAYER " + (slot + 1) + " · 0% · READY");
                rows[slot].color = PlayerColors[slot];

                var barBack = CreatePanel(
                    "Progress Back",
                    card.transform,
                    new Vector2(0.5f, 1f),
                    new Vector2(0f, -60f),
                    new Vector2(310f, 20f),
                    new Color(0.09f, 0.105f, 0.15f, 1f));
                var fillObject = CreatePanel(
                    "Progress Fill",
                    barBack.transform,
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(298f, 12f),
                    PlayerColors[slot]);
                fills[slot] = fillObject.GetComponent<Image>();
                fills[slot].type = Image.Type.Filled;
                fills[slot].fillMethod = Image.FillMethod.Horizontal;
                fills[slot].fillOrigin = 0;
                fills[slot].fillAmount = 0f;
            }

            var controlsPanel = CreatePanel(
                "Controls Panel",
                root.transform,
                new Vector2(0f, 1f),
                new Vector2(22f, -22f),
                new Vector2(330f, 88f),
                new Color(0.025f, 0.032f, 0.052f, 0.86f));
            CreateHudText(
                "Controls",
                controlsPanel.transform,
                font,
                new Vector2(0f, -12f),
                new Vector2(300f, 56f),
                17,
                FontStyle.Bold,
                "LEFT CLICK · HOLD TO INFLATE\nRELEASE · REST");

            var pausePanel = CreatePanel(
                "Pause Panel",
                root.transform,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(720f, 180f),
                new Color(0.02f, 0.02f, 0.04f, 0.96f));
            CreateHudText(
                "Pause Message",
                pausePanel.transform,
                font,
                new Vector2(0f, -24f),
                new Vector2(680f, 130f),
                28,
                FontStyle.Bold,
                "PLAYER DISCONNECTED\nMATCH PAUSED");
            pausePanel.SetActive(false);

            var resultPanel = CreatePanel(
                "Result Panel",
                root.transform,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(720f, 220f),
                new Color(0.02f, 0.02f, 0.04f, 0.96f));
            var resultText = CreateHudText(
                "Result Message",
                resultPanel.transform,
                font,
                new Vector2(0f, -28f),
                new Vector2(680f, 170f),
                30,
                FontStyle.Bold,
                "ROUND RESULTS");
            resultPanel.SetActive(false);

            root.GetComponent<BalloonBlowHudBindings>().Configure(
                canvas,
                phase,
                timer,
                round,
                instructions,
                rows,
                fills,
                resultText,
                pausePanel,
                controlsPanel,
                resultPanel);
            return root;
        }

        private static GameObject CreateStationLabelTemplate(
            BalloonBlowMaterials materials)
        {
            var font = RequireBuiltinFont();
            var root = new GameObject(
                "BalloonBlowStationLabel",
                typeof(BalloonBlowStationLabel));

            var highlight = CreatePrimitive(
                "Local Highlight",
                PrimitiveType.Cube,
                root.transform,
                new Vector3(0f, 0f, 0.04f),
                Quaternion.identity,
                new Vector3(2.85f, 1.18f, 0.035f),
                materials.Highlight,
                false);
            var backing = CreatePrimitive(
                "Label Backing",
                PrimitiveType.Cube,
                root.transform,
                Vector3.zero,
                Quaternion.identity,
                new Vector3(2.65f, 0.98f, 0.06f),
                materials.LabelBack,
                false);
            highlight.SetActive(false);

            var nameText = CreateWorldText(
                "Player Name",
                root.transform,
                font,
                new Vector3(0f, 0.25f, -0.055f),
                64,
                0.05f,
                "PLAYER 1");
            var progressText = CreateWorldText(
                "Progress Text",
                root.transform,
                font,
                new Vector3(0f, -0.03f, -0.055f),
                56,
                0.04f,
                "0%");

            CreatePrimitive(
                "Progress Back",
                PrimitiveType.Cube,
                root.transform,
                new Vector3(0f, -0.34f, -0.07f),
                Quaternion.identity,
                new Vector3(2.18f, 0.14f, 0.035f),
                materials.ProgressBack,
                false);
            var fill = CreatePrimitive(
                "Progress Fill",
                PrimitiveType.Cube,
                root.transform,
                new Vector3(0f, -0.34f, -0.095f),
                Quaternion.identity,
                new Vector3(2.08f, 0.085f, 0.035f),
                materials.ProgressFill,
                false);

            root.GetComponent<BalloonBlowStationLabel>().Configure(
                nameText,
                progressText,
                fill.transform,
                fill.GetComponent<Renderer>(),
                highlight.GetComponent<Renderer>());
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
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = anchoredPosition;
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
            Vector2 anchoredPosition,
            Vector2 size,
            int fontSize,
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
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            var text = textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = new Color(0.94f, 0.97f, 1f);
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = sampleText;
            return text;
        }

        private static TextMesh CreateWorldText(
            string name,
            Transform parent,
            Font font,
            Vector3 localPosition,
            int fontSize,
            float characterSize,
            string sampleText)
        {
            var textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);
            textObject.transform.localPosition = localPosition;
            var text = textObject.AddComponent<TextMesh>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.characterSize = characterSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            text.text = sampleText;
            var renderer = text.GetComponent<MeshRenderer>();
            if (renderer != null && font.material != null)
            {
                renderer.sharedMaterial = font.material;
            }
            return text;
        }

        private static Font RequireBuiltinFont()
        {
            var font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            if (font == null)
            {
                throw new InvalidOperationException(
                    "Unity built-in LegacyRuntime.ttf font could not be loaded.");
            }
            return font;
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

        private static BalloonBlowMaterials CreateMaterials()
        {
            var balloon = new Material[BalloonBlowRules.PlayerCount];
            var stations = new Material[BalloonBlowRules.PlayerCount];
            for (var slot = 0; slot < BalloonBlowRules.PlayerCount; slot++)
            {
                balloon[slot] = CreateOrLoadMaterial(
                    "BalloonBlowBalloon" + (slot + 1),
                    PlayerColors[slot],
                    PlayerColors[slot] * 0.18f);
                stations[slot] = CreateOrLoadMaterial(
                    "BalloonBlowStation" + (slot + 1),
                    Color.Lerp(PlayerColors[slot], Color.black, 0.35f));
            }

            return new BalloonBlowMaterials
            {
                Stage = CreateOrLoadMaterial(
                    "BalloonBlowStage",
                    new Color(0.11f, 0.13f, 0.2f)),
                Backdrop = CreateOrLoadMaterial(
                    "BalloonBlowBackdrop",
                    new Color(0.055f, 0.035f, 0.12f)),
                Trim = CreateOrLoadMaterial(
                    "BalloonBlowTrim",
                    new Color(0.95f, 0.48f, 0.14f),
                    new Color(0.22f, 0.055f, 0.01f)),
                Plaque = CreateOrLoadMaterial(
                    "BalloonBlowPlaque",
                    new Color(0.16f, 0.08f, 0.23f)),
                LabelBack = CreateOrLoadMaterial(
                    "BalloonBlowLabelBack",
                    new Color(0.025f, 0.03f, 0.055f)),
                ProgressBack = CreateOrLoadMaterial(
                    "BalloonBlowProgressBack",
                    new Color(0.09f, 0.1f, 0.14f)),
                ProgressFill = CreateOrLoadMaterial(
                    "BalloonBlowProgressFill",
                    Color.white),
                Highlight = CreateOrLoadMaterial(
                    "BalloonBlowLocalHighlight",
                    new Color(1f, 0.82f, 0.14f),
                    new Color(0.48f, 0.24f, 0.01f)),
                Balloon = balloon,
                Station = stations
            };
        }

        private static Material CreateOrLoadMaterial(
            string name,
            Color color,
            Color? emission = null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Universal Render Pipeline/Lit is required for Balloon " +
                    "Blow prototype materials.");
            }

            var path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            material = new Material(shader) { name = name };
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
                material.SetFloat("_Smoothness", 0.35f);
            }
            if (emission.HasValue && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void ValidateSceneContract(GameObject root)
        {
            var playerAnchors = FindDescendant(root.transform, "Player Anchors");
            var balloonAnchors = FindDescendant(root.transform, "Balloon Anchors");
            var labelAnchors = FindDescendant(
                root.transform,
                "Station Label Anchors");
            var hud = root.GetComponentInChildren<BalloonBlowHudBindings>(true);
            var labels = root.GetComponentsInChildren<
                BalloonBlowStationLabel>(true);

            if (root.GetComponent<NetworkObject>() == null ||
                root.GetComponent<NetworkBalloonBlowState>() == null ||
                root.GetComponent<BalloonBlowNetworkView>() == null ||
                playerAnchors == null ||
                playerAnchors.childCount != BalloonBlowRules.PlayerCount ||
                balloonAnchors == null ||
                balloonAnchors.childCount != BalloonBlowRules.PlayerCount ||
                labelAnchors == null ||
                labelAnchors.childCount != BalloonBlowRules.PlayerCount ||
                labels.Length != BalloonBlowRules.PlayerCount ||
                root.GetComponentInChildren<CinemachineCamera>(true) == null ||
                root.GetComponentInChildren<AudioSource>(true) == null ||
                FindDescendant(root.transform, "Art Replacement Anchors") == null ||
                hud == null)
            {
                throw new InvalidOperationException(
                    "Generated Balloon Blow scene is missing its network, " +
                    "station, balloon, label, camera, audio, art or HUD contract.");
            }

            if (!hud.HasRequiredReferences ||
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    hud.gameObject) != HudPrefabPath)
            {
                throw new InvalidOperationException(
                    "Balloon Blow HUD must remain a configured prefab instance.");
            }
            for (var index = 0; index < labels.Length; index++)
            {
                if (!labels[index].HasRequiredReferences ||
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        labels[index].gameObject) != StationLabelPrefabPath)
                {
                    throw new InvalidOperationException(
                        "Every Balloon Blow station label must remain a " +
                        "configured prefab instance.");
                }
            }

            if (root.GetComponentInChildren<Camera>(true) != null ||
                root.GetComponentInChildren<AudioListener>(true) != null)
            {
                throw new InvalidOperationException(
                    "Balloon Blow additive scene must reuse Board's output " +
                    "Camera and AudioListener.");
            }
        }

        private static Transform FindDescendant(
            Transform root,
            string childName)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == childName)
            {
                return root;
            }
            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindDescendant(root.GetChild(index), childName);
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
            EnsureFolder(UiPrefabFolder);
            EnsureFolder(ProjectRoot + "/Art");
            EnsureFolder(ProjectRoot + "/Art/Minigames");
            EnsureFolder(ProjectRoot + "/Art/Minigames/BalloonBlow");
            EnsureFolder(MaterialFolder);
        }

        private static void EnsureBalloonBlowInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            for (var index = scenes.Count - 1; index >= 0; index--)
            {
                if (scenes[index].path == BalloonBlowScenePath)
                {
                    scenes.RemoveAt(index);
                }
            }

            var insertAfter = -1;
            for (var index = 0; index < scenes.Count; index++)
            {
                if (scenes[index].path == StableFootingScenePath)
                {
                    insertAfter = index;
                    break;
                }
            }
            scenes.Insert(
                insertAfter >= 0 ? insertAfter + 1 : scenes.Count,
                new EditorBuildSettingsScene(BalloonBlowScenePath, true));
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
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }

        private sealed class ArenaReferences
        {
            public Transform[] PlayerAnchors;
            public Transform[] BalloonAnchors;
            public BalloonBlowStationLabel[] StationLabels;
        }

        private sealed class BalloonBlowMaterials
        {
            public Material Stage;
            public Material Backdrop;
            public Material Trim;
            public Material Plaque;
            public Material LabelBack;
            public Material ProgressBack;
            public Material ProgressFill;
            public Material Highlight;
            public Material[] Balloon;
            public Material[] Station;
        }
    }
}
