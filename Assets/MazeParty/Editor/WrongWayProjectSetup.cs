using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.WrongWay;
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
    /// Generates the additive WrongWay race scene and its lightweight art.
    /// Existing build scenes are preserved; WrongWay is inserted immediately
    /// after Minefield (or Board when Minefield is not registered).
    /// </summary>
    public static class WrongWayProjectSetup
    {
        private const string MenuPath =
            "MazeParty/Minigames/Rebuild WrongWay";
        private const string ProjectRoot = "Assets/MazeParty";
        private const string ScenesFolder =
            ProjectRoot + "/Scenes";
        private const string UiFolder =
            ProjectRoot + "/UI";
        private const string UiPrefabFolder =
            UiFolder + "/Prefabs";
        private const string WrongWayHudPrefabPath =
            UiPrefabFolder + "/WrongWayHud.prefab";
        private const string ArtFolder =
            ProjectRoot + "/Art";
        private const string MinigameArtFolder =
            ArtFolder + "/Minigames";
        private const string WrongWayArtFolder =
            MinigameArtFolder + "/WrongWay";
        private const string MaterialFolder =
            WrongWayArtFolder + "/Materials";
        private const string BoardScenePath =
            ScenesFolder + "/Board.unity";
        private const string MinefieldScenePath =
            ScenesFolder + "/Minefield.unity";

        public const string WrongWayScenePath =
            "Assets/MazeParty/Scenes/WrongWay.unity";

        [MenuItem(MenuPath)]
        public static void RebuildWrongWay()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log(
                    "WrongWay rebuild canceled; open scene changes were left untouched.");
                return;
            }

            BuildWrongWayAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var wrongWay =
                SceneManager.GetSceneByPath(WrongWayScenePath);
            if (wrongWay.IsValid() && wrongWay.isLoaded)
            {
                SceneManager.SetActiveScene(wrongWay);
            }
            else
            {
                EditorSceneManager.OpenScene(
                    WrongWayScenePath,
                    OpenSceneMode.Single);
            }

            Debug.Log(
                "WrongWay rebuilt: four 50-step lanes, network state, " +
                "lead-follow race camera, connected HUD prefab and " +
                "additive-safe presentation.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRebuildWrongWay()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static void BuildWrongWayAssets()
        {
            EnsureFolders();
            var hudPrefab = LoadOrCreateWrongWayHudPrefab();
            BuildWrongWayScene(CreateMaterials(), hudPrefab);
            AssetDatabase.SaveAssets();
        }

        private static void BuildWrongWayScene(
            WrongWayMaterials materials,
            GameObject hudPrefab)
        {
            var previousActive = SceneManager.GetActiveScene();
            var previousActivePath = previousActive.path;
            var scratchScene = default(Scene);
            var loadedWrongWay =
                SceneManager.GetSceneByPath(WrongWayScenePath);

            if (loadedWrongWay.IsValid() && loadedWrongWay.isLoaded)
            {
                if (loadedWrongWay.isDirty)
                {
                    throw new InvalidOperationException(
                        "WrongWay.unity has unsaved changes. Save or discard " +
                        "them before rebuilding the generated scene.");
                }

                if (SceneManager.sceneCount == 1)
                {
                    scratchScene = EditorSceneManager.OpenScene(
                        BoardScenePath,
                        OpenSceneMode.Additive);
                    SceneManager.SetActiveScene(scratchScene);
                }

                EditorSceneManager.CloseScene(loadedWrongWay, true);
            }

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            var root = new GameObject("WrongWay Network State");
            var arenaPresentation =
                new GameObject("Arena Presentation");
            arenaPresentation.transform.SetParent(
                root.transform,
                false);

            CreateArena(arenaPresentation.transform, materials);
            CreateLighting(arenaPresentation.transform);
            CreateRaceCamera(root.transform);
            var hud = InstantiateWrongWayHud(
                hudPrefab,
                root.transform);

            // NGO only assigns a stable in-scene hash after the scene is saved
            // and registered as an enabled build scene.
            EditorSceneManager.SaveScene(
                scene,
                WrongWayScenePath);
            EnsureWrongWayInBuildSettings();

            root.AddComponent<NetworkObject>();
            root.AddComponent<NetworkWrongWayState>();
            var networkView = root.AddComponent<WrongWayNetworkView>();
            ConfigureNetworkView(networkView, hud);

            ValidateSceneContract(root);
            EditorSceneManager.SaveScene(
                scene,
                WrongWayScenePath);

            if (previousActivePath == WrongWayScenePath)
            {
                SceneManager.SetActiveScene(scene);
            }
            else if (previousActive.IsValid() &&
                     previousActive.isLoaded &&
                     previousActive.path != WrongWayScenePath)
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
            WrongWayMaterials materials)
        {
            var stairArena = new GameObject("Stair Arena");
            stairArena.transform.SetParent(parent, false);

            var outerWidth =
                NetworkWrongWayState.LaneSpacing *
                (WrongWayRules.PlayerCount - 1) +
                NetworkWrongWayState.StepWidth +
                4f;
            var groundDepth =
                WrongWayNetworkView.StartPlatformDepth +
                WrongWayNetworkView.CourseLength +
                WrongWayNetworkView.FinishPlatformDepth +
                5f;
            var groundCenterZ =
                (WrongWayNetworkView.CourseLength -
                 WrongWayNetworkView.StartPlatformDepth +
                 WrongWayNetworkView.FinishPlatformDepth) * 0.5f;

            var ground = CreateCube(
                "Backdrop Floor",
                stairArena.transform,
                new Vector3(
                    NetworkWrongWayState.ArenaCenterX,
                    -0.35f,
                    groundCenterZ),
                new Vector3(outerWidth, 0.5f, groundDepth),
                Quaternion.identity,
                materials.Ground);
            RemoveCollider(ground);

            for (var slot = 0;
                 slot < WrongWayRules.PlayerCount;
                 slot++)
            {
                CreateLane(
                    stairArena.transform,
                    slot,
                    materials.Lanes[slot],
                    materials.Finish);
            }

            CreateLaneRails(
                stairArena.transform,
                materials.Rail);
            CreateFinishArch(
                stairArena.transform,
                outerWidth - 2f,
                materials.Finish);
        }

        private static void CreateLane(
            Transform parent,
            int slot,
            Material laneMaterial,
            Material finishMaterial)
        {
            var lane = new GameObject("Lane " + (slot + 1));
            lane.transform.SetParent(parent, false);
            var laneX = WrongWayNetworkView.GetLaneX(slot);

            var startPlatform = CreateCube(
                "Start Platform",
                lane.transform,
                new Vector3(
                    laneX,
                    -0.1f,
                    -WrongWayNetworkView.StartPlatformDepth * 0.5f),
                new Vector3(
                    NetworkWrongWayState.StepWidth,
                    0.2f,
                    WrongWayNetworkView.StartPlatformDepth),
                Quaternion.identity,
                laneMaterial);
            RemoveCollider(startPlatform);

            for (var step = 1;
                 step <= WrongWayRules.StepCount;
                 step++)
            {
                var topHeight =
                    step * NetworkWrongWayState.StepHeight;
                var tread = CreateCube(
                    "Step " + step.ToString("00"),
                    lane.transform,
                    new Vector3(
                        laneX,
                        topHeight * 0.5f,
                        (step - 0.5f) *
                        NetworkWrongWayState.StepDepth),
                    new Vector3(
                        NetworkWrongWayState.StepWidth,
                        topHeight,
                        NetworkWrongWayState.StepDepth - 0.025f),
                    Quaternion.identity,
                    laneMaterial);
                RemoveCollider(tread);
            }

            var finishPlatform = CreateCube(
                "Finish Platform",
                lane.transform,
                new Vector3(
                    laneX,
                    WrongWayNetworkView.CourseHeight * 0.5f,
                    WrongWayNetworkView.CourseLength +
                    WrongWayNetworkView.FinishPlatformDepth * 0.5f),
                new Vector3(
                    NetworkWrongWayState.StepWidth,
                    WrongWayNetworkView.CourseHeight,
                    WrongWayNetworkView.FinishPlatformDepth),
                Quaternion.identity,
                finishMaterial);
            RemoveCollider(finishPlatform);
        }

        private static void CreateLaneRails(
            Transform parent,
            Material railMaterial)
        {
            var slopeDegrees = Mathf.Atan2(
                WrongWayNetworkView.CourseHeight,
                WrongWayNetworkView.CourseLength) * Mathf.Rad2Deg;
            var railLength = Mathf.Sqrt(
                WrongWayNetworkView.CourseLength *
                WrongWayNetworkView.CourseLength +
                WrongWayNetworkView.CourseHeight *
                WrongWayNetworkView.CourseHeight);

            for (var boundary = 0;
                 boundary <= WrongWayRules.PlayerCount;
                 boundary++)
            {
                var leftEdge =
                    WrongWayNetworkView.GetLaneX(0) -
                    NetworkWrongWayState.StepWidth * 0.5f;
                var x = leftEdge +
                        boundary * NetworkWrongWayState.LaneSpacing;
                var rail = CreateCube(
                    "Lane Rail " + (boundary + 1),
                    parent,
                    new Vector3(
                        x,
                        WrongWayNetworkView.CourseHeight * 0.5f + 0.42f,
                        WrongWayNetworkView.CourseLength * 0.5f),
                    new Vector3(0.085f, 0.12f, railLength),
                    Quaternion.Euler(-slopeDegrees, 0f, 0f),
                    railMaterial);
                RemoveCollider(rail);
            }
        }

        private static void CreateFinishArch(
            Transform parent,
            float width,
            Material material)
        {
            var finishRoot = new GameObject("Finish Arch");
            finishRoot.transform.SetParent(parent, false);
            var finishZ =
                WrongWayNetworkView.CourseLength +
                WrongWayNetworkView.FinishPlatformDepth * 0.65f;
            var finishTop =
                WrongWayNetworkView.CourseHeight + 3.2f;
            var halfWidth = width * 0.5f;

            var left = CreateCube(
                "Finish Arch Left",
                finishRoot.transform,
                new Vector3(
                    NetworkWrongWayState.ArenaCenterX - halfWidth,
                    WrongWayNetworkView.CourseHeight + 1.55f,
                    finishZ),
                new Vector3(0.35f, 3.1f, 0.35f),
                Quaternion.identity,
                material);
            var right = CreateCube(
                "Finish Arch Right",
                finishRoot.transform,
                new Vector3(
                    NetworkWrongWayState.ArenaCenterX + halfWidth,
                    WrongWayNetworkView.CourseHeight + 1.55f,
                    finishZ),
                new Vector3(0.35f, 3.1f, 0.35f),
                Quaternion.identity,
                material);
            var header = CreateCube(
                "Finish Arch Header",
                finishRoot.transform,
                new Vector3(
                    NetworkWrongWayState.ArenaCenterX,
                    finishTop,
                    finishZ),
                new Vector3(width + 0.35f, 0.65f, 0.45f),
                Quaternion.identity,
                material);
            RemoveCollider(left);
            RemoveCollider(right);
            RemoveCollider(header);
        }

        private static WrongWayMaterials CreateMaterials()
        {
            return new WrongWayMaterials
            {
                Ground = CreateOrUpdateMaterial(
                    "WrongWayGround",
                    new Color(0.035f, 0.045f, 0.075f)),
                Rail = CreateOrUpdateMaterial(
                    "WrongWayRail",
                    new Color(0.78f, 0.84f, 0.92f)),
                Finish = CreateOrUpdateMaterial(
                    "WrongWayFinish",
                    new Color(1f, 0.72f, 0.08f)),
                Lanes = new[]
                {
                    CreateOrUpdateMaterial(
                        "WrongWayLaneBlue",
                        new Color(0.1f, 0.38f, 0.75f)),
                    CreateOrUpdateMaterial(
                        "WrongWayLaneRed",
                        new Color(0.78f, 0.16f, 0.12f)),
                    CreateOrUpdateMaterial(
                        "WrongWayLaneGreen",
                        new Color(0.12f, 0.58f, 0.24f)),
                    CreateOrUpdateMaterial(
                        "WrongWayLanePurple",
                        new Color(0.48f, 0.2f, 0.72f))
                }
            };
        }

        private static Material CreateOrUpdateMaterial(
            string name,
            Color color)
        {
            var path = MaterialFolder + "/" + name + ".mat";
            var material =
                AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader =
                    Shader.Find("Universal Render Pipeline/Lit") ??
                    Shader.Find("Standard");
                if (shader == null)
                {
                    throw new InvalidOperationException(
                        "No supported Lit shader is available for WrongWay.");
                }

                material = new Material(shader)
                {
                    name = name
                };
                AssetDatabase.CreateAsset(material, path);
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

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CreateLighting(Transform parent)
        {
            var lightObject =
                new GameObject("WrongWay Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation =
                Quaternion.Euler(48f, -32f, 0f);

            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.color = new Color(1f, 0.95f, 0.88f);
        }

        private static void CreateRaceCamera(Transform parent)
        {
            var cameraObject =
                new GameObject("CM_WrongWayRace");
            cameraObject.transform.SetParent(parent, false);

            var focus =
                WrongWayNetworkView.CalculateCameraFocus(0);
            cameraObject.transform.SetPositionAndRotation(
                WrongWayNetworkView.CalculateCameraPosition(focus),
                WrongWayNetworkView.CalculateCameraRotation(focus));

            var camera =
                cameraObject.AddComponent<CinemachineCamera>();
            camera.Priority = 0;
            var lens = camera.Lens;
            lens.ModeOverride =
                LensSettings.OverrideModes.Perspective;
            lens.FieldOfView =
                WrongWayNetworkView.CameraFieldOfView;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 150f;
            camera.Lens = lens;
        }

        private static GameObject CreateCube(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Quaternion rotation,
            Material material)
        {
            var cube =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.SetPositionAndRotation(
                position,
                rotation);
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }

        private static void RemoveCollider(GameObject target)
        {
            var collider = target.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
        }

        private static GameObject LoadOrCreateWrongWayHudPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(
                WrongWayHudPrefabPath);
            if (existing != null)
            {
                ValidateWrongWayHudPrefab(existing);
                return existing;
            }

            var template = CreateWrongWayHudTemplate();
            try
            {
                var prefab = PrefabUtility.SaveAsPrefabAsset(
                    template,
                    WrongWayHudPrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        "WrongWayHud.prefab could not be created.");
                }

                ValidateWrongWayHudPrefab(prefab);
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(template);
            }
        }

        private static GameObject CreateWrongWayHudTemplate()
        {
            var font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            if (font == null)
            {
                throw new InvalidOperationException(
                    "Unity built-in LegacyRuntime.ttf font could not be loaded.");
            }

            var canvasObject = new GameObject(
                "WrongWay HUD",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.transform.localScale = Vector3.one;
            SetUiLayer(canvasObject);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 45;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = new GameObject(
                "WrongWay HUD Panel",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            SetUiLayer(panel);
            panel.transform.SetParent(canvasObject.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 1f);
            panelRect.anchorMax = new Vector2(0.5f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.anchoredPosition = new Vector2(0f, -22f);
            panelRect.sizeDelta = new Vector2(1120f, 330f);
            var panelImage = panel.GetComponent<Image>();
            panelImage.color =
                new Color(0.025f, 0.035f, 0.07f, 0.88f);
            panelImage.raycastTarget = false;

            var phaseText = CreateHudText(
                "Phase",
                panel.transform,
                font,
                new Vector2(0f, -16f),
                new Vector2(1060f, 44f),
                30,
                TextAnchor.MiddleCenter,
                FontStyle.Bold);
            var instructionText = CreateHudText(
                "Instruction",
                panel.transform,
                font,
                new Vector2(0f, -58f),
                new Vector2(1060f, 34f),
                19,
                TextAnchor.MiddleCenter,
                FontStyle.Normal);
            instructionText.text =
                "W A S D  ·  Match the shown direction and climb 50 steps";
            var promptText = CreateHudText(
                "Local Prompt",
                panel.transform,
                font,
                new Vector2(0f, -115f),
                new Vector2(1060f, 78f),
                50,
                TextAnchor.MiddleCenter,
                FontStyle.Bold);

            var playerColors = new[]
            {
                new Color(0.18f, 0.62f, 1f),
                new Color(1f, 0.32f, 0.24f),
                new Color(0.25f, 0.86f, 0.42f),
                new Color(0.72f, 0.38f, 1f)
            };
            var progressRows = new Text[WrongWayRules.PlayerCount];
            for (var slot = 0; slot < progressRows.Length; slot++)
            {
                progressRows[slot] = CreateHudText(
                    "Player " + (slot + 1) + " Progress",
                    panel.transform,
                    font,
                    new Vector2(0f, -194f - slot * 29f),
                    new Vector2(1000f, 28f),
                    19,
                    TextAnchor.MiddleLeft,
                    FontStyle.Bold);
                progressRows[slot].color = playerColors[slot];
            }

            var bindings =
                canvasObject.AddComponent<WrongWayHudBindings>();
            bindings.Configure(
                canvas,
                phaseText,
                instructionText,
                promptText,
                progressRows);
            canvasObject.SetActive(false);
            return canvasObject;
        }

        private static Text CreateHudText(
            string name,
            Transform parent,
            Font font,
            Vector2 anchoredPosition,
            Vector2 size,
            int fontSize,
            TextAnchor alignment,
            FontStyle style)
        {
            var textObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            SetUiLayer(textObject);
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
            return text;
        }

        private static void SetUiLayer(GameObject target)
        {
            var uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
            {
                target.layer = uiLayer;
            }
        }

        private static void ValidateWrongWayHudPrefab(GameObject prefab)
        {
            var bindings = prefab.GetComponent<WrongWayHudBindings>();
            if (bindings == null || !bindings.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "WrongWayHud.prefab is missing its required bindings. " +
                    "Repair the prefab explicitly instead of rebuilding over " +
                    "designer-authored UI.");
            }
        }

        private static WrongWayHudBindings InstantiateWrongWayHud(
            GameObject hudPrefab,
            Transform parent)
        {
            var instance = PrefabUtility.InstantiatePrefab(
                hudPrefab,
                parent) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "WrongWayHud.prefab could not be instantiated.");
            }

            instance.transform.SetLocalPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            instance.transform.localScale = hudPrefab.transform.localScale;
            var bindings = instance.GetComponent<WrongWayHudBindings>();
            if (bindings == null || !bindings.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "WrongWay HUD instance has incomplete bindings.");
            }

            return bindings;
        }

        private static void ConfigureNetworkView(
            WrongWayNetworkView view,
            WrongWayHudBindings hud)
        {
            var serializedView = new SerializedObject(view);
            var hudProperty = serializedView.FindProperty("hud");
            if (hudProperty == null)
            {
                throw new InvalidOperationException(
                    "WrongWayNetworkView no longer exposes its HUD contract.");
            }

            hudProperty.objectReferenceValue = hud;
            serializedView.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ValidateSceneContract(GameObject root)
        {
            var networkView = root.GetComponent<WrongWayNetworkView>();
            var hud = root.GetComponentInChildren<WrongWayHudBindings>(true);
            if (root.GetComponent<NetworkObject>() == null ||
                root.GetComponent<NetworkWrongWayState>() == null ||
                networkView == null ||
                FindDescendant(
                    root.transform,
                    "Arena Presentation") == null ||
                FindDescendant(
                    root.transform,
                    "Stair Arena") == null ||
                root.GetComponentInChildren<CinemachineCamera>(
                    true) == null ||
                hud == null ||
                !hud.HasRequiredReferences ||
                PrefabUtility.GetPrefabInstanceStatus(hud.gameObject) !=
                PrefabInstanceStatus.Connected)
            {
                throw new InvalidOperationException(
                    "Generated WrongWay scene is missing its network or " +
                    "presentation contract.");
            }

            var serializedView = new SerializedObject(networkView);
            if (serializedView.FindProperty("hud")?.objectReferenceValue != hud)
            {
                throw new InvalidOperationException(
                    "WrongWayNetworkView must reference the connected HUD " +
                    "prefab instance in its scene.");
            }

            for (var slot = 0;
                 slot < WrongWayRules.PlayerCount;
                 slot++)
            {
                var lane = FindDescendant(
                    root.transform,
                    "Lane " + (slot + 1));
                if (lane == null)
                {
                    throw new InvalidOperationException(
                        "Generated WrongWay scene is missing lane " +
                        (slot + 1) +
                        ".");
                }

                var stepCount = 0;
                for (var child = 0;
                     child < lane.transform.childCount;
                     child++)
                {
                    if (lane.transform.GetChild(child).name
                        .StartsWith(
                            "Step ",
                            StringComparison.Ordinal))
                    {
                        stepCount++;
                    }
                }

                if (stepCount != WrongWayRules.StepCount)
                {
                    throw new InvalidOperationException(
                        "Every WrongWay lane must contain exactly 50 steps.");
                }
            }

            if (root.GetComponentInChildren<Camera>(true) != null ||
                root.GetComponentInChildren<AudioListener>(true) != null)
            {
                throw new InvalidOperationException(
                    "WrongWay must not contain a Unity Camera or AudioListener; " +
                    "the additive Board scene owns the output camera.");
            }

            if (FindDescendant(root.transform, "Crusher") != null)
            {
                throw new InvalidOperationException(
                    "WrongWay must not include a crusher or death device.");
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

            for (var index = 0;
                 index < root.childCount;
                 index++)
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

        private static void EnsureWrongWayInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            for (var index = scenes.Count - 1;
                 index >= 0;
                 index--)
            {
                if (scenes[index].path == WrongWayScenePath)
                {
                    scenes.RemoveAt(index);
                }
            }

            var insertAfter = FindSceneIndex(
                scenes,
                MinefieldScenePath);
            if (insertAfter < 0)
            {
                insertAfter = FindSceneIndex(
                    scenes,
                    BoardScenePath);
            }

            var targetIndex = insertAfter >= 0
                ? insertAfter + 1
                : scenes.Count;
            scenes.Insert(
                targetIndex,
                new EditorBuildSettingsScene(
                    WrongWayScenePath,
                    true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static int FindSceneIndex(
            IReadOnlyList<EditorBuildSettingsScene> scenes,
            string path)
        {
            for (var index = 0;
                 index < scenes.Count;
                 index++)
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
            EnsureFolder(WrongWayArtFolder);
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

        private sealed class WrongWayMaterials
        {
            public Material Ground;
            public Material Rail;
            public Material Finish;
            public Material[] Lanes;
        }
    }
}
