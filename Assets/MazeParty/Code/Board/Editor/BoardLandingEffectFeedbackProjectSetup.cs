#if UNITY_EDITOR
using System;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class BoardLandingEffectFeedbackProjectSetup
    {
        internal const string PrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardLandingEffectFeedback.prefab";
        internal const string MoneyIconPath =
            "Assets/Ignore/Modern UI Pack/Textures/Icon/Business & Commerce/Money Filled.png";

        [MenuItem("MazeParty/Board/Ensure Landing Effect Feedback Prefab")]
        public static void EnsureFromMenu()
        {
            EnsureInstalled();
            AssetDatabase.SaveAssets();
            Debug.Log("Board landing-effect feedback prefab is ready.");
        }

        public static BoardLandingEffectFeedbackView EnsureInstalled()
        {
            var existingRoot = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabPath);
            var existing = existingRoot != null
                ? existingRoot.GetComponent<BoardLandingEffectFeedbackView>()
                : null;
            if (existing != null)
            {
                Validate(existing);
                return existing;
            }

            EnsureFolder("Assets/MazeParty/Prefabs/Board/UI");
            var moneyIcon = AssetDatabase.LoadAssetAtPath<Sprite>(MoneyIconPath);
            if (moneyIcon == null)
            {
                throw new InvalidOperationException(
                    "Required map money icon is missing: '" + MoneyIconPath + "'.");
            }

            var font = AssetDatabase.LoadAssetAtPath<Font>(
                GameFonts.EnglishKoreanAssetPath);
            if (font == null)
            {
                throw new InvalidOperationException(
                    "Required localized board font is missing: '" +
                    GameFonts.EnglishKoreanAssetPath + "'.");
            }

            var root = new GameObject(
                "BoardLandingEffectFeedback",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(LocalizedFontScope),
                typeof(BoardLandingEffectFeedbackView));
            try
            {
                var rootRect = (RectTransform)root.transform;
                rootRect.sizeDelta = new Vector2(300f, 84f);
                rootRect.localScale = Vector3.one * 0.01f;
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 120;
                var scaler = root.GetComponent<CanvasScaler>();
                scaler.dynamicPixelsPerUnit = 10f;

                var iconObject = new GameObject(
                    "EffectIcon",
                    typeof(RectTransform),
                    typeof(BoardMapIcon));
                iconObject.transform.SetParent(root.transform, false);
                var iconRect = (RectTransform)iconObject.transform;
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.anchoredPosition = new Vector2(45f, 0f);
                iconRect.sizeDelta = new Vector2(64f, 64f);
                var icon = iconObject.GetComponent<BoardMapIcon>();
                icon.SetIcon(BoardMapIconKind.GoldGain, moneyIcon);
                icon.color = new Color(1f, 0.82f, 0.16f, 1f);
                icon.raycastTarget = false;

                var labelObject = new GameObject(
                    "ValueLabel",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Text));
                labelObject.transform.SetParent(root.transform, false);
                var labelRect = (RectTransform)labelObject.transform;
                labelRect.anchorMin = new Vector2(0f, 0f);
                labelRect.anchorMax = new Vector2(1f, 1f);
                labelRect.offsetMin = new Vector2(82f, 0f);
                labelRect.offsetMax = new Vector2(-4f, 0f);
                var label = labelObject.GetComponent<Text>();
                label.font = font;
                label.fontSize = 44;
                label.fontStyle = FontStyle.Normal;
                label.alignment = TextAnchor.MiddleLeft;
                label.color = Color.white;
                label.raycastTarget = false;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                label.text = "+3";

                var view = root.GetComponent<BoardLandingEffectFeedbackView>();
                view.Configure(icon, label, moneyIcon);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        "Could not save board landing-effect feedback prefab.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            var createdRoot = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabPath);
            var created = createdRoot != null
                ? createdRoot.GetComponent<BoardLandingEffectFeedbackView>()
                : null;
            Validate(created);
            return created;
        }

        private static void Validate(BoardLandingEffectFeedbackView view)
        {
            if (view == null || !view.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Board landing-effect feedback prefab has missing bindings.");
            }

            var canvas = view.GetComponent<Canvas>();
            if (canvas == null || canvas.renderMode != RenderMode.WorldSpace)
            {
                throw new InvalidOperationException(
                    "Board landing-effect feedback must remain an authored world-space Canvas.");
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var slash = path.LastIndexOf('/');
            var parent = path.Substring(0, slash);
            var name = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
