using MazeParty.Multiplayer;
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    /// <summary>
    /// Owns the reusable timer graphic nested inside the common HUD.
    /// </summary>
    public static class MinigameTimerDialProjectSetup
    {
        private const string MenuPath =
            "MazeParty/Minigames/Install Common Minigame Timer";
        private const string UiPrefabFolder =
            "Assets/MazeParty/Prefabs/Minigames/Common/UI";

        public const string TimerPrefabPath =
            "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigameTimerDial.prefab";

        [MenuItem(MenuPath)]
        public static void BuildAndMigrateAll()
        {
            EnsurePrefabExists();
            MinigameStartCountdownProjectSetup.Install();
        }

        [MenuItem(MenuPath, true)]
        private static bool CanBuildAndMigrateAll()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static GameObject EnsurePrefabExists()
        {
            EnsureFolder(UiPrefabFolder);
            var prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    TimerPrefabPath);
            if (prefab == null)
            {
                var template = CreateTemplate();
                try
                {
                    prefab =
                        PrefabUtility.SaveAsPrefabAsset(
                            template,
                            TimerPrefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(
                        template);
                }
            }

            var binding =
                prefab != null
                    ? prefab.GetComponent<MinigameTimerDial>()
                    : null;
            if (binding == null ||
                !binding.HasRequiredReferences ||
                prefab.GetComponent<Canvas>() != null)
            {
                throw new InvalidOperationException(
                    "MinigameTimerDial.prefab must be a Canvas-free " +
                    "nested UI prefab with complete serialized bindings.");
            }

            return prefab;
        }

        public static MinigameTimerDial InstantiateTimer(
            Transform canvasTransform)
        {
            if (canvasTransform == null)
            {
                throw new ArgumentNullException(
                    nameof(canvasTransform));
            }

            var prefab = EnsurePrefabExists();
            var instance =
                PrefabUtility.InstantiatePrefab(
                    prefab,
                    canvasTransform) as GameObject;
            var binding =
                instance != null
                    ? instance.GetComponent<MinigameTimerDial>()
                    : null;
            if (binding == null ||
                !binding.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "The shared timer prefab could not be instantiated.");
            }

            return binding;
        }

        private static GameObject CreateTemplate()
        {
            var font =
                Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            if (font == null)
            {
                throw new InvalidOperationException(
                    "Unity built-in LegacyRuntime.ttf is required.");
            }

            var root =
                new GameObject(
                    "MinigameTimerDial",
                    typeof(RectTransform),
                    typeof(MinigameTimerDial));
            var rootRect =
                root.GetComponent<RectTransform>();
            rootRect.anchorMin =
                rootRect.anchorMax =
                    new Vector2(1f, 1f);
            rootRect.pivot =
                new Vector2(1f, 1f);
            rootRect.anchoredPosition =
                new Vector2(-30f, -30f);
            rootRect.sizeDelta =
                new Vector2(144f, 144f);
            root.transform.localScale = Vector3.one;

            var ringObject =
                new GameObject(
                    "Remaining Ring",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(MinigameTimerRingGraphic));
            ringObject.transform.SetParent(
                root.transform,
                false);
            var ringRect =
                ringObject.GetComponent<RectTransform>();
            ringRect.anchorMin = Vector2.zero;
            ringRect.anchorMax = Vector2.one;
            ringRect.offsetMin = new Vector2(6f, 6f);
            ringRect.offsetMax = new Vector2(-6f, -6f);
            var ring =
                ringObject.GetComponent<
                    MinigameTimerRingGraphic>();
            ring.color =
                new Color(0.92f, 0.97f, 1f, 1f);
            ring.raycastTarget = false;
            ring.Configure(9f, 128);

            var textObject =
                new GameObject(
                    "Time",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Text));
            textObject.transform.SetParent(
                root.transform,
                false);
            var textRect =
                textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin =
                new Vector2(14f, 14f);
            textRect.offsetMax =
                new Vector2(-14f, -14f);
            var text =
                textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = 32;
            text.fontStyle = FontStyle.Bold;
            text.alignment =
                TextAnchor.MiddleCenter;
            text.horizontalOverflow =
                HorizontalWrapMode.Overflow;
            text.verticalOverflow =
                VerticalWrapMode.Truncate;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = "01:00";

            root.GetComponent<MinigameTimerDial>()
                .Configure(ring, text);
            SetUiLayer(root);
            return root;
        }

        private static void SetUiLayer(GameObject root)
        {
            root.layer = LayerMask.NameToLayer("UI");
            for (var index = 0;
                 index < root.transform.childCount;
                 index++)
            {
                SetUiLayer(
                    root.transform.GetChild(index)
                        .gameObject);
            }
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
                    "Invalid asset folder: " + path);
            }

            var parent =
                path.Substring(0, separator);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(
                parent,
                path.Substring(separator + 1));
        }
    }
}
