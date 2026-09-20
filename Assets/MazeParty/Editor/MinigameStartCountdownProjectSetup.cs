using System;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    /// <summary>
    /// Creates the shared UI prefab once and links one instance in Board.unity.
    /// Re-running setup preserves authored prefab design.
    /// </summary>
    public static class MinigameStartCountdownProjectSetup
    {
        public const string PrefabPath =
            "Assets/MazeParty/UI/Prefabs/MinigameStartCountdown.prefab";
        public const string BoardScenePath =
            "Assets/MazeParty/Scenes/Board.unity";

        [MenuItem("MazeParty/Minigames/Install Shared Start Countdown")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Stop Play mode before installing the countdown UI.");
            }

            EnsurePrefabExists();
            EnsureBoardSceneInstance();
            AssetDatabase.SaveAssets();
            Debug.Log("Shared minigame start countdown UI is installed.");
        }

        [MenuItem("MazeParty/Minigames/Install Shared Start Countdown", true)]
        private static bool CanInstall()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static GameObject EnsurePrefabExists()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                var template = CreateTemplate();
                try
                {
                    prefab = PrefabUtility.SaveAsPrefabAsset(template, PrefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(template);
                }
            }

            var view = prefab != null
                ? prefab.GetComponent<MinigameStartCountdownView>()
                : null;
            if (view == null || !view.HasRequiredReferences ||
                prefab.GetComponent<Canvas>() == null)
            {
                throw new InvalidOperationException(
                    "The shared countdown prefab needs its Canvas and " +
                    "serialized UI bindings.");
            }

            return prefab;
        }

        public static void EnsureBoardSceneInstance()
        {
            var prefab = EnsurePrefabExists();
            var scene = SceneManager.GetSceneByPath(BoardScenePath);
            var openedForSetup = !scene.IsValid() || !scene.isLoaded;
            if (openedForSetup)
            {
                scene = EditorSceneManager.OpenScene(
                    BoardScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                var match = FindRootComponent<NetworkMatchState>(scene);
                if (match == null)
                {
                    throw new InvalidOperationException(
                        "Board scene has no NetworkMatchState.");
                }

                var view = FindRootComponent<MinigameStartCountdownView>(scene);
                var changed = false;
                if (view == null)
                {
                    var instance = PrefabUtility.InstantiatePrefab(
                        prefab,
                        scene) as GameObject;
                    view = instance != null
                        ? instance.GetComponent<MinigameStartCountdownView>()
                        : null;
                    if (view == null)
                    {
                        throw new InvalidOperationException(
                            "Could not instantiate the countdown prefab.");
                    }

                    instance.name = "Minigame Start Countdown";
                    changed = true;
                }

                if (view.transform.parent != null ||
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        view.gameObject) != PrefabPath)
                {
                    throw new InvalidOperationException(
                        "Countdown must be a standalone Board scene " +
                        "instance of its authored prefab.");
                }

                if (view.MatchState != match)
                {
                    view.ConfigureMatch(match);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(view);
                    changed = true;
                }

                if (changed)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally
            {
                if (openedForSetup)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static T FindRootComponent<T>(Scene scene)
            where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var component = root.GetComponent<T>();
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static GameObject CreateTemplate()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                throw new InvalidOperationException(
                    "Unity LegacyRuntime.ttf was not found.");
            }

            var root = new GameObject(
                "Minigame Start Countdown",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(MinigameStartCountdownView));
            root.transform.localScale = Vector3.one;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var content = new GameObject(
                "Countdown Content",
                typeof(RectTransform));
            content.transform.SetParent(root.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0.5f, 0.5f);
            contentRect.anchorMax = new Vector2(0.5f, 0.5f);
            contentRect.pivot = new Vector2(0.5f, 0.5f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(220f, 220f);

            var backdrop = new GameObject(
                "Backdrop",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            backdrop.transform.SetParent(content.transform, false);
            var backdropRect = backdrop.GetComponent<RectTransform>();
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = Vector2.zero;
            backdropRect.offsetMax = Vector2.zero;
            var backdropImage = backdrop.GetComponent<Image>();
            backdropImage.color = new Color(0.035f, 0.075f, 0.13f, 0.78f);
            backdropImage.raycastTarget = false;

            var numeralObject = new GameObject(
                "Numeral",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text),
                typeof(Outline));
            numeralObject.transform.SetParent(content.transform, false);
            var numeralRect = numeralObject.GetComponent<RectTransform>();
            numeralRect.anchorMin = Vector2.zero;
            numeralRect.anchorMax = Vector2.one;
            numeralRect.offsetMin = new Vector2(12f, 12f);
            numeralRect.offsetMax = new Vector2(-12f, -12f);
            var numeral = numeralObject.GetComponent<Text>();
            numeral.font = font;
            numeral.fontSize = 158;
            numeral.fontStyle = FontStyle.Bold;
            numeral.alignment = TextAnchor.MiddleCenter;
            numeral.color = Color.white;
            numeral.raycastTarget = false;
            numeral.text = "3";
            var outline = numeralObject.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(3f, -3f);

            root.GetComponent<MinigameStartCountdownView>()
                .ConfigureUiBindings(canvas, content, numeral);
            content.SetActive(false);
            SetUiLayer(root);
            return root;
        }

        private static void SetUiLayer(GameObject root)
        {
            root.layer = LayerMask.NameToLayer("UI");
            for (var index = 0; index < root.transform.childCount; index++)
            {
                SetUiLayer(root.transform.GetChild(index).gameObject);
            }
        }
    }
}
