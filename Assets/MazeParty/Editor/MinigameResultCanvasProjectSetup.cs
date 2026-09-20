using System;
using System.Linq;
using MazeParty.Gameplay.BoardFlowTestbed;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    /// <summary>
    /// One-time migration of the authored final-result panel out of BoardCanvas.
    /// Existing result visuals are cloned verbatim before the legacy child is removed.
    /// Subsequent runs only validate and bind prefab instances; they never restyle UI.
    /// </summary>
    public static class MinigameResultCanvasProjectSetup
    {
        public const string PrefabPath =
            "Assets/MazeParty/UI/Prefabs/MinigameResultCanvas.prefab";

        private const string BoardPrefabPath =
            "Assets/MazeParty/UI/Prefabs/BoardCanvas.prefab";
        private const string BoardScenePath =
            "Assets/MazeParty/Scenes/Board.unity";
        private const string TestbedScenePath =
            "Assets/MazeParty/Dev/BoardFlowTestbed/BoardFlowTestbed.unity";

        [MenuItem("MazeParty/UI/Migrate Final Minigame Result Canvas")]
        public static void MigrateProject()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Result Canvas migration requires Edit Mode.");
            }

            EnsurePrefabMigrated();
            EnsureSavedScene(BoardScenePath);
            EnsureSavedScene(TestbedScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("Final minigame results now use a separate prefab Canvas.");
        }

        internal static void EnsurePrefabMigrated()
        {
            var boardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                BoardPrefabPath);
            if (boardPrefab == null)
            {
                throw new InvalidOperationException(
                    "BoardCanvas.prefab is required before result migration.");
            }

            var legacyPanel = FindDescendant(
                boardPrefab.transform,
                "SkippedResultPanel");
            var resultPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabPath);
            if (resultPrefab == null)
            {
                if (legacyPanel == null)
                {
                    throw new InvalidOperationException(
                        "Result Canvas prefab is missing and BoardCanvas has " +
                        "no authored result panel to migrate.");
                }

                var sourceContents = PrefabUtility.LoadPrefabContents(
                    BoardPrefabPath);
                GameObject template;
                try
                {
                    var sourcePanel = FindDescendant(
                        sourceContents.transform,
                        "SkippedResultPanel");
                    if (sourcePanel == null)
                    {
                        throw new InvalidOperationException(
                            "BoardCanvas source panel disappeared during migration.");
                    }
                    template = CreateFromLegacyPanel(
                        sourceContents,
                        sourcePanel);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(sourceContents);
                }
                try
                {
                    resultPrefab = PrefabUtility.SaveAsPrefabAsset(
                        template,
                        PrefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(template);
                }
                if (resultPrefab == null)
                {
                    throw new InvalidOperationException(
                        "Failed to save MinigameResultCanvas.prefab.");
                }
            }

            NormalizeRootScale();
            resultPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabPath);
            ValidatePrefab(resultPrefab);
            if (legacyPanel != null)
            {
                var contents = PrefabUtility.LoadPrefabContents(BoardPrefabPath);
                try
                {
                    var panel = FindDescendant(
                        contents.transform,
                        "SkippedResultPanel");
                    if (panel != null)
                    {
                        UnityEngine.Object.DestroyImmediate(panel);
                        PrefabUtility.SaveAsPrefabAsset(
                            contents,
                            BoardPrefabPath);
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
        }

        internal static MinigameResultCanvasBindings EnsureSceneInstance(
            Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException(
                    "Result Canvas requires a loaded scene.");
            }

            EnsurePrefabMigrated();
            var views = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    MinigameResultCanvasBindings>(true))
                .ToArray();
            if (views.Length > 1)
            {
                throw new InvalidOperationException(
                    scene.path + " has multiple final result Canvases.");
            }

            var result = views.Length == 1 ? views[0] : null;
            if (result == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    PrefabPath);
                var instance = PrefabUtility.InstantiatePrefab(prefab, scene)
                    as GameObject;
                if (instance == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate MinigameResultCanvas.prefab.");
                }
                instance.name = "Minigame Result Canvas";
                result = instance.GetComponent<
                    MinigameResultCanvasBindings>();
            }

            if (result == null || !result.HasRequiredReferences ||
                result.transform.parent != null ||
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    result.gameObject) != PrefabPath)
            {
                throw new InvalidOperationException(
                    scene.path + " has an invalid result Canvas instance.");
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var boardView in root.GetComponentsInChildren<
                             BoardFlowView>(true))
                {
                    boardView.ConfigureResultUiBindings(result);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(
                        boardView);
                    EditorUtility.SetDirty(boardView);
                }

                foreach (var simulator in root.GetComponentsInChildren<
                             BoardFlowLocalSimulator>(true))
                {
                    var serialized = new SerializedObject(simulator);
                    var property = serialized.FindProperty("resultUiBindings");
                    if (property == null)
                    {
                        throw new InvalidOperationException(
                            "BoardFlowLocalSimulator has no result binding.");
                    }
                    property.objectReferenceValue = result;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(simulator);
                }
            }

            return result;
        }

        private static void EnsureSavedScene(string path)
        {
            var scene = SceneManager.GetSceneByPath(path);
            var openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere)
            {
                scene = EditorSceneManager.OpenScene(
                    path,
                    OpenSceneMode.Additive);
            }
            else if (scene.isDirty)
            {
                throw new InvalidOperationException(
                    path + " has unsaved changes. Save them before migrating UI.");
            }

            try
            {
                EnsureSceneInstance(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedHere)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static GameObject CreateFromLegacyPanel(
            GameObject boardPrefab,
            GameObject legacyPanel)
        {
            var root = new GameObject(
                "MinigameResultCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(MinigameResultCanvasBindings));
            root.transform.localScale = Vector3.one;
            root.layer = boardPrefab.layer;
            var sourceCanvas = boardPrefab.GetComponent<Canvas>();
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sourceCanvas != null
                ? sourceCanvas.sortingOrder + 40
                : 60;

            var sourceScaler = boardPrefab.GetComponent<CanvasScaler>();
            var scaler = root.GetComponent<CanvasScaler>();
            if (sourceScaler != null)
            {
                scaler.uiScaleMode = sourceScaler.uiScaleMode;
                scaler.referenceResolution = sourceScaler.referenceResolution;
                scaler.screenMatchMode = sourceScaler.screenMatchMode;
                scaler.matchWidthOrHeight = sourceScaler.matchWidthOrHeight;
            }

            var panel = UnityEngine.Object.Instantiate(
                legacyPanel,
                root.transform,
                false);
            panel.name = legacyPanel.name;
            panel.SetActive(false);
            root.GetComponent<MinigameResultCanvasBindings>().Configure(
                canvas,
                panel,
                RequireText(panel, "Result Title"),
                RequireText(panel, "Result Note"),
                RequireText(panel, "MinefieldResultSummary"));
            root.transform.localScale = Vector3.one;
            return root;
        }

        private static void NormalizeRootScale()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabPath);
            if (prefab == null || prefab.transform.localScale == Vector3.one)
            {
                return;
            }

            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                contents.transform.localScale = Vector3.one;
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void ValidatePrefab(GameObject prefab)
        {
            var bindings = prefab != null
                ? prefab.GetComponent<MinigameResultCanvasBindings>()
                : null;
            if (bindings == null || !bindings.HasRequiredReferences ||
                prefab.GetComponent<Canvas>() != bindings.RootCanvas ||
                prefab.GetComponentsInChildren<Canvas>(true).Length != 1)
            {
                throw new InvalidOperationException(
                    "MinigameResultCanvas.prefab has incomplete bindings.");
            }
        }

        private static Text RequireText(GameObject root, string name)
        {
            var target = FindDescendant(root.transform, name);
            var text = target != null ? target.GetComponent<Text>() : null;
            if (text == null)
            {
                throw new InvalidOperationException(
                    "Result panel is missing Text '" + name + "'.");
            }
            return text;
        }

        private static GameObject FindDescendant(Transform root, string name)
        {
            if (root.name == name)
            {
                return root.gameObject;
            }
            foreach (Transform child in root)
            {
                var match = FindDescendant(child, name);
                if (match != null)
                {
                    return match;
                }
            }
            return null;
        }
    }
}
