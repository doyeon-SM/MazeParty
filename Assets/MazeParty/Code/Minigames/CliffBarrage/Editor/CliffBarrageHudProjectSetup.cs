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
    /// Authors the minimal timer HUD once and installs its linked scene root.
    /// Re-running setup does not replace an existing prefab's design.
    /// </summary>
    public static class CliffBarrageHudProjectSetup
    {
        public const string PrefabPath =
            "Assets/MazeParty/Prefabs/Minigames/CliffBarrage/UI/CliffBarrageHud.prefab";
        public const string ScenePath =
            "Assets/MazeParty/Scenes/Minigames/CliffBarrage/CliffBarrage.unity";

        [MenuItem("MazeParty/Minigames/Install Cliff Barrage HUD")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Stop Play mode before installing the Cliff Barrage HUD.");
            }
            EnsurePrefabExists();
            EnsureSceneInstance();
            AssetDatabase.SaveAssets();
            Debug.Log("Cliff Barrage shared timer HUD is installed.");
        }

        [MenuItem("MazeParty/Minigames/Install Cliff Barrage HUD", true)]
        private static bool CanInstall()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static GameObject EnsurePrefabExists()
        {
            MinigameTimerDialProjectSetup.EnsurePrefabExists();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                var template = CreateTemplate();
                try
                {
                    prefab = PrefabUtility.SaveAsPrefabAsset(
                        template, PrefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(template);
                }
            }

            if (prefab != null && prefab.transform.localScale != Vector3.one)
            {
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
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }

            var view = prefab != null
                ? prefab.GetComponent<CliffBarrageHudView>() : null;
            if (view == null || !view.HasRequiredReferences ||
                prefab.GetComponent<Canvas>() == null ||
                prefab.transform.localScale != Vector3.one ||
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    view.TimerDial.gameObject) !=
                MinigameTimerDialProjectSetup.TimerPrefabPath)
            {
                throw new InvalidOperationException(
                    "CliffBarrageHud.prefab needs an authored Canvas, " +
                    "nested shared timer, and serialized bindings.");
            }
            return prefab;
        }

        public static void EnsureSceneInstance()
        {
            var prefab = EnsurePrefabExists();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                throw new InvalidOperationException(
                    "Build CliffBarrage.unity before installing its HUD.");
            }

            var scene = SceneManager.GetSceneByPath(ScenePath);
            var openedForSetup = !scene.IsValid() || !scene.isLoaded;
            if (openedForSetup)
            {
                scene = EditorSceneManager.OpenScene(
                    ScenePath, OpenSceneMode.Additive);
            }
            try
            {
                CliffBarrageHudView view = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var candidate = root.GetComponent<CliffBarrageHudView>();
                    if (candidate == null)
                    {
                        continue;
                    }
                    if (view != null)
                    {
                        throw new InvalidOperationException(
                            "Cliff Barrage scene has multiple HUD roots.");
                    }
                    view = candidate;
                }

                if (view == null)
                {
                    if (scene.isDirty)
                    {
                        throw new InvalidOperationException(
                            "Save CliffBarrage.unity changes before " +
                            "installing the HUD.");
                    }
                    var instance = PrefabUtility.InstantiatePrefab(
                        prefab, scene) as GameObject;
                    view = instance != null
                        ? instance.GetComponent<CliffBarrageHudView>() : null;
                    if (view == null)
                    {
                        throw new InvalidOperationException(
                            "Could not instantiate Cliff Barrage HUD prefab.");
                    }
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }

                if (view.transform.parent != null ||
                    !view.HasRequiredReferences ||
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        view.gameObject) != PrefabPath)
                {
                    throw new InvalidOperationException(
                        "Cliff Barrage HUD must be a standalone linked " +
                        "prefab instance in the scene.");
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

        private static GameObject CreateTemplate()
        {
            var font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            if (font == null)
            {
                throw new InvalidOperationException(
                    "LegacyRuntime.ttf is required.");
            }
            var root = new GameObject(
                "CliffBarrageHud", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler),
                typeof(CliffBarrageHudView));
            root.transform.localScale = Vector3.one;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 45;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var timer = MinigameTimerDialProjectSetup.InstantiateTimer(
                root.transform);
            var label = new GameObject("Round Label",
                typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image));
            label.transform.SetParent(root.transform, false);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = labelRect.anchorMax =
                new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(1f, 1f);
            labelRect.anchoredPosition = new Vector2(-30f, -185f);
            labelRect.sizeDelta = new Vector2(144f, 34f);
            var labelImage = label.GetComponent<Image>();
            labelImage.color = new Color(0.018f, 0.029f, 0.064f, 0.86f);
            labelImage.raycastTarget = false;

            var textObject = new GameObject("Round",
                typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Text));
            textObject.transform.SetParent(label.transform, false);
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var roundText = textObject.GetComponent<Text>();
            roundText.font = font;
            roundText.fontSize = 19;
            roundText.fontStyle = FontStyle.Bold;
            roundText.alignment = TextAnchor.MiddleCenter;
            roundText.color = new Color(0.92f, 0.97f, 1f);
            roundText.raycastTarget = false;
            roundText.text = "ROUND 1 / 3";

            root.GetComponent<CliffBarrageHudView>()
                .Configure(canvas, timer, roundText);
            SetUiLayer(root);
            root.transform.localScale = Vector3.one;
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
