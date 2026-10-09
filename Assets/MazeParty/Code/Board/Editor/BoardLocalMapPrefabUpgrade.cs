using Arikan;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class BoardLocalMapPrefabUpgrade
    {
        private const string BoardCanvasPrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab";

        [MenuItem("MazeParty/Board/Add Full Map Close Control")]
        public static void AddFullMapCloseControl()
        {
            var root = PrefabUtility.LoadPrefabContents(
                BoardCanvasPrefabPath);
            try
            {
                Ensure(root);
                PrefabUtility.SaveAsPrefabAsset(
                    root,
                    BoardCanvasPrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log(
                    "Board full-map close control was added without " +
                    "rebuilding the existing map UI.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static void Ensure(GameObject root)
        {
            var map = new SerializedObject(root.GetComponent<BoardMapView>());
            var local = root.GetComponent<BoardMinimapView>();
            var localData = new SerializedObject(local);
            var panel = (GameObject)map.FindProperty("minimapPanel").objectReferenceValue;
            if (map.FindProperty("fullMapPanel").objectReferenceValue == null)
            {
                var full = Object.Instantiate(panel, root.transform);
                full.name = "Board Full Map";
                var fullRect = full.GetComponent<RectTransform>();
                fullRect.anchorMin = fullRect.anchorMax = fullRect.pivot = Vector2.one * 0.5f;
                fullRect.anchoredPosition = Vector2.zero;
                fullRect.sizeDelta = new Vector2(780f, 780f);
                full.GetComponent<Image>().color = new Color(0.025f, 0.045f, 0.08f, 0.98f);
                var surface = full.GetComponentInChildren<MiniMapView>(true).otherDotCanvas;
                Place(surface, new Vector2(390f, -390f), new Vector2(728f, 728f));
                var title = full.transform.Find("Minimap Title").GetComponent<Text>();
                Place(title.rectTransform, new Vector2(320f, -25f), new Vector2(600f, 32f));
                title.text = "FULL MAP / NORTH ^";
                title.fontSize = 20;
                var descriptionTransform = full.transform.Find("Current Tile");
                var description = descriptionTransform != null
                    ? descriptionTransform.GetComponent<Text>()
                    : null;
                if (description != null)
                {
                    Place(description.rectTransform, new Vector2(320f, -650f),
                        new Vector2(600f, 50f));
                }
                var legend = full.transform.Find("Minimap Legend").GetComponent<Text>();
                Place(legend.rectTransform, new Vector2(320f, -696f), new Vector2(600f, 24f));
                legend.fontSize = 14;
                legend.text = "M CLOSE  /  1-4 PLAYERS  /  K KEY SHOP  /  R RESPAWN";
                var rooms = new BoardMinimapView.Room[BoardMapView.CellCount];
                var originalRooms = localData.FindProperty("rooms");
                for (var i = 0; i < rooms.Length; i++)
                {
                    var source = originalRooms.GetArrayElementAtIndex(i);
                    var floor = Remap<Image>(source.FindPropertyRelative("Floor"), panel.transform, full.transform);
                    var symbol = Remap<Text>(source.FindPropertyRelative("Symbol"), panel.transform, full.transform);
                    var walls = new Image[4];
                    var exits = new Text[4];
                    symbol.fontSize = 17;
                    for (var side = 0; side < 4; side++)
                    {
                        walls[side] = Remap<Image>(source.FindPropertyRelative("Walls").GetArrayElementAtIndex(side), panel.transform, full.transform);
                        exits[side] = Remap<Text>(source.FindPropertyRelative("Exits").GetArrayElementAtIndex(side), panel.transform, full.transform);
                        exits[side].fontSize = 14;
                        exits[side].rectTransform.sizeDelta = new Vector2(16f, 16f);
                    }
                    rooms[i] = new BoardMinimapView.Room { Floor = floor, Symbol = symbol, Walls = walls, Exits = exits };
                }
                var players = new Image[4];
                var highlights = new GameObject[4];
                for (var slot = 0; slot < 4; slot++)
                {
                    players[slot] = Remap<Image>(localData.FindProperty("players").GetArrayElementAtIndex(slot), panel.transform, full.transform);
                    var originalHighlight = (GameObject)localData.FindProperty("localHighlights").GetArrayElementAtIndex(slot).objectReferenceValue;
                    highlights[slot] = full.transform.Find(AnimationUtility.CalculateTransformPath(originalHighlight.transform, panel.transform)).gameObject;
                    var label = highlights[slot].GetComponent<Text>();
                    label.text = "YOU";
                    label.fontSize = 10;
                    label.rectTransform.sizeDelta = new Vector2(32f, 20f);
                }
                var fullView = full.AddComponent<BoardMinimapView>();
                // The current authored full-map design intentionally has no copied
                // title, legend, current-tile description or key-shop distance row.
                // They are optional bindings, not placeholders for setup to repair.
                Object.DestroyImmediate(title.gameObject);
                if (description != null)
                    Object.DestroyImmediate(description.gameObject);
                Object.DestroyImmediate(legend.gameObject);
                var distance = full.transform.Find("Key Shop Distance");
                if (distance != null)
                    Object.DestroyImmediate(distance.gameObject);
                fullView.Configure(
                    full.GetComponentInChildren<MiniMapView>(true),
                    rooms,
                    players,
                    highlights,
                    null,
                    null);
                var fullData = new SerializedObject(fullView);
                fullData.FindProperty("radiusInTiles").floatValue = 0f;
                fullData.FindProperty("followHeading").boolValue = false;
                fullData.FindProperty("localPlayerOnly").boolValue = true;
                fullData.FindProperty("showKeyShopDetails").boolValue = false;
                fullData.FindProperty("showTravelCounts").boolValue = true;
                fullData.FindProperty("headingFormat").stringValue = "FULL MAP / NORTH ^";
                fullData.ApplyModifiedPropertiesWithoutUndo();
                full.SetActive(false);
                map.FindProperty("fullMapPanel").objectReferenceValue = full;
                map.FindProperty("fullMap").objectReferenceValue = fullView;
                map.ApplyModifiedPropertiesWithoutUndo();
            }

            EnsureFullMapCloseButton(root, map);

            if (localData.FindProperty("circularMask").objectReferenceValue is Mask existingMask)
            {
                if (existingMask.GetComponent<CanvasRenderer>() == null)
                    existingMask.gameObject.AddComponent<CanvasRenderer>();
                return;
            }
            var clip = new GameObject("Local Map Circle", typeof(RectTransform), typeof(CanvasRenderer), typeof(BoardMapCircleGraphic), typeof(Mask));
            clip.layer = LayerMask.NameToLayer("UI");
            clip.transform.SetParent(panel.transform, false);
            Place((RectTransform)clip.transform, new Vector2(162f, -180f), new Vector2(280f, 280f));
            var graphic = clip.GetComponent<BoardMapCircleGraphic>();
            graphic.color = new Color(0.04f, 0.08f, 0.12f, 1f);
            graphic.raycastTarget = false;
            var projection = (MiniMapView)localData.FindProperty("projection").objectReferenceValue;
            var localSurface = projection.otherDotCanvas;
            localSurface.SetParent(clip.transform, false);
            localSurface.anchorMin = localSurface.anchorMax = localSurface.pivot = Vector2.one * 0.5f;
            localSurface.anchoredPosition = Vector2.zero;
            localSurface.sizeDelta = new Vector2(280f, 280f);
            localData.FindProperty("circularMask").objectReferenceValue = clip.GetComponent<Mask>();
            localData.FindProperty("radiusInTiles").floatValue = 2f;
            localData.ApplyModifiedPropertiesWithoutUndo();
            panel.transform.Find("Minimap Legend").GetComponent<Text>().text = "RADIUS 2 TILES  /  ^ YOU\nM FULL MAP";
        }

        private static T Remap<T>(SerializedProperty property, Transform source, Transform destination) where T : Component
        {
            var component = (T)property.objectReferenceValue;
            return destination.Find(AnimationUtility.CalculateTransformPath(component.transform, source)).GetComponent<T>();
        }

        private static void EnsureFullMapCloseButton(
            GameObject root,
            SerializedObject map)
        {
            var closeProperty = map.FindProperty("fullMapCloseButton");
            if (closeProperty.objectReferenceValue != null)
            {
                return;
            }

            var fullMapPanel =
                (GameObject)map.FindProperty("fullMapPanel").objectReferenceValue;
            var existing = fullMapPanel.transform.Find("Full Map Close Button");
            Button closeButton;
            if (existing != null)
            {
                closeButton = existing.GetComponent<Button>();
            }
            else
            {
                var template = root.GetComponentsInChildren<Button>(true);
                Button source = null;
                foreach (var candidate in template)
                {
                    if (candidate.name == "ItemShopCloseButton")
                    {
                        source = candidate;
                        break;
                    }
                }

                if (source == null)
                {
                    throw new System.InvalidOperationException(
                        "BoardCanvas.prefab requires ItemShopCloseButton as " +
                        "the authored source for the full-map close control.");
                }

                var closeObject = Object.Instantiate(
                    source.gameObject,
                    fullMapPanel.transform,
                    false);
                closeObject.name = "Full Map Close Button";
                closeButton = closeObject.GetComponent<Button>();
                var rect = closeObject.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.one;
                rect.anchorMax = Vector2.one;
                rect.pivot = Vector2.one;
                rect.anchoredPosition = new Vector2(-16f, -16f);
                rect.sizeDelta = new Vector2(120f, 44f);
                closeObject.transform.SetAsLastSibling();
            }

            if (closeButton == null)
            {
                throw new System.InvalidOperationException(
                    "Full Map Close Button must have a Button component.");
            }

            closeButton.gameObject.SetActive(false);
            closeProperty.objectReferenceValue = closeButton;
            map.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
