using System.Linq;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Testbed;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class GameplayTestbedSetup
    {
        public const string ScenePath = "Assets/MazeParty/Dev/GameplayTestbed/GameplayTestbed.unity";
        private const string MaterialFolder = "Assets/MazeParty/Dev/GameplayTestbed/Materials";

        private static Font _font;
        private static Sprite _uiSprite;

        [MenuItem("MazeParty/Gameplay/Rebuild Gameplay Testbed")]
        public static void RebuildGameplayTestbed()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Exit Play Mode before rebuilding the gameplay testbed.");

            var previousActiveScene = SceneManager.GetActiveScene();

            EnsureFolder("Assets/MazeParty/Dev");
            EnsureFolder("Assets/MazeParty/Dev/GameplayTestbed");
            EnsureFolder(MaterialFolder);

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            var floorMaterial = GetOrCreateMaterial("Floor", new Color(0.08f, 0.12f, 0.17f));
            var wallMaterial = GetOrCreateMaterial("Wall", new Color(0.18f, 0.25f, 0.34f));
            var firstPersonMaterial = GetOrCreateMaterial("FirstPersonZone", new Color(0.12f, 0.34f, 0.5f));
            var boardMaterial = GetOrCreateMaterial("BoardZone", new Color(0.22f, 0.45f, 0.27f));
            var minigameMaterial = GetOrCreateMaterial("MinigameZone", new Color(0.48f, 0.2f, 0.31f));
            var playerMaterial = GetOrCreateMaterial("Player", new Color(0.2f, 0.85f, 1f));
            var targetMaterial = GetOrCreateMaterial("Target", new Color(1f, 0.32f, 0.2f));
            var interactableMaterial = GetOrCreateMaterial("Interactable", new Color(1f, 0.72f, 0.18f));

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            scene.name = "GameplayTestbed";
            SceneManager.SetActiveScene(scene);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.34f, 0.4f, 0.5f);
            RenderSettings.ambientEquatorColor = new Color(0.16f, 0.2f, 0.26f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.06f, 0.08f);

            CreateLighting();
            var mainCamera = CreateMainCamera();
            var worldRoot = new GameObject("WORLD");
            var spawnRoot = new GameObject("SPAWNS");
            var systemsRoot = new GameObject("GAMEPLAY SYSTEMS");

            CreateFirstPersonArea(worldRoot.transform, floorMaterial, wallMaterial, firstPersonMaterial, targetMaterial, interactableMaterial);
            CreateBoardArea(worldRoot.transform, floorMaterial, wallMaterial, boardMaterial, targetMaterial, interactableMaterial);
            CreateMinigameArea(worldRoot.transform, floorMaterial, wallMaterial, minigameMaterial, targetMaterial);

            var firstPersonSpawn = CreateMarker(spawnRoot.transform, "Spawn_FirstPerson", new Vector3(0f, 1.05f, -7f), Quaternion.identity);
            var boardSpawn = CreateMarker(spawnRoot.transform, "Spawn_Board", new Vector3(30f, 1.05f, -6f), Quaternion.identity);
            var minigameSpawn = CreateMarker(spawnRoot.transform, "Spawn_Minigame", new Vector3(-30f, 1.05f, -6f), Quaternion.identity);

            var player = CreatePlayer(playerMaterial, firstPersonSpawn.position);
            var lookPivot = new GameObject("CameraPivot").transform;
            lookPivot.SetParent(player.transform, false);
            lookPivot.localPosition = new Vector3(0f, 0.7f, 0f);

            var characterController = player.GetComponent<CharacterController>();
            var inputSource = player.AddComponent<GameplayInputSource>();
            var playerMotor = player.AddComponent<GameplayPlayerMotor>();
            var playerHealth = player.AddComponent<GameplayHealth>();
            playerMotor.Configure(lookPivot);

            var firstPersonCamera = CreateCinemachineCamera(
                "CM_FirstPerson",
                lookPivot,
                Vector3.zero,
                Quaternion.identity,
                false,
                70f,
                0);
            var boardCamera = CreateCinemachineCamera(
                "CM_BoardTopDown",
                systemsRoot.transform,
                new Vector3(30f, 19f, 0f),
                Quaternion.Euler(90f, 0f, 0f),
                true,
                12f,
                100);
            var minigameCamera = CreateCinemachineCamera(
                "CM_Minigame",
                systemsRoot.transform,
                new Vector3(-30f, 10f, -15f),
                LookAtRotation(new Vector3(-30f, 1f, 1f) - new Vector3(-30f, 10f, -15f)),
                false,
                55f,
                0);

            var cameraDirector = systemsRoot.AddComponent<GameplayCameraDirector>();
            cameraDirector.Configure(mainCamera, firstPersonCamera, boardCamera, minigameCamera);

            CreateCanvas();
            var controller = systemsRoot.AddComponent<GameplayTestbedController>();
            controller.Configure(
                inputSource,
                playerMotor,
                playerHealth,
                cameraDirector,
                firstPersonSpawn,
                boardSpawn,
                minigameSpawn);

            EditorUtility.SetDirty(characterController);
            EditorUtility.SetDirty(playerMotor);
            EditorUtility.SetDirty(cameraDirector);
            EditorUtility.SetDirty(controller);

            EditorSceneManager.SaveScene(scene, ScenePath);
            RemoveTestbedFromBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                SceneManager.SetActiveScene(previousActiveScene);
            EditorSceneManager.CloseScene(scene, true);
            Selection.activeGameObject = null;
            Debug.Log("Gameplay testbed rebuilt at " + ScenePath + ". It remains excluded from Build Settings; the previously open scene was not modified.");
        }

        private static void CreateLighting()
        {
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.95f, 0.86f);
            light.intensity = 1.35f;
            light.shadows = LightShadows.Soft;
        }

        private static Camera CreateMainCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(
                new Vector3(30f, 19f, 0f),
                Quaternion.Euler(90f, 0f, 0f));

            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.035f, 0.055f);
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 500f;
            cameraObject.AddComponent<AudioListener>();

            var brain = cameraObject.AddComponent<CinemachineBrain>();
            brain.DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Styles.EaseInOut,
                0.4f);
            return camera;
        }

        private static CinemachineCamera CreateCinemachineCamera(
            string name,
            Transform parent,
            Vector3 position,
            Quaternion rotation,
            bool orthographic,
            float lensValue,
            int priority)
        {
            var cameraObject = new GameObject(name);
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.localPosition = position;
            cameraObject.transform.localRotation = rotation;

            var virtualCamera = cameraObject.AddComponent<CinemachineCamera>();
            virtualCamera.Priority = priority;

            var lens = virtualCamera.Lens;
            lens.NearClipPlane = 0.05f;
            lens.FarClipPlane = 500f;
            if (orthographic)
            {
                lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
                lens.OrthographicSize = lensValue;
            }
            else
            {
                lens.ModeOverride = LensSettings.OverrideModes.Perspective;
                lens.FieldOfView = lensValue;
            }
            virtualCamera.Lens = lens;
            return virtualCamera;
        }

        private static GameObject CreatePlayer(Material material, Vector3 position)
        {
            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Local Test Player";
            player.transform.position = position;
            player.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(player.GetComponent<CapsuleCollider>());

            var controller = player.AddComponent<CharacterController>();
            controller.center = Vector3.zero;
            controller.height = 2f;
            controller.radius = 0.45f;
            controller.skinWidth = 0.05f;
            controller.stepOffset = 0.3f;
            player.AddComponent<PlayerAvatarVisual>();
            return player;
        }

        private static void CreateFirstPersonArea(
            Transform parent,
            Material floor,
            Material wall,
            Material zone,
            Material target,
            Material interactable)
        {
            var root = new GameObject("FIRST PERSON TEST AREA").transform;
            root.SetParent(parent);
            CreateBox(root, "FP Floor", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f), floor);
            CreateBoundaryWalls(root, Vector3.zero, new Vector2(20f, 20f), wall);

            CreateBox(root, "Corridor Left", new Vector3(-2.4f, 1f, 1f), new Vector3(0.7f, 2f, 9f), zone);
            CreateBox(root, "Corridor Right", new Vector3(2.4f, 1f, 1f), new Vector3(0.7f, 2f, 9f), zone);
            CreateBox(root, "Low Step", new Vector3(0f, 0.25f, -2f), new Vector3(3.6f, 0.5f, 1f), zone);

            CreateTarget(
                root,
                "Interaction Console",
                new Vector3(0f, 1f, 2.8f),
                new Vector3(1.5f, 2f, 0.5f),
                interactable,
                "Toggle interaction console",
                false,
                true);
            CreateTarget(
                root,
                "Push Damage Target",
                new Vector3(0f, 1f, 7f),
                new Vector3(1.5f, 2f, 1.5f),
                target,
                "Reset damage target",
                false,
                false);

            CreateWorldLabel(root, "FIRST PERSON / INTERACTION", new Vector3(0f, 3.2f, 9.2f), Color.cyan);
        }

        private static void CreateBoardArea(
            Transform parent,
            Material floor,
            Material wall,
            Material zone,
            Material target,
            Material interactable)
        {
            var root = new GameObject("BOARD TOP VIEW TEST AREA").transform;
            root.SetParent(parent);
            var center = new Vector3(30f, 0f, 0f);
            CreateBox(root, "Board Floor", center + Vector3.down * 0.5f, new Vector3(20f, 1f, 20f), floor);
            CreateBoundaryWalls(root, center, new Vector2(20f, 20f), wall);

            var tilePositions = new[]
            {
                new Vector3(26f, 0.12f, -4f), new Vector3(30f, 0.12f, -4f), new Vector3(34f, 0.12f, -4f),
                new Vector3(34f, 0.12f, 0f), new Vector3(34f, 0.12f, 4f), new Vector3(30f, 0.12f, 4f),
                new Vector3(26f, 0.12f, 4f), new Vector3(26f, 0.12f, 0f)
            };
            for (var i = 0; i < tilePositions.Length; i++)
                CreateBox(root, "Board Tile " + (i + 1), tilePositions[i], new Vector3(2.7f, 0.24f, 2.7f), zone);

            CreateTarget(
                root,
                "Board Die",
                new Vector3(30f, 0.75f, 1f),
                Vector3.one * 1.4f,
                interactable,
                "Roll board die",
                true,
                false);
            CreateTarget(
                root,
                "Board Attack Target",
                new Vector3(34f, 1f, 0f),
                new Vector3(1.4f, 2f, 1.4f),
                target,
                "Inspect board target",
                false,
                false);

            CreateWorldLabel(root, "BOARD TOP VIEW / DICE", new Vector3(30f, 0.2f, 8.5f), Color.green);
        }

        private static void CreateMinigameArea(
            Transform parent,
            Material floor,
            Material wall,
            Material zone,
            Material target)
        {
            var root = new GameObject("MINIGAME TEST AREA").transform;
            root.SetParent(parent);
            var center = new Vector3(-30f, 0f, 0f);
            CreateBox(root, "Minigame Floor", center + Vector3.down * 0.5f, new Vector3(20f, 1f, 20f), floor);
            CreateBoundaryWalls(root, center, new Vector2(20f, 20f), wall);

            CreateBox(root, "Minigame Ramp", new Vector3(-30f, 0.55f, 0f), new Vector3(6f, 0.5f, 3f), zone, Quaternion.Euler(0f, 0f, 8f));
            CreateBox(root, "Minigame Obstacle Left", new Vector3(-34f, 1f, 3f), new Vector3(2f, 2f, 2f), zone);
            CreateBox(root, "Minigame Obstacle Right", new Vector3(-26f, 1f, 3f), new Vector3(2f, 2f, 2f), zone);

            CreateTarget(root, "Minigame Target A", new Vector3(-34f, 1f, 6f), Vector3.one * 1.6f, target, "Inspect target A", false, false);
            CreateTarget(root, "Minigame Target B", new Vector3(-30f, 1f, 6f), Vector3.one * 1.6f, target, "Inspect target B", false, false);
            CreateTarget(root, "Minigame Target C", new Vector3(-26f, 1f, 6f), Vector3.one * 1.6f, target, "Inspect target C", false, false);

            CreateWorldLabel(root, "MINIGAME / PRIMARY + SECONDARY", new Vector3(-30f, 3f, 9.2f), new Color(1f, 0.4f, 0.65f));
        }

        private static void CreateCanvas()
        {
            var canvasObject = new GameObject("GameplayTestbedCanvas");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            var inputModule = eventSystemObject.AddComponent<InputSystemUIInputModule>();
            inputModule.AssignDefaultActions();

            CreateTopHud(canvasObject.transform);
            CreateDebugPanel(canvasObject.transform);
            CreateInventoryHud(canvasObject.transform);
            CreateSelectionPanel(canvasObject.transform);
            CreateStatusHud(canvasObject.transform);
            CreateReticle(canvasObject.transform);
        }

        private static void CreateTopHud(Transform canvas)
        {
            var panel = CreatePanel(
                canvas,
                "TopHudPanel",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -18f),
                new Vector2(1260f, 96f),
                new Color(0.035f, 0.055f, 0.09f, 0.94f));

            CreateText(panel.transform, "ActionTimerText", "TOP VIEW / IDLE", 28, TextAnchor.MiddleCenter,
                new Vector2(-455f, 0f), new Vector2(280f, 70f), Color.white);
            CreateText(panel.transform, "ShieldTimerText", "HP SHIELD  --", 28, TextAnchor.MiddleCenter,
                new Vector2(-155f, 0f), new Vector2(320f, 70f), new Color(0.3f, 1f, 0.72f));
            CreateText(panel.transform, "ChoiceTimerText", "CHOICE  --", 28, TextAnchor.MiddleCenter,
                new Vector2(175f, 0f), new Vector2(250f, 70f), Color.white);
            CreateText(panel.transform, "HealthText", "HP  100 / 100", 28, TextAnchor.MiddleCenter,
                new Vector2(475f, 0f), new Vector2(220f, 70f), new Color(1f, 0.55f, 0.55f));
        }

        private static void CreateDebugPanel(Transform canvas)
        {
            var panel = CreatePanel(
                canvas,
                "DebugPanel",
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(18f, -18f),
                new Vector2(330f, 640f),
                new Color(0.03f, 0.045f, 0.07f, 0.94f));
            panel.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);

            CreateText(panel.transform, "DebugTitle", "GAMEPLAY TESTBED", 23, TextAnchor.MiddleCenter,
                new Vector2(0f, -35f), new Vector2(300f, 45f), new Color(0.3f, 0.9f, 1f));
            CreateText(panel.transform, "ControlHelp",
                "WASD Move | LMB Primary | RMB Interact\nF1 First Person | F2 Board | F3 Minigame\nEsc Release Cursor | R Reset",
                17, TextAnchor.MiddleCenter, new Vector2(0f, -91f), new Vector2(300f, 72f), Color.white);

            var y = -155f;
            CreateButton(panel.transform, "StartActionButton", "START ACTION / END TOP VIEW", new Vector2(0f, y), new Vector2(290f, 44f), new Color(0.15f, 0.55f, 0.75f));
            y -= 51f;
            CreateButton(panel.transform, "IncomingHitButton", "REMOTE HIT + PUSH", new Vector2(0f, y), new Vector2(290f, 44f), new Color(0.7f, 0.2f, 0.22f));
            y -= 51f;
            CreateButton(panel.transform, "IncomingPushButton", "REMOTE PUSH ONLY", new Vector2(0f, y), new Vector2(290f, 44f), new Color(0.48f, 0.22f, 0.62f));
            y -= 51f;
            CreateButton(panel.transform, "AddRewardButton", "TRY ADD REWARD", new Vector2(0f, y), new Vector2(290f, 44f), new Color(0.24f, 0.5f, 0.3f));
            y -= 51f;
            CreateButton(panel.transform, "EndActionButton", "END ACTION / CLEAR ITEM", new Vector2(0f, y), new Vector2(290f, 44f), new Color(0.52f, 0.34f, 0.18f));
            y -= 51f;
            CreateButton(panel.transform, "ResetButton", "RESET TESTBED (R)", new Vector2(0f, y), new Vector2(290f, 44f), new Color(0.32f, 0.38f, 0.46f));
            y -= 62f;

            CreateButton(panel.transform, "FirstPersonButton", "FIRST PERSON  F1", new Vector2(-98f, y), new Vector2(92f, 42f), new Color(0.12f, 0.4f, 0.58f));
            CreateButton(panel.transform, "BoardButton", "BOARD  F2", new Vector2(0f, y), new Vector2(92f, 42f), new Color(0.2f, 0.48f, 0.27f));
            CreateButton(panel.transform, "MinigameButton", "MINIGAME  F3", new Vector2(98f, y), new Vector2(92f, 42f), new Color(0.55f, 0.2f, 0.38f));
        }

        private static void CreateInventoryHud(Transform canvas)
        {
            var root = new GameObject("InventoryHud");
            var rect = root.AddComponent<RectTransform>();
            rect.SetParent(canvas, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 30f);
            rect.sizeDelta = new Vector2(570f, 128f);

            CreateText(root.transform, "InventoryTitle", "ITEM SLOTS - CHOICE UI ONLY", 18, TextAnchor.MiddleCenter,
                new Vector2(0f, 103f), new Vector2(520f, 28f), new Color(0.75f, 0.85f, 1f));

            for (var i = 0; i < 3; i++)
            {
                var slot = CreatePanel(
                    root.transform,
                    "InventorySlot" + i,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2((i - 1) * 180f, 35f),
                    new Vector2(160f, 86f),
                    new Color(0.18f, 0.32f, 0.5f, 0.94f));
                CreateText(slot.transform, "SlotLabel", "EMPTY", 19, TextAnchor.MiddleCenter,
                    Vector2.zero, new Vector2(146f, 72f), Color.white);
            }

            CreateText(canvas, "AmmoText", "AMMO  --", 30, TextAnchor.MiddleRight,
                new Vector2(-45f, 58f), new Vector2(300f, 70f), Color.white, new Vector2(1f, 0f));
        }

        private static void CreateSelectionPanel(Transform canvas)
        {
            var panel = CreatePanel(
                canvas,
                "ItemSelectionPanel",
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 20f),
                new Vector2(800f, 390f),
                new Color(0.025f, 0.04f, 0.07f, 0.97f));

            CreateText(panel.transform, "SelectionTitle", "CHOOSE ONE ITEM - PERSONAL 30 SECOND LIMIT", 30, TextAnchor.MiddleCenter,
                new Vector2(0f, 148f), new Vector2(740f, 50f), Color.white);
            CreateText(panel.transform, "SelectionRule",
                "The shared 3:00 clock is already running. During the first 5 seconds only HP damage is blocked; hits and push still apply.",
                18, TextAnchor.MiddleCenter, new Vector2(0f, 105f), new Vector2(720f, 48f), new Color(0.72f, 0.86f, 1f));

            for (var i = 0; i < 3; i++)
            {
                var button = CreateButton(
                    panel.transform,
                    "ChoiceButton" + i,
                    "ITEM " + (i + 1),
                    new Vector2((i - 1) * 240f, 28f),
                    new Vector2(215f, 82f),
                    new Color(0.16f, 0.37f, 0.58f));
                var hover = button.gameObject.AddComponent<TestbedItemChoiceButton>();
                hover.Configure(i);
            }

            var tooltip = CreateText(panel.transform, "TooltipText", "Hover an item for details.", 18, TextAnchor.MiddleCenter,
                new Vector2(0f, -67f), new Vector2(700f, 62f), new Color(1f, 0.86f, 0.42f));
            tooltip.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.7f);

            CreateButton(panel.transform, "NoItemButton", "DO NOT USE", new Vector2(0f, -142f), new Vector2(300f, 54f), new Color(0.42f, 0.2f, 0.23f));
        }

        private static void CreateStatusHud(Transform canvas)
        {
            var panel = CreatePanel(
                canvas,
                "StatusPanel",
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 172f),
                new Vector2(1180f, 68f),
                new Color(0.03f, 0.045f, 0.07f, 0.91f));
            CreateText(panel.transform, "StatusText", "Ready.", 19, TextAnchor.MiddleCenter,
                Vector2.zero, new Vector2(1130f, 56f), Color.white);
        }

        private static void CreateReticle(Transform canvas)
        {
            var reticle = CreateText(canvas, "Reticle", "+", 32, TextAnchor.MiddleCenter,
                Vector2.zero, new Vector2(50f, 50f), Color.white);
            reticle.raycastTarget = false;
            reticle.gameObject.AddComponent<Outline>().effectColor = Color.black;
        }

        private static GameObject CreateTarget(
            Transform parent,
            string name,
            Vector3 position,
            Vector3 scale,
            Material material,
            string prompt,
            bool rollsDie,
            bool kinematic)
        {
            var target = CreateBox(parent, name, position, scale, material);
            var body = target.AddComponent<Rigidbody>();
            body.mass = 2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.isKinematic = kinematic;

            var targetComponent = target.AddComponent<TestbedTarget>();
            targetComponent.Configure(prompt, rollsDie);
            return target;
        }

        private static GameObject CreateBox(
            Transform parent,
            string name,
            Vector3 position,
            Vector3 scale,
            Material material,
            Quaternion? rotation = null)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent);
            box.transform.SetPositionAndRotation(position, rotation ?? Quaternion.identity);
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = material;
            return box;
        }

        private static void CreateBoundaryWalls(Transform parent, Vector3 center, Vector2 size, Material material)
        {
            const float wallHeight = 2.5f;
            const float thickness = 0.7f;
            CreateBox(parent, "North Wall", center + new Vector3(0f, wallHeight * 0.5f, size.y * 0.5f), new Vector3(size.x, wallHeight, thickness), material);
            CreateBox(parent, "South Wall", center + new Vector3(0f, wallHeight * 0.5f, -size.y * 0.5f), new Vector3(size.x, wallHeight, thickness), material);
            CreateBox(parent, "East Wall", center + new Vector3(size.x * 0.5f, wallHeight * 0.5f, 0f), new Vector3(thickness, wallHeight, size.y), material);
            CreateBox(parent, "West Wall", center + new Vector3(-size.x * 0.5f, wallHeight * 0.5f, 0f), new Vector3(thickness, wallHeight, size.y), material);
        }

        private static Transform CreateMarker(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            var marker = new GameObject(name).transform;
            marker.SetParent(parent);
            marker.SetPositionAndRotation(position, rotation);
            return marker;
        }

        private static void CreateWorldLabel(Transform parent, string text, Vector3 position, Color color)
        {
            var labelObject = new GameObject(text);
            labelObject.transform.SetParent(parent);
            labelObject.transform.position = position;
            labelObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var textMesh = labelObject.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.fontSize = 48;
            textMesh.characterSize = 0.12f;
            textMesh.color = color;
        }

        private static GameObject CreatePanel(
            Transform parent,
            string name,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 position,
            Vector2 size,
            Color color)
        {
            var panel = new GameObject(name);
            var rect = panel.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var image = panel.AddComponent<Image>();
            image.color = color;
            image.sprite = _uiSprite;
            if (_uiSprite != null)
                image.type = Image.Type.Sliced;
            return panel;
        }

        private static Text CreateText(
            Transform parent,
            string name,
            string value,
            int fontSize,
            TextAnchor alignment,
            Vector2 position,
            Vector2 size,
            Color color,
            Vector2? anchor = null)
        {
            var textObject = new GameObject(name);
            var rect = textObject.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            var resolvedAnchor = anchor ?? new Vector2(0.5f, 0.5f);
            rect.anchorMin = rect.anchorMax = resolvedAnchor;
            rect.pivot = resolvedAnchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var text = textObject.AddComponent<Text>();
            text.font = _font;
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static Button CreateButton(
            Transform parent,
            string name,
            string label,
            Vector2 position,
            Vector2 size,
            Color color)
        {
            var buttonObject = CreatePanel(
                parent,
                name,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                position,
                size,
                color);
            var image = buttonObject.GetComponent<Image>();
            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f);
            colors.disabledColor = new Color(0.35f, 0.35f, 0.35f, 0.75f);
            colors.colorMultiplier = 1f;
            button.colors = colors;

            CreateText(buttonObject.transform, "Label", label, 17, TextAnchor.MiddleCenter, Vector2.zero, size - new Vector2(12f, 8f), Color.white);
            return button;
        }

        private static Material GetOrCreateMaterial(string name, Color color)
        {
            var path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.25f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Quaternion LookAtRotation(Vector3 direction)
        {
            return Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            var slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        private static void RemoveTestbedFromBuildSettings()
        {
            var current = EditorBuildSettings.scenes;
            var filtered = current.Where(scene => scene.path != ScenePath).ToArray();
            if (filtered.Length != current.Length)
                EditorBuildSettings.scenes = filtered;
        }
    }
}
