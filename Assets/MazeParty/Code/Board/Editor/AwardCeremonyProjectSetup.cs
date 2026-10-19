using System;
using System.Linq;
using MazeParty.Multiplayer;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    /// <summary>
    /// Creates the initial authored ceremony prefabs and installs prefab instances
    /// into Board.unity. Existing prefab designs are validated but never restyled.
    /// </summary>
    public static class AwardCeremonyProjectSetup
    {
        public const string CanvasPrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/AwardCeremonyCanvas.prefab";
        public const string StagePrefabPath =
            "Assets/MazeParty/Prefabs/Board/World/AwardCeremonyStage.prefab";

        private const string BoardScenePath =
            "Assets/MazeParty/Scenes/Board/Board.unity";
        private const string AssetFolder =
            "Assets/MazeParty/Prefabs/Board/AwardCeremony";
        private const string VisibleClipPath =
            AssetFolder + "/AwardOverlayVisible.anim";
        private const string SlideClipPath =
            AssetFolder + "/AwardOverlaySlideUp.anim";
        private const string ControllerPath =
            AssetFolder + "/AwardOverlay.controller";
        private const string PodiumMaterialPath =
            AssetFolder + "/AwardPodium.mat";
        private const string StageMaterialPath =
            AssetFolder + "/AwardStage.mat";

        private static readonly Vector3 StageWorldPosition =
            new Vector3(1860f, 0f, 0f);

        [MenuItem("MazeParty/Gameplay/Install Award Ceremony")]
        public static void InstallAwardCeremony()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Award ceremony setup requires Edit Mode.");
            }
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                if (SceneManager.GetSceneAt(index).isDirty)
                {
                    throw new InvalidOperationException(
                        "Save all open scenes before installing the award ceremony.");
                }
            }

            EnsureAssets();
            var scene = EditorSceneManager.OpenScene(
                BoardScenePath,
                OpenSceneMode.Single);
            EnsureSceneInstances(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "Award ceremony prefabs and Board scene instances are ready.");
        }

        internal static void EnsureAssets()
        {
            EnsureFolders();
            EnsureAnimationController();

            var canvasPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                CanvasPrefabPath);
            if (canvasPrefab == null)
            {
                var template = CreateCanvasTemplate();
                try
                {
                    canvasPrefab = PrefabUtility.SaveAsPrefabAsset(
                        template,
                        CanvasPrefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(template);
                }
            }
            NormalizeCanvasRuntimeDefaults();
            canvasPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                CanvasPrefabPath);
            ValidateCanvasPrefab(canvasPrefab);

            var stagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                StagePrefabPath);
            if (stagePrefab == null)
            {
                var template = CreateStageTemplate();
                try
                {
                    stagePrefab = PrefabUtility.SaveAsPrefabAsset(
                        template,
                        StagePrefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(template);
                }
            }
            ValidateStagePrefab(stagePrefab);
        }

        internal static void EnsureSceneInstances(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException(
                    "Award ceremony installation requires a loaded scene.");
            }
            EnsureAssets();

            var canvasBindings = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    AwardCeremonyCanvasBindings>(true))
                .ToArray();
            if (canvasBindings.Length > 1)
            {
                throw new InvalidOperationException(
                    scene.path + " has multiple award ceremony Canvases.");
            }
            if (canvasBindings.Length == 0)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    CanvasPrefabPath);
                var instance = PrefabUtility.InstantiatePrefab(prefab, scene)
                    as GameObject;
                if (instance == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate AwardCeremonyCanvas.prefab.");
                }
                instance.name = "Award Ceremony Canvas";
                canvasBindings = new[]
                {
                    instance.GetComponent<AwardCeremonyCanvasBindings>()
                };
            }
            ValidateScenePrefabInstance(
                canvasBindings[0].gameObject,
                CanvasPrefabPath,
                canvasBindings[0].HasRequiredReferences,
                scene.path + " has an invalid award ceremony Canvas.");

            var stageBindings = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    AwardCeremonyStageBindings>(true))
                .ToArray();
            if (stageBindings.Length > 1)
            {
                throw new InvalidOperationException(
                    scene.path + " has multiple award ceremony stages.");
            }
            if (stageBindings.Length == 0)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    StagePrefabPath);
                var instance = PrefabUtility.InstantiatePrefab(prefab, scene)
                    as GameObject;
                if (instance == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate AwardCeremonyStage.prefab.");
                }
                instance.name = "Award Ceremony Stage";
                instance.transform.position = StageWorldPosition;
                stageBindings = new[]
                {
                    instance.GetComponent<AwardCeremonyStageBindings>()
                };
            }
            ValidateScenePrefabInstance(
                stageBindings[0].gameObject,
                StagePrefabPath,
                stageBindings[0].HasRequiredReferences,
                scene.path + " has an invalid award ceremony stage.");
        }

        private static GameObject CreateCanvasTemplate()
        {
            var root = new GameObject(
                "AwardCeremonyCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(AwardCeremonyCanvasBindings),
                typeof(AwardCeremonyView));
            root.transform.localScale = Vector3.one;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 120;
            canvas.enabled = false;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            var raycaster = root.GetComponent<GraphicRaycaster>();
            raycaster.enabled = false;

            var overlay = CreateRect(
                "Bonus Award Overlay",
                root.transform,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero).gameObject;
            var overlayImage = overlay.AddComponent<Image>();
            overlayImage.color = new Color(0.025f, 0.045f, 0.1f, 0.985f);
            overlay.AddComponent<CanvasGroup>();
            var overlayAnimator = overlay.AddComponent<Animator>();
            overlayAnimator.runtimeAnimatorController =
                AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    ControllerPath);

            var topRule = CreateRect(
                "Top Gold Rule",
                overlay.transform,
                new Vector2(0.08f, 0.78f),
                new Vector2(0.92f, 0.79f),
                Vector2.zero,
                Vector2.zero).gameObject.AddComponent<Image>();
            topRule.color = new Color(1f, 0.72f, 0.12f, 0.95f);

            var step = CreateText(
                overlay.transform,
                "Award Step",
                "BONUS KEY AWARD 1 / 2",
                34,
                new Color(1f, 0.82f, 0.35f),
                new Vector2(0.1f, 0.68f),
                new Vector2(0.9f, 0.77f),
                TextAnchor.MiddleCenter);
            var category = CreateText(
                overlay.transform,
                "Award Category",
                "MOST GOLD HELD",
                72,
                Color.white,
                new Vector2(0.08f, 0.50f),
                new Vector2(0.92f, 0.68f),
                TextAnchor.MiddleCenter);
            var winner = CreateText(
                overlay.transform,
                "Award Winner",
                "PLAYER 1",
                42,
                Color.white,
                new Vector2(0.08f, 0.24f),
                new Vector2(0.92f, 0.51f),
                TextAnchor.MiddleCenter);
            var reward = CreateText(
                overlay.transform,
                "Award Reward",
                "+1 KEY EACH",
                38,
                new Color(1f, 0.72f, 0.12f),
                new Vector2(0.1f, 0.14f),
                new Vector2(0.9f, 0.24f),
                TextAnchor.MiddleCenter);

            var finalPanel = CreateRect(
                "Return Controls",
                root.transform,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero).gameObject;
            var inputLock = CreateText(
                finalPanel.transform,
                "Input Lock",
                "WINNER REVEAL  -  CONTROLS UNLOCK IN 3",
                28,
                Color.white,
                new Vector2(0.04f, 0.11f),
                new Vector2(0.65f, 0.17f),
                TextAnchor.MiddleCenter);
            var status = CreateText(
                finalPanel.transform,
                "Return Status",
                "The first-place podium is in the spotlight.",
                24,
                new Color(0.78f, 0.86f, 0.96f),
                new Vector2(0.04f, 0.03f),
                new Vector2(0.65f, 0.11f),
                TextAnchor.MiddleCenter);
            var leaveButton = CreateButton(
                finalPanel.transform,
                "Leave Room Button",
                "CLEAN UP BOARD",
                new Vector2(0.70f, 0.05f),
                new Vector2(0.92f, 0.14f),
                out var leaveButtonText);

            finalPanel.SetActive(false);
            var bindings = root.GetComponent<AwardCeremonyCanvasBindings>();
            bindings.Configure(
                canvas,
                raycaster,
                overlay,
                overlayAnimator,
                step,
                category,
                winner,
                reward,
                finalPanel,
                inputLock,
                leaveButton,
                leaveButtonText,
                status);
            root.GetComponent<AwardCeremonyView>().Configure(bindings);
            SetLayerRecursively(root.transform, LayerMask.NameToLayer("UI"));
            return root;
        }

        private static void NormalizeCanvasRuntimeDefaults()
        {
            var contents = PrefabUtility.LoadPrefabContents(CanvasPrefabPath);
            try
            {
                var changed = false;
                var canvas = contents.GetComponent<Canvas>();
                if (canvas != null && canvas.enabled)
                {
                    canvas.enabled = false;
                    changed = true;
                }
                var raycaster = contents.GetComponent<GraphicRaycaster>();
                if (raycaster != null && raycaster.enabled)
                {
                    raycaster.enabled = false;
                    changed = true;
                }
                var uiLayer = LayerMask.NameToLayer("UI");
                foreach (var transform in
                         contents.GetComponentsInChildren<Transform>(true))
                {
                    if (transform.gameObject.layer == uiLayer)
                    {
                        continue;
                    }
                    transform.gameObject.layer = uiLayer;
                    changed = true;
                }
                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(
                        contents,
                        CanvasPrefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static GameObject CreateStageTemplate()
        {
            return AwardCeremonyFeedbackAuthoring.CreateStage(
                EnsureMaterial(StageMaterialPath, new Color(0.035f, 0.055f, 0.11f)),
                EnsureMaterial(PodiumMaterialPath, new Color(0.42f, 0.46f, 0.54f)));
        }

        private static void EnsureAnimationController()
        {
            var visibleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                VisibleClipPath);
            if (visibleClip == null)
            {
                visibleClip = new AnimationClip
                {
                    name = "AwardOverlayVisible",
                    frameRate = 60f
                };
                visibleClip.SetCurve(
                    string.Empty,
                    typeof(RectTransform),
                    "m_AnchoredPosition.y",
                    AnimationCurve.Constant(0f, 0.01f, 0f));
                visibleClip.SetCurve(
                    string.Empty,
                    typeof(CanvasGroup),
                    "m_Alpha",
                    AnimationCurve.Constant(0f, 0.01f, 1f));
                AssetDatabase.CreateAsset(visibleClip, VisibleClipPath);
            }

            var slideClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                SlideClipPath);
            if (slideClip == null)
            {
                slideClip = new AnimationClip
                {
                    name = "AwardOverlaySlideUp",
                    frameRate = 60f
                };
                slideClip.SetCurve(
                    string.Empty,
                    typeof(RectTransform),
                    "m_AnchoredPosition.y",
                    AnimationCurve.EaseInOut(0f, 0f, 0.75f, 1220f));
                slideClip.SetCurve(
                    string.Empty,
                    typeof(CanvasGroup),
                    "m_Alpha",
                    AnimationCurve.EaseInOut(0f, 1f, 0.75f, 0f));
                AssetDatabase.CreateAsset(slideClip, SlideClipPath);
            }

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(
                    ControllerPath) != null)
            {
                return;
            }
            var controller = AnimatorController.CreateAnimatorControllerAtPath(
                ControllerPath);
            var machine = controller.layers[0].stateMachine;
            var visibleState = machine.AddState("Visible");
            visibleState.motion = visibleClip;
            var slideState = machine.AddState("SlideUp");
            slideState.motion = slideClip;
            machine.defaultState = visibleState;
        }

        private static Material EnsureMaterial(string path, Color color)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }
            var shader = Shader.Find("MazeParty/Toon Shader FX") ??
                         Shader.Find("Standard");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "No supported shader is available for the ceremony stage.");
            }
            material = new Material(shader)
            {
                name = System.IO.Path.GetFileNameWithoutExtension(path),
                color = color
            };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static GameObject CreatePrimitive(
            string name,
            PrimitiveType type,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            var result = GameObject.CreatePrimitive(type);
            result.name = name;
            result.transform.SetParent(parent, false);
            result.transform.localPosition = localPosition;
            result.transform.localScale = localScale;
            var collider = result.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
            var renderer = result.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
            return result;
        }

        private static RectTransform CreateRect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            var result = new GameObject(name, typeof(RectTransform));
            var rect = result.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.localScale = Vector3.one;
            return rect;
        }

        private static Text CreateText(
            Transform parent,
            string name,
            string value,
            int fontSize,
            Color color,
            Vector2 anchorMin,
            Vector2 anchorMax,
            TextAnchor alignment)
        {
            var rect = CreateRect(
                name,
                parent,
                anchorMin,
                anchorMax,
                new Vector2(8f, 4f),
                new Vector2(-8f, -4f));
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Normal;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static Button CreateButton(
            Transform parent,
            string name,
            string label,
            Vector2 anchorMin,
            Vector2 anchorMax,
            out Text buttonText)
        {
            var rect = CreateRect(
                name,
                parent,
                anchorMin,
                anchorMax,
                Vector2.zero,
                Vector2.zero);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.92f, 0.57f, 0.08f, 0.96f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 0.72f, 0.18f);
            colors.pressedColor = new Color(0.78f, 0.40f, 0.04f);
            colors.disabledColor = new Color(0.25f, 0.28f, 0.34f, 0.8f);
            button.colors = colors;
            buttonText = CreateText(
                rect,
                "Label",
                label,
                30,
                Color.white,
                Vector2.zero,
                Vector2.one,
                TextAnchor.MiddleCenter);
            return button;
        }

        private static void ValidateCanvasPrefab(GameObject prefab)
        {
            var bindings = prefab != null
                ? prefab.GetComponent<AwardCeremonyCanvasBindings>()
                : null;
            var view = prefab != null
                ? prefab.GetComponent<AwardCeremonyView>()
                : null;
            if (bindings == null || view == null ||
                !bindings.HasRequiredReferences ||
                view.Bindings != bindings ||
                prefab.GetComponentsInChildren<Canvas>(true).Length != 1 ||
                prefab.GetComponent<Canvas>() != bindings.RootCanvas ||
                prefab.GetComponent<GraphicRaycaster>() !=
                bindings.RootRaycaster)
            {
                throw new InvalidOperationException(
                    "AwardCeremonyCanvas.prefab has incomplete bindings.");
            }
        }

        private static void ValidateStagePrefab(GameObject prefab)
        {
            var bindings = prefab != null
                ? prefab.GetComponent<AwardCeremonyStageBindings>()
                : null;
            var presentation = prefab != null
                ? prefab.GetComponent<AwardCeremonyPresentation>()
                : null;
            if (bindings == null || presentation == null ||
                !bindings.HasRequiredReferences ||
                presentation.Bindings != bindings ||
                prefab.GetComponentsInChildren<Camera>(true).Length != 0 ||
                prefab.GetComponentsInChildren<AudioListener>(true).Length != 0 ||
                prefab.GetComponentsInChildren<CinemachineCamera>(true).Length != 1)
            {
                throw new InvalidOperationException(
                    "AwardCeremonyStage.prefab has incomplete bindings.");
            }
        }

        private static void ValidateScenePrefabInstance(
            GameObject instance,
            string prefabPath,
            bool referencesValid,
            string message)
        {
            if (instance == null || instance.transform.parent != null ||
                !referencesValid ||
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance) !=
                prefabPath)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/MazeParty/Prefabs/Board", "UI");
            EnsureFolder("Assets/MazeParty/Prefabs/Board", "World");
            EnsureFolder("Assets/MazeParty/Prefabs/Board", "AwardCeremony");
        }

        private static void EnsureFolder(string parent, string child)
        {
            var path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root)
            {
                SetLayerRecursively(child, layer);
            }
        }
    }
}
