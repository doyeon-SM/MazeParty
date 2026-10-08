using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class BoardMapInfoPrefabUpgrade
    {
        public static void Ensure(GameObject root)
        {
            var map = new SerializedObject(root.GetComponent<BoardMapView>());
            Upgrade(root.GetComponent<BoardMinimapView>(),
                (GameObject)map.FindProperty("minimapPanel").objectReferenceValue, false);
            Upgrade((BoardMinimapView)map.FindProperty("fullMap").objectReferenceValue,
                (GameObject)map.FindProperty("fullMapPanel").objectReferenceValue, true);
        }

        private static void Upgrade(BoardMinimapView view, GameObject panel, bool full)
        {
            var data = new SerializedObject(view);
            var rooms = data.FindProperty("rooms");
            EnsureLandingEffectLayer(data, rooms);
            for (var index = 0; index < rooms.arraySize; index++)
            {
                var room = rooms.GetArrayElementAtIndex(index);
                var floor = (Image)room.FindPropertyRelative("Floor").objectReferenceValue;
                var typeProperty = room.FindPropertyRelative("TypeIcon");
                if (typeProperty.objectReferenceValue == null)
                {
                    typeProperty.objectReferenceValue = Icon(
                        "Tile Type",
                        floor.transform,
                        BoardMapIconKind.Room,
                        new Vector2(.28f, .72f),
                        20f);
                }
                var effectProperty = room.FindPropertyRelative("EffectIcon");
                if (effectProperty.objectReferenceValue == null)
                {
                    effectProperty.objectReferenceValue = Icon(
                        "Landing Effect",
                        floor.transform,
                        BoardMapIconKind.GoldGain,
                        new Vector2(.72f, .72f),
                        20f);
                }
                var arrows = room.FindPropertyRelative("ProgressArrows");
                if (arrows.arraySize != 4)
                    arrows.arraySize = 4;
                for (var side = 0; side < 4; side++)
                {
                    var arrowProperty = arrows.GetArrayElementAtIndex(side);
                    if (arrowProperty.objectReferenceValue != null)
                        continue;
                    var position = side == 0 ? new Vector2(.5f, .9f) : side == 1 ? new Vector2(.9f, .5f) :
                        side == 2 ? new Vector2(.5f, .1f) : new Vector2(.1f, .5f);
                    var arrow = Icon("Available Next Tile " + side, floor.transform, BoardMapIconKind.Arrow, position, 19f);
                    arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f * side);
                    arrow.color = new Color(.35f, 1f, .65f);
                    arrowProperty.objectReferenceValue = arrow;
                }
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            EnsureLandingEffectLayer(data, rooms);
            data.ApplyModifiedPropertiesWithoutUndo();

            // The authored full-map design deliberately omits its title, legend,
            // current-tile copy and key-shop distance row. Setup must never infer
            // that their null bindings are an incomplete migration and recreate them.
            if (full || data.FindProperty("shopDistanceText").objectReferenceValue != null)
                return;

            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(panelRect.sizeDelta.x, 450f);
            var line = new GameObject("Key Shop Distance", typeof(RectTransform));
            line.layer = LayerMask.NameToLayer("UI");
            line.transform.SetParent(panel.transform, false);
            var rect = (RectTransform)line.transform;
            Place(rect, new Vector2(162f, -347f), new Vector2(220f, 28f));
            var key = Icon("Key Icon", line.transform, BoardMapIconKind.Key, new Vector2(0f, .5f), 24f);
            key.rectTransform.anchoredPosition = new Vector2(12f, 0f);
            key.color = new Color(1f, .8f, .2f);
            var labelObject = new GameObject("Distance", typeof(RectTransform), typeof(Text));
            labelObject.layer = LayerMask.NameToLayer("UI");
            labelObject.transform.SetParent(line.transform, false);
            var label = labelObject.GetComponent<Text>();
            label.font = panel.transform.Find("Minimap Title").GetComponent<Text>().font;
            label.fontSize = 16;
            label.fontStyle = FontStyle.Bold;
            label.color = key.color;
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            label.text = ": --";
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(34f, 0f);
            label.rectTransform.offsetMax = Vector2.zero;
            data.FindProperty("shopDistanceIcon").objectReferenceValue = key;
            data.FindProperty("shopDistanceText").objectReferenceValue = label;
            data.ApplyModifiedPropertiesWithoutUndo();
            var description = panel.transform.Find("Current Tile").GetComponent<Text>();
            Place(description.rectTransform, new Vector2(162f, -387f),
                new Vector2(300f, 44f));
            var legend = panel.transform.Find("Minimap Legend").GetComponent<Text>();
            Place(legend.rectTransform, new Vector2(162f, -430f),
                new Vector2(300f, 26f));
            legend.text = "ARROW: NEXT AVAILABLE TILE\nRADIUS 2 TILES  /  M FULL MAP";
        }

        private static BoardMapIcon Icon(string name, Transform parent, BoardMapIconKind kind, Vector2 anchor, float size)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(BoardMapIcon));
            obj.layer = LayerMask.NameToLayer("UI");
            obj.transform.SetParent(parent, false);
            var icon = obj.GetComponent<BoardMapIcon>();
            icon.SetIcon(kind);
            icon.raycastTarget = false;
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = anchor;
            icon.rectTransform.anchoredPosition = Vector2.zero;
            icon.rectTransform.sizeDelta = Vector2.one * size;
            return icon;
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = Vector2.one * .5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void EnsureLandingEffectLayer(
            SerializedObject data,
            SerializedProperty rooms)
        {
            var projection = data.FindProperty("projection")
                .objectReferenceValue as Arikan.MiniMapView;
            var surface = projection != null ? projection.otherDotCanvas : null;
            if (surface == null)
                return;

            var layer = surface.Find("Landing Effect Layer") as RectTransform;
            if (layer == null)
            {
                var layerObject = new GameObject(
                    "Landing Effect Layer",
                    typeof(RectTransform));
                layerObject.layer = LayerMask.NameToLayer("UI");
                layerObject.transform.SetParent(surface, false);
                layer = (RectTransform)layerObject.transform;
            }

            layer.anchorMin = Vector2.zero;
            layer.anchorMax = Vector2.one;
            layer.pivot = Vector2.one * 0.5f;
            layer.anchoredPosition = Vector2.zero;
            layer.sizeDelta = Vector2.zero;
            layer.localScale = Vector3.one;
            data.FindProperty("landingEffectLayer").objectReferenceValue = layer;

            var markerSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/MazeParty/Resources/MazeParty/Expressions/WheelDot.png");
            if (markerSprite == null)
            {
                var markerAssets = AssetDatabase.LoadAllAssetsAtPath(
                    "Assets/MazeParty/Resources/MazeParty/Expressions/WheelDot.png");
                for (var index = 0; index < markerAssets.Length; index++)
                {
                    if (markerAssets[index] is Sprite sprite)
                    {
                        markerSprite = sprite;
                        break;
                    }
                }
            }

            var players = data.FindProperty("players");
            for (var slot = 0; slot < players.arraySize; slot++)
            {
                var marker = players.GetArrayElementAtIndex(slot)
                    .objectReferenceValue as Image;
                if (marker == null)
                    continue;

                if (marker.sprite == null)
                    marker.sprite = markerSprite;
                marker.type = Image.Type.Simple;
                marker.preserveAspect = true;
                marker.raycastTarget = false;
            }

            for (var index = 0; index < rooms.arraySize; index++)
            {
                var room = rooms.GetArrayElementAtIndex(index);
                var floor = room.FindPropertyRelative("Floor")
                    .objectReferenceValue as Image;
                var effect = room.FindPropertyRelative("EffectIcon")
                    .objectReferenceValue as BoardMapIcon;
                if (floor == null || effect == null)
                    continue;

                var rect = effect.rectTransform;
                if (rect.parent != layer)
                    rect.SetParent(layer, false);
                rect.anchorMin = rect.anchorMax = rect.pivot =
                    Vector2.one * 0.5f;
                rect.anchoredPosition = floor.rectTransform.anchoredPosition;
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                effect.raycastTarget = false;
            }

            layer.SetAsLastSibling();
        }
    }
}
