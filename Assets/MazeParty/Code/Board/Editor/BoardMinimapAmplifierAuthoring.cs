using System;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Editor
{
    public static class BoardMinimapAmplifierAuthoring
    {
        public static void Ensure(GameObject root)
        {
            var binding = root.GetComponent<BoardMapCanvasBindings>();
            var data = new SerializedObject(binding);
            var property = data.FindProperty("amplifierOutline");
            if (property.objectReferenceValue != null) return;
            var map = new SerializedObject(root.GetComponent<BoardMapView>());
            var panel = (GameObject)map.FindProperty("minimapPanel").objectReferenceValue;
            var circle = panel.transform.Find("Local Map Circle") as RectTransform;
            if (circle == null) throw new InvalidOperationException("Authored minimap circle is missing.");
            var outline = panel.GetComponentInChildren<BoardMinimapAmplifierOutline>(true);
            if (outline == null)
            {
                var go = new GameObject("Tile Amplifier Outline", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(BoardMinimapAmplifierOutline));
                var rect = (RectTransform)go.transform;
                rect.SetParent(circle.parent, false);
                rect.anchorMin = circle.anchorMin; rect.anchorMax = circle.anchorMax;
                rect.pivot = circle.pivot; rect.anchoredPosition = circle.anchoredPosition;
                rect.sizeDelta = circle.sizeDelta;
                rect.SetSiblingIndex(circle.GetSiblingIndex() + 1);
                outline = go.GetComponent<BoardMinimapAmplifierOutline>();
                outline.color = new Color(.68f, .2f, 1f, 1f);
                outline.raycastTarget = false;
                outline.enabled = false;
            }
            property.objectReferenceValue = outline;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
