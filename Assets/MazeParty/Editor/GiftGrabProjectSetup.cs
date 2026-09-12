using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.GiftGrab;
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
    /// Builds Gift Grab's additive production arena and its prefab-owned UI.
    /// Existing designer prefabs and prototype materials are never overwritten.
    /// </summary>
    public static class GiftGrabProjectSetup
    {
        private const string MenuPath =
            "MazeParty/Minigames/Rebuild Gift Grab";
        private const string ProjectRoot = "Assets/MazeParty";
        private const string ScenesFolder = ProjectRoot + "/Scenes";
        private const string UiPrefabFolder = ProjectRoot + "/UI/Prefabs";
        private const string MaterialFolder =
            ProjectRoot + "/Art/Minigames/GiftGrab/Materials";
        private const string BalloonBlowScenePath =
            ScenesFolder + "/BalloonBlow.unity";

        public const string GiftGrabScenePath =
            ScenesFolder + "/GiftGrab.unity";
        public const string HudPrefabPath =
            UiPrefabFolder + "/GiftGrabHud.prefab";
        public const string BaseLabelPrefabPath =
            UiPrefabFolder + "/GiftGrabBaseLabel.prefab";

        private static readonly Color[] PlayerColors =
        {
            new Color(0.16f, 0.48f, 0.95f),
            new Color(0.92f, 0.2f, 0.16f),
            new Color(0.18f, 0.78f, 0.32f),
            new Color(0.7f, 0.26f, 0.9f)
        };

        [MenuItem(MenuPath)]
        public static void RebuildGiftGrab()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log(
                    "Gift Grab rebuild canceled; open scene changes were " +
                    "left untouched.");
                return;
            }

            BuildGiftGrabAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var scene = SceneManager.GetSceneByPath(GiftGrabScenePath);
            if (scene.IsValid() && scene.isLoaded)
            {
                SceneManager.SetActiveScene(scene);
            }
            else
            {
                EditorSceneManager.OpenScene(
                    GiftGrabScenePath,
                    OpenSceneMode.Single);
            }

            Debug.Log(
                "Gift Grab rebuilt: symmetric arena, 19 reusable gift " +
                "presentations, prefab HUD and prefab world labels. Existing " +
                "designer prefab styling was preserved.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRebuildGiftGrab()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static void BuildGiftGrabAssets()
        {
            EnsureFolders();
            var materials = CreateMaterials();
            var hudPrefab = LoadOrCreateHudPrefab();
            var labelPrefab = LoadOrCreateBaseLabelPrefab(materials);
            BuildGiftGrabScene(materials, hudPrefab, labelPrefab);
            AssetDatabase.SaveAssets();
        }

        private static void BuildGiftGrabScene(
            GiftGrabMaterials materials,
            GameObject hudPrefab,
            GameObject labelPrefab)
        {
            var previousActive = SceneManager.GetActiveScene();
            var previousActivePath = previousActive.path;
            var loaded = SceneManager.GetSceneByPath(GiftGrabScenePath);
            var replaceSingleOpenScene =
                SceneManager.sceneCount == 1 &&
                ((loaded.IsValid() && loaded.isLoaded) ||
                 string.IsNullOrEmpty(previousActivePath));

            if (loaded.IsValid() && loaded.isLoaded)
            {
                if (loaded.isDirty)
                {
                    throw new InvalidOperationException(
                        "GiftGrab.unity has unsaved changes. Save or discard " +
                        "them before rebuilding the generated scene.");
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

            var root = new GameObject("Gift Grab Network State");
            var arenaPresentation = new GameObject("Arena Presentation");
            arenaPresentation.transform.SetParent(root.transform, false);
            var arena = CreateArena(
                arenaPresentation.transform,
                materials,
                labelPrefab);
            CreateLighting(arenaPresentation.transform);
            var sharedCamera = CreateSharedCamera(root.transform);

            var runtimePlayers = new GameObject("Runtime Players");
            runtimePlayers.transform.SetParent(root.transform, false);

            var audioAnchor = new GameObject("Audio Replacement Anchor");
            audioAnchor.transform.SetParent(root.transform, false);
            audioAnchor.transform.position = new Vector3(
                NetworkGiftGrabState.ArenaCenterX,
                2f,
                0f);
            var audioSource = audioAnchor.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;

            CreateReplacementAnchors(root.transform);

            // Give Netcode's in-scene NetworkObject a stable scene identity.
            EditorSceneManager.SaveScene(scene, GiftGrabScenePath);
            root.AddComponent<NetworkObject>();
            var state = root.AddComponent<NetworkGiftGrabState>();
            var view = root.AddComponent<GiftGrabNetworkView>();

            var hudObject = PrefabUtility.InstantiatePrefab(
                hudPrefab,
                root.transform) as GameObject;
            var hud = hudObject != null
                ? hudObject.GetComponent<GiftGrabHudBindings>()
                : null;
            if (hud == null || !hud.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "GiftGrabHud.prefab could not be instantiated with its " +
                    "required serialized bindings.");
            }

            view.Configure(
                state,
                sharedCamera,
                runtimePlayers.transform,
                arena.GiftRoot,
                arena.PlayerAnchors,
                arena.BaseAnchors,
                arena.DepositedGiftAnchors,
                arena.PlayerLabels,
                arena.BaseLabels,
                arena.PushVfx,
                arena.ThrowVfx,
                arena.DropVfx,
                arena.StunVfx,
                arenaPresentation,
                audioSource,
                hud);

            ValidateSceneContract(root);
            EditorSceneManager.SaveScene(scene, GiftGrabScenePath);
            EnsureGiftGrabInBuildSettings();

            if (previousActivePath == GiftGrabScenePath)
            {
                SceneManager.SetActiveScene(scene);
            }
            else if (previousActive.IsValid() &&
                     previousActive.isLoaded &&
                     previousActive.path != GiftGrabScenePath)
            {
                SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static ArenaReferences CreateArena(
            Transform parent,
            GiftGrabMaterials materials,
            GameObject labelPrefab)
        {
            var centerX = NetworkGiftGrabState.ArenaCenterX;
            CreatePrimitive(
                "Arena Floor",
                PrimitiveType.Cube,
                parent,
                new Vector3(centerX, -0.45f, 0f),
                Quaternion.identity,
                new Vector3(18f, 0.8f, 18f),
                materials.Floor,
                true);
            CreatePrimitive(
                "North Boundary",
                PrimitiveType.Cube,
                parent,
                new Vector3(centerX, 0.15f, 8.7f),
                Quaternion.identity,
                new Vector3(18.6f, 0.55f, 0.35f),
                materials.Trim,
                false);
            CreatePrimitive(
                "South Boundary",
                PrimitiveType.Cube,
                parent,
                new Vector3(centerX, 0.15f, -8.7f),
                Quaternion.identity,
                new Vector3(18.6f, 0.55f, 0.35f),
                materials.Trim,
                false);
            CreatePrimitive(
                "West Boundary",
                PrimitiveType.Cube,
                parent,
                new Vector3(centerX - 8.7f, 0.15f, 0f),
                Quaternion.identity,
                new Vector3(0.35f, 0.55f, 18.6f),
                materials.Trim,
                false);
            CreatePrimitive(
                "East Boundary",
                PrimitiveType.Cube,
                parent,
                new Vector3(centerX + 8.7f, 0.15f, 0f),
                Quaternion.identity,
                new Vector3(0.35f, 0.55f, 18.6f),
                materials.Trim,
                false);

            var playerRoot = new GameObject("Player Anchors").transform;
            playerRoot.SetParent(parent, false);
            var baseRoot = new GameObject("Base Anchors").transform;
            baseRoot.SetParent(parent, false);
            var depositRoot = new GameObject(
                "Deposited Gift Display Anchors").transform;
            depositRoot.SetParent(parent, false);
            var playerLabelRoot = new GameObject(
                "Player Label Anchors").transform;
            playerLabelRoot.SetParent(parent, false);
            var baseLabelRoot = new GameObject(
                "Base Label Anchors").transform;
            baseLabelRoot.SetParent(parent, false);

            var playerAnchors = new Transform[GiftGrabRules.PlayerCount];
            var baseAnchors = new Transform[GiftGrabRules.PlayerCount];
            var depositAnchors = new Transform[GiftGrabRules.PlayerCount];
            var playerLabels = new GiftGrabBaseLabel[GiftGrabRules.PlayerCount];
            var baseLabels = new GiftGrabBaseLabel[GiftGrabRules.PlayerCount];
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                var basePosition = NetworkGiftGrabState.GetBaseCenter(slot);
                var startPosition =
                    NetworkGiftGrabState.GetPlayerStartPosition(slot);
                var baseWorld = new Vector3(
                    basePosition.x,
                    0.02f,
                    basePosition.y);

                var baseAnchor = new GameObject(
                    "Base Anchor " + (slot + 1)).transform;
                baseAnchor.SetParent(baseRoot, false);
                baseAnchor.position = baseWorld;
                baseAnchors[slot] = baseAnchor;
                CreatePrimitive(
                    "Base Pad " + (slot + 1),
                    PrimitiveType.Cylinder,
                    baseAnchor,
                    Vector3.zero,
                    Quaternion.identity,
                    new Vector3(
                        NetworkGiftGrabState.BaseRadius * 2f,
                        0.12f,
                        NetworkGiftGrabState.BaseRadius * 2f),
                    materials.Bases[slot],
                    false);
                CreatePrimitive(
                    "Base Rim " + (slot + 1),
                    PrimitiveType.Cylinder,
                    baseAnchor,
                    new Vector3(0f, -0.04f, 0f),
                    Quaternion.identity,
                    new Vector3(
                        NetworkGiftGrabState.BaseRadius * 2f + 0.4f,
                        0.06f,
                        NetworkGiftGrabState.BaseRadius * 2f + 0.4f),
                    materials.Highlight,
                    false);

                var playerAnchor = new GameObject(
                    "Player Anchor " + (slot + 1)).transform;
                playerAnchor.SetParent(playerRoot, false);
                playerAnchor.position = new Vector3(
                    startPosition.x,
                    GiftGrabNetworkView.PlayerPresentationHeight,
                    startPosition.y);
                playerAnchors[slot] = playerAnchor;

                var deposit = new GameObject(
                    "Stored Gift Display " + (slot + 1)).transform;
                deposit.SetParent(depositRoot, false);
                var towardCenter = new Vector3(
                    centerX - baseWorld.x,
                    0f,
                    -baseWorld.z).normalized;
                deposit.position = baseWorld + towardCenter * 0.5f;
                depositAnchors[slot] = deposit;

                playerLabels[slot] = InstantiateLabel(
                    labelPrefab,
                    playerLabelRoot,
                    "Player Label " + (slot + 1),
                    playerAnchor.position + Vector3.up * 2.9f);
                baseLabels[slot] = InstantiateLabel(
                    labelPrefab,
                    baseLabelRoot,
                    "Base Label " + (slot + 1),
                    baseWorld + new Vector3(0f, 0.7f, 0f));
            }

            var giftRoot = new GameObject("Gift Anchors").transform;
            giftRoot.SetParent(parent, false);
            for (var giftId = 0;
                 giftId < GiftGrabRules.TotalGiftCount;
                 giftId++)
            {
                var anchor = new GameObject(
                    "Gift Anchor " + giftId.ToString("00")).transform;
                anchor.SetParent(giftRoot, false);
                var ring = giftId < GiftGrabRules.InitialGiftCount ? 3.1f : 4.6f;
                var angle = giftId * Mathf.PI * 2f /
                            GiftGrabRules.TotalGiftCount;
                anchor.position = new Vector3(
                    centerX + Mathf.Cos(angle) * ring,
                    GiftGrabNetworkView.GiftPresentationHeight,
                    Mathf.Sin(angle) * ring);
                CreateGiftVisual(anchor, materials);
            }

            var effects = CreateVfx(parent, materials);
            return new ArenaReferences
            {
                PlayerAnchors = playerAnchors,
                BaseAnchors = baseAnchors,
                DepositedGiftAnchors = depositAnchors,
                PlayerLabels = playerLabels,
                BaseLabels = baseLabels,
                GiftRoot = giftRoot,
                PushVfx = effects.Push,
                ThrowVfx = effects.Throw,
                DropVfx = effects.Drop,
                StunVfx = effects.Stun
            };
        }

        private static void CreateGiftVisual(
            Transform anchor,
            GiftGrabMaterials materials)
        {
            var visual = new GameObject("Gift Visual").transform;
            visual.SetParent(anchor, false);
            CreatePrimitive(
                "Gift Box",
                PrimitiveType.Cube,
                visual,
                Vector3.zero,
                Quaternion.identity,
                new Vector3(0.7f, 0.55f, 0.7f),
                materials.GiftWrap,
                false);
            CreatePrimitive(
                "Ribbon X",
                PrimitiveType.Cube,
                visual,
                new Vector3(0f, 0.01f, 0f),
                Quaternion.identity,
                new Vector3(0.12f, 0.58f, 0.73f),
                materials.GiftRibbon,
                false);
            CreatePrimitive(
                "Ribbon Z",
                PrimitiveType.Cube,
                visual,
                new Vector3(0f, 0.01f, 0f),
                Quaternion.identity,
                new Vector3(0.73f, 0.58f, 0.12f),
                materials.GiftRibbon,
                false);
            CreatePrimitive(
                "Bow Left",
                PrimitiveType.Sphere,
                visual,
                new Vector3(-0.16f, 0.38f, 0f),
                Quaternion.identity,
                new Vector3(0.26f, 0.18f, 0.2f),
                materials.GiftRibbon,
                false);
            CreatePrimitive(
                "Bow Right",
                PrimitiveType.Sphere,
                visual,
                new Vector3(0.16f, 0.38f, 0f),
                Quaternion.identity,
                new Vector3(0.26f, 0.18f, 0.2f),
                materials.GiftRibbon,
                false);
        }

        private static VfxReferences CreateVfx(
            Transform parent,
            GiftGrabMaterials materials)
        {
            var root = new GameObject("VFX Replacement Anchors").transform;
            root.SetParent(parent, false);
            var push = CreateEffect(
                root,
                "Push VFX Anchor",
                PrimitiveType.Cylinder,
                materials.PushVfx,
                new Vector3(1.4f, 0.16f, 1.4f));
            var thrown = CreateEffect(
                root,
                "Throw VFX Anchor",
                PrimitiveType.Sphere,
                materials.ThrowVfx,
                new Vector3(0.7f, 0.12f, 0.7f));
            var drop = CreateEffect(
                root,
                "Drop VFX Anchor",
                PrimitiveType.Cylinder,
                materials.DropVfx,
                new Vector3(0.9f, 0.08f, 0.9f));
            var stun = new Transform[GiftGrabRules.PlayerCount];
            for (var slot = 0; slot < stun.Length; slot++)
            {
                stun[slot] = CreateEffect(
                    root,
                    "Stun VFX " + (slot + 1),
                    PrimitiveType.Cylinder,
                    materials.StunVfx,
                    new Vector3(0.95f, 0.12f, 0.95f));
            }
            return new VfxReferences
            {
                Push = push,
                Throw = thrown,
                Drop = drop,
                Stun = stun
            };
        }

        private static Transform CreateEffect(
            Transform parent,
            string name,
            PrimitiveType primitive,
            Material material,
            Vector3 scale)
        {
            var anchor = new GameObject(name).transform;
            anchor.SetParent(parent, false);
            CreatePrimitive(
                "Prototype Effect",
                primitive,
                anchor,
                Vector3.zero,
                Quaternion.identity,
                scale,
                material,
                false);
            anchor.gameObject.SetActive(false);
            return anchor;
        }

        private static GiftGrabBaseLabel InstantiateLabel(
            GameObject prefab,
            Transform parent,
            string name,
            Vector3 position)
        {
            var instance = PrefabUtility.InstantiatePrefab(
                prefab,
                parent) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "Could not instantiate GiftGrabBaseLabel.prefab.");
            }
            instance.name = name;
            instance.transform.position = position;
            instance.transform.rotation =
                GiftGrabNetworkView.SharedCameraRotation;
            var label = instance.GetComponent<GiftGrabBaseLabel>();
            if (label == null || !label.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Gift Grab world-label prefab bindings are invalid.");
            }
            return label;
        }

        private static void CreateLighting(Transform parent)
        {
            var lightObject = new GameObject("Gift Grab Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(52f, -34f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.94f, 0.84f);
        }

        private static CinemachineCamera CreateSharedCamera(Transform parent)
        {
            var cameraObject = new GameObject("CM_GiftGrabShared");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.SetPositionAndRotation(
                GiftGrabNetworkView.SharedCameraPosition,
                GiftGrabNetworkView.SharedCameraRotation);
            var camera = cameraObject.AddComponent<CinemachineCamera>();
            camera.Priority = 0;
            var lens = camera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize =
                GiftGrabNetworkView.SharedCameraOrthographicSize;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 80f;
            camera.Lens = lens;
            return camera;
        }

        private static void CreateReplacementAnchors(Transform parent)
        {
            var root = new GameObject("Art Replacement Anchors").transform;
            root.SetParent(parent, false);
            new GameObject("Arena Art Anchor").transform.SetParent(root, false);
            new GameObject("Gift Art Anchor").transform.SetParent(root, false);
            new GameObject("Base Art Anchor").transform.SetParent(root, false);
            new GameObject("Player Carry Art Anchor").transform.SetParent(
                root,
                false);
        }

        private static GameObject LoadOrCreateHudPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
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
                ? prefab.GetComponent<GiftGrabHudBindings>()
                : null;
            if (binding == null || !binding.HasRequiredReferences ||
                prefab.transform.localScale != Vector3.one)
            {
                throw new InvalidOperationException(
                    "GiftGrabHud.prefab must have complete serialized " +
                    "bindings and a renderable unit root scale. Repair the " +
                    "existing prefab directly; setup will not overwrite it.");
            }
            return prefab;
        }

        private static GameObject LoadOrCreateBaseLabelPrefab(
            GiftGrabMaterials materials)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                BaseLabelPrefabPath);
            if (prefab == null)
            {
                var template = CreateBaseLabelTemplate(materials);
                try
                {
                    prefab = PrefabUtility.SaveAsPrefabAsset(
                        template,
                        BaseLabelPrefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(template);
                }
            }

            var binding = prefab != null
                ? prefab.GetComponent<GiftGrabBaseLabel>()
                : null;
            if (binding == null || !binding.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "GiftGrabBaseLabel.prefab is missing its serialized " +
                    "binding contract. Repair it without rebuilding it.");
            }
            return prefab;
        }

        private static GameObject CreateHudTemplate()
        {
            var font = RequireBuiltinFont();
            var root = new GameObject(
                "GiftGrabHud",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GiftGrabHudBindings));
            root.transform.localScale = Vector3.one;
            var rect = root.GetComponent<RectTransform>();
            rect.localScale = Vector3.one;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 45;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var header = CreatePanel(
                "Header Panel",
                root.transform,
                new Vector2(0.5f, 1f),
                new Vector2(0f, -18f),
                new Vector2(900f, 214f),
                new Color(0.055f, 0.025f, 0.09f, 0.92f));
            var phase = CreateHudText(
                "Phase", header.transform, font,
                new Vector2(0f, -10f), new Vector2(850f, 34f), 22,
                FontStyle.Bold, "GIFT GRAB · GET READY");
            var timer = CreateHudText(
                "Timer", header.transform, font,
                new Vector2(-190f, -46f), new Vector2(230f, 48f), 38,
                FontStyle.Bold, "01:00");
            var round = CreateHudText(
                "Round", header.transform, font,
                new Vector2(190f, -52f), new Vector2(230f, 34f), 18,
                FontStyle.Bold, "ROUND 1 / 2");
            var instruction = CreateHudText(
                "Instructions", header.transform, font,
                new Vector2(0f, -101f), new Vector2(850f, 40f), 16,
                FontStyle.Bold,
                "GRAB GIFTS · PROTECT YOUR BASE · PUSH WITH LEFT CLICK");
            var localStatus = CreateHudText(
                "Local Status", header.transform, font,
                new Vector2(0f, -150f), new Vector2(850f, 44f), 15,
                FontStyle.Normal,
                "YOU · 0 STORED · HANDS FREE · STUN 0.0s · ACTION 0.0s");

            var neutralGift = CreateHudText(
                "Loose Gift Count", root.transform, font,
                new Vector2(-28f, -26f), new Vector2(300f, 42f), 18,
                FontStyle.Bold, "LOOSE GIFTS  10");
            var neutralRect = neutralGift.rectTransform;
            neutralRect.anchorMin = neutralRect.anchorMax = Vector2.one;
            neutralRect.pivot = Vector2.one;

            var rows = new Text[GiftGrabRules.PlayerCount];
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                var x = -570f + slot * 380f;
                var card = CreatePanel(
                    "Player " + (slot + 1) + " Card",
                    root.transform,
                    new Vector2(0.5f, 0f),
                    new Vector2(x, 22f),
                    new Vector2(350f, 72f),
                    new Color(0.025f, 0.032f, 0.052f, 0.92f));
                rows[slot] = CreateHudText(
                    "Player " + (slot + 1) + " Row",
                    card.transform,
                    font,
                    new Vector2(0f, -12f),
                    new Vector2(320f, 46f),
                    15,
                    FontStyle.Bold,
                    "PLAYER " + (slot + 1) + " · 0 STORED");
                rows[slot].color = PlayerColors[slot];
            }

            var controls = CreatePanel(
                "Controls Panel", root.transform,
                new Vector2(0f, 1f), new Vector2(20f, -20f),
                new Vector2(330f, 104f),
                new Color(0.025f, 0.032f, 0.052f, 0.88f));
            CreateHudText(
                "Controls", controls.transform, font,
                new Vector2(0f, -10f), new Vector2(300f, 78f), 16,
                FontStyle.Bold,
                "WASD · MOVE + AUTO PICKUP\nLEFT CLICK · THROW / PUSH");

            var pause = CreatePanel(
                "Pause Panel", root.transform,
                new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(720f, 180f),
                new Color(0.02f, 0.02f, 0.04f, 0.97f));
            CreateHudText(
                "Pause Message", pause.transform, font,
                new Vector2(0f, -24f), new Vector2(680f, 130f), 28,
                FontStyle.Bold, "PLAYER DISCONNECTED\nMATCH PAUSED");
            pause.SetActive(false);

            var resultPanel = CreatePanel(
                "Result Panel", root.transform,
                new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(740f, 230f),
                new Color(0.02f, 0.02f, 0.04f, 0.97f));
            var result = CreateHudText(
                "Result Message", resultPanel.transform, font,
                new Vector2(0f, -28f), new Vector2(700f, 180f), 30,
                FontStyle.Bold, "ROUND RESULTS");
            resultPanel.SetActive(false);

            root.GetComponent<GiftGrabHudBindings>().Configure(
                canvas,
                phase,
                timer,
                round,
                instruction,
                localStatus,
                neutralGift,
                rows,
                result,
                pause,
                controls,
                resultPanel,
                new Color(1f, 0.88f, 0.25f, 1f));
            root.transform.localScale = Vector3.one;
            return root;
        }

        private static GameObject CreateBaseLabelTemplate(
            GiftGrabMaterials materials)
        {
            var font = RequireBuiltinFont();
            var root = new GameObject(
                "GiftGrabBaseLabel",
                typeof(GiftGrabBaseLabel));
            var highlight = CreatePrimitive(
                "Local Highlight",
                PrimitiveType.Cube,
                root.transform,
                new Vector3(0f, 0f, 0.04f),
                Quaternion.identity,
                new Vector3(3.05f, 1.02f, 0.035f),
                materials.Highlight,
                false);
            CreatePrimitive(
                "Label Backing",
                PrimitiveType.Cube,
                root.transform,
                Vector3.zero,
                Quaternion.identity,
                new Vector3(2.82f, 0.82f, 0.06f),
                materials.LabelBack,
                false);
            var swatch = CreatePrimitive(
                "Owner Swatch",
                PrimitiveType.Cube,
                root.transform,
                new Vector3(-1.22f, 0f, -0.05f),
                Quaternion.identity,
                new Vector3(0.16f, 0.62f, 0.035f),
                materials.Highlight,
                false);
            var title = CreateWorldText(
                "Title", root.transform, font,
                new Vector3(0.06f, 0.18f, -0.055f),
                58, 0.047f, "PLAYER 1 BASE");
            var detail = CreateWorldText(
                "Detail", root.transform, font,
                new Vector3(0.06f, -0.17f, -0.055f),
                52, 0.04f, "0 GIFTS");
            highlight.SetActive(false);
            root.GetComponent<GiftGrabBaseLabel>().Configure(
                title,
                detail,
                swatch.GetComponent<Renderer>(),
                highlight.GetComponent<Renderer>());
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
            FontStyle style,
            string sample)
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
            rect.anchoredPosition = position;
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
            text.text = sample;
            return text;
        }

        private static TextMesh CreateWorldText(
            string name,
            Transform parent,
            Font font,
            Vector3 position,
            int fontSize,
            float characterSize,
            string sample)
        {
            var textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);
            textObject.transform.localPosition = position;
            var text = textObject.AddComponent<TextMesh>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.characterSize = characterSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            text.text = sample;
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
                    "Unity built-in LegacyRuntime.ttf could not be loaded.");
            }
            return font;
        }

        private static GameObject CreatePrimitive(
            string name,
            PrimitiveType type,
            Transform parent,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            Material material,
            bool keepCollider)
        {
            var gameObject = GameObject.CreatePrimitive(type);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = position;
            gameObject.transform.localRotation = rotation;
            gameObject.transform.localScale = scale;
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

        private static GiftGrabMaterials CreateMaterials()
        {
            var bases = new Material[GiftGrabRules.PlayerCount];
            for (var slot = 0; slot < bases.Length; slot++)
            {
                bases[slot] = CreateOrLoadMaterial(
                    "GiftGrabBase" + (slot + 1),
                    Color.Lerp(PlayerColors[slot], Color.black, 0.25f));
            }
            return new GiftGrabMaterials
            {
                Floor = CreateOrLoadMaterial(
                    "GiftGrabFloor", new Color(0.075f, 0.12f, 0.16f)),
                Trim = CreateOrLoadMaterial(
                    "GiftGrabTrim", new Color(0.86f, 0.53f, 0.14f)),
                GiftWrap = CreateOrLoadMaterial(
                    "GiftGrabGiftWrap", new Color(0.93f, 0.16f, 0.29f)),
                GiftRibbon = CreateOrLoadMaterial(
                    "GiftGrabGiftRibbon", new Color(1f, 0.85f, 0.22f),
                    new Color(0.18f, 0.1f, 0.01f)),
                LabelBack = CreateOrLoadMaterial(
                    "GiftGrabLabelBack", new Color(0.025f, 0.03f, 0.055f)),
                Highlight = CreateOrLoadMaterial(
                    "GiftGrabHighlight", new Color(1f, 0.82f, 0.14f),
                    new Color(0.34f, 0.18f, 0.01f)),
                PushVfx = CreateOrLoadMaterial(
                    "GiftGrabPushVfx", new Color(1f, 0.32f, 0.18f),
                    new Color(0.32f, 0.03f, 0.01f)),
                ThrowVfx = CreateOrLoadMaterial(
                    "GiftGrabThrowVfx", new Color(0.28f, 0.84f, 1f),
                    new Color(0.02f, 0.2f, 0.32f)),
                DropVfx = CreateOrLoadMaterial(
                    "GiftGrabDropVfx", new Color(1f, 0.8f, 0.18f),
                    new Color(0.26f, 0.12f, 0.01f)),
                StunVfx = CreateOrLoadMaterial(
                    "GiftGrabStunVfx", new Color(0.92f, 0.28f, 1f),
                    new Color(0.25f, 0.02f, 0.3f)),
                Bases = bases
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
                    "Universal Render Pipeline/Lit is required for Gift Grab " +
                    "prototype materials.");
            }
            var path = MaterialFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
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
                material.SetFloat("_Smoothness", 0.28f);
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
            var players = FindDescendant(root.transform, "Player Anchors");
            var bases = FindDescendant(root.transform, "Base Anchors");
            var deposits = FindDescendant(
                root.transform,
                "Deposited Gift Display Anchors");
            var gifts = FindDescendant(root.transform, "Gift Anchors");
            var hud = root.GetComponentInChildren<GiftGrabHudBindings>(true);
            var labels = root.GetComponentsInChildren<GiftGrabBaseLabel>(true);
            if (root.GetComponent<NetworkObject>() == null ||
                root.GetComponent<NetworkGiftGrabState>() == null ||
                root.GetComponent<GiftGrabNetworkView>() == null ||
                players == null || players.childCount != GiftGrabRules.PlayerCount ||
                bases == null || bases.childCount != GiftGrabRules.PlayerCount ||
                deposits == null ||
                deposits.childCount != GiftGrabRules.PlayerCount ||
                gifts == null || gifts.childCount != GiftGrabRules.TotalGiftCount ||
                labels.Length != GiftGrabRules.PlayerCount * 2 ||
                root.GetComponentInChildren<CinemachineCamera>(true) == null ||
                root.GetComponentInChildren<AudioSource>(true) == null ||
                FindDescendant(root.transform, "Art Replacement Anchors") == null ||
                FindDescendant(root.transform, "VFX Replacement Anchors") == null ||
                hud == null)
            {
                throw new InvalidOperationException(
                    "Generated Gift Grab scene is missing its state, arena, " +
                    "gift, label, camera, audio, VFX, art or HUD contract.");
            }
            if (!hud.HasRequiredReferences ||
                hud.transform.localScale != Vector3.one ||
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    hud.gameObject) != HudPrefabPath)
            {
                throw new InvalidOperationException(
                    "Gift Grab HUD must remain a renderable configured prefab " +
                    "instance with unit root scale.");
            }
            foreach (var label in labels)
            {
                if (!label.HasRequiredReferences ||
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        label.gameObject) != BaseLabelPrefabPath)
                {
                    throw new InvalidOperationException(
                        "Every Gift Grab visible world label must remain a " +
                        "configured prefab instance.");
                }
            }
            if (root.GetComponentInChildren<Camera>(true) != null ||
                root.GetComponentInChildren<AudioListener>(true) != null)
            {
                throw new InvalidOperationException(
                    "Gift Grab must reuse Board's output Camera and " +
                    "AudioListener.");
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
            EnsureFolder(ProjectRoot + "/Art/Minigames/GiftGrab");
            EnsureFolder(MaterialFolder);
        }

        private static void EnsureGiftGrabInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            for (var index = scenes.Count - 1; index >= 0; index--)
            {
                if (scenes[index].path == GiftGrabScenePath)
                {
                    scenes.RemoveAt(index);
                }
            }
            var insertAfter = -1;
            for (var index = 0; index < scenes.Count; index++)
            {
                if (scenes[index].path == BalloonBlowScenePath)
                {
                    insertAfter = index;
                    break;
                }
            }
            scenes.Insert(
                insertAfter >= 0 ? insertAfter + 1 : scenes.Count,
                new EditorBuildSettingsScene(GiftGrabScenePath, true));
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
                    "Invalid asset folder path: " + path);
            }
            var parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }

        private sealed class ArenaReferences
        {
            public Transform[] PlayerAnchors;
            public Transform[] BaseAnchors;
            public Transform[] DepositedGiftAnchors;
            public GiftGrabBaseLabel[] PlayerLabels;
            public GiftGrabBaseLabel[] BaseLabels;
            public Transform GiftRoot;
            public Transform PushVfx;
            public Transform ThrowVfx;
            public Transform DropVfx;
            public Transform[] StunVfx;
        }

        private sealed class VfxReferences
        {
            public Transform Push;
            public Transform Throw;
            public Transform Drop;
            public Transform[] Stun;
        }

        private sealed class GiftGrabMaterials
        {
            public Material Floor;
            public Material Trim;
            public Material GiftWrap;
            public Material GiftRibbon;
            public Material LabelBack;
            public Material Highlight;
            public Material PushVfx;
            public Material ThrowVfx;
            public Material DropVfx;
            public Material StunVfx;
            public Material[] Bases;
        }
    }
}
