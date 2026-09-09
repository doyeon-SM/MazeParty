using System;
using MazeParty.Dev.MinigameSoloTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        private const string QuickPlayMenuPath =
            "MazeParty/Developer/Play Minefield Solo";
        private const string QuickPlayWrongWayMenuPath =
            "MazeParty/Developer/Play WrongWay Solo";
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
            return minefield != null
                ? (Component)minefield
                : FindRuntimeHarnessOfType<
                    WrongWaySoloTestController>();
        }

        private static void DestroyRuntimeHarnesses()
        {
            DestroyRuntimeHarnessesOfType<
                MinefieldSoloTestController>();
            DestroyRuntimeHarnessesOfType<
                WrongWaySoloTestController>();
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
