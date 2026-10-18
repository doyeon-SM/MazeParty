using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Editor
{
    /// <summary>Adds missing authored tile-icon backdrops without restyling existing ones.</summary>
    public static class BoardMapIconBackdropUpgrade
    {
        public static void Ensure(GameObject root)
        {
            foreach (var view in root.GetComponentsInChildren<BoardMinimapView>(true))
            {
                var data = new SerializedObject(view);
                var rooms = data.FindProperty("rooms");
                var offset = Vector2.zero;
                for (var index = 0; index < rooms.arraySize; index++)
                {
                    var room = rooms.GetArrayElementAtIndex(index);
                    EnsureIcon(room, "TypeIcon", "TypeBackdrop", offset);
                    EnsureIcon(room, "EffectIcon", "EffectBackdrop", offset);
                }
                data.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void EnsureIcon(SerializedProperty room, string iconField,
            string backdropField, Vector2 offset)
        {
            var binding = room.FindPropertyRelative(backdropField);
            if (binding.objectReferenceValue != null) return;
            var icon = (BoardMapIcon)room.FindPropertyRelative(iconField).objectReferenceValue;
            var floor = (UnityEngine.UI.Image)room.FindPropertyRelative("Floor").objectReferenceValue;
            var rect = icon.rectTransform;
            rect.sizeDelta *= 0.7f;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = (rect.parent == floor.transform
                ? Vector2.zero : floor.rectTransform.anchoredPosition) + offset;
            var obj = new GameObject(icon.name + " Black Backdrop",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(BoardMapCircleGraphic));
            obj.layer = icon.gameObject.layer;
            obj.transform.SetParent(icon.transform.parent, false);
            obj.transform.SetSiblingIndex(icon.transform.GetSiblingIndex());
            var circle = obj.GetComponent<BoardMapCircleGraphic>();
            circle.color = Color.black;
            circle.raycastTarget = false;
            var background = circle.rectTransform;
            background.anchorMin = background.anchorMax = background.pivot = Vector2.one * 0.5f;
            background.anchoredPosition = rect.anchoredPosition;
            background.sizeDelta = Vector2.one * Mathf.Max(rect.sizeDelta.x, rect.sizeDelta.y) +
                                   Vector2.one * 2f;
            obj.SetActive(icon.isActiveAndEnabled);
            binding.objectReferenceValue = circle;
        }
    }
}
