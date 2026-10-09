using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class BoardMapInfoPrefabUpgrade
    {
        private const string HomeFilledPath =
            "Assets/Ignore/Modern UI Pack/Textures/Icon/Navigation/Home Filled.png";
        private const string MoneyFilledPath =
            "Assets/Ignore/Modern UI Pack/Textures/Icon/Business & Commerce/Money Filled.png";
        private const string AddPath =
            "Assets/Ignore/Modern UI Pack/Textures/Icon/Navigation/Add.png";
        private const string WarningFilledPath =
            "Assets/Ignore/Modern UI Pack/Textures/Icon/Navigation/Warning Filled.png";
        private const string ArrowUpPath =
            "Assets/Ignore/Modern UI Pack/Textures/Icon/Navigation/Arrow Up.png";
        private const string LocationMarkFilledPath =
            "Assets/Ignore/Modern UI Pack/Textures/Icon/Map/Location Mark Filled.png";
        private const string HelpFilledPath =
            "Assets/Ignore/Modern UI Pack/Textures/Icon/Navigation/Help Filled.png";

        public static void Ensure(GameObject root)
        {
            var map = new SerializedObject(root.GetComponent<BoardMapView>());
            Upgrade(root.GetComponent<BoardMinimapView>());
            Upgrade((BoardMinimapView)map.FindProperty("fullMap")
                .objectReferenceValue);
        }

        private static void Upgrade(BoardMinimapView view)
        {
            var data = new SerializedObject(view);
            var home = RequireSprite(HomeFilledPath);
            var money = RequireSprite(MoneyFilledPath);
            var damage = RequireSprite(AddPath, "Add");
            var warning = RequireSprite(WarningFilledPath);
            var arrowUp = RequireSprite(ArrowUpPath);
            var location = RequireSprite(LocationMarkFilledPath);
            var help = RequireSprite(HelpFilledPath);
            data.FindProperty("homeFilledIcon").objectReferenceValue = home;
            data.FindProperty("moneyFilledIcon").objectReferenceValue = money;
            data.FindProperty("damageIcon").objectReferenceValue = damage;
            data.FindProperty("warningFilledIcon").objectReferenceValue = warning;
            data.FindProperty("arrowUpIcon").objectReferenceValue = arrowUp;
            data.FindProperty("locationMarkFilledIcon").objectReferenceValue =
                location;
            data.FindProperty("helpFilledIcon").objectReferenceValue = help;
            SetColorArray(
                data.FindProperty("typeIconColors"),
                Color.gray,
                Color.gray,
                new Color(1f, .82f, .12f, 1f),
                Color.white);
            SetColorArray(
                data.FindProperty("effectIconColors"),
                Color.white,
                new Color(1f, .82f, .12f, 1f),
                new Color(1f, .2f, .18f, 1f),
                Color.white,
                new Color(.35f, 1f, .5f, 1f),
                new Color(.35f, 1f, .5f, 1f),
                new Color(1f, .2f, .18f, 1f),
                new Color(1f, .2f, .18f, 1f),
                new Color(1f, .2f, .18f, 1f));
            data.FindProperty("startFloorColor").colorValue =
                data.FindProperty("floorColor").colorValue;
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
                        24f);
                }
                var effectProperty = room.FindPropertyRelative("EffectIcon");
                if (effectProperty.objectReferenceValue == null)
                {
                    effectProperty.objectReferenceValue = Icon(
                        "Landing Effect",
                        floor.transform,
                        BoardMapIconKind.GoldGain,
                        new Vector2(.72f, .72f),
                        24f);
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

                for (var side = 0; side < 4; side++)
                {
                    var arrow = arrows.GetArrayElementAtIndex(side)
                        .objectReferenceValue as BoardMapIcon;
                    if (arrow != null)
                        arrow.SetIcon(BoardMapIconKind.Arrow, arrowUp);
                }
            }
            // Current-tile and key-shop distance copy were deliberately removed
            // from both authored maps. Keep the bindings empty so setup never
            // interprets that design choice as a missing migration.
            data.FindProperty("currentTile").objectReferenceValue = null;
            data.FindProperty("shopDistanceIcon").objectReferenceValue = null;
            data.FindProperty("shopDistanceText").objectReferenceValue = null;
            data.FindProperty("showKeyShopDetails").boolValue = false;
            data.ApplyModifiedPropertiesWithoutUndo();
            EnsureLandingEffectLayer(data, rooms, location);
            var mine = data.FindProperty("mineGraphic")
                .objectReferenceValue as BoardMapMineGraphic;
            if (mine != null)
            {
                mine.SetIcon(help);
                mine.color = new Color(1f, .2f, .18f, 1f);
                EditorUtility.SetDirty(mine);
            }
            data.ApplyModifiedPropertiesWithoutUndo();
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

        private static void EnsureLandingEffectLayer(
            SerializedObject data,
            SerializedProperty rooms,
            Sprite markerSprite = null)
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

            if (markerSprite == null)
            {
                markerSprite = RequireSprite(LocationMarkFilledPath);
            }

            var players = data.FindProperty("players");
            for (var slot = 0; slot < players.arraySize; slot++)
            {
                var marker = players.GetArrayElementAtIndex(slot)
                    .objectReferenceValue as Image;
                if (marker == null)
                    continue;

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

        private static Sprite RequireSprite(
            string path,
            string spriteName = null)
        {
            if (!string.IsNullOrEmpty(spriteName))
            {
                var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                for (var index = 0; index < assets.Length; index++)
                {
                    if (assets[index] is Sprite sprite &&
                        sprite.name == spriteName)
                    {
                        return sprite;
                    }
                }
            }

            var result = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (result == null)
            {
                throw new System.InvalidOperationException(
                    "ModernUIPack map icon is missing or not imported as a " +
                    "Sprite: " + path);
            }
            return result;
        }

        private static void SetColorArray(
            SerializedProperty property,
            params Color[] colors)
        {
            property.arraySize = colors.Length;
            for (var index = 0; index < colors.Length; index++)
                property.GetArrayElementAtIndex(index).colorValue = colors[index];
        }
    }
}
