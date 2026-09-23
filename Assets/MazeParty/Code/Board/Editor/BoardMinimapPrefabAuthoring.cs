using System;
using Arikan;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class BoardMinimapPrefabAuthoring
    {
        private const string Path = "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab";

        [MenuItem("MazeParty/Board/Install Live Minimap")]
        public static void Install()
        {
            var root = PrefabUtility.LoadPrefabContents(Path);
            try
            {
                Ensure(root, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
                PrefabUtility.SaveAsPrefabAsset(root, Path);
                AssetDatabase.SaveAssets();
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static void Ensure(GameObject root, Font font)
        {
            var existing = root.GetComponent<BoardMinimapView>();
            if (existing != null)
            {
                BoardLocalMapPrefabUpgrade.Ensure(root);
                BoardMapInfoPrefabUpgrade.Ensure(root);
                if (!existing.HasRequiredReferences)
                    throw new InvalidOperationException("Board minimap bindings are incomplete.");
                return;
            }
            var map = root.GetComponent<BoardMapView>();
            var serialized = new SerializedObject(map);
            var panel = (GameObject)serialized.FindProperty("minimapPanel").objectReferenceValue;
            var oldCells = serialized.FindProperty("minimapCells");
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = Vector2.one;
            panelRect.anchoredPosition = new Vector2(-22f, -22f);
            panelRect.sizeDelta = new Vector2(324f, 410f);
            var title = panel.transform.Find("Minimap Title").GetComponent<Text>();
            Place(title.rectTransform, new Vector2(162f, -23f), new Vector2(292f, 28f));
            title.text = "BOARD / N ^";
            var legend = panel.transform.Find("Minimap Legend").GetComponent<Text>();
            Place(legend.rectTransform, new Vector2(162f, -385f), new Vector2(300f, 28f));
            legend.fontSize = 11;
            legend.text = "^ YOU   1-4 LIVE   K KEY SHOP   R RESPAWN\nLINE WALL / ARROW EXIT / ORANGE BLOCKED";

            // An inscribed square keeps the entire board visible through every heading.
            var surface = Rect("Heading Map", panel.transform);
            Place(surface, new Vector2(162f, -180f), new Vector2(198f, 198f));
            var rooms = new BoardMinimapView.Room[BoardMapView.CellCount];
            for (var i = 0; i < rooms.Length; i++)
            {
                var cell = oldCells.GetArrayElementAtIndex(i);
                var floor = (Image)cell.FindPropertyRelative("Background").objectReferenceValue;
                var symbol = (Text)cell.FindPropertyRelative("Marker").objectReferenceValue;
                floor.transform.SetParent(surface, false);
                var rect = floor.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
                rect.sizeDelta = Vector2.one * (198f / 7f);
                rect.anchoredPosition = new Vector2((i % 7 - 3) * 198f / 7f, (i / 7 - 3) * 198f / 7f);
                symbol.fontSize = 10;
                var walls = new Image[4];
                var exits = new Text[4];
                var arrows = new[] { "^", ">", "v", "<" };
                for (var side = 0; side < 4; side++)
                {
                    var wall = Rect("Wall " + side, rect).gameObject.AddComponent<Image>();
                    wall.raycastTarget = false;
                    wall.color = new Color(0.62f, 0.73f, 0.82f);
                    var wr = wall.rectTransform;
                    var vertical = side == 1 || side == 3;
                    var edge = side == 0 || side == 1 ? 1f : 0f;
                    wr.anchorMin = vertical ? new Vector2(edge, 0f) : new Vector2(0f, edge);
                    wr.anchorMax = vertical ? new Vector2(edge, 1f) : new Vector2(1f, edge);
                    wr.sizeDelta = vertical ? new Vector2(1.5f, 0f) : new Vector2(0f, 1.5f);
                    wr.anchoredPosition = Vector2.zero;
                    walls[side] = wall;
                    var arrow = Label("Exit " + side, rect, font, arrows[side], 10);
                    arrow.rectTransform.anchorMin = arrow.rectTransform.anchorMax =
                        side == 0 ? new Vector2(0.5f, 1f) : side == 1 ? new Vector2(1f, 0.5f) :
                        side == 2 ? new Vector2(0.5f, 0f) : new Vector2(0f, 0.5f);
                    arrow.rectTransform.sizeDelta = new Vector2(10f, 10f);
                    arrow.rectTransform.anchoredPosition = side == 0 ? new Vector2(0f, -4f) :
                        side == 1 ? new Vector2(-4f, 0f) : side == 2 ? new Vector2(0f, 4f) : new Vector2(4f, 0f);
                    exits[side] = arrow;
                }
                rooms[i] = new BoardMinimapView.Room { Floor = floor, Symbol = symbol, Walls = walls, Exits = exits };
            }
            var dots = new Image[4];
            var highlights = new GameObject[4];
            for (var slot = 0; slot < 4; slot++)
            {
                var dot = Rect("Live Player " + (slot + 1), surface).gameObject.AddComponent<Image>();
                dot.raycastTarget = false;
                dot.rectTransform.sizeDelta = Vector2.one * 15f;
                var number = Label("Seat", dot.transform, font, (slot + 1).ToString(), 11);
                Stretch(number.rectTransform);
                number.color = new Color(0.02f, 0.03f, 0.04f);
                number.fontStyle = FontStyle.Bold;
                var highlight = Label("You Heading", dot.transform, font, "^", 18);
                highlight.rectTransform.anchoredPosition = new Vector2(0f, 13f);
                highlight.rectTransform.sizeDelta = new Vector2(22f, 20f);
                highlights[slot] = highlight.gameObject;
                dots[slot] = dot;
                dot.gameObject.SetActive(false);
            }
            var description = Label("Current Tile", panel.transform, font, "CURRENT TILE  --", 14);
            Place(description.rectTransform, new Vector2(162f, -338f), new Vector2(300f, 50f));

            // Disabled data-only bounds avoid the vendor's automatic target creation.
            var boundsRoot = new GameObject("Minimap World Bounds");
            boundsRoot.SetActive(false);
            boundsRoot.transform.SetParent(panel.transform, false);
            var top = new GameObject("TopRight").transform;
            top.SetParent(boundsRoot.transform, false);
            var bottom = new GameObject("BottomLeft").transform;
            bottom.SetParent(boundsRoot.transform, false);
            var bounds = boundsRoot.AddComponent<MiniMapBounds>();
            bounds.topRight = top;
            bounds.bottomLeft = bottom;
            bounds.enabled = false;
            var projection = surface.gameObject.AddComponent<MiniMapView>();
            projection.enabled = false;
            projection.otherDotCanvas = surface;
            projection.centeredDotCanvas = surface;
            projection.miniMapBounds = bounds;
            var view = root.AddComponent<BoardMinimapView>();
            view.Configure(projection, rooms, dots, highlights, description, title);
            serialized.Update();
            serialized.FindProperty("liveMinimap").objectReferenceValue = view;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            BoardLocalMapPrefabUpgrade.Ensure(root);
            BoardMapInfoPrefabUpgrade.Ensure(root);
            if (!view.HasRequiredReferences) throw new InvalidOperationException("Minimap setup failed.");
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.layer = LayerMask.NameToLayer("UI");
            obj.transform.SetParent(parent, false);
            return (RectTransform)obj.transform;
        }

        private static Text Label(string name, Transform parent, Font font, string text, int size)
        {
            var label = Rect(name, parent).gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.text = text;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.color = Color.white;
            return label;
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
