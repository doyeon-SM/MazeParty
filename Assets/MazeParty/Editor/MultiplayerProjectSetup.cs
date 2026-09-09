using System.Collections.Generic;
using System.Linq;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class MultiplayerProjectSetup
    {
        private const string Root = "Assets/MazeParty";
        private const string ScenesFolder = Root + "/Scenes";
        private const string PrefabsFolder = Root + "/Prefabs";
        private const string BootstrapPath = ScenesFolder + "/OnlineBootstrap.unity";
        private const string BoardPath = ScenesFolder + "/Board.unity";
        private const string MinefieldPath = ScenesFolder + "/Minefield.unity";
        private const string WrongWayPath = ScenesFolder + "/WrongWay.unity";
        private const string PlayerPrefabPath = PrefabsFolder + "/NetworkPlayer.prefab";
        private const string LobbyFloorMaterialPath =
            Root + "/Board/Materials/RoomNormalA.mat";
        private const string LobbyWallMaterialPath =
            Root + "/Board/Materials/RoomNormalB.mat";

        [MenuItem("MazeParty/Multiplayer/Rebuild Online Prototype")]
        public static void BuildOnlinePrototype()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("Online prototype rebuild canceled; open scene changes were left untouched.");
                return;
            }

            EnsureFolders();

            var playerPrefab = CreatePlayerPrefab();
            CreateBootstrapScene(playerPrefab);
            BoardFlowProjectSetup.BuildBoardSceneBase();
            BoardFlowProjectSetup.BuildLocalTestbedFromBoard();
            MinefieldProjectSetup.BuildMinefieldAssets();
            WrongWayProjectSetup.BuildWrongWayAssets();
            ConfigureBuildSettings();
            BoardFlowProjectSetup.AddNetworkStateAndSave();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(BootstrapPath, OpenSceneMode.Single);
            Debug.Log(
                "MazeParty online prototype rebuilt: 4-player NetworkManager, Relay lobby, " +
                "NetworkPlayer prefab, and the 32-room Board flow vertical slice.");
        }




        private static void EnsureFolders()
        {
            EnsureFolder(Root);
            EnsureFolder(ScenesFolder);
            EnsureFolder(PrefabsFolder);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = path.Substring(0, path.LastIndexOf('/'));
            var folderName = path.Substring(path.LastIndexOf('/') + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        private static GameObject CreatePlayerPrefab()
        {
            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "NetworkPlayer";

            var capsuleCollider = player.GetComponent<CapsuleCollider>();
            if (capsuleCollider != null)
            {
                Object.DestroyImmediate(capsuleCollider);
            }

            var controller = player.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.5f;
            controller.center = Vector3.zero;

            player.AddComponent<NetworkObject>();
            var networkTransform = player.AddComponent<NetworkTransform>();
            networkTransform.Interpolate = true;
            networkTransform.SyncScaleX = false;
            networkTransform.SyncScaleY = false;
            networkTransform.SyncScaleZ = false;

            player.AddComponent<PlayerAvatarVisual>();
            player.AddComponent<NetworkPlayerAvatar>();
            // Four reusable boundaries are created by this component per player
            // instance (16 total for the fixed four-player match).
            var boundaryWalls = player.AddComponent<PlayerBoardBoundaryWalls>();
            boundaryWalls.ConfigureVisualMaterial(
                LoadRequiredMaterial(LobbyWallMaterialPath));

            var prefab = PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            Object.DestroyImmediate(player);
            return prefab;
        }

        private static void CreateBootstrapScene(GameObject playerPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Lobby Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.055f, 0.09f);
            camera.fieldOfView = 48f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.transform.position = new Vector3(3.5f, 11.5f, -10.5f);
            cameraObject.transform.LookAt(new Vector3(3.5f, 0.65f, 0f));

            var lightObject = new GameObject("Lobby Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            CreateLobbyArena();

            var lobbyView = CreateLobbyCanvas();
            CreateEventSystem();

            var runtime = new GameObject("Online Network Runtime");
            // TODO(STEAM-TRANSPORT): replace UnityTransport with an NGO-compatible
            // Steam NetworkingSockets transport adapter; session/UI code stays unchanged.
            var transport = runtime.AddComponent<UnityTransport>();
            // This limits Relay/UTP disconnect detection for force-closed lobby clients.
            // It is separate from the MPS gameplay reconnection retention window.
            // Keep the backend Disconnect Removal Time at 75-90 seconds so this
            // detection delay plus the 60-second gameplay grace cannot evict the seat.
            transport.DisconnectTimeoutMS = 15000;
            var networkManager = runtime.AddComponent<NetworkManager>();
            networkManager.RunInBackground = true;
            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
            networkManager.NetworkConfig.EnableSceneManagement = true;
            networkManager.NetworkConfig.ConnectionApproval = false;
            networkManager.NetworkConfig.ForceSamePrefabs = true;

            var sessionController = runtime.AddComponent<OnlineSessionController>();
            sessionController.ConfigureSceneReferences(camera, light, lobbyView);

            EditorSceneManager.SaveScene(scene, BootstrapPath);
        }

        private static void CreateLobbyArena()
        {
            var center = new Vector3(3.5f, 0f, 0f);
            var innerSize = new Vector2(12f, 8f);
            var root = new GameObject("Lobby Waiting Room");
            var arena = root.AddComponent<LobbyArena>();
            arena.Configure(
                center,
                innerSize,
                new[]
                {
                    center + new Vector3(-2.7f, 1f, -1.8f),
                    center + new Vector3(2.7f, 1f, -1.8f),
                    center + new Vector3(-2.7f, 1f, 1.8f),
                    center + new Vector3(2.7f, 1f, 1.8f)
                });

            var floorMaterial = LoadRequiredMaterial(LobbyFloorMaterialPath);
            var wallMaterial = LoadRequiredMaterial(LobbyWallMaterialPath);
            CreateLobbyPrimitive(
                "Floor",
                root.transform,
                center + Vector3.down * 0.25f,
                new Vector3(innerSize.x + 1f, 0.5f, innerSize.y + 1f),
                floorMaterial);

            const float thickness = 0.5f;
            const float height = 0.8f;
            CreateLobbyPrimitive(
                "North Wall",
                root.transform,
                center + new Vector3(0f, height * 0.5f, innerSize.y * 0.5f + thickness * 0.5f),
                new Vector3(innerSize.x + thickness * 2f, height, thickness),
                wallMaterial);
            CreateLobbyPrimitive(
                "South Wall",
                root.transform,
                center + new Vector3(0f, height * 0.5f, -innerSize.y * 0.5f - thickness * 0.5f),
                new Vector3(innerSize.x + thickness * 2f, height, thickness),
                wallMaterial);
            CreateLobbyPrimitive(
                "East Wall",
                root.transform,
                center + new Vector3(innerSize.x * 0.5f + thickness * 0.5f, height * 0.5f, 0f),
                new Vector3(thickness, height, innerSize.y),
                wallMaterial);
            CreateLobbyPrimitive(
                "West Wall",
                root.transform,
                center + new Vector3(-innerSize.x * 0.5f - thickness * 0.5f, height * 0.5f, 0f),
                new Vector3(thickness, height, innerSize.y),
                wallMaterial);
        }

        private static void CreateLobbyPrimitive(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Material material)
        {
            var value = GameObject.CreatePrimitive(PrimitiveType.Cube);
            value.name = name;
            value.transform.SetParent(parent, false);
            value.transform.position = position;
            value.transform.localScale = scale;
            value.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static Material LoadRequiredMaterial(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                throw new System.InvalidOperationException(
                    "Required URP material is missing: " + path);
            }

            return material;
        }

        private static OnlineLobbyView CreateLobbyCanvas()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                throw new System.InvalidOperationException(
                    "Unity built-in LegacyRuntime.ttf font could not be loaded.");
            }

            var canvasObject = new GameObject(
                "Lobby Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(CanvasGroup),
                typeof(OnlineLobbyView));
            canvasObject.layer = LayerMask.NameToLayer("UI");

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var canvasGroup = canvasObject.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;

            var window = CreateUiObject("Lobby Window", canvasObject.transform);
            var windowRect = window.GetComponent<RectTransform>();
            windowRect.anchorMin = new Vector2(0f, 1f);
            windowRect.anchorMax = new Vector2(0f, 1f);
            windowRect.pivot = new Vector2(0f, 1f);
            windowRect.anchoredPosition = new Vector2(24f, -24f);
            windowRect.sizeDelta = new Vector2(500f, 0f);

            var windowImage = window.AddComponent<Image>();
            windowImage.color = new Color(0.025f, 0.04f, 0.075f, 0.96f);

            var windowLayout = window.AddComponent<VerticalLayoutGroup>();
            windowLayout.padding = new RectOffset(24, 24, 24, 24);
            windowLayout.spacing = 14f;
            windowLayout.childAlignment = TextAnchor.UpperLeft;
            windowLayout.childControlWidth = true;
            windowLayout.childControlHeight = true;
            windowLayout.childForceExpandWidth = true;
            windowLayout.childForceExpandHeight = false;

            var windowFitter = window.AddComponent<ContentSizeFitter>();
            windowFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateText(
                "Title",
                window.transform,
                "MazeParty Online (4 Players)",
                font,
                28,
                TextAnchor.MiddleLeft,
                42f);

            var connectionPanel = CreateVerticalContainer(
                "Connection Panel",
                window.transform,
                330f);

            CreateText(
                "Player Name Label",
                connectionPanel.transform,
                "Player Name",
                font,
                18,
                TextAnchor.MiddleLeft,
                26f);
            var displayNameInput = CreateInputField(
                "Player Name Input",
                connectionPanel.transform,
                "Enter display name",
                font,
                InputField.ContentType.Standard,
                16);
            var createButton = CreateButton(
                "Create Private Room Button",
                connectionPanel.transform,
                "Create Private Room",
                font,
                out _);

            CreateText(
                "Invite Code Label",
                connectionPanel.transform,
                "Invite Code",
                font,
                18,
                TextAnchor.MiddleLeft,
                26f);
            var joinCodeInput = CreateInputField(
                "Invite Code Input",
                connectionPanel.transform,
                "Enter invite code",
                font,
                InputField.ContentType.Alphanumeric,
                16);
            var joinButton = CreateButton(
                "Join by Code Button",
                connectionPanel.transform,
                "Join by Code",
                font,
                out _);

            CreateText(
                "Connection Help",
                connectionPanel.transform,
                "One local player connects from each game process.",
                font,
                16,
                TextAnchor.MiddleLeft,
                36f);

            var sessionPanel = CreateVerticalContainer(
                "Session Panel",
                window.transform,
                500f);
            var inviteCodeText = CreateText(
                "Invite Code",
                sessionPanel.transform,
                "Invite Code: -",
                font,
                20,
                TextAnchor.MiddleLeft,
                30f);
            var copyButton = CreateButton(
                "Copy Code Button",
                sessionPanel.transform,
                "Copy Code",
                font,
                out _);
            var sessionSummaryText = CreateText(
                "Session Summary",
                sessionPanel.transform,
                "Players: 0/4   Phase: Lobby",
                font,
                18,
                TextAnchor.MiddleLeft,
                30f);

            var playerRows = new Text[MultiplayerConstants.MaxPlayers];
            for (var index = 0; index < playerRows.Length; index++)
            {
                playerRows[index] = CreateText(
                    "Player Row " + (index + 1),
                    sessionPanel.transform,
                    "- Waiting for player...",
                    font,
                    18,
                    TextAnchor.MiddleLeft,
                    28f);
            }

            var readyButton = CreateButton(
                "Ready Button",
                sessionPanel.transform,
                "Ready",
                font,
                out var readyButtonText);
            var startButton = CreateButton(
                "Start Game Button",
                sessionPanel.transform,
                "Start 4-Player Game",
                font,
                out var startButtonText);
            var startHintText = CreateText(
                "Start Hint",
                sessionPanel.transform,
                "Exactly four ready players are required.",
                font,
                16,
                TextAnchor.MiddleLeft,
                36f);
            var runningText = CreateText(
                "Running Message",
                sessionPanel.transform,
                "The board game is running.",
                font,
                16,
                TextAnchor.MiddleLeft,
                36f);
            var leaveButton = CreateButton(
                "Leave Session Button",
                sessionPanel.transform,
                "Leave Session",
                font,
                out _);

            var statusText = CreateText(
                "Status",
                window.transform,
                "Create a private room or join with an invite code.",
                font,
                16,
                TextAnchor.UpperLeft,
                52f);

            var quitButton = CreateButton(
                "Quit Game Button",
                window.transform,
                "Quit Game",
                font,
                out _);
            quitButton.targetGraphic.color = new Color(0.62f, 0.14f, 0.16f, 1f);

            sessionPanel.SetActive(false);
            runningText.gameObject.SetActive(false);

            var lobbyView = canvasObject.GetComponent<OnlineLobbyView>();
            lobbyView.Configure(
                canvasGroup,
                connectionPanel,
                sessionPanel,
                displayNameInput,
                joinCodeInput,
                createButton,
                joinButton,
                copyButton,
                readyButton,
                startButton,
                leaveButton,
                quitButton,
                inviteCodeText,
                sessionSummaryText,
                readyButtonText,
                startButtonText,
                playerRows,
                startHintText.gameObject,
                runningText.gameObject,
                statusText);
            return lobbyView;
        }

        private static void CreateEventSystem()
        {
            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();

            var inputModule = eventSystemObject.AddComponent<InputSystemUIInputModule>();
            inputModule.AssignDefaultActions();
        }

        private static GameObject CreateVerticalContainer(
            string name,
            Transform parent,
            float preferredHeight)
        {
            var container = CreateUiObject(name, parent);
            var layout = container.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var layoutElement = container.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = preferredHeight;
            return container;
        }

        private static Text CreateText(
            string name,
            Transform parent,
            string value,
            Font font,
            int fontSize,
            TextAnchor alignment,
            float preferredHeight)
        {
            var textObject = CreateUiObject(name, parent);
            var text = textObject.AddComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.color = new Color(0.92f, 0.95f, 1f);
            text.alignment = alignment;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;

            var layoutElement = textObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = preferredHeight;
            return text;
        }

        private static InputField CreateInputField(
            string name,
            Transform parent,
            string placeholderValue,
            Font font,
            InputField.ContentType contentType,
            int characterLimit)
        {
            var inputObject = CreateUiObject(name, parent);
            var background = inputObject.AddComponent<Image>();
            background.color = new Color(0.11f, 0.15f, 0.23f, 1f);

            var inputField = inputObject.AddComponent<InputField>();
            inputField.targetGraphic = background;
            inputField.contentType = contentType;
            inputField.lineType = InputField.LineType.SingleLine;
            inputField.characterLimit = characterLimit;
            inputField.caretColor = Color.white;
            inputField.selectionColor = new Color(0.25f, 0.55f, 1f, 0.5f);

            var valueText = CreateText(
                "Text",
                inputObject.transform,
                string.Empty,
                font,
                18,
                TextAnchor.MiddleLeft,
                40f);
            Object.DestroyImmediate(valueText.GetComponent<LayoutElement>());
            SetStretch(valueText.rectTransform, 12f, 12f, 6f, 6f);
            valueText.horizontalOverflow = HorizontalWrapMode.Overflow;

            var placeholder = CreateText(
                "Placeholder",
                inputObject.transform,
                placeholderValue,
                font,
                18,
                TextAnchor.MiddleLeft,
                40f);
            Object.DestroyImmediate(placeholder.GetComponent<LayoutElement>());
            SetStretch(placeholder.rectTransform, 12f, 12f, 6f, 6f);
            placeholder.color = new Color(0.55f, 0.62f, 0.72f, 1f);
            placeholder.fontStyle = FontStyle.Italic;

            inputField.textComponent = valueText;
            inputField.placeholder = placeholder;

            var layoutElement = inputObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 42f;
            return inputField;
        }

        private static Button CreateButton(
            string name,
            Transform parent,
            string label,
            Font font,
            out Text labelText)
        {
            var buttonObject = CreateUiObject(name, parent);
            var image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.12f, 0.34f, 0.62f, 1f);

            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.9f, 0.95f, 1f, 1f);
            colors.pressedColor = new Color(0.72f, 0.82f, 0.95f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.45f, 0.48f, 0.54f, 0.65f);
            button.colors = colors;

            labelText = CreateText(
                "Label",
                buttonObject.transform,
                label,
                font,
                18,
                TextAnchor.MiddleCenter,
                42f);
            Object.DestroyImmediate(labelText.GetComponent<LayoutElement>());
            SetStretch(labelText.rectTransform, 8f, 8f, 4f, 4f);

            var layoutElement = buttonObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 44f;
            return button;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            var uiObject = new GameObject(name, typeof(RectTransform));
            uiObject.layer = LayerMask.NameToLayer("UI");
            if (parent != null)
            {
                uiObject.transform.SetParent(parent, false);
            }

            return uiObject;
        }

        private static void SetStretch(
            RectTransform rectTransform,
            float left,
            float right,
            float top,
            float bottom)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = new Vector2(left, bottom);
            rectTransform.offsetMax = new Vector2(-right, -top);
        }









        private static void CreateBoardScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Board Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 55f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.transform.position = new Vector3(0f, 16f, -14f);
            cameraObject.transform.LookAt(Vector3.zero);

            var lightObject = new GameObject("Board Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            lightObject.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Board Floor";
            floor.transform.localScale = new Vector3(2f, 1f, 2f);

            CreateBoundary("North Boundary", new Vector3(0f, 1f, 10f), new Vector3(21f, 2f, 1f));
            CreateBoundary("South Boundary", new Vector3(0f, 1f, -10f), new Vector3(21f, 2f, 1f));
            CreateBoundary("East Boundary", new Vector3(10f, 1f, 0f), new Vector3(1f, 2f, 21f));
            CreateBoundary("West Boundary", new Vector3(-10f, 1f, 0f), new Vector3(1f, 2f, 21f));

            EditorSceneManager.SaveScene(scene, BoardPath);
        }

        private static void CreateBoardNetworkState()
        {
            var scene = EditorSceneManager.OpenScene(BoardPath, OpenSceneMode.Single);

            // NGO assigns in-scene object hashes only after the scene is a saved,
            // enabled build scene. Keep this ordering when replacing this setup tool.
            var matchState = new GameObject("Network Match State");
            matchState.AddComponent<NetworkObject>();
            matchState.AddComponent<NetworkMatchState>();

            EditorSceneManager.SaveScene(scene, BoardPath);
        }


        private static void CreateBoundary(string name, Vector3 position, Vector3 scale)
        {
            var boundary = GameObject.CreatePrimitive(PrimitiveType.Cube);
            boundary.name = name;
            boundary.transform.position = position;
            boundary.transform.localScale = scale;
        }

        private static void ConfigureBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(BootstrapPath, true),
                new EditorBuildSettingsScene(BoardPath, true),
                new EditorBuildSettingsScene(MinefieldPath, true),
                new EditorBuildSettingsScene(WrongWayPath, true)
            };

            const string originalSample = "Assets/Scenes/SampleScene.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(originalSample) != null)
            {
                scenes.Add(new EditorBuildSettingsScene(originalSample, false));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
