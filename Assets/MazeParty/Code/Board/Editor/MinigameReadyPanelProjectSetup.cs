using System;
using MazeParty.Gameplay.Minigames;
using MazeParty.Multiplayer;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace MazeParty.Editor
{
    /// <summary>
    /// One-time, non-destructive migration from the old rule-card image to the
    /// authored ready/control/video presentation. Once the new structure exists,
    /// setup only repairs serialized bindings and leaves its design untouched.
    /// </summary>
    public static class MinigameReadyPanelProjectSetup
    {
        public const string ModulePrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/Modules/MinigameReadyPanel.prefab";
        public const string InputSpriteAssetPath =
            "Assets/Ignore/Input Sprites for TextMesh Pro/all input icons same size.asset";
        public const string ControlFontAssetPath =
            "Assets/Ignore/Modern UI Pack/Fonts/OpenSans-Regular SDF.asset";

        private const string BoardCanvasPrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab";
        private const string ModuleObjectName = "MinigameReadyPanel";
        private const int ControlRowCount = 4;
        private static int PreviewClipSlotCount =>
            BoardCanvasBindings.RequiredMinigamePreviewClipSlotCount;

        [MenuItem("MazeParty/UI/Upgrade Minigame Ready Presentation")]
        public static void UpgradeMinigameReadyPresentation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Minigame ready presentation migration requires Edit Mode.");
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                BoardCanvasPrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "BoardCanvas.prefab must exist before migrating its ready panel.");
            }

            EnsureInstalled(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "MinigameReadyPanel now uses vertical ready/control lists and " +
                "an optional VideoPlayer preview without rebuilding BoardCanvas.");
        }

        public static GameObject EnsureInstalled(GameObject prefab)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            EnsureModulePresentation();
            BindBoardCanvas();
            return AssetDatabase.LoadAssetAtPath<GameObject>(
                BoardCanvasPrefabPath);
        }

        private static void EnsureModulePresentation()
        {
            var module = AssetDatabase.LoadAssetAtPath<GameObject>(
                ModulePrefabPath);
            if (module == null)
            {
                throw new InvalidOperationException(
                    "MinigameReadyPanel prefab is missing: " + ModulePrefabPath);
            }

            var needsStructure = !HasPresentationStructure(module);
            var needsNormalText = !AllTextUsesNormalStyle(module);
            if (!needsStructure && !needsNormalText)
            {
                return;
            }

            var contents = PrefabUtility.LoadPrefabContents(ModulePrefabPath);
            try
            {
                if (needsStructure)
                {
                    MigratePresentationStructure(contents);
                }
                NormalizeTextStyles(contents);
                if (PrefabUtility.SaveAsPrefabAsset(
                        contents,
                        ModulePrefabPath) == null)
                {
                    throw new InvalidOperationException(
                        "Failed to save the minigame ready panel migration.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void MigratePresentationStructure(GameObject root)
        {
            var legacyImage = FindDescendant(root.transform, "MinigameRuleImage");
            if (legacyImage != null)
            {
                UnityEngine.Object.DestroyImmediate(legacyImage.gameObject);
            }
            var legacyPlaceholder = FindDescendant(
                root.transform,
                "MinigameRulePlaceholderText");
            if (legacyPlaceholder != null)
            {
                UnityEngine.Object.DestroyImmediate(legacyPlaceholder.gameObject);
            }

            var font = ResolvePanelFont(root);
            var inputSprites = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(
                InputSpriteAssetPath);
            if (inputSprites == null)
            {
                throw new InvalidOperationException(
                    "Minigame control sprite asset is missing: " +
                    InputSpriteAssetPath);
            }

            SetRect(
                Require(root.transform, "Ready Title"),
                new Vector2(155f, 365f),
                new Vector2(760f, 44f));
            var description = FindDescendant(root.transform, "Ready Note") ??
                              FindDescendant(
                                  root.transform,
                                  "MinigameDescriptionText");
            if (description == null)
            {
                throw new InvalidOperationException(
                    "MinigameReadyPanel requires its authored description Text.");
            }
            description.name = "MinigameDescriptionText";
            SetRect(
                description,
                new Vector2(155f, -148f),
                new Vector2(760f, 72f));
            var descriptionText = description.GetComponent<Text>();
            descriptionText.text = "---";
            descriptionText.fontSize = 19;
            descriptionText.alignment = TextAnchor.MiddleCenter;

            var status = Require(root.transform, "MinigameReadyStatus");
            SetRect(
                status,
                new Vector2(-420f, 346f),
                new Vector2(290f, 40f));
            var statusText = status.GetComponent<Text>();
            statusText.fontSize = 20;
            statusText.alignment = TextAnchor.MiddleLeft;

            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                var playerState = Require(
                    root.transform,
                    "MinigameReadyPlayerState" + slot);
                SetRect(
                    playerState,
                    new Vector2(-420f, 288f - slot * 52f),
                    new Vector2(290f, 42f));
                playerState.GetComponent<Text>().alignment =
                    TextAnchor.MiddleLeft;
            }

            SetRect(
                Require(root.transform, "ReadyButton"),
                new Vector2(-420f, -350f),
                new Vector2(290f, 64f));

            var previewFrame = EnsureUiObject(
                root.transform,
                "MinigamePreviewFrame");
            SetRect(
                previewFrame.transform,
                new Vector2(155f, 110f),
                new Vector2(760f, 428f));
            var frameImage = EnsureComponent<Image>(previewFrame);
            frameImage.color = new Color(0.012f, 0.02f, 0.035f, 1f);
            frameImage.raycastTarget = false;

            var previewObject = EnsureUiObject(
                previewFrame.transform,
                "MinigamePreviewVideo");
            var previewRect = (RectTransform)previewObject.transform;
            previewRect.anchorMin = Vector2.zero;
            previewRect.anchorMax = Vector2.one;
            previewRect.pivot = new Vector2(0.5f, 0.5f);
            previewRect.offsetMin = new Vector2(6f, 6f);
            previewRect.offsetMax = new Vector2(-6f, -6f);
            var previewImage = EnsureComponent<RawImage>(previewObject);
            previewImage.texture = null;
            previewImage.color = Color.white;
            previewImage.raycastTarget = false;
            previewImage.enabled = false;
            var previewPlayer = EnsureComponent<VideoPlayer>(previewObject);
            previewPlayer.source = VideoSource.VideoClip;
            previewPlayer.clip = null;
            previewPlayer.renderMode = VideoRenderMode.APIOnly;
            previewPlayer.playOnAwake = false;
            previewPlayer.isLooping = true;
            previewPlayer.waitForFirstFrame = true;
            previewPlayer.skipOnDrop = true;
            previewPlayer.audioOutputMode = VideoAudioOutputMode.None;

            var previewPlaceholder = EnsureLegacyText(
                previewFrame.transform,
                "MinigamePreviewPlaceholder",
                font);
            previewPlaceholder.text = "PREVIEW";
            previewPlaceholder.fontSize = 24;
            previewPlaceholder.alignment = TextAnchor.MiddleCenter;
            SetRectStretch(
                previewPlaceholder.transform,
                new Vector2(30f, 30f),
                new Vector2(-30f, -30f));

            var controlsTitle = EnsureLegacyText(
                root.transform,
                "MinigameControlsTitle",
                font);
            controlsTitle.text = "CONTROLS";
            controlsTitle.fontSize = 20;
            controlsTitle.alignment = TextAnchor.MiddleLeft;
            SetRect(
                controlsTitle.transform,
                new Vector2(-420f, 42f),
                new Vector2(290f, 38f));

            for (var row = 0; row < ControlRowCount; row++)
            {
                var control = EnsureTmpText(
                    root.transform,
                    "MinigameControlRow" + row);
                control.spriteAsset = inputSprites;
                control.text = row == 0
                    ? "<sprite name=\"w\"><sprite name=\"a\">" +
                      "<sprite name=\"s\"><sprite name=\"d\">  MOVE"
                    : string.Empty;
                control.fontSize = 19f;
                control.fontStyle = FontStyles.Normal;
                control.fontWeight = FontWeight.Regular;
                control.color = new Color(0.93f, 0.96f, 1f, 1f);
                control.alignment = TextAlignmentOptions.MidlineLeft;
                control.textWrappingMode = TextWrappingModes.NoWrap;
                control.overflowMode = TextOverflowModes.Ellipsis;
                control.richText = true;
                control.raycastTarget = false;
                SetRect(
                    control.transform,
                    new Vector2(-420f, -12f - row * 54f),
                    new Vector2(300f, 46f));
            }
        }

        private static void BindBoardCanvas()
        {
            var contents = PrefabUtility.LoadPrefabContents(
                BoardCanvasPrefabPath);
            try
            {
                var panel = Require(contents.transform, ModuleObjectName);
                var bindings = contents.GetComponent<BoardCanvasBindings>();
                if (bindings == null)
                {
                    throw new InvalidOperationException(
                        "BoardCanvas.prefab requires BoardCanvasBindings.");
                }

                var serialized = new SerializedObject(bindings);
                var references = serialized.FindProperty("references");
                if (references == null)
                {
                    throw new InvalidOperationException(
                        "BoardCanvasBindings references are unavailable.");
                }

                SetObjectReference(
                    references,
                    "MinigameDescription",
                    Require(panel, "MinigameDescriptionText").GetComponent<Text>());
                SetObjectReference(
                    references,
                    "MinigamePreviewPlaceholder",
                    Require(panel, "MinigamePreviewPlaceholder").GetComponent<Text>());
                var videoObject = Require(panel, "MinigamePreviewVideo");
                SetObjectReference(
                    references,
                    "MinigamePreviewImage",
                    videoObject.GetComponent<RawImage>());
                SetObjectReference(
                    references,
                    "MinigamePreviewPlayer",
                    videoObject.GetComponent<VideoPlayer>());

                var controlRows = references.FindPropertyRelative(
                    "MinigameControlRows");
                if (controlRows == null)
                {
                    throw new InvalidOperationException(
                        "BoardCanvasBindings is missing MinigameControlRows.");
                }
                controlRows.arraySize = ControlRowCount;
                for (var row = 0; row < ControlRowCount; row++)
                {
                    controlRows.GetArrayElementAtIndex(row).objectReferenceValue =
                        Require(panel, "MinigameControlRow" + row)
                            .GetComponent<TMP_Text>();
                }

                var previewClips = serialized.FindProperty(
                    "minigamePreviewClips");
                if (previewClips == null)
                {
                    throw new InvalidOperationException(
                        "BoardCanvasBindings is missing minigamePreviewClips.");
                }
                if (previewClips.arraySize < PreviewClipSlotCount)
                {
                    previewClips.arraySize = PreviewClipSlotCount;
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(bindings);
                if (PrefabUtility.SaveAsPrefabAsset(
                        contents,
                        BoardCanvasPrefabPath) == null)
                {
                    throw new InvalidOperationException(
                        "Failed to save BoardCanvas minigame presentation bindings.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static bool HasPresentationStructure(GameObject root)
        {
            if (FindDescendant(root.transform, "MinigameRuleImage") != null ||
                FindDescendant(
                    root.transform,
                    "MinigameRulePlaceholderText") != null ||
                FindDescendant(root.transform, "MinigameDescriptionText") == null ||
                FindDescendant(root.transform, "MinigamePreviewFrame") == null ||
                FindDescendant(root.transform, "MinigamePreviewPlaceholder") == null ||
                FindDescendant(root.transform, "MinigameControlsTitle") == null)
            {
                return false;
            }

            var video = FindDescendant(root.transform, "MinigamePreviewVideo");
            if (video == null ||
                video.GetComponent<RawImage>() == null ||
                video.GetComponent<VideoPlayer>() == null)
            {
                return false;
            }

            for (var row = 0; row < ControlRowCount; row++)
            {
                var text = FindDescendant(
                    root.transform,
                    "MinigameControlRow" + row)?.GetComponent<TMP_Text>();
                if (text == null ||
                    AssetDatabase.GetAssetPath(text.spriteAsset) !=
                    InputSpriteAssetPath)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AllTextUsesNormalStyle(GameObject root)
        {
            var legacy = root.GetComponentsInChildren<Text>(true);
            for (var index = 0; index < legacy.Length; index++)
            {
                if (legacy[index].fontStyle != FontStyle.Normal)
                {
                    return false;
                }
            }
            var tmp = root.GetComponentsInChildren<TMP_Text>(true);
            for (var index = 0; index < tmp.Length; index++)
            {
                if (tmp[index].fontStyle != FontStyles.Normal ||
                    tmp[index].fontWeight != FontWeight.Regular)
                {
                    return false;
                }
            }
            return true;
        }

        private static void NormalizeTextStyles(GameObject root)
        {
            var legacy = root.GetComponentsInChildren<Text>(true);
            for (var index = 0; index < legacy.Length; index++)
            {
                legacy[index].fontStyle = FontStyle.Normal;
                EditorUtility.SetDirty(legacy[index]);
            }
            var tmp = root.GetComponentsInChildren<TMP_Text>(true);
            for (var index = 0; index < tmp.Length; index++)
            {
                tmp[index].fontStyle = FontStyles.Normal;
                tmp[index].fontWeight = FontWeight.Regular;
                EditorUtility.SetDirty(tmp[index]);
            }
        }

        private static Font ResolvePanelFont(GameObject root)
        {
            var title = FindDescendant(root.transform, "Ready Title")
                ?.GetComponent<Text>();
            var font = title != null ? title.font : null;
            return font != null
                ? font
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static Text EnsureLegacyText(
            Transform parent,
            string name,
            Font font)
        {
            var target = EnsureUiObject(parent, name);
            var text = EnsureComponent<Text>(target);
            text.font = font;
            text.fontStyle = FontStyle.Normal;
            text.color = new Color(0.93f, 0.96f, 1f, 1f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static TextMeshProUGUI EnsureTmpText(
            Transform parent,
            string name)
        {
            var target = EnsureUiObject(parent, name);
            var text = EnsureComponent<TextMeshProUGUI>(target);
            if (text.font == null)
            {
                text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    ControlFontAssetPath);
                if (text.font == null)
                {
                    throw new InvalidOperationException(
                        "Minigame control font is missing: " +
                        ControlFontAssetPath);
                }
            }
            return text;
        }

        private static GameObject EnsureUiObject(Transform parent, string name)
        {
            var existing = FindDescendant(parent, name);
            if (existing != null)
            {
                return existing.gameObject;
            }

            var created = new GameObject(name, typeof(RectTransform));
            created.layer = LayerMask.NameToLayer("UI");
            created.transform.SetParent(parent, false);
            return created;
        }

        private static T EnsureComponent<T>(GameObject target)
            where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static void SetObjectReference(
            SerializedProperty references,
            string name,
            UnityEngine.Object value)
        {
            var property = references.FindPropertyRelative(name);
            if (property == null || value == null)
            {
                throw new InvalidOperationException(
                    "BoardCanvas minigame binding is missing: " + name);
            }
            property.objectReferenceValue = value;
        }

        private static void SetRect(
            Transform target,
            Vector2 position,
            Vector2 size)
        {
            var rect = target as RectTransform;
            if (rect == null)
            {
                throw new InvalidOperationException(
                    target.name + " requires a RectTransform.");
            }
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void SetRectStretch(
            Transform target,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            var rect = target as RectTransform;
            if (rect == null)
            {
                throw new InvalidOperationException(
                    target.name + " requires a RectTransform.");
            }
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static Transform Require(Transform root, string objectName)
        {
            var result = FindDescendant(root, objectName);
            if (result == null)
            {
                throw new InvalidOperationException(
                    "Required MinigameReadyPanel object is missing: " +
                    objectName);
            }
            return result;
        }

        private static Transform FindDescendant(
            Transform root,
            string objectName)
        {
            if (root == null)
            {
                return null;
            }
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                if (transforms[index].name == objectName)
                {
                    return transforms[index];
                }
            }
            return null;
        }
    }
}
