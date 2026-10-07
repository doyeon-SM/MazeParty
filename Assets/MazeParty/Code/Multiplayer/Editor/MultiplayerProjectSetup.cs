using System.Collections.Generic;
using System.Linq;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using Unity.Netcode;
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
        private const string UiFolder = "Assets/MazeParty/Prefabs/Multiplayer";
        private const string UiPrefabsFolder = "Assets/MazeParty/Prefabs/Multiplayer/UI";
        private const string RoundedPanelSpritePath =
            "Assets/Ignore/Modern UI Pack/Textures/Border/Rounded/1024px/" +
            "Rounded Filled 1024px.png";
        private const string MatchSkyboxPath =
            "Assets/Ignore/Fantasy Skybox FREE/Cubemaps/Classic/" +
            "FS000_Night_01.mat";
        private const string BootstrapPath = "Assets/MazeParty/Scenes/Multiplayer/OnlineBootstrap.unity";
        private const string BoardPath = "Assets/MazeParty/Scenes/Board/Board.unity";
        private const string MinefieldPath = "Assets/MazeParty/Scenes/Minigames/Minefield/Minefield.unity";
        private const string WrongWayPath = "Assets/MazeParty/Scenes/Minigames/WrongWay/WrongWay.unity";
        private const string RedLightGreenLightPath =
            "Assets/MazeParty/Scenes/Minigames/RedLightGreenLight/RedLightGreenLight.unity";
        private const string StableFootingPath =
            "Assets/MazeParty/Scenes/Minigames/StableFooting/StableFooting.unity";
        private const string BalloonBlowPath =
            "Assets/MazeParty/Scenes/Minigames/BalloonBlow/BalloonBlow.unity";
        private const string GiftGrabPath =
            "Assets/MazeParty/Scenes/Minigames/GiftGrab/GiftGrab.unity";
        private const string TerritoryPaintPath =
            "Assets/MazeParty/Scenes/Minigames/TerritoryPaint/TerritoryPaint.unity";
        private const string TagChasePath =
            "Assets/MazeParty/Scenes/Minigames/TagChase/TagChase.unity";
        private const string RacePath =
            "Assets/MazeParty/Scenes/Minigames/Race/Race.unity";
        private const string LobbyCanvasPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab";
        private const string MinigameScheduleTowerPrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/MinigameScheduleTower.prefab";

        [MenuItem("MazeParty/Multiplayer/Rebuild Online Prototype")]
        public static void BuildOnlinePrototype()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("Online prototype rebuild canceled; open scene changes were left untouched.");
                return;
            }

            EnsureFolders();
            MinigameTimerDialProjectSetup.EnsurePrefabExists();

            var playerPrefab = MultiplayerPresentationPrefabProjectSetup
                .LoadOrCreatePlayerPrefab();
            var lobbyArenaPrefab = MultiplayerPresentationPrefabProjectSetup
                .LoadOrCreateLobbyArenaPrefab();
            CreateBootstrapScene(playerPrefab, lobbyArenaPrefab);
            BoardFlowProjectSetup.BuildBoardSceneBase();
            BoardFlowProjectSetup.BuildLocalTestbedFromBoard();
            MinefieldProjectSetup.BuildMinefieldAssets();
            WrongWayProjectSetup.BuildWrongWayAssets();
            RedLightGreenLightProjectSetup.BuildRedLightGreenLightAssets();
            StableFootingProjectSetup.BuildStableFootingAssets();
            BalloonBlowProjectSetup.BuildBalloonBlowAssets();
            GiftGrabProjectSetup.BuildGiftGrabAssets();
            TerritoryPaintProjectSetup.BuildTerritoryPaintAssets();
            TagChaseProjectSetup.BuildTagChaseAssets();
            RaceProjectSetup.BuildRaceAssets();
            SequenceMemoryProjectSetup.BuildSequenceMemoryAssets();
            BouncingBallsProjectSetup.BuildBouncingBallsAssets();
            BombPassingProjectSetup.BuildBombPassingAssets();
            ConfigureBuildSettings();
            BoardFlowProjectSetup.AddNetworkStateAndSave();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(BootstrapPath, OpenSceneMode.Single);
            Debug.Log(
                "MazeParty online prototype rebuilt: 4-player NetworkManager, Relay lobby, " +
                "NetworkPlayer prefab, and the 32-room Board flow vertical slice.");
        }

        [MenuItem("MazeParty/Setup/Upgrade Lobby Map Selector")]
        public static void UpgradeLobbyMapSelector()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                LobbyCanvasPrefabPath);
            if (prefab == null)
            {
                throw new System.InvalidOperationException(
                    "LobbyCanvas.prefab is missing at " + LobbyCanvasPrefabPath + ".");
            }

            var contents = PrefabUtility.LoadPrefabContents(LobbyCanvasPrefabPath);
            try
            {
                var view = contents.GetComponent<OnlineLobbyView>();
                if (view == null)
                {
                    throw new System.InvalidOperationException(
                        "LobbyCanvas.prefab has no OnlineLobbyView component.");
                }

                var serializedView = new SerializedObject(view);
                var rootProperty = serializedView.FindProperty(
                    "boardMapSelectionRoot");
                var previousProperty = serializedView.FindProperty(
                    "previousBoardMapButton");
                var nextProperty = serializedView.FindProperty(
                    "nextBoardMapButton");
                var nameProperty = serializedView.FindProperty("boardMapNameText");
                if (rootProperty == null || previousProperty == null ||
                    nextProperty == null || nameProperty == null)
                {
                    throw new System.InvalidOperationException(
                        "OnlineLobbyView map-selector fields are unavailable. " +
                        "Wait for scripts to compile, then run this command again.");
                }

                var row = rootProperty.objectReferenceValue as GameObject;
                var created = false;
                if (row == null)
                {
                    var sessionPanel = FindRequiredChild(
                        contents.transform,
                        "Session Panel");
                    var readyButton = FindRequiredChild(
                        sessionPanel,
                        "Ready Button");
                    row = FindChild(sessionPanel, "Board Map Selection");
                    if (row == null)
                    {
                        var font = Resources.Load<Font>(
                            "MazeParty/Fonts/PlayerNameFont");
                        if (font == null)
                        {
                            font = Resources.GetBuiltinResource<Font>(
                                "LegacyRuntime.ttf");
                        }

                        if (font == null)
                        {
                            throw new System.InvalidOperationException(
                                "A font is required to author the lobby map selector.");
                        }

                        row = CreateBoardMapSelectionRow(
                            sessionPanel,
                            font,
                            out var previous,
                            out var next,
                            out var mapName);
                        row.transform.SetSiblingIndex(
                            readyButton.GetSiblingIndex());

                        var panelRect = sessionPanel.GetComponent<RectTransform>();
                        panelRect.sizeDelta = new Vector2(680f, 640f);
                        panelRect.anchoredPosition = new Vector2(-205f, -130f);
                        previousProperty.objectReferenceValue = previous;
                        nextProperty.objectReferenceValue = next;
                        nameProperty.objectReferenceValue = mapName;
                        created = true;
                    }
                }

                var previousButton = previousProperty.objectReferenceValue as Button;
                if (previousButton == null)
                {
                    previousButton = FindRequiredChild(
                            row.transform,
                            "Previous Map Button")
                        .GetComponent<Button>();
                    previousProperty.objectReferenceValue = previousButton;
                }

                var nextButton = nextProperty.objectReferenceValue as Button;
                if (nextButton == null)
                {
                    nextButton = FindRequiredChild(
                            row.transform,
                            "Next Map Button")
                        .GetComponent<Button>();
                    nextProperty.objectReferenceValue = nextButton;
                }

                var mapNameText = nameProperty.objectReferenceValue as Text;
                if (mapNameText == null)
                {
                    mapNameText = FindRequiredChild(row.transform, "Map Name")
                        .GetComponent<Text>();
                    nameProperty.objectReferenceValue = mapNameText;
                }

                if (previousButton == null || nextButton == null ||
                    mapNameText == null)
                {
                    throw new System.InvalidOperationException(
                        "The existing lobby map selector is incomplete. " +
                        "Repair its authored bindings without recreating it.");
                }

                rootProperty.objectReferenceValue = row;
                var bindingsChanged = serializedView.ApplyModifiedPropertiesWithoutUndo();
                if (!view.HasRequiredReferences)
                {
                    throw new System.InvalidOperationException(
                        "LobbyCanvas.prefab is still missing required OnlineLobbyView bindings.");
                }

                if (created || bindingsChanged)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, LobbyCanvasPrefabPath);
                    AssetDatabase.SaveAssets();
                }

                Debug.Log(created
                    ? "Lobby map selector authored and bound in LobbyCanvas.prefab."
                    : "Lobby map selector bindings validated; authored design was preserved.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }




        private static void EnsureFolders()
        {
            EnsureFolder(Root);
            EnsureFolder(ScenesFolder);
            EnsureFolder(PrefabsFolder);
            EnsureFolder(UiFolder);
            EnsureFolder(UiPrefabsFolder);
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

        private static void CreateBootstrapScene(
            GameObject playerPrefab,
            GameObject lobbyArenaPrefab)
        {
            var lobbyCanvasPrefab = LoadOrCreateLobbyCanvasPrefab();
            var scheduleTowerPrefab = LoadOrCreateScheduleTowerPrefab();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var matchSkybox = AssetDatabase.LoadAssetAtPath<Material>(
                MatchSkyboxPath);
            if (matchSkybox == null)
            {
                throw new System.InvalidOperationException(
                    "The shared match skybox is missing at " +
                    MatchSkyboxPath + ".");
            }
            RenderSettings.skybox = matchSkybox;

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

            var lobbyArena = (GameObject)PrefabUtility.InstantiatePrefab(
                lobbyArenaPrefab,
                scene);
            lobbyArena.transform.position = new Vector3(3.5f, 0f, 0f);

            var lobbyCanvas = (GameObject)PrefabUtility.InstantiatePrefab(
                lobbyCanvasPrefab,
                scene);
            lobbyCanvas.transform.localScale =
                lobbyCanvasPrefab.transform.localScale;
            var lobbyView = lobbyCanvas.GetComponent<OnlineLobbyView>();

            var scheduleTower = (GameObject)PrefabUtility.InstantiatePrefab(
                scheduleTowerPrefab,
                scene);
            scheduleTower.transform.localScale =
                scheduleTowerPrefab.transform.localScale;
            GameMenuProjectSetup.EnsureSceneInstance(scene);
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
            networkManager.NetworkConfig.ConnectionApproval = true;
            networkManager.NetworkConfig.ForceSamePrefabs = true;

            var sessionController = runtime.AddComponent<OnlineSessionController>();
            sessionController.ConfigureSceneReferences(camera, light, lobbyView);

            EditorSceneManager.SaveScene(scene, BootstrapPath);
        }

        private static GameObject LoadOrCreateLobbyCanvasPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                LobbyCanvasPrefabPath);
            if (prefab == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(LobbyCanvasPrefabPath) != null)
                {
                    throw new System.InvalidOperationException(
                        "An incompatible asset already exists at " +
                        LobbyCanvasPrefabPath + ".");
                }

                var template = CreateLobbyCanvasTemplate();
                try
                {
                    prefab = PrefabUtility.SaveAsPrefabAsset(
                        template.gameObject,
                        LobbyCanvasPrefabPath);
                }
                finally
                {
                    Object.DestroyImmediate(template.gameObject);
                }
            }

            var view = prefab != null
                ? prefab.GetComponent<OnlineLobbyView>()
                : null;
            if (view == null || !view.HasRequiredReferences)
            {
                throw new System.InvalidOperationException(
                    "LobbyCanvas.prefab is missing required OnlineLobbyView bindings. " +
                    "Repair the prefab without recreating it so designer changes are preserved.");
            }

            return prefab;
        }

        private static OnlineLobbyView CreateLobbyCanvasTemplate()
        {
            var font = Resources.Load<Font>("MazeParty/Fonts/PlayerNameFont");
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

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
                typeof(LocalizedFontScope),
                typeof(OnlineLobbyView));
            canvasObject.transform.localScale = Vector3.one;
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
            var openJoinPopupButton = CreateButton(
                "Open Join Popup Button",
                connectionPanel.transform,
                "Join by Code",
                font,
                out _);

            var joinCodePopup = CreateVerticalContainer(
                "Join Code Popup",
                window.transform,
                220f);

            CreateText(
                "Invite Code Label",
                joinCodePopup.transform,
                "Invite Code",
                font,
                18,
                TextAnchor.MiddleLeft,
                26f);
            var joinCodeInput = CreateInputField(
                "Invite Code Input",
                joinCodePopup.transform,
                "Enter invite code",
                font,
                InputField.ContentType.Custom,
                16);
            joinCodeInput.inputType = InputField.InputType.Password;
            joinCodeInput.characterValidation = InputField.CharacterValidation.Alphanumeric;
            joinCodeInput.asteriskChar = '*';
            var joinButton = CreateButton(
                "Join by Code Button",
                joinCodePopup.transform,
                "Join by Code",
                font,
                out _);
            var cancelJoinButton = CreateButton(
                "Cancel Join Button",
                joinCodePopup.transform,
                "Cancel",
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
                700f);
            var inviteCodeText = CreateText(
                "Invite Code",
                sessionPanel.transform,
                "Invite Code: -",
                font,
                20,
                TextAnchor.MiddleLeft,
                30f);
            var revealCodeButton = CreateButton(
                "View Code Button",
                sessionPanel.transform,
                "View Code",
                font,
                out _);
            var revealControl =
                revealCodeButton.gameObject.AddComponent<HoldToRevealButton>();
            revealControl.Configure(revealCodeButton);
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

            var boardMapSelectionRoot = CreateBoardMapSelectionRow(
                sessionPanel.transform,
                font,
                out var previousBoardMapButton,
                out var nextBoardMapButton,
                out var boardMapNameText);

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
            var customizationButton = CreateButton(
                "Wardrobe Button",
                sessionPanel.transform,
                "Wardrobe",
                font,
                out _);
            var customizationPanel = CreateLobbyCustomization(
                canvasObject.transform,
                font,
                out var paletteButtons,
                out var paletteOutlines);

            var statusText = CreateText(
                "Status",
                window.transform,
                "Create a private room or join with an invite code.",
                font,
                16,
                TextAnchor.UpperLeft,
                52f);
            var buildVersionText = CreateText(
                "Build Version",
                canvasObject.transform,
                BuildVersionCompatibility.DisplayText,
                font,
                18,
                TextAnchor.MiddleLeft,
                32f);
            Object.DestroyImmediate(
                buildVersionText.GetComponent<LayoutElement>());
            var buildVersionRect = buildVersionText.rectTransform;
            buildVersionRect.anchorMin = Vector2.zero;
            buildVersionRect.anchorMax = Vector2.zero;
            buildVersionRect.pivot = Vector2.zero;
            buildVersionRect.anchoredPosition = new Vector2(24f, 24f);
            buildVersionRect.sizeDelta = new Vector2(240f, 32f);
            buildVersionText.fontStyle = FontStyle.Bold;
            buildVersionText.color = new Color(0.75f, 0.89f, 1f, 0.92f);
            var buildVersionShadow =
                buildVersionText.gameObject.AddComponent<Shadow>();
            buildVersionShadow.effectColor =
                new Color(0.015f, 0.025f, 0.06f, 0.85f);
            buildVersionShadow.effectDistance = new Vector2(1f, -1f);
            buildVersionShadow.useGraphicAlpha = true;

            sessionPanel.SetActive(false);
            joinCodePopup.SetActive(false);
            customizationPanel.SetActive(false);
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
                inviteCodeText,
                sessionSummaryText,
                readyButtonText,
                startButtonText,
                playerRows,
                boardMapSelectionRoot,
                previousBoardMapButton,
                nextBoardMapButton,
                boardMapNameText,
                startHintText.gameObject,
                runningText.gameObject,
                statusText,
                buildVersionText,
                customizationPanel,
                paletteButtons,
                paletteOutlines);
            lobbyView.ConfigureInteractionPanels(
                joinCodePopup,
                openJoinPopupButton,
                cancelJoinButton,
                revealControl,
                customizationButton);
            PlayerExpressionAuthoring.EnsureLobbySelectors(canvasObject);
            return lobbyView;
        }

        private static GameObject CreateLobbyCustomization(
            Transform parent,
            Font font,
            out Button[] paletteButtons,
            out Outline[] paletteOutlines)
        {
            var customizationPanel = CreateUiObject(
                "Player Customization",
                parent);
            var customizationRect = customizationPanel.GetComponent<RectTransform>();
            customizationRect.anchorMin = new Vector2(1f, 0.5f);
            customizationRect.anchorMax = new Vector2(1f, 0.5f);
            customizationRect.pivot = new Vector2(1f, 0.5f);
            customizationRect.anchoredPosition = new Vector2(-24f, 0f);
            customizationRect.sizeDelta = new Vector2(520f, 360f);
            var panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                RoundedPanelSpritePath);
            if (panelSprite == null)
            {
                throw new System.InvalidOperationException(
                    "Wardrobe panel sprite is missing at " +
                    RoundedPanelSpritePath + ".");
            }

            var panelImage = customizationPanel.AddComponent<Image>();
            panelImage.sprite = panelSprite;
            panelImage.type = Image.Type.Sliced;
            panelImage.color = new Color(0.045f, 0.035f, 0.14f, 0.98f);
            panelImage.raycastTarget = false;
            var layout = customizationPanel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var panelSize = customizationPanel.AddComponent<LayoutElement>();
            panelSize.preferredHeight = 360f;
            panelSize.enabled = false;

            var colorLabel = CreateText(
                "Color Label",
                customizationPanel.transform,
                "Color",
                font,
                20,
                TextAnchor.MiddleLeft,
                28f);
            colorLabel.gameObject.AddComponent<LocalizedText>().Configure("Color");

            var paletteGrid = CreateUiObject(
                "Body Color Palette",
                customizationPanel.transform);
            var grid = paletteGrid.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(102f, 36f);
            grid.spacing = new Vector2(8f, 8f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            grid.childAlignment = TextAnchor.MiddleLeft;
            var gridSize = paletteGrid.AddComponent<LayoutElement>();
            gridSize.preferredHeight = 88f;

            paletteButtons = new Button[LobbyColorPalette.Count];
            paletteOutlines = new Outline[LobbyColorPalette.Count];
            for (var index = 0; index < LobbyColorPalette.Count; index++)
            {
                var buttonObject = CreateUiObject(
                    LobbyColorPalette.GetDisplayName(index) + " Color Button",
                    paletteGrid.transform);
                var image = buttonObject.AddComponent<Image>();
                image.color = LobbyColorPalette.GetColor(index);
                var outline = buttonObject.AddComponent<Outline>();
                outline.effectColor = Color.white;
                outline.effectDistance = new Vector2(3f, -3f);
                var button = buttonObject.AddComponent<Button>();
                button.targetGraphic = image;
                var colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1f, 1f, 1f, 0.82f);
                colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
                colors.selectedColor = colors.highlightedColor;
                colors.disabledColor = new Color(0.2f, 0.2f, 0.2f, 0.32f);
                button.colors = colors;

                var label = CreateText(
                    "Label",
                    buttonObject.transform,
                    LobbyColorPalette.GetDisplayName(index),
                    font,
                    14,
                    TextAnchor.MiddleCenter,
                    36f);
                Object.DestroyImmediate(label.GetComponent<LayoutElement>());
                SetStretch(label.rectTransform, 4f, 4f, 2f, 2f);
                label.color = index == 2 ? Color.black : Color.white;

                paletteButtons[index] = button;
                paletteOutlines[index] = outline;
            }

            var footer = CreateUiObject(
                "Customization Footer",
                customizationPanel.transform);
            var footerLayout = footer.AddComponent<VerticalLayoutGroup>();
            footerLayout.spacing = 8f;
            footerLayout.childAlignment = TextAnchor.UpperLeft;
            footerLayout.childControlWidth = true;
            footerLayout.childControlHeight = true;
            footerLayout.childForceExpandWidth = true;
            footerLayout.childForceExpandHeight = false;
            var footerSize = footer.AddComponent<LayoutElement>();
            footerSize.preferredHeight = 176f;

            return customizationPanel;
        }

        private static GameObject LoadOrCreateScheduleTowerPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                MinigameScheduleTowerPrefabPath);
            if (prefab == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(
                        MinigameScheduleTowerPrefabPath) != null)
                {
                    throw new System.InvalidOperationException(
                        "An incompatible asset already exists at " +
                        MinigameScheduleTowerPrefabPath + ".");
                }

                var template = CreateScheduleTowerTemplate();
                try
                {
                    prefab = PrefabUtility.SaveAsPrefabAsset(
                        template,
                        MinigameScheduleTowerPrefabPath);
                }
                finally
                {
                    Object.DestroyImmediate(template);
                }
            }

            var view = prefab != null
                ? prefab.GetComponent<MinigameScheduleTowerView>()
                : null;
            if (view == null || !view.HasRequiredReferences)
            {
                throw new System.InvalidOperationException(
                    "MinigameScheduleTower.prefab is missing required UI bindings. " +
                    "Repair the prefab without recreating it so designer changes are preserved.");
            }

            return prefab;
        }

        private static GameObject CreateScheduleTowerTemplate()
        {
            var font = Resources.Load<Font>("MazeParty/Fonts/PlayerNameFont");
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            if (font == null)
            {
                throw new System.InvalidOperationException(
                    "A font is required to build MinigameScheduleTower.prefab.");
            }

            var root = new GameObject(
                "Minigame Schedule Tower",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(MinigameScheduleTowerView));
            root.transform.localScale = Vector3.one;
            root.layer = LayerMask.NameToLayer("UI");

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 55;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var panelObject = CreateUiObject("Tower Panel", root.transform);
            var panel = panelObject.GetComponent<RectTransform>();
            panel.anchorMin = new Vector2(1f, 0.5f);
            panel.anchorMax = new Vector2(1f, 0.5f);
            panel.pivot = new Vector2(1f, 0.5f);
            panel.anchoredPosition = new Vector2(-30f, 0f);
            panel.sizeDelta = new Vector2(310f, 860f);
            var panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0.02f, 0.03f, 0.055f, 0.93f);
            panelImage.raycastTarget = false;

            var title = CreateTowerText(
                "Title",
                panel,
                "MINIGAME TOWER",
                font,
                new Vector2(0f, -24f),
                new Vector2(280f, 42f),
                25,
                FontStyle.Bold);
            var subtitle = CreateTowerText(
                "Subtitle",
                panel,
                "TURN 1  ·  15 BLOCKS LEFT",
                font,
                new Vector2(0f, -66f),
                new Vector2(280f, 32f),
                17,
                FontStyle.Normal);

            var blocks = new Image[MinigameScheduleTowerView.MaximumVisibleBlocks];
            var blockLabels = new Text[blocks.Length];
            for (var index = 0; index < blocks.Length; index++)
            {
                var blockObject = CreateUiObject(
                    "Block " + (index + 1),
                    panel);
                var blockRect = blockObject.GetComponent<RectTransform>();
                blockRect.anchorMin = new Vector2(0.5f, 1f);
                blockRect.anchorMax = new Vector2(0.5f, 1f);
                blockRect.pivot = new Vector2(0.5f, 1f);
                blockRect.anchoredPosition =
                    new Vector2(0f, -108f - index * 48f);
                blockRect.sizeDelta = new Vector2(
                    264f - Mathf.Min(index, 8) * 4f,
                    40f);
                blocks[index] = blockObject.AddComponent<Image>();
                blocks[index].color = new Color(0.12f, 0.16f, 0.23f, 0.98f);
                blocks[index].raycastTarget = false;
                blockLabels[index] = CreateTowerText(
                    "Label",
                    blockRect,
                    "???",
                    font,
                    Vector2.zero,
                    blockRect.sizeDelta,
                    18,
                    index == 0 ? FontStyle.Bold : FontStyle.Normal);
            }

            root.GetComponent<MinigameScheduleTowerView>().Configure(
                canvas,
                panel,
                title,
                subtitle,
                blocks,
                blockLabels);
            return root;
        }

        private static Text CreateTowerText(
            string name,
            Transform parent,
            string value,
            Font font,
            Vector2 anchoredPosition,
            Vector2 size,
            int fontSize,
            FontStyle style)
        {
            var textObject = CreateUiObject(name, parent);
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var text = textObject.AddComponent<Text>();
            text.text = value;
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
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

        private static GameObject CreateBoardMapSelectionRow(
            Transform parent,
            Font font,
            out Button previousButton,
            out Button nextButton,
            out Text mapNameText)
        {
            var row = CreateUiObject("Board Map Selection", parent);
            var rowRect = row.GetComponent<RectTransform>();
            rowRect.sizeDelta = new Vector2(rowRect.sizeDelta.x, 48f);
            var background = row.AddComponent<Image>();
            background.color = new Color(0.05f, 0.08f, 0.13f, 0.94f);
            background.raycastTarget = false;

            var layout = row.AddComponent<LayoutElement>();
            layout.preferredHeight = 48f;

            previousButton = CreateBoardMapArrowButton(
                "Previous Map Button",
                row.transform,
                "<",
                font,
                true);
            nextButton = CreateBoardMapArrowButton(
                "Next Map Button",
                row.transform,
                ">",
                font,
                false);

            mapNameText = CreateText(
                "Map Name",
                row.transform,
                "Map: Unavailable Map",
                font,
                18,
                TextAnchor.MiddleCenter,
                40f);
            Object.DestroyImmediate(mapNameText.GetComponent<LayoutElement>());
            SetStretch(mapNameText.rectTransform, 62f, 62f, 4f, 4f);
            mapNameText.fontStyle = FontStyle.Bold;
            return row;
        }

        private static Button CreateBoardMapArrowButton(
            string name,
            Transform parent,
            string label,
            Font font,
            bool previous)
        {
            var buttonObject = CreateUiObject(name, parent);
            var rect = buttonObject.GetComponent<RectTransform>();
            var anchor = previous ? 0f : 1f;
            rect.anchorMin = new Vector2(anchor, 0.5f);
            rect.anchorMax = new Vector2(anchor, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(previous ? 27f : -27f, 0f);
            rect.sizeDelta = new Vector2(44f, 36f);

            var image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.12f, 0.25f, 0.32f, 1f);
            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.9f, 0.96f, 1f, 1f);
            colors.pressedColor = new Color(0.7f, 0.82f, 0.9f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.42f, 0.46f, 0.5f, 0.55f);
            button.colors = colors;
            buttonObject.AddComponent<UiSoundEmitter>();

            var labelText = CreateText(
                "Arrow",
                buttonObject.transform,
                label,
                font,
                22,
                TextAnchor.MiddleCenter,
                32f);
            Object.DestroyImmediate(labelText.GetComponent<LayoutElement>());
            SetStretch(labelText.rectTransform, 4f, 4f, 2f, 2f);
            labelText.fontStyle = FontStyle.Bold;
            return button;
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

        private static GameObject FindChild(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(candidate => candidate.name == name)
                ?.gameObject;
        }

        private static Transform FindRequiredChild(Transform root, string name)
        {
            var child = FindChild(root, name);
            if (child == null)
            {
                throw new System.InvalidOperationException(
                    "LobbyCanvas.prefab is missing authored object '" + name + "'.");
            }

            return child.transform;
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
                new EditorBuildSettingsScene(WrongWayPath, true),
                new EditorBuildSettingsScene(RedLightGreenLightPath, true),
                new EditorBuildSettingsScene(StableFootingPath, true),
                new EditorBuildSettingsScene(BalloonBlowPath, true),
                new EditorBuildSettingsScene(GiftGrabPath, true),
                new EditorBuildSettingsScene(TerritoryPaintPath, true),
                new EditorBuildSettingsScene(TagChasePath, true),
                new EditorBuildSettingsScene(RacePath, true)
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
