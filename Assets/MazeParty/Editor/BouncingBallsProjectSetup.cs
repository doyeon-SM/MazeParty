using System;
using System.Collections.Generic;
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
    /// Creates the four-goal stage and prefab-authored Canvas. Rerunning setup
    /// reuses existing materials and HUD design instead of replacing them.
    /// </summary>
    public static class BouncingBallsProjectSetup
    {
        private const string MenuPath = "MazeParty/Minigames/Rebuild Bouncing Balls";
        private const string ProjectRoot = "Assets/MazeParty";
        private const string SceneFolder = ProjectRoot + "/Scenes/Minigames";
        private const string PrefabFolder = ProjectRoot + "/UI/Prefabs";
        private const string MaterialFolder =
            ProjectRoot + "/Art/Minigames/BouncingBalls/Materials";

        public const string ScenePath = SceneFolder + "/BouncingBalls.unity";
        public const string HudPrefabPath = PrefabFolder + "/BouncingBallsHud.prefab";
        public const float ArenaCenterX = 1260f;
        private const int PlayerCount = 4;
        private const int BallCount = 3;
        private const float Boundary = 8f;
        private const float GoalHalfWidth = 3.1f;
        private const float ShieldRail = 7.35f;

        private static readonly Color[] PlayerColors =
        {
            new Color(0.20f, 0.60f, 1f),
            new Color(1f, 0.32f, 0.24f),
            new Color(0.25f, 0.86f, 0.48f),
            new Color(0.82f, 0.35f, 1f)
        };

        [MenuItem(MenuPath)]
        public static void RebuildBouncingBalls()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("Bouncing Balls rebuild canceled; scene edits were preserved.");
                return;
            }

            BuildBouncingBallsAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("Bouncing Balls stage, shared camera, and authored HUD are ready.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRebuildBouncingBalls()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static void BuildBouncingBallsAssets()
        {
            EnsureFolders();
            MinigameTimerDialProjectSetup.EnsurePrefabExists();
            var materials = CreateMaterials();
            var hudPrefab = LoadOrCreateHudPrefab();
            BuildScene(materials, hudPrefab);
            AssetDatabase.SaveAssets();
        }

        private static void BuildScene(Materials materials, GameObject hudPrefab)
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
                        "BouncingBalls.unity has unsaved edits. Save or discard them first.");
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

            var root = new GameObject("Bouncing Balls Network State");
            var arena = new GameObject("Arena Presentation");
            arena.transform.SetParent(root.transform, false);
            arena.transform.position = new Vector3(ArenaCenterX, 0f, 0f);
            var references = CreateArena(arena.transform, materials);
            CreateLighting(arena.transform);
            var sharedCamera = CreateSharedCamera(root.transform);
            CreateArtReplacementAnchors(root.transform);

            // A saved scene path is required before NGO can assign an in-scene hash.
            EditorSceneManager.SaveScene(scene, ScenePath);
            root.AddComponent<NetworkObject>();
            var state = root.AddComponent<NetworkBouncingBallsState>();
            var view = root.AddComponent<BouncingBallsNetworkView>();

            var hudInstance = PrefabUtility.InstantiatePrefab(
                hudPrefab, root.transform) as GameObject;
            var hud = hudInstance != null
                ? hudInstance.GetComponent<BouncingBallsHudBindings>()
                : null;
            if (hud == null || !hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "BouncingBallsHud.prefab is missing required authored bindings.");
            }

            BindView(view, state, sharedCamera, arena,
                references.Shields, references.Balls,
                references.ShieldRenderers, references.BallRenderers, hud);
            ValidateScene(root);
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

        private static ArenaReferences CreateArena(Transform parent, Materials materials)
        {
            CreatePrimitive("Field", PrimitiveType.Cube, parent,
                new Vector3(0f, 0f, 1.05f), Quaternion.identity,
                new Vector3(16.4f, 16.4f, 0.35f), materials.Field);
            CreatePrimitive("Center Disc", PrimitiveType.Cylinder, parent,
                new Vector3(0f, 0f, 0.72f), Quaternion.Euler(90f, 0f, 0f),
                new Vector3(2.1f, 0.025f, 2.1f), materials.Trim);

            var wallRoot = new GameObject("Boundary Walls").transform;
            wallRoot.SetParent(parent, false);
            var segmentHalf = (Boundary - GoalHalfWidth) * 0.5f;
            var segmentCenter = GoalHalfWidth + segmentHalf;
            for (var side = -1; side <= 1; side += 2)
            {
                CreatePrimitive("Bottom Wall " + side, PrimitiveType.Cube,
                    wallRoot, new Vector3(side * segmentCenter, -Boundary, 0.32f),
                    Quaternion.identity, new Vector3(segmentHalf * 2f, 0.26f, 0.6f),
                    materials.Wall);
                CreatePrimitive("Top Wall " + side, PrimitiveType.Cube,
                    wallRoot, new Vector3(side * segmentCenter, Boundary, 0.32f),
                    Quaternion.identity, new Vector3(segmentHalf * 2f, 0.26f, 0.6f),
                    materials.Wall);
                CreatePrimitive("Left Wall " + side, PrimitiveType.Cube,
                    wallRoot, new Vector3(-Boundary, side * segmentCenter, 0.32f),
                    Quaternion.identity, new Vector3(0.26f, segmentHalf * 2f, 0.6f),
                    materials.Wall);
                CreatePrimitive("Right Wall " + side, PrimitiveType.Cube,
                    wallRoot, new Vector3(Boundary, side * segmentCenter, 0.32f),
                    Quaternion.identity, new Vector3(0.26f, segmentHalf * 2f, 0.6f),
                    materials.Wall);
            }

            var goalRoot = new GameObject("Goals").transform;
            goalRoot.SetParent(parent, false);
            var shieldRoot = new GameObject("Shields").transform;
            shieldRoot.SetParent(parent, false);
            var ballRoot = new GameObject("Balls").transform;
            ballRoot.SetParent(parent, false);

            // Slot order is bottom, right, top, left throughout rules and view.
            var shieldPositions = new[]
            {
                new Vector3(0f, -ShieldRail, 0.38f),
                new Vector3(ShieldRail, 0f, 0.38f),
                new Vector3(0f, ShieldRail, 0.38f),
                new Vector3(-ShieldRail, 0f, 0.38f)
            };
            var goalPositions = new[]
            {
                new Vector3(0f, -8.28f, 0.58f),
                new Vector3(8.28f, 0f, 0.58f),
                new Vector3(0f, 8.28f, 0.58f),
                new Vector3(-8.28f, 0f, 0.58f)
            };
            var shields = new Transform[PlayerCount];
            var shieldRenderers = new Renderer[PlayerCount];
            for (var slot = 0; slot < PlayerCount; slot++)
            {
                var vertical = slot == 1 || slot == 3;
                CreatePrimitive("Goal " + (slot + 1), PrimitiveType.Cube,
                    goalRoot, goalPositions[slot], Quaternion.identity,
                    vertical
                        ? new Vector3(0.26f, GoalHalfWidth * 2f, 0.16f)
                        : new Vector3(GoalHalfWidth * 2f, 0.26f, 0.16f),
                    materials.Goals[slot]);
                var shield = CreatePrimitive("Shield " + (slot + 1),
                    PrimitiveType.Cube, shieldRoot, shieldPositions[slot],
                    Quaternion.identity,
                    vertical
                        ? new Vector3(0.34f, 2.2f, 0.42f)
                        : new Vector3(2.2f, 0.34f, 0.42f),
                    materials.Shields[slot]);
                shields[slot] = shield.transform;
                shieldRenderers[slot] = shield.GetComponent<Renderer>();
            }

            var balls = new Transform[BallCount];
            var ballRenderers = new Renderer[BallCount];
            for (var index = 0; index < BallCount; index++)
            {
                var ball = CreatePrimitive("Ball " + (index + 1),
                    PrimitiveType.Sphere, ballRoot,
                    new Vector3((index - 1) * 0.7f, 0f, 0.12f),
                    Quaternion.identity, Vector3.one * 0.48f,
                    materials.NeutralBall);
                balls[index] = ball.transform;
                ballRenderers[index] = ball.GetComponent<Renderer>();
            }

            return new ArenaReferences
            {
                Shields = shields,
                ShieldRenderers = shieldRenderers,
                Balls = balls,
                BallRenderers = ballRenderers
            };
        }

        private static void CreateLighting(Transform parent)
        {
            var lightObject = new GameObject("Bouncing Balls Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(25f, -30f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.35f;
            light.color = new Color(0.95f, 0.96f, 1f);
        }

        private static CinemachineCamera CreateSharedCamera(Transform parent)
        {
            var objectCamera = new GameObject("CM_BouncingBallsShared");
            objectCamera.transform.SetParent(parent, false);
            objectCamera.transform.SetPositionAndRotation(
                new Vector3(ArenaCenterX, 0f, -20f), Quaternion.identity);
            var camera = objectCamera.AddComponent<CinemachineCamera>();
            camera.Priority = 0;
            var lens = camera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize = 11f;
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
            new GameObject("Shield Art Anchor").transform.SetParent(anchors, false);
            new GameObject("Ball Art Anchor").transform.SetParent(anchors, false);
            new GameObject("VFX Anchor").transform.SetParent(anchors, false);
            new GameObject("Audio Anchor").transform.SetParent(anchors, false);
        }

        private static void BindView(
            BouncingBallsNetworkView view,
            NetworkBouncingBallsState state,
            CinemachineCamera camera,
            GameObject arena,
            Transform[] shields,
            Transform[] balls,
            Renderer[] shieldRenderers,
            Renderer[] ballRenderers,
            BouncingBallsHudBindings hud)
        {
            var serialized = new SerializedObject(view);
            SetReference(serialized, "state", state);
            SetReference(serialized, "sharedCamera", camera);
            SetReference(serialized, "arenaPresentation", arena);
            SetArray(serialized, "shieldTransforms", shields);
            SetArray(serialized, "ballTransforms", balls);
            SetArray(serialized, "shieldRenderers", shieldRenderers);
            SetArray(serialized, "ballRenderers", ballRenderers);
            SetReference(serialized, "hud", hud);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(view);
        }

        private static SerializedProperty RequireProperty(
            SerializedObject serialized, string name)
        {
            var property = serialized.FindProperty(name);
            if (property == null)
            {
                throw new InvalidOperationException(
                    serialized.targetObject.GetType().Name +
                    " must expose serialized field '" + name + "'.");
            }
            return property;
        }

        private static void SetReference(
            SerializedObject serialized, string name, UnityEngine.Object value)
        {
            RequireProperty(serialized, name).objectReferenceValue = value;
        }

        private static void SetArray<T>(
            SerializedObject serialized, string name, T[] values)
            where T : UnityEngine.Object
        {
            var property = RequireProperty(serialized, name);
            if (!property.isArray)
            {
                throw new InvalidOperationException(name + " must be an array.");
            }
            property.arraySize = values.Length;
            for (var index = 0; index < values.Length; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue =
                    values[index];
            }
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
                        template, HudPrefabPath);
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
                ? prefab.GetComponent<BouncingBallsHudBindings>()
                : null;
            if (binding == null || !binding.HasRequiredReferences ||
                prefab.transform.localScale != Vector3.one)
            {
                throw new InvalidOperationException(
                    "Repair BouncingBallsHud.prefab bindings without replacing its design.");
            }
            return prefab;
        }

        private static GameObject CreateHudTemplate()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                throw new InvalidOperationException("LegacyRuntime.ttf is required.");
            }

            var canvasObject = new GameObject("BouncingBallsHud",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(BouncingBallsHudBindings));
            canvasObject.transform.localScale = Vector3.one;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 45;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var visibleRoot = new GameObject("HUD Root", typeof(RectTransform));
            visibleRoot.transform.SetParent(canvasObject.transform, false);
            var rootRect = visibleRoot.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var header = CreatePanel("Header Panel", visibleRoot.transform,
                new Vector2(0.5f, 1f), new Vector2(0f, -22f),
                new Vector2(840f, 118f),
                new Color(0.024f, 0.034f, 0.075f, 0.92f));
            var phaseText = CreateText("Phase", header.transform, font,
                new Vector2(0f, -8f), new Vector2(760f, 44f), 26,
                "BOUNCING BALLS · GET READY");
            var roundText = CreateText("Round", header.transform, font,
                new Vector2(0f, -57f), new Vector2(760f, 34f), 21,
                "ROUND 1 / 2");
            var timer = MinigameTimerDialProjectSetup.InstantiateTimer(
                visibleRoot.transform);

            var controls = CreatePanel("Controls Panel", visibleRoot.transform,
                new Vector2(0f, 1f), new Vector2(24f, -24f),
                new Vector2(350f, 104f),
                new Color(0.018f, 0.03f, 0.065f, 0.92f));
            var instructionText = CreateText("Instructions", controls.transform,
                font, new Vector2(0f, -15f), new Vector2(326f, 80f), 19,
                "A / D · MOVE SHIELD\nKEEP BALLS OUT OF YOUR GOAL");

            var names = new Text[PlayerCount];
            var scores = new Text[PlayerCount];
            var conceded = new Text[PlayerCount];
            for (var slot = 0; slot < PlayerCount; slot++)
            {
                var panel = CreatePanel("Player Card " + (slot + 1),
                    visibleRoot.transform, new Vector2(0.5f, 0f),
                    new Vector2(-690f + slot * 460f, 28f),
                    new Vector2(420f, 158f),
                    new Color(0.022f, 0.03f, 0.067f, 0.94f));
                CreatePanel("Player Accent", panel.transform,
                    new Vector2(0.5f, 1f), Vector2.zero,
                    new Vector2(420f, 8f), PlayerColors[slot]);
                names[slot] = CreateText("Player Name", panel.transform,
                    font, new Vector2(0f, -20f), new Vector2(380f, 32f),
                    21, "PLAYER " + (slot + 1));
                names[slot].color = PlayerColors[slot];
                scores[slot] = CreateText("Score", panel.transform, font,
                    new Vector2(0f, -58f), new Vector2(380f, 51f),
                    34, "SCORE 0");
                conceded[slot] = CreateText("Conceded", panel.transform,
                    font, new Vector2(0f, -113f), new Vector2(380f, 28f),
                    16, "CONCEDED 0");
            }

            canvasObject.GetComponent<BouncingBallsHudBindings>().Configure(
                canvas, visibleRoot, timer, roundText, phaseText,
                instructionText, names, scores, conceded);
            SetUiLayer(canvasObject);
            return canvasObject;
        }

        private static GameObject CreatePanel(
            string name, Transform parent, Vector2 anchor,
            Vector2 position, Vector2 size, Color color)
        {
            var panel = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
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

        private static Text CreateText(
            string name, Transform parent, Font font, Vector2 position,
            Vector2 size, int fontSize, string sample)
        {
            var item = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text));
            item.transform.SetParent(parent, false);
            var rect = item.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var text = item.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.color = new Color(0.94f, 0.97f, 1f);
            text.raycastTarget = false;
            text.text = sample;
            return text;
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
            var result = new Materials
            {
                Field = CreateOrLoadMaterial("Field", new Color(0.045f, 0.12f, 0.17f)),
                Wall = CreateOrLoadMaterial("Wall", new Color(0.27f, 0.38f, 0.49f)),
                Trim = CreateOrLoadMaterial("Trim", new Color(0.19f, 0.54f, 0.65f)),
                NeutralBall = CreateOrLoadMaterial("NeutralBall", new Color(0.96f, 0.95f, 0.78f)),
                Goals = new Material[PlayerCount],
                Shields = new Material[PlayerCount]
            };
            for (var slot = 0; slot < PlayerCount; slot++)
            {
                result.Goals[slot] = CreateOrLoadMaterial(
                    "Goal" + (slot + 1),
                    Color.Lerp(PlayerColors[slot], Color.black, 0.52f));
                result.Shields[slot] = CreateOrLoadMaterial(
                    "Shield" + (slot + 1), PlayerColors[slot]);
            }
            return result;
        }

        private static Material CreateOrLoadMaterial(string name, Color color)
        {
            var path = MaterialFolder + "/BouncingBalls" + name + ".mat";
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
            material = new Material(shader) { name = "BouncingBalls" + name };
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.35f);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void ValidateScene(GameObject root)
        {
            var hud = root.GetComponentInChildren<BouncingBallsHudBindings>(true);
            if (root.GetComponent<NetworkObject>() == null ||
                root.GetComponent<NetworkBouncingBallsState>() == null ||
                root.GetComponent<BouncingBallsNetworkView>() == null ||
                FindDescendant(root.transform, "Goals")?.childCount != PlayerCount ||
                FindDescendant(root.transform, "Shields")?.childCount != PlayerCount ||
                FindDescendant(root.transform, "Balls")?.childCount != BallCount ||
                root.GetComponentsInChildren<CinemachineCamera>(true).Length != 1 ||
                root.GetComponentsInChildren<Camera>(true).Length != 0 ||
                root.GetComponentsInChildren<AudioListener>(true).Length != 0 ||
                root.GetComponentInChildren<Light>(true) == null ||
                hud == null || !hud.HasRequiredReferences ||
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    hud.gameObject) != HudPrefabPath)
            {
                throw new InvalidOperationException(
                    "Generated Bouncing Balls scene failed network, arena, camera, or prefab HUD contract.");
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

        private static void EnsureFolders()
        {
            EnsureFolder(SceneFolder);
            EnsureFolder(PrefabFolder);
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
                "Assets/MazeParty/Scenes/Minigames/SequenceMemory.unity");
            scenes.Insert(after >= 0 ? after + 1 : scenes.Count,
                new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private sealed class ArenaReferences
        {
            public Transform[] Shields;
            public Transform[] Balls;
            public Renderer[] ShieldRenderers;
            public Renderer[] BallRenderers;
        }

        private sealed class Materials
        {
            public Material Field;
            public Material Wall;
            public Material Trim;
            public Material NeutralBall;
            public Material[] Goals;
            public Material[] Shields;
        }
    }
}
