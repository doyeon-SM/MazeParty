using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.SequenceMemory;
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
    /// Builds the additive Sequence Memory stage and its prefab-authored HUD.
    /// Existing HUD prefabs and material assets are validated and reused
    /// without replacing designer-authored changes.
    /// </summary>
    public static class SequenceMemoryProjectSetup
    {
        private const string MenuPath =
            "MazeParty/Minigames/Rebuild Sequence Memory";
        private const string ProjectRoot = "Assets/MazeParty";
        private const string ScenesFolder = "Assets/MazeParty/Scenes/Minigames/SequenceMemory";
        private const string MinigameScenesFolder =
            "Assets/MazeParty/Scenes/Minigames/SequenceMemory";
        private const string UiPrefabFolder = "Assets/MazeParty/Prefabs/Minigames/SequenceMemory/UI";
        private const string CorePrefabFolder =
            ProjectRoot + "/Prefabs/Minigames/SequenceMemory";
        private const string MaterialFolder =
            ProjectRoot + "/Art/Minigames/SequenceMemory/Materials";
        private const string RaceScenePath = "Assets/MazeParty/Scenes/Minigames/Race/Race.unity";

        public const string ScenePath =
            "Assets/MazeParty/Scenes/Minigames/SequenceMemory/SequenceMemory.unity";
        public const string HudPrefabPath =
            "Assets/MazeParty/Prefabs/Minigames/SequenceMemory/UI/SequenceMemoryHud.prefab";
        public const float ArenaCenterX = 1140f;

        private static readonly Vector3 SharedCameraPosition =
            new Vector3(ArenaCenterX, 12f, -14f);
        private static readonly Quaternion SharedCameraRotation =
            Quaternion.Euler(32f, 0f, 0f);
        private const float SharedCameraOrthographicSize = 9.5f;

        private static readonly float[] PlayerOffsets =
        {
            -4.5f,
            -1.5f,
            1.5f,
            4.5f
        };

        private static readonly Color[] PlayerColors =
        {
            new Color(0.18f, 0.55f, 1f),
            new Color(1f, 0.28f, 0.2f),
            new Color(0.2f, 0.84f, 0.4f),
            new Color(0.76f, 0.32f, 1f)
        };

        [MenuItem(MenuPath)]
        public static void RebuildSequenceMemory()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log(
                    "Sequence Memory rebuild canceled; open scene changes " +
                    "were left untouched.");
                return;
            }

            BuildSequenceMemoryAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isLoaded)
            {
                SceneManager.SetActiveScene(scene);
            }
            else
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            Debug.Log(
                "Sequence Memory rebuilt: fixed NPC and four player " +
                "stations, shared camera, two tone sources and prefab HUD.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRebuildSequenceMemory()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static void BuildSequenceMemoryAssets()
        {
            EnsureFolders();
            var materials = CreateMaterials();
            var hudPrefab = LoadOrCreateHudPrefab();
            BuildScene(materials, hudPrefab);
            AssetDatabase.SaveAssets();
        }

        private static void BuildScene(
            SequenceMemoryMaterials materials,
            GameObject hudPrefab)
        {
            var previousActive = SceneManager.GetActiveScene();
            var previousPath = previousActive.path;
            var loaded = SceneManager.GetSceneByPath(ScenePath);
            var replaceSingleOpenScene =
                SceneManager.sceneCount == 1 &&
                ((loaded.IsValid() && loaded.isLoaded) ||
                 string.IsNullOrEmpty(previousPath));

            if (loaded.IsValid() && loaded.isLoaded)
            {
                if (loaded.isDirty)
                {
                    throw new InvalidOperationException(
                        "SequenceMemory.unity has unsaved changes. Save or " +
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

            var root = new GameObject("Sequence Memory Network State");
            var arenaPresentation = new GameObject("Arena Presentation");
            arenaPresentation.transform.SetParent(root.transform, false);
            var arena = CreateArena(
                arenaPresentation.transform,
                materials);
            CreateLighting(arenaPresentation.transform);
            var sharedCamera = CreateSharedCamera(root.transform);

            var playerRoot = new GameObject("Runtime Players");
            playerRoot.transform.SetParent(root.transform, false);
            CreateArtReplacementAnchors(root.transform);

            // NGO needs an asset-backed scene path before assigning a stable
            // GlobalObjectIdHash to the in-scene NetworkObject.
            EditorSceneManager.SaveScene(scene, ScenePath);

            root.AddComponent<NetworkObject>();
            var state = root.AddComponent<NetworkSequenceMemoryState>();
            var view = root.AddComponent<SequenceMemoryNetworkView>();
            var npcToneSource = root.AddComponent<AudioSource>();
            ConfigureToneSource(npcToneSource);
            var playerToneSource = root.AddComponent<AudioSource>();
            ConfigureToneSource(playerToneSource);

            var hudObject = PrefabUtility.InstantiatePrefab(
                hudPrefab,
                root.transform) as GameObject;
            var hud = hudObject != null
                ? hudObject.GetComponent<SequenceMemoryHudBindings>()
                : null;
            if (hud == null || !hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "SequenceMemoryHud.prefab could not be instantiated " +
                    "with its required serialized bindings.");
            }

            BindView(
                view,
                state,
                sharedCamera,
                playerRoot.transform,
                arena.PlayerAnchors,
                arena.NpcAnchor,
                arenaPresentation,
                npcToneSource,
                playerToneSource,
                hud);

            ValidateSceneContract(root);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureInBuildSettings();

            if (previousPath == ScenePath)
            {
                SceneManager.SetActiveScene(scene);
            }
            else if (previousActive.IsValid() &&
                     previousActive.isLoaded &&
                     previousActive.path != ScenePath)
            {
                SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static ArenaReferences CreateArena(
            Transform parent,
            SequenceMemoryMaterials materials)
        {
            CreatePrimitive(
                "Stage Floor",
                PrimitiveType.Cube,
                parent,
                new Vector3(ArenaCenterX, -0.4f, 0f),
                Quaternion.identity,
                new Vector3(15.5f, 0.8f, 10f),
                materials.Stage,
                true);
            CreatePrimitive(
                "Backdrop",
                PrimitiveType.Cube,
                parent,
                new Vector3(ArenaCenterX, 3.2f, 4.55f),
                Quaternion.identity,
                new Vector3(15.5f, 7.2f, 0.35f),
                materials.Backdrop,
                false);
            CreatePrimitive(
                "Stage Trim",
                PrimitiveType.Cube,
                parent,
                new Vector3(ArenaCenterX, 0.05f, -4.55f),
                Quaternion.identity,
                new Vector3(15.5f, 0.32f, 0.4f),
                materials.Trim,
                false);

            var playerAnchorRoot =
                new GameObject("Player Anchors").transform;
            playerAnchorRoot.SetParent(parent, false);
            var stationRoot =
                new GameObject("Station Placeholders").transform;
            stationRoot.SetParent(parent, false);
            var playerAnchors =
                new Transform[SequenceMemoryRules.PlayerCount];

            for (var slot = 0;
                 slot < SequenceMemoryRules.PlayerCount;
                 slot++)
            {
                var position = new Vector3(
                    ArenaCenterX + PlayerOffsets[slot],
                    0f,
                    -2f);
                var anchor = new GameObject(
                    "Player Anchor " + (slot + 1)).transform;
                anchor.SetParent(playerAnchorRoot, false);
                anchor.position = position;
                anchor.rotation = Quaternion.identity;
                playerAnchors[slot] = anchor;

                var station = CreatePrimitive(
                    "Station Placeholder " + (slot + 1),
                    PrimitiveType.Cylinder,
                    stationRoot,
                    new Vector3(position.x, -0.02f, position.z),
                    Quaternion.identity,
                    new Vector3(1.35f, 0.14f, 1.35f),
                    materials.Stations[slot],
                    false);
                MinigameCorePrefabUtility.Connect(
                    station,
                    CorePrefabFolder + "/Station" + (slot + 1) + ".prefab");
            }

            var npcAnchor = new GameObject("NPC Anchor").transform;
            npcAnchor.SetParent(parent, false);
            npcAnchor.position = new Vector3(ArenaCenterX, 0f, 2.7f);
            CreatePrimitive(
                "NPC Placeholder Body",
                PrimitiveType.Capsule,
                npcAnchor,
                new Vector3(0f, 1.05f, 0f),
                Quaternion.identity,
                new Vector3(1f, 1.15f, 1f),
                materials.Npc,
                false);
            CreatePrimitive(
                "NPC Placeholder Head",
                PrimitiveType.Sphere,
                npcAnchor,
                new Vector3(0f, 2.55f, 0f),
                Quaternion.identity,
                new Vector3(0.92f, 0.92f, 0.92f),
                materials.NpcAccent,
                false);
            CreatePrimitive(
                "NPC Podium",
                PrimitiveType.Cylinder,
                npcAnchor,
                new Vector3(0f, 0.15f, 0f),
                Quaternion.identity,
                new Vector3(1.65f, 0.3f, 1.65f),
                materials.Trim,
                false);
            npcAnchor = MinigameCorePrefabUtility.Connect(
                npcAnchor.gameObject,
                CorePrefabFolder + "/Npc.prefab").transform;

            return new ArenaReferences
            {
                PlayerAnchors = playerAnchors,
                NpcAnchor = npcAnchor
            };
        }

        private static void CreateLighting(Transform parent)
        {
            var lightObject =
                new GameObject("Sequence Memory Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation =
                Quaternion.Euler(48f, -28f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.95f, 0.88f);
        }

        private static CinemachineCamera CreateSharedCamera(Transform parent)
        {
            var cameraObject = new GameObject("CM_SequenceMemoryShared");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.SetPositionAndRotation(
                SharedCameraPosition,
                SharedCameraRotation);

            var camera = cameraObject.AddComponent<CinemachineCamera>();
            camera.Priority = 0;
            var lens = camera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize = SharedCameraOrthographicSize;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 100f;
            camera.Lens = lens;
            return camera;
        }

        private static void ConfigureToneSource(AudioSource source)
        {
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.clip = null;
        }

        private static void CreateArtReplacementAnchors(Transform parent)
        {
            var root = new GameObject("Art Replacement Anchors").transform;
            root.SetParent(parent, false);
            new GameObject("Arena Art Anchor").transform.SetParent(root, false);
            new GameObject("NPC Art Anchor").transform.SetParent(root, false);
            new GameObject("Station Art Anchor").transform.SetParent(root, false);
            new GameObject("Tone Audio Anchor").transform.SetParent(root, false);
            new GameObject("VFX Anchor").transform.SetParent(root, false);
        }

        private static void BindView(
            SequenceMemoryNetworkView view,
            NetworkSequenceMemoryState state,
            CinemachineCamera sharedCamera,
            Transform playerRoot,
            Transform[] playerAnchors,
            Transform npcAnchor,
            GameObject arenaPresentation,
            AudioSource npcToneSource,
            AudioSource playerToneSource,
            SequenceMemoryHudBindings hud)
        {
            var serialized = new SerializedObject(view);
            SetRequiredReference(serialized, "state", state);
            SetRequiredReference(serialized, "sharedCamera", sharedCamera);
            SetRequiredReference(serialized, "playerRoot", playerRoot);
            SetRequiredReference(serialized, "npcAnchor", npcAnchor);
            SetRequiredReference(
                serialized,
                "arenaPresentation",
                arenaPresentation);
            SetRequiredReference(
                serialized,
                "npcToneSource",
                npcToneSource);
            SetRequiredReference(
                serialized,
                "playerToneSource",
                playerToneSource);
            SetRequiredReference(serialized, "hud", hud);
            SetRequiredReferenceArray(
                serialized,
                "playerAnchors",
                playerAnchors);

            RequireSerializedProperty(serialized, "highTone");
            RequireSerializedProperty(serialized, "middleTone");
            RequireSerializedProperty(serialized, "lowTone");
            serialized.FindProperty("highTone").objectReferenceValue = null;
            serialized.FindProperty("middleTone").objectReferenceValue = null;
            serialized.FindProperty("lowTone").objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(view);
        }

        private static void SetRequiredReference(
            SerializedObject serialized,
            string propertyName,
            UnityEngine.Object value)
        {
            var property = RequireSerializedProperty(
                serialized,
                propertyName);
            property.objectReferenceValue = value;
        }

        private static void SetRequiredReferenceArray<T>(
            SerializedObject serialized,
            string propertyName,
            T[] values)
            where T : UnityEngine.Object
        {
            var property = RequireSerializedProperty(
                serialized,
                propertyName);
            if (!property.isArray)
            {
                throw new InvalidOperationException(
                    serialized.targetObject.GetType().Name + "." +
                    propertyName + " must be a serialized array.");
            }
            property.arraySize = values.Length;
            for (var index = 0; index < values.Length; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue =
                    values[index];
            }
        }

        private static SerializedProperty RequireSerializedProperty(
            SerializedObject serialized,
            string propertyName)
        {
            var property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    serialized.targetObject.GetType().Name +
                    " must expose serialized field '" + propertyName + "'.");
            }
            return property;
        }

        private static GameObject LoadOrCreateHudPrefab()
        {
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

            // Root scale is a renderability contract, not a style choice.
            // Unity can serialize a transient root RectTransform at zero scale
            // on its first prefab save, so normalize only that root value.
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
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    HudPrefabPath);
            }

            var binding = prefab != null
                ? prefab.GetComponent<SequenceMemoryHudBindings>()
                : null;
            if (binding == null ||
                !binding.HasRequiredReferences ||
                prefab.transform.localScale != Vector3.one)
            {
                throw new InvalidOperationException(
                    "SequenceMemoryHud.prefab is missing its serialized " +
                    "binding contract or unit root scale. Repair the prefab " +
                    "instead of replacing designer-authored UI.");
            }
            return prefab;
        }

        private static GameObject CreateHudTemplate()
        {
            var font = RequireBuiltinFont();
            var canvasObject = new GameObject(
                "SequenceMemoryHud",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(SequenceMemoryHudBindings));
            canvasObject.transform.localScale = Vector3.one;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 45;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var visibleRoot = new GameObject(
                "HUD Root",
                typeof(RectTransform));
            visibleRoot.transform.SetParent(canvasObject.transform, false);
            StretchToParent(visibleRoot.GetComponent<RectTransform>());

            var problemPanel = CreatePanel(
                "NPC Problem Panel",
                visibleRoot.transform,
                new Vector2(0.5f, 1f),
                new Vector2(0f, -22f),
                new Vector2(1080f, 160f),
                new Color(0.045f, 0.055f, 0.12f, 0.94f));
            CreateHudText(
                "Problem Label",
                problemPanel.transform,
                font,
                new Vector2(0f, -14f),
                new Vector2(1000f, 32f),
                18,
                FontStyle.Bold,
                "NPC SEQUENCE");
            var npcSequenceText = CreateHudText(
                "NPC Sequence",
                problemPanel.transform,
                font,
                new Vector2(0f, -48f),
                new Vector2(1000f, 76f),
                52,
                FontStyle.Bold,
                "A  S  D  A  S");
            npcSequenceText.color = new Color(1f, 0.82f, 0.25f);
            var playerRows =
                new Image[SequenceMemoryRules.PlayerCount];
            var playerNames = new Text[SequenceMemoryRules.PlayerCount];
            var playerInputs = new Text[SequenceMemoryRules.PlayerCount];
            var playerStatuses = new Text[SequenceMemoryRules.PlayerCount];
            for (var slot = 0;
                 slot < SequenceMemoryRules.PlayerCount;
                 slot++)
            {
                var x = -690f + slot * 460f;
                var row = CreatePanel(
                    "Player Row " + (slot + 1),
                    visibleRoot.transform,
                    new Vector2(0.5f, 0f),
                    new Vector2(x, 34f),
                    new Vector2(420f, 190f),
                    new Color(0.025f, 0.032f, 0.07f, 0.94f));
                playerRows[slot] = row.GetComponent<Image>();
                var accent = CreatePanel(
                    "Player Accent",
                    row.transform,
                    new Vector2(0.5f, 1f),
                    new Vector2(0f, 0f),
                    new Vector2(420f, 8f),
                    PlayerColors[slot]);
                accent.GetComponent<Image>().raycastTarget = false;

                playerNames[slot] = CreateHudText(
                    "Player Name",
                    row.transform,
                    font,
                    new Vector2(0f, -18f),
                    new Vector2(380f, 34f),
                    20,
                    FontStyle.Bold,
                    "PLAYER " + (slot + 1));
                playerNames[slot].color = PlayerColors[slot];
                playerInputs[slot] = CreateHudText(
                    "Player Input",
                    row.transform,
                    font,
                    new Vector2(0f, -62f),
                    new Vector2(380f, 54f),
                    32,
                    FontStyle.Bold,
                    "A S D");
                playerStatuses[slot] = CreateHudText(
                    "Player Status",
                    row.transform,
                    font,
                    new Vector2(0f, -124f),
                    new Vector2(380f, 32f),
                    17,
                    FontStyle.Bold,
                    "ENTERING");
                playerStatuses[slot].color =
                    new Color(0.72f, 0.8f, 0.94f);
            }

            canvasObject.GetComponent<SequenceMemoryHudBindings>().Configure(
                canvas,
                visibleRoot,
                npcSequenceText,
                playerRows,
                playerNames,
                playerInputs,
                playerStatuses);
            SetUiLayer(canvasObject);
            return canvasObject;
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
            FontStyle fontStyle,
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
            text.fontStyle = fontStyle;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.color = new Color(0.94f, 0.97f, 1f);
            text.raycastTarget = false;
            text.text = sampleText;
            return text;
        }

        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Font RequireBuiltinFont()
        {
            var font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            if (font == null)
            {
                throw new InvalidOperationException(
                    "Unity built-in LegacyRuntime.ttf font is required.");
            }
            return font;
        }

        private static void SetUiLayer(GameObject root)
        {
            root.layer = LayerMask.NameToLayer("UI");
            for (var index = 0; index < root.transform.childCount; index++)
            {
                SetUiLayer(root.transform.GetChild(index).gameObject);
            }
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

        private static SequenceMemoryMaterials CreateMaterials()
        {
            var stations =
                new Material[SequenceMemoryRules.PlayerCount];
            for (var slot = 0;
                 slot < SequenceMemoryRules.PlayerCount;
                 slot++)
            {
                stations[slot] = CreateOrLoadMaterial(
                    "SequenceMemoryStation" + (slot + 1),
                    Color.Lerp(PlayerColors[slot], Color.black, 0.32f),
                    0.3f);
            }

            return new SequenceMemoryMaterials
            {
                Stage = CreateOrLoadMaterial(
                    "SequenceMemoryStage",
                    new Color(0.075f, 0.09f, 0.15f),
                    0.25f),
                Backdrop = CreateOrLoadMaterial(
                    "SequenceMemoryBackdrop",
                    new Color(0.035f, 0.028f, 0.09f),
                    0.2f),
                Trim = CreateOrLoadMaterial(
                    "SequenceMemoryTrim",
                    new Color(0.95f, 0.58f, 0.12f),
                    0.35f),
                Npc = CreateOrLoadMaterial(
                    "SequenceMemoryNpc",
                    new Color(0.22f, 0.62f, 0.94f),
                    0.3f),
                NpcAccent = CreateOrLoadMaterial(
                    "SequenceMemoryNpcAccent",
                    new Color(1f, 0.78f, 0.2f),
                    0.35f),
                Stations = stations
            };
        }

        private static Material CreateOrLoadMaterial(
            string name,
            Color color,
            float smoothness)
        {
            var path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Universal Render Pipeline/Lit is required for Sequence " +
                    "Memory prototype materials.");
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
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void ValidateSceneContract(GameObject root)
        {
            var hud = root.GetComponentInChildren<
                SequenceMemoryHudBindings>(true);
            var playerAnchors = FindDescendant(
                root.transform,
                "Player Anchors");
            var stations = FindDescendant(
                root.transform,
                "Station Placeholders");
            var npcAnchor = FindDescendant(root.transform, "NPC Anchor");

            if (root.GetComponent<NetworkObject>() == null ||
                root.GetComponent<NetworkSequenceMemoryState>() == null ||
                root.GetComponent<SequenceMemoryNetworkView>() == null ||
                root.GetComponents<AudioSource>().Length != 2 ||
                playerAnchors == null ||
                playerAnchors.childCount != SequenceMemoryRules.PlayerCount ||
                stations == null ||
                stations.childCount != SequenceMemoryRules.PlayerCount ||
                npcAnchor == null ||
                npcAnchor.Find("NPC Placeholder Body") == null ||
                root.GetComponentsInChildren<CinemachineCamera>(true).Length != 1 ||
                root.GetComponentsInChildren<Camera>(true).Length != 0 ||
                root.GetComponentsInChildren<AudioListener>(true).Length != 0 ||
                root.GetComponentInChildren<Light>(true) == null ||
                FindDescendant(root.transform, "Runtime Players") == null ||
                FindDescendant(root.transform, "Art Replacement Anchors") == null ||
                hud == null ||
                !hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Generated Sequence Memory scene is missing its network, " +
                    "station, NPC, camera, tone-source, art or HUD contract.");
            }

            var prefabPath = PrefabUtility
                .GetPrefabAssetPathOfNearestInstanceRoot(hud.gameObject);
            if (prefabPath != HudPrefabPath)
            {
                throw new InvalidOperationException(
                    "Sequence Memory Canvas must be instantiated from " +
                    "SequenceMemoryHud.prefab.");
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
            EnsureFolder(ProjectRoot);
            EnsureFolder(ScenesFolder);
            EnsureFolder(MinigameScenesFolder);
            EnsureFolder(UiPrefabFolder);
            EnsureFolder(ProjectRoot + "/Art");
            EnsureFolder(ProjectRoot + "/Art/Minigames");
            EnsureFolder(ProjectRoot + "/Art/Minigames/SequenceMemory");
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
            for (var index = scenes.Count - 1; index >= 0; index--)
            {
                if (scenes[index].path == ScenePath)
                {
                    scenes.RemoveAt(index);
                }
            }

            var insertAfter = -1;
            for (var index = 0; index < scenes.Count; index++)
            {
                if (scenes[index].path == RaceScenePath)
                {
                    insertAfter = index;
                    break;
                }
            }
            scenes.Insert(
                insertAfter >= 0 ? insertAfter + 1 : scenes.Count,
                new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private sealed class ArenaReferences
        {
            public Transform[] PlayerAnchors;
            public Transform NpcAnchor;
        }

        private sealed class SequenceMemoryMaterials
        {
            public Material Stage;
            public Material Backdrop;
            public Material Trim;
            public Material Npc;
            public Material NpcAccent;
            public Material[] Stations;
        }
    }
}
