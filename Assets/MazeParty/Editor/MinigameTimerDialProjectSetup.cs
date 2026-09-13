using MazeParty.Multiplayer;
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    /// <summary>
    /// Owns the reusable timer prefab and nests it into every production
    /// minigame HUD without rebuilding or restyling the surrounding prefab.
    /// </summary>
    public static class MinigameTimerDialProjectSetup
    {
        private const string MenuPath =
            "MazeParty/Minigames/Migrate Shared Timer Dial";
        private const string UiPrefabFolder =
            "Assets/MazeParty/UI/Prefabs";

        public const string TimerPrefabPath =
            UiPrefabFolder + "/MinigameTimerDial.prefab";

        private static readonly string[] HudPrefabPaths =
        {
            UiPrefabFolder + "/MinefieldHud.prefab",
            UiPrefabFolder + "/WrongWayHud.prefab",
            UiPrefabFolder + "/RedLightGreenLightHud.prefab",
            UiPrefabFolder + "/StableFootingHud.prefab",
            UiPrefabFolder + "/BalloonBlowHud.prefab",
            UiPrefabFolder + "/GiftGrabHud.prefab",
            UiPrefabFolder + "/TerritoryPaintHud.prefab",
            UiPrefabFolder + "/TagChaseHud.prefab",
            UiPrefabFolder + "/RaceHud.prefab"
        };

        [MenuItem(MenuPath)]
        public static void BuildAndMigrateAll()
        {
            EnsurePrefabExists();
            for (var index = 0;
                 index < HudPrefabPaths.Length;
                 index++)
            {
                EnsureHudTimer(HudPrefabPaths[index]);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "Shared minigame timer dial created and nested into " +
                HudPrefabPaths.Length + " existing HUD prefabs.");
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

        public static void EnsureHudTimer(string hudPrefabPath)
        {
            if (string.IsNullOrWhiteSpace(hudPrefabPath))
            {
                throw new ArgumentException(
                    "A HUD prefab path is required.",
                    nameof(hudPrefabPath));
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(
                    hudPrefabPath) == null)
            {
                return;
            }

            var contents =
                PrefabUtility.LoadPrefabContents(
                    hudPrefabPath);
            try
            {
                var canvas =
                    contents.GetComponentInChildren<Canvas>(true);
                if (canvas == null)
                {
                    throw new InvalidOperationException(
                        hudPrefabPath +
                        " does not contain a Canvas.");
                }

                var timer =
                    contents.GetComponentInChildren<
                        MinigameTimerDial>(true);
                if (timer == null)
                {
                    timer = InstantiateTimer(
                        canvas.transform);
                }

                BindTimerToHud(
                    contents,
                    timer);
                SetUiLayer(timer.gameObject);
                PrefabUtility.SaveAsPrefabAsset(
                    contents,
                    hudPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(
                    contents);
            }
        }

        private static void BindTimerToHud(
            GameObject root,
            MinigameTimerDial timer)
        {
            var bindings =
                root.GetComponentsInChildren<MonoBehaviour>(true);
            var bound = false;
            for (var index = 0;
                 index < bindings.Length;
                 index++)
            {
                var binding = bindings[index];
                if (binding == null ||
                    binding is MinigameTimerDial ||
                    binding is MinigameTimerRingGraphic)
                {
                    continue;
                }

                var serialized =
                    new SerializedObject(binding);
                var timerProperty =
                    serialized.FindProperty("timerDial");
                if (timerProperty == null)
                {
                    continue;
                }

                timerProperty.objectReferenceValue =
                    timer;
                var legacyTimer =
                    serialized.FindProperty("timerText");
                if (legacyTimer != null)
                {
                    var legacyText =
                        legacyTimer.objectReferenceValue as Text;
                    if (legacyText != null &&
                        legacyText != timer.TimeText)
                    {
                        HideLegacyTimer(legacyText);
                    }
                    legacyTimer.objectReferenceValue =
                        timer.TimeText;
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(binding);
                bound = true;
            }

            if (!bound)
            {
                throw new InvalidOperationException(
                    root.name +
                    " does not expose a serialized timerDial binding.");
            }
        }

        private static void HideLegacyTimer(Text legacyText)
        {
            var parent = legacyText.transform.parent;
            if (parent != null &&
                parent.name.IndexOf(
                    "Timer",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                parent.gameObject.SetActive(false);
                return;
            }

            legacyText.gameObject.SetActive(false);
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
