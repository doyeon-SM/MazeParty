using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class BoardHudFeedbackProjectSetup
    {
        public static void EnsureInstalled()
        {
            BoardEventPresentationProjectSetup.EnsurePanel();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(BoardCanvasSplitProjectSetup.HudPrefabPath);
            if (asset == null) return;
            var bindings = asset.GetComponent<BoardHudCanvasBindings>();
            if (bindings.EventPanelView != null && bindings.EventPanelView.HasRequiredReferences &&
                bindings.PlayerStatFeedback != null && bindings.PlayerStatFeedback.Length == 4 &&
                System.Array.TrueForAll(bindings.PlayerStatFeedback, value => value != null &&
                    value.HasRequiredReferences && HasHealthTrailSound(value))) return;
            var root = PrefabUtility.LoadPrefabContents(BoardCanvasSplitProjectSetup.HudPrefabPath);
            try { Ensure(root); PrefabUtility.SaveAsPrefabAsset(root, BoardCanvasSplitProjectSetup.HudPrefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static void Ensure(GameObject root)
        {
            var hud = root.GetComponent<BoardHudCanvasBindings>();
            var data = new SerializedObject(hud);
            data.FindProperty("references.EventPanelView").objectReferenceValue =
                hud.BoardEventPopupPanel.GetComponent<BoardEventPanelView>();
            var entries = data.FindProperty("references.PlayerStatFeedback");
            entries.arraySize = 4;
            var healthTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Ignore/Modern UI Pack/Textures/Icon/Navigation/Add.png");
            for (var slot = 0; slot < 4; slot++)
            {
                var binding = entries.GetArrayElementAtIndex(slot);
                if (binding.objectReferenceValue != null)
                {
                    var existing = new SerializedObject(binding.objectReferenceValue);
                    EnsureSilentSound(existing.FindProperty("healthTrail").objectReferenceValue as Slider);
                    existing.FindProperty("deltas").GetArrayElementAtIndex(1)
                        .FindPropertyRelative("ValueIcon").objectReferenceValue = hud.PlayerKeyIcons[slot];
                    existing.FindProperty("deltas").GetArrayElementAtIndex(2)
                        .FindPropertyRelative("ValueIcon").objectReferenceValue = hud.PlayerGoldIcons[slot];
                    existing.ApplyModifiedPropertiesWithoutUndo();
                    foreach (var label in new[] { hud.PlayerHealthDeltaTexts[slot],
                        hud.PlayerKeyDeltaTexts[slot], hud.PlayerGoldDeltaTexts[slot] })
                        label.verticalOverflow = VerticalWrapMode.Overflow;
                    continue;
                }
                var main = hud.PlayerHealthSliders[slot];
                var fill = hud.PlayerHealthFills[slot];
                var trailObject = new GameObject("Health Trail", typeof(RectTransform), typeof(Slider));
                trailObject.layer = main.gameObject.layer;
                var trailRect = (RectTransform)trailObject.transform;
                trailRect.SetParent(main.transform, false);
                trailRect.SetAsFirstSibling();
                trailRect.anchorMin = Vector2.zero;
                trailRect.anchorMax = Vector2.one;
                trailRect.sizeDelta = new Vector2(0f, 4f);
                var trailFillObject = new GameObject("Trail Fill", typeof(RectTransform), typeof(Image));
                trailFillObject.layer = main.gameObject.layer;
                var trailFill = trailFillObject.GetComponent<Image>();
                trailFill.transform.SetParent(trailRect, false);
                trailFill.rectTransform.anchorMin = Vector2.zero;
                trailFill.rectTransform.anchorMax = Vector2.one;
                trailFill.rectTransform.sizeDelta = Vector2.zero;
                trailFill.sprite = fill.sprite;
                trailFill.type = fill.type;
                trailFill.raycastTarget = false;
                trailFill.color = fill.color * new Color(0.45f, 0.45f, 0.45f, 1f);
                var trail = trailObject.GetComponent<Slider>();
                EnsureSilentSound(trail);
                trail.interactable = false;
                trail.transition = Selectable.Transition.None;
                trail.fillRect = trailFill.rectTransform;
                trail.targetGraphic = null;
                trail.navigation = new Navigation { mode = Navigation.Mode.None };
                var labels = new[] { hud.PlayerHealthDeltaTexts[slot],
                    hud.PlayerKeyDeltaTexts[slot], hud.PlayerGoldDeltaTexts[slot] };
                var numbers = new[] { hud.PlayerHealthTexts[slot],
                    hud.PlayerKeyTexts[slot], hud.PlayerGoldTexts[slot] };
                var textures = new[] { healthTexture,
                    hud.PlayerKeyIcons[slot].texture, hud.PlayerGoldIcons[slot].texture };
                var deltas = new BoardHudStatFeedbackView.Delta[3];
                for (var kind = 0; kind < 3; kind++)
                {
                    var label = labels[kind];
                    var number = numbers[kind];
                    var rect = label.rectTransform;
                    rect.anchorMin = number.rectTransform.anchorMin;
                    rect.anchorMax = number.rectTransform.anchorMax;
                    rect.pivot = number.rectTransform.pivot;
                    rect.anchoredPosition = number.rectTransform.anchoredPosition;
                    rect.sizeDelta = number.rectTransform.sizeDelta;
                    label.alignment = TextAnchor.MiddleCenter;
                    label.horizontalOverflow = HorizontalWrapMode.Overflow;
                    label.verticalOverflow = VerticalWrapMode.Overflow;
                    label.fontSize = Mathf.Max(16, number.fontSize);
                    label.raycastTarget = false;
                    var outline = label.GetComponent<Outline>();
                    if (outline == null) outline = label.gameObject.AddComponent<Outline>();
                    outline.effectColor = Color.black;
                    outline.effectDistance = new Vector2(1f, -1f);
                    var group = label.GetComponent<CanvasGroup>();
                    if (group == null) group = label.gameObject.AddComponent<CanvasGroup>();
                    group.blocksRaycasts = false;
                    group.interactable = false;
                    deltas[kind] = new BoardHudStatFeedbackView.Delta
                        { Label = label, Group = group, Value = number,
                            ValueIcon = kind == 1 ? hud.PlayerKeyIcons[slot] :
                                kind == 2 ? hud.PlayerGoldIcons[slot] : null };
                    label.gameObject.SetActive(false);
                }
                var feedback = hud.PlayerCards[slot].gameObject.AddComponent<BoardHudStatFeedbackView>();
                feedback.Configure(main, fill, trail, trailFill, deltas);
                binding.objectReferenceValue = feedback;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool HasHealthTrailSound(BoardHudStatFeedbackView feedback)
        {
            var data = new SerializedObject(feedback);
            var trail = data.FindProperty("healthTrail").objectReferenceValue as Slider;
            return trail != null && trail.GetComponent<UiSoundEmitter>() != null;
        }

        private static void EnsureSilentSound(Slider slider)
        {
            if (slider == null || slider.GetComponent<UiSoundEmitter>() != null) return;
            // Display-only health bars participate in the UI sound contract without input sounds.
            slider.gameObject.AddComponent<UiSoundEmitter>().Configure(string.Empty, string.Empty);
        }
    }
}
