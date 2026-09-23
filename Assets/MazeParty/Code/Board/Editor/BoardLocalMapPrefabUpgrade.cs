using Arikan;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class BoardLocalMapPrefabUpgrade
    {
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
                fullRect.sizeDelta = new Vector2(640f, 720f);
                full.GetComponent<Image>().color = new Color(0.025f, 0.045f, 0.08f, 0.98f);
                var surface = full.GetComponentInChildren<MiniMapView>(true).otherDotCanvas;
                Place(surface, new Vector2(320f, -335f), new Vector2(560f, 560f));
                var title = full.transform.Find("Minimap Title").GetComponent<Text>();
                Place(title.rectTransform, new Vector2(320f, -25f), new Vector2(600f, 32f));
                title.text = "FULL MAP / NORTH ^";
                title.fontSize = 20;
                var description = full.transform.Find("Current Tile").GetComponent<Text>();
                Place(description.rectTransform, new Vector2(320f, -650f), new Vector2(600f, 50f));
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
                fullView.Configure(full.GetComponentInChildren<MiniMapView>(true), rooms, players, highlights, description, title);
                var fullData = new SerializedObject(fullView);
                fullData.FindProperty("radiusInTiles").floatValue = 0f;
                fullData.FindProperty("followHeading").boolValue = false;
                fullData.FindProperty("headingFormat").stringValue = "FULL MAP / NORTH ^";
                fullData.ApplyModifiedPropertiesWithoutUndo();
                full.SetActive(false);
                map.FindProperty("fullMapPanel").objectReferenceValue = full;
                map.FindProperty("fullMap").objectReferenceValue = fullView;
                map.ApplyModifiedPropertiesWithoutUndo();
            }

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

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
