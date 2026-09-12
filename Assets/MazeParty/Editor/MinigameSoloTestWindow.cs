using System;
using MazeParty.Dev.MinigameSoloTest;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.EditorTools
{
    public sealed class MinigameSoloTestWindow : EditorWindow
    {
        private const string SelectedIndexKey =
            "MazeParty.MinigameSoloTest.SelectedIndex";
        private const string SeedKey =
            "MazeParty.MinigameSoloTest.Seed";
        private const string RandomSeedKey =
            "MazeParty.MinigameSoloTest.RandomSeed";

        private int _selectedIndex;
        private int _seed;
        private bool _randomSeed;

        [MenuItem("MazeParty/Developer/Minigame Solo Tester")]
        private static void Open()
        {
            var window = GetWindow<MinigameSoloTestWindow>();
            window.titleContent = new GUIContent("Minigame Solo");
            window.minSize = new Vector2(420f, 260f);
            window.Show();
        }

        private void OnEnable()
        {
            _selectedIndex = EditorPrefs.GetInt(SelectedIndexKey, 0);
            _seed = EditorPrefs.GetInt(SeedKey, 12345);
            _randomSeed = EditorPrefs.GetBool(RandomSeedKey, true);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "Minigame Solo Tester",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Runs the selected production minigame scene with a local " +
                "one-player harness. No room, services sign-in, or extra " +
                "players are required.",
                MessageType.Info);

            var descriptors = MinigameSoloTestCatalog.All;
            var names = new string[descriptors.Count];
            for (var index = 0; index < descriptors.Count; index++)
            {
                names[index] = descriptors[index].DisplayName;
            }

            _selectedIndex = Mathf.Clamp(
                _selectedIndex,
                0,
                Mathf.Max(0, descriptors.Count - 1));
            _selectedIndex = EditorGUILayout.Popup(
                "Minigame",
                _selectedIndex,
                names);
            _randomSeed = EditorGUILayout.Toggle(
                "Random seed on launch",
                _randomSeed);
            using (new EditorGUI.DisabledScope(_randomSeed))
            {
                _seed = EditorGUILayout.IntField("Seed", _seed);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "Controls",
                GetControlsLabel(
                    descriptors[_selectedIndex].Id));
            EditorGUILayout.HelpBox(
                "This harness validates local controls, penalties, presentation, " +
                "round timing, and deterministic layouts. NGO RPC and server " +
                "authority still require the multiplayer test flow.",
                MessageType.None);

            GUILayout.FlexibleSpace();
            if (MinigameSoloTestLauncher.IsActive)
            {
                if (GUILayout.Button("Stop Solo Test", GUILayout.Height(34f)))
                {
                    MinigameSoloTestLauncher.Stop();
                }
            }
            else
            {
                using (new EditorGUI.DisabledScope(
                           descriptors.Count == 0 ||
                           !MinigameSoloTestLauncher.CanStart))
                {
                    if (GUILayout.Button(
                            "Play Solo",
                            GUILayout.Height(34f)))
                    {
                        SavePreferences();
                        var seed = _randomSeed
                            ? MinigameSoloTestLauncher.CreateRandomSeed()
                            : _seed;
                        MinigameSoloTestLauncher.Start(
                            descriptors[_selectedIndex].Id,
                            seed);
                    }
                }
            }
        }

        private void SavePreferences()
        {
            EditorPrefs.SetInt(SelectedIndexKey, _selectedIndex);
            EditorPrefs.SetInt(SeedKey, _seed);
            EditorPrefs.SetBool(RandomSeedKey, _randomSeed);
        }

        private static string GetControlsLabel(
            MinigameSoloTestId id)
        {
            switch (id)
            {
                case MinigameSoloTestId.WrongWay:
                    return "WASD match prompt · R restart · " +
                           "N next seed · Esc stop";
                case MinigameSoloTestId.RedLightGreenLight:
                    return "WASD move on green · freeze on red · " +
                           "R restart · N next seed · Esc stop";
                case MinigameSoloTestId.Minefield:
                default:
                    return "WASD move · stop + RMB sonar · " +
                           "R restart · N next seed · Esc stop";
            }
        }

        internal static void RepaintOpenWindows()
        {
            var windows =
                Resources.FindObjectsOfTypeAll<MinigameSoloTestWindow>();
            for (var index = 0; index < windows.Length; index++)
            {
                windows[index].Repaint();
            }
        }
    }

    [InitializeOnLoad]
    internal static class MinigameSoloTestLauncher
    {
        public const string HudPrefabPath =
            "Assets/MazeParty/UI/Prefabs/Dev/MinigameSoloHud.prefab";

        private const string QuickPlayMenuPath =
            "MazeParty/Developer/Play Minefield Solo";
        private const string QuickPlayWrongWayMenuPath =
            "MazeParty/Developer/Play WrongWay Solo";
        private const string QuickPlayRedLightGreenLightMenuPath =
            "MazeParty/Developer/Play Red Light Green Light Solo";
        private const string ActiveKey =
            "MazeParty.MinigameSoloTest.Active";
        private const string TestIdKey =
            "MazeParty.MinigameSoloTest.Id";
        private const string TestSeedKey =
            "MazeParty.MinigameSoloTest.RuntimeSeed";
        private const string PreviousStartSceneKey =
            "MazeParty.MinigameSoloTest.PreviousStartScene";
        private const string StartSceneRestoredKey =
            "MazeParty.MinigameSoloTest.StartSceneRestored";

        static MinigameSoloTestLauncher()
        {
            EditorApplication.playModeStateChanged -=
                HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged +=
                HandlePlayModeStateChanged;
            EditorApplication.quitting -= HandleEditorQuitting;
            EditorApplication.quitting += HandleEditorQuitting;

            if (!SessionState.GetBool(ActiveKey, false))
            {
                return;
            }

            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += FinishSession;
            }
        }

        internal static bool IsActive =>
            SessionState.GetBool(ActiveKey, false);

        internal static bool CanStart =>
            !EditorApplication.isCompiling &&
            !EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem(QuickPlayMenuPath, false, 2100)]
        private static void QuickPlayMinefield()
        {
            Start(
                MinigameSoloTestId.Minefield,
                CreateRandomSeed());
        }

        [MenuItem(QuickPlayMenuPath, true)]
        private static bool ValidateQuickPlayMinefield()
        {
            return CanStart;
        }

        [MenuItem(QuickPlayWrongWayMenuPath, false, 2101)]
        private static void QuickPlayWrongWay()
        {
            Start(
                MinigameSoloTestId.WrongWay,
                CreateRandomSeed());
        }

        [MenuItem(QuickPlayWrongWayMenuPath, true)]
        private static bool ValidateQuickPlayWrongWay()
        {
            return CanStart;
        }

        [MenuItem(QuickPlayRedLightGreenLightMenuPath, false, 2102)]
        private static void QuickPlayRedLightGreenLight()
        {
            Start(
                MinigameSoloTestId.RedLightGreenLight,
                CreateRandomSeed());
        }

        [MenuItem(QuickPlayRedLightGreenLightMenuPath, true)]
        private static bool ValidateQuickPlayRedLightGreenLight()
        {
            return CanStart;
        }

        internal static int CreateRandomSeed()
        {
            return unchecked(
                (int)(DateTime.UtcNow.Ticks ^ Environment.TickCount));
        }

        internal static bool Start(
            MinigameSoloTestId id,
            int seed)
        {
            if (!CanStart || IsActive)
            {
                return false;
            }
            if (!MinigameSoloTestCatalog.TryGet(id, out var descriptor))
            {
                Debug.LogError(
                    "[Minigame Solo Test] Unknown minigame id: " + id);
                return false;
            }

            try
            {
                EnsureMinigameSoloHudPrefab();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }

            var sceneAsset =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    descriptor.ScenePath);
            if (sceneAsset == null)
            {
                Debug.LogError(
                    "[Minigame Solo Test] Scene not found: " +
                    descriptor.ScenePath);
                return false;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return false;
            }

            DestroyRuntimeHarnesses();

            var previousStartScene =
                EditorSceneManager.playModeStartScene;
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetInt(TestIdKey, (int)id);
            SessionState.SetInt(TestSeedKey, seed);
            SessionState.SetString(
                PreviousStartSceneKey,
                previousStartScene != null
                    ? AssetDatabase.GetAssetPath(previousStartScene)
                    : string.Empty);
            SessionState.SetBool(StartSceneRestoredKey, false);

            try
            {
                EditorSceneManager.playModeStartScene = sceneAsset;
                EditorApplication.isPlaying = true;
                RepaintWindows();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                FinishSession();
                return false;
            }
        }

        internal static void Stop()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
            }
            else
            {
                FinishSession();
            }
        }

        private static void HandlePlayModeStateChanged(
            PlayModeStateChange state)
        {
            if (!IsActive)
            {
                return;
            }

            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    RestorePlayModeStartScene();
                    EditorApplication.delayCall += InjectRuntimeHarness;
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    FinishSession();
                    break;
            }

            RepaintWindows();
        }

        private static void InjectRuntimeHarness()
        {
            if (!IsActive || !EditorApplication.isPlaying)
            {
                return;
            }
            if (FindRuntimeHarness() != null)
            {
                return;
            }

            var id = (MinigameSoloTestId)SessionState.GetInt(
                TestIdKey,
                (int)MinigameSoloTestId.Minefield);
            if (!MinigameSoloTestCatalog.TryGet(id, out var descriptor))
            {
                FailAndStop("Unknown minigame id: " + id);
                return;
            }
            if (!string.Equals(
                    SceneManager.GetActiveScene().path,
                    descriptor.ScenePath,
                    StringComparison.Ordinal))
            {
                FailAndStop(
                    "Expected " + descriptor.ScenePath +
                    " but Play Mode loaded " +
                    SceneManager.GetActiveScene().path + ".");
                return;
            }

            try
            {
                switch (id)
                {
                    case MinigameSoloTestId.Minefield:
                    {
                        var bootstrap = new GameObject(
                            "[Developer] Minigame Solo Test");
                        var controller =
                            bootstrap.AddComponent<
                                MinefieldSoloTestController>();
                        if (controller == null)
                        {
                            throw new InvalidOperationException(
                                "Could not attach the Minefield solo harness.");
                        }
                        controller.ConfigureHud(
                            InstantiateSoloHud(bootstrap.transform));
                        controller.Begin(
                            SessionState.GetInt(TestSeedKey, 12345));
                        break;
                    }
                    case MinigameSoloTestId.WrongWay:
                    {
                        var bootstrap = new GameObject(
                            "[Developer] Minigame Solo Test");
                        var controller =
                            bootstrap.AddComponent<
                                WrongWaySoloTestController>();
                        if (controller == null)
                        {
                            throw new InvalidOperationException(
                                "Could not attach the WrongWay solo harness.");
                        }
                        controller.ConfigureHud(
                            InstantiateSoloHud(bootstrap.transform));
                        controller.Begin(
                            SessionState.GetInt(TestSeedKey, 12345));
                        break;
                    }
                    case MinigameSoloTestId.RedLightGreenLight:
                    {
                        var bootstrap = new GameObject(
                            "[Developer] Minigame Solo Test");
                        var controller =
                            bootstrap.AddComponent<
                                RedLightGreenLightSoloTestController>();
                        if (controller == null)
                        {
                            throw new InvalidOperationException(
                                "Could not attach the Red Light, Green Light " +
                                "solo harness.");
                        }
                        controller.ConfigureHud(
                            InstantiateSoloHud(bootstrap.transform));
                        controller.Begin(
                            SessionState.GetInt(TestSeedKey, 12345));
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(id));
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.isPlaying = false;
            }
        }

        internal static GameObject EnsureMinigameSoloHudPrefab()
        {
            var existing =
                AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
            if (existing != null)
            {
                ValidateMinigameSoloHudPrefab(existing);
                return existing;
            }

            EnsureAssetFolder("Assets/MazeParty/UI");
            EnsureAssetFolder("Assets/MazeParty/UI/Prefabs");
            EnsureAssetFolder("Assets/MazeParty/UI/Prefabs/Dev");

            var font =
                Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(
                "UI/Skin/UISprite.psd");
            var root = new GameObject(
                "MinigameSoloHud",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(MinigameSoloHudView));
            root.transform.localScale = Vector3.one;

            try
            {
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 200;
                var scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                var eventSystemObject = new GameObject(
                    "EventSystem",
                    typeof(EventSystem),
                    typeof(InputSystemUIInputModule));
                eventSystemObject.transform.SetParent(root.transform, false);
                eventSystemObject
                    .GetComponent<InputSystemUIInputModule>()
                    .AssignDefaultActions();

                var panel = CreateHudPanel(
                    root.transform,
                    "SoloHudPanel",
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(18f, -18f),
                    new Vector2(570f, 370f),
                    new Color(0.025f, 0.04f, 0.065f, 0.96f),
                    sprite);

                var header = CreateHudText(
                    panel.transform,
                    "HeaderText",
                    "DEVELOPER SOLO TEST",
                    new Vector2(0f, -14f),
                    new Vector2(530f, 28f),
                    16,
                    TextAnchor.MiddleLeft,
                    new Color(0.3f, 0.9f, 1f),
                    font,
                    FontStyle.Bold);
                var primaryStatus = CreateHudText(
                    panel.transform,
                    "PrimaryStatusText",
                    "ROUND 1 / 3  ·  READY  ·  00:00",
                    new Vector2(0f, -47f),
                    new Vector2(530f, 28f),
                    16,
                    TextAnchor.MiddleLeft,
                    Color.white,
                    font,
                    FontStyle.Bold);
                var secondaryStatus = CreateHudText(
                    panel.transform,
                    "SecondaryStatusText",
                    "STATUS",
                    new Vector2(0f, -78f),
                    new Vector2(530f, 42f),
                    15,
                    TextAnchor.MiddleLeft,
                    new Color(0.78f, 0.88f, 1f),
                    font,
                    FontStyle.Bold);

                var featurePanel = CreateHudPanel(
                    panel.transform,
                    "FeaturePanel",
                    new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f),
                    new Vector2(0f, -126f),
                    new Vector2(530f, 72f),
                    new Color(0.08f, 0.13f, 0.21f, 0.96f),
                    sprite);
                var feature = CreateHudText(
                    featurePanel.transform,
                    "FeatureText",
                    "GET READY",
                    Vector2.zero,
                    new Vector2(505f, 62f),
                    28,
                    TextAnchor.MiddleCenter,
                    Color.white,
                    font,
                    FontStyle.Bold,
                    new Vector2(0.5f, 0.5f));
                var help = CreateHudText(
                    panel.transform,
                    "HelpText",
                    "Controls",
                    new Vector2(0f, -207f),
                    new Vector2(530f, 40f),
                    14,
                    TextAnchor.MiddleCenter,
                    new Color(0.72f, 0.82f, 0.94f),
                    font);
                var feedback = CreateHudText(
                    panel.transform,
                    "FeedbackText",
                    string.Empty,
                    new Vector2(0f, -249f),
                    new Vector2(530f, 28f),
                    15,
                    TextAnchor.MiddleCenter,
                    Color.white,
                    font,
                    FontStyle.Bold);

                var restart = CreateHudButton(
                    panel.transform,
                    "RestartButton",
                    "Restart Round (R)",
                    new Vector2(-135f, 54f),
                    new Vector2(255f, 38f),
                    new Color(0.16f, 0.4f, 0.64f),
                    font,
                    sprite);
                var nextSeed = CreateHudButton(
                    panel.transform,
                    "NextSeedButton",
                    "Next Seed (N)",
                    new Vector2(135f, 54f),
                    new Vector2(255f, 38f),
                    new Color(0.22f, 0.5f, 0.34f),
                    font,
                    sprite);
                var stop = CreateHudButton(
                    panel.transform,
                    "StopButton",
                    "Stop Solo Test (Esc)",
                    new Vector2(0f, 10f),
                    new Vector2(530f, 36f),
                    new Color(0.5f, 0.2f, 0.24f),
                    font,
                    sprite);

                var view = root.GetComponent<MinigameSoloHudView>();
                view.Configure(
                    header,
                    primaryStatus,
                    secondaryStatus,
                    feature,
                    help,
                    feedback,
                    restart,
                    nextSeed,
                    stop,
                    Color.white,
                    new Color(0.35f, 1f, 0.55f, 1f),
                    new Color(1f, 0.72f, 0.15f, 1f),
                    new Color(1f, 0.36f, 0.28f, 1f));
                EditorUtility.SetDirty(view);

                var created = PrefabUtility.SaveAsPrefabAsset(
                    root,
                    HudPrefabPath);
                if (created == null)
                {
                    throw new InvalidOperationException(
                        "Could not create " + HudPrefabPath + ".");
                }

                ValidateMinigameSoloHudPrefab(created);
                return created;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [MenuItem(
            "MazeParty/Developer/Ensure Minigame Solo HUD Prefab",
            false,
            2110)]
        private static void EnsureMinigameSoloHudPrefabFromMenu()
        {
            var prefab = EnsureMinigameSoloHudPrefab();
            AssetDatabase.SaveAssets();
            Selection.activeObject = prefab;
            Debug.Log(
                "Minigame solo HUD prefab is ready at " +
                HudPrefabPath + ". Existing design was preserved.");
        }

        private static MinigameSoloHudView InstantiateSoloHud(
            Transform parent)
        {
            var prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Missing minigame solo HUD prefab at " + HudPrefabPath +
                    ". Exit Play Mode and launch the test again to create it.");
            }

            ValidateMinigameSoloHudPrefab(prefab);
            var instance = UnityEngine.Object.Instantiate(
                prefab,
                parent,
                false);
            instance.name = "Minigame Solo HUD";
            var view = instance.GetComponent<MinigameSoloHudView>();
            if (view == null || !view.HasRequiredReferences)
            {
                UnityEngine.Object.Destroy(instance);
                throw new InvalidOperationException(
                    "The minigame solo HUD prefab contract is invalid.");
            }
            return view;
        }

        private static void ValidateMinigameSoloHudPrefab(GameObject prefab)
        {
            var view = prefab.GetComponent<MinigameSoloHudView>();
            if (prefab.GetComponent<Canvas>() == null ||
                view == null ||
                !view.HasRequiredReferences ||
                prefab.GetComponentInChildren<EventSystem>(true) == null ||
                prefab.GetComponentInChildren<InputSystemUIInputModule>(true) ==
                null)
            {
                throw new InvalidOperationException(
                    HudPrefabPath + " is missing required bindings. " +
                    "Repair the prefab instead of rebuilding it so custom " +
                    "design changes are preserved.");
            }
        }

        private static GameObject CreateHudPanel(
            Transform parent,
            string name,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 position,
            Vector2 size,
            Color color,
            Sprite sprite)
        {
            var panel = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image));
            var rect = panel.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var image = panel.GetComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            if (sprite != null)
            {
                image.type = Image.Type.Sliced;
            }
            return panel;
        }

        private static Text CreateHudText(
            Transform parent,
            string name,
            string value,
            Vector2 position,
            Vector2 size,
            int fontSize,
            TextAnchor alignment,
            Color color,
            Font font,
            FontStyle fontStyle = FontStyle.Normal,
            Vector2? anchor = null)
        {
            var textObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Text));
            var rect = textObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var resolvedAnchor = anchor ?? new Vector2(0.5f, 1f);
            rect.anchorMin = rect.anchorMax = resolvedAnchor;
            rect.pivot = resolvedAnchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var text = textObject.GetComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static Button CreateHudButton(
            Transform parent,
            string name,
            string label,
            Vector2 position,
            Vector2 size,
            Color color,
            Font font,
            Sprite sprite)
        {
            var buttonObject = CreateHudPanel(
                parent,
                name,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                position,
                size,
                color,
                sprite);
            var image = buttonObject.GetComponent<Image>();
            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation
            {
                mode = Navigation.Mode.None
            };

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f);
            colors.disabledColor = new Color(0.35f, 0.35f, 0.35f, 0.75f);
            button.colors = colors;

            CreateHudText(
                buttonObject.transform,
                "Label",
                label,
                Vector2.zero,
                size - new Vector2(12f, 6f),
                15,
                TextAnchor.MiddleCenter,
                Color.white,
                font,
                FontStyle.Bold,
                new Vector2(0.5f, 0.5f));
            return button;
        }

        private static void EnsureAssetFolder(string path)
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
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(
                parent,
                path.Substring(slash + 1));
        }

        private static void FailAndStop(string message)
        {
            Debug.LogError("[Minigame Solo Test] " + message);
            EditorApplication.isPlaying = false;
        }

        private static void RestorePlayModeStartScene()
        {
            if (!IsActive ||
                SessionState.GetBool(StartSceneRestoredKey, false))
            {
                return;
            }

            var previousPath = SessionState.GetString(
                PreviousStartSceneKey,
                string.Empty);
            EditorSceneManager.playModeStartScene =
                string.IsNullOrEmpty(previousPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<SceneAsset>(
                        previousPath);
            SessionState.SetBool(StartSceneRestoredKey, true);
        }

        private static void FinishSession()
        {
            if (!IsActive)
            {
                return;
            }

            DestroyRuntimeHarnesses();
            RestorePlayModeStartScene();
            SessionState.EraseBool(ActiveKey);
            SessionState.EraseInt(TestIdKey);
            SessionState.EraseInt(TestSeedKey);
            SessionState.EraseString(PreviousStartSceneKey);
            SessionState.EraseBool(StartSceneRestoredKey);
            RepaintWindows();
        }

        private static Component FindRuntimeHarness()
        {
            var minefield =
                FindRuntimeHarnessOfType<
                    MinefieldSoloTestController>();
            if (minefield != null)
            {
                return minefield;
            }

            var wrongWay =
                FindRuntimeHarnessOfType<
                    WrongWaySoloTestController>();
            return wrongWay != null
                ? (Component)wrongWay
                : FindRuntimeHarnessOfType<
                    RedLightGreenLightSoloTestController>();
        }

        private static void DestroyRuntimeHarnesses()
        {
            DestroyRuntimeHarnessesOfType<
                MinefieldSoloTestController>();
            DestroyRuntimeHarnessesOfType<
                WrongWaySoloTestController>();
            DestroyRuntimeHarnessesOfType<
                RedLightGreenLightSoloTestController>();
        }

        private static T FindRuntimeHarnessOfType<T>()
            where T : Component
        {
            var controllers =
                Resources.FindObjectsOfTypeAll<T>();
            for (var index = 0; index < controllers.Length; index++)
            {
                var controller = controllers[index];
                if (controller != null &&
                    !EditorUtility.IsPersistent(controller))
                {
                    return controller;
                }
            }

            return null;
        }

        private static void DestroyRuntimeHarnessesOfType<T>()
            where T : Component
        {
            var controllers =
                Resources.FindObjectsOfTypeAll<T>();
            for (var index = 0; index < controllers.Length; index++)
            {
                var controller = controllers[index];
                if (controller == null ||
                    EditorUtility.IsPersistent(controller))
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(
                    controller.gameObject);
            }
        }

        private static void HandleEditorQuitting()
        {
            RestorePlayModeStartScene();
        }

        private static void RepaintWindows()
        {
            MinigameSoloTestWindow.RepaintOpenWindows();
        }
    }
}
