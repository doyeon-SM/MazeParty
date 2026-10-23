using System;
using MazeParty.Multiplayer;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class BoardEventPresentationProjectSetup
    {
        public const string SplitPrefabPath = "Assets/MazeParty/Prefabs/Board/World/BoardEventSplitView.prefab";

        public static BoardEventSplitView EnsureSplitRig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(SplitPrefabPath);
            if (existing != null) return existing.GetComponent<BoardEventSplitView>();
            var root = new GameObject("Board Event Split View");
            try
            {
                var cameras = new Camera[4];
                for (var index = 0; index < cameras.Length; index++)
                {
                    var cameraObject = new GameObject("Event Camera " + index);
                    cameraObject.transform.SetParent(root.transform, false);
                    cameras[index] = cameraObject.AddComponent<Camera>();
                    cameras[index].enabled = false;
                    BoardFeedbackAuthoring.ConfigureNightCamera(cameras[index]);
                }
                root.AddComponent<BoardEventSplitView>().Configure(cameras);
                return PrefabUtility.SaveAsPrefabAsset(root, SplitPrefabPath).GetComponent<BoardEventSplitView>();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        public static void EnsurePanel()
        {
            var path = BoardEventPopupProjectSetup.ModulePrefabPath;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) return;
            var existing = asset.GetComponent<BoardEventPanelView>();
            if (existing != null && existing.HasRequiredReferences) return;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var message = root.GetComponentInChildren<TMP_Text>(true);
                var rect = message.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
                rect.anchoredPosition = new Vector2(0f, 65f);
                rect.sizeDelta = new Vector2(700f, 62f);
                message.fontSizeMax = 28f;
                message.fontSizeMin = 18f;
                var captions = new TMP_Text[3];
                var values = new TMP_Text[3];
                var borders = new Image[12];
                var allTexts = new TMP_Text[6];
                var names = new[] { "Target", "Resource", "Method" };
                for (var index = 0; index < 3; index++)
                {
                    var x = (index - 1) * 238f;
                    captions[index] = Text(root.transform, names[index] + " Caption", message,
                        new Vector2(x, 10f), new Vector2(220f, 28f), 20f);
                    values[index] = Text(root.transform, names[index] + " Value", message,
                        new Vector2(x, -43f), new Vector2(214f, 54f), 24f);
                    var frame = Rect(root.transform, names[index] + " Box",
                        new Vector2(x, -43f), new Vector2(222f, 64f));
                    for (var edge = 0; edge < 4; edge++)
                    {
                        var vertical = edge % 2 == 0;
                        var position = vertical ? new Vector2(edge == 0 ? -111f : 111f, 0f) :
                            new Vector2(0f, edge == 1 ? 32f : -32f);
                        var size = vertical ? new Vector2(1.5f, 64f) : new Vector2(222f, 1.5f);
                        var border = Rect(frame, "Border " + edge, position, size).gameObject.AddComponent<Image>();
                        border.color = Color.white;
                        border.raycastTarget = false;
                        borders[index * 4 + edge] = border;
                    }
                    allTexts[index * 2] = captions[index];
                    allTexts[index * 2 + 1] = values[index];
                }
                root.AddComponent<BoardEventPanelView>().Configure(message, captions, values, borders);
                var fonts = new SerializedObject(root.GetComponent<BoardEventPopupFonts>());
                var extras = fonts.FindProperty("additionalTexts");
                extras.arraySize = allTexts.Length;
                for (var index = 0; index < allTexts.Length; index++)
                    extras.GetArrayElementAtIndex(index).objectReferenceValue = allTexts[index];
                fonts.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }
        private static TMP_Text Text(Transform parent, string name, TMP_Text source,
            Vector2 position, Vector2 size, float fontSize)
        {
            var text = Rect(parent, name, position, size).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = source.font;
            text.spriteAsset = source.spriteAsset;
            text.color = Color.white;
            text.fontSize = fontSize;
            text.fontSizeMax = fontSize;
            text.fontSizeMin = 16f;
            text.enableAutoSizing = true;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            text.text = "?";
            return text;
        }
    }
}
