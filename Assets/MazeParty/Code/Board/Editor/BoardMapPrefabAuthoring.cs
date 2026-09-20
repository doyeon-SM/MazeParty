using System;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    /// <summary>Authors the board maps and status badges on BoardCanvas.prefab.</summary>
    public static class BoardMapPrefabAuthoring
    {
        private const string PrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab";

        [MenuItem("MazeParty/Board/Upgrade Board Map UI Prefab")]
        public static void UpgradePrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font == null)
                {
                    throw new InvalidOperationException(
                        "Unity LegacyRuntime.ttf was not found.");
                }
                Ensure(root, font);
                UpgradeExistingMinimapLayout(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static void Ensure(GameObject root, Font font)
        {
            if (root.GetComponent<BoardMapView>() == null)
            {
                CreateMaps(root, font);
            }
            if (root.GetComponent<BoardPlayerStatusBadges>() == null)
            {
                CreateStatusBadges(root, font);
            }
        }

        private static void CreateMaps(GameObject root, Font font)
        {
            var overview = CreatePanel(root.transform, "Board Overview Map",
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-22f, 0f), new Vector2(406f, 456f),
                new Vector2(1f, 0.5f));
            var minimap = CreatePanel(root.transform, "Board Action Minimap",
                new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-22f, -22f), new Vector2(282f, 330f),
                new Vector2(1f, 1f));
            var overviewCells = CreateCells(overview.transform, font, 47f, 4f,
                19f, 62f, 18);
            var minimapCells = CreateCells(minimap.transform, font, 32f, 3f,
                19f, 46f, 12);
            CreateLabel(overview.transform, "Overview Map Title",
                "NORTH ^  /  BOARD ROUTE", font,
                new Vector2(203f, -25f), new Vector2(378f, 31f), 19);
            CreateLabel(overview.transform, "Overview Map Legend",
                "CYAN  YOU / ROUTE    GOLD  KEY SHOP    1-4  PLAYERS", font,
                new Vector2(203f, -430f), new Vector2(388f, 31f), 12);
            CreateLabel(minimap.transform, "Minimap Title",
                "NORTH ^  /  BOARD", font,
                new Vector2(141f, -21f), new Vector2(260f, 28f), 16);
            CreateLabel(minimap.transform, "Minimap Legend",
                "CYAN YOU LIVE / K SHOP\nOTHER PLAYERS BEFORE MOVE", font,
                new Vector2(141f, -310f), new Vector2(270f, 24f), 10);
            overview.SetActive(false);
            minimap.SetActive(false);
            root.AddComponent<BoardMapView>().Configure(
                overview, minimap, overviewCells, minimapCells);
        }

        private static void UpgradeExistingMinimapLayout(GameObject root)
        {
            var minimap = FindDescendant(root.transform,
                "Board Action Minimap");
            if (minimap == null) return;
            var rect = minimap.GetComponent<RectTransform>();
            // Migrate only the initial bottom-right layout. Re-running setup
            // must preserve user-edited prefab positions.
            if (rect != null && rect.anchorMin == new Vector2(1f, 0f) &&
                Mathf.Approximately(rect.anchoredPosition.y, 148f))
            {
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = rect.anchorMin;
                rect.pivot = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(-22f, -22f);
            }
            var legend = FindDescendant(minimap, "Minimap Legend");
            var label = legend != null ? legend.GetComponent<Text>() : null;
            if (label != null && (label.text ==
                "CYAN YOU  GOLD SHOP  1-4 PLAYERS" || label.text ==
                "CYAN YOU  GOLD SHOP  1-4 PRIOR TILES"))
            {
                label.text = "CYAN YOU LIVE / K SHOP\nOTHER PLAYERS BEFORE MOVE";
            }
        }

        private static BoardMapView.Cell[] CreateCells(Transform panel,
            Font font, float size, float gap, float left, float top,
            int markerFontSize)
        {
            var cells = new BoardMapView.Cell[BoardMapView.CellCount];
            for (var y = 0; y < BoardMapView.GridSize; y++)
            {
                for (var x = 0; x < BoardMapView.GridSize; x++)
                {
                    var coordinateName = x + "_" + y;
                    var cell = NewUiObject("Map Cell " + coordinateName,
                        panel, typeof(Image));
                    var rect = cell.GetComponent<RectTransform>();
                    rect.anchorMin = new Vector2(0f, 1f);
                    rect.anchorMax = rect.anchorMin;
                    rect.pivot = new Vector2(0f, 1f);
                    rect.anchoredPosition = new Vector2(
                        left + x * (size + gap),
                        -top - (BoardMapView.GridSize - 1 - y) * (size + gap));
                    rect.sizeDelta = new Vector2(size, size);
                    var background = cell.GetComponent<Image>();
                    background.color = new Color(0.15f, 0.23f, 0.31f, 1f);
                    background.raycastTarget = false;

                    var marker = NewUiObject("Marker " + coordinateName,
                        cell.transform, typeof(Text)).GetComponent<Text>();
                    var markerRect = marker.rectTransform;
                    markerRect.anchorMin = Vector2.zero;
                    markerRect.anchorMax = Vector2.one;
                    markerRect.offsetMin = Vector2.zero;
                    markerRect.offsetMax = Vector2.zero;
                    marker.font = font;
                    marker.fontSize = markerFontSize;
                    marker.fontStyle = FontStyle.Bold;
                    marker.alignment = TextAnchor.MiddleCenter;
                    marker.color = Color.white;
                    marker.raycastTarget = false;
                    cells[y * BoardMapView.GridSize + x] =
                        new BoardMapView.Cell
                        {
                            Background = background,
                            Marker = marker
                        };
                }
            }
            return cells;
        }

        private static void CreateStatusBadges(GameObject root, Font font)
        {
            var count = MultiplayerConstants.MaxPlayers;
            var baseIcons = new Text[count];
            var forced = new Text[count];
            var combat = new Text[count];
            var shop = new Text[count];
            var death = new Text[count];
            for (var slot = 0; slot < count; slot++)
            {
                var card = FindDescendant(root.transform, "PlayerCard" + slot);
                var action = FindDescendant(card, "PlayerActionIcon" + slot);
                if (card == null || action == null)
                {
                    throw new InvalidOperationException(
                        "Board player card is missing its action icon: " + slot);
                }
                baseIcons[slot] = action.GetComponent<Text>();
                forced[slot] = CreateBadge(card, "PlayerForcedIcon" + slot,
                    "AUTO", font, new Color(0.42f, 0.78f, 1f));
                combat[slot] = CreateBadge(card, "PlayerCombatIcon" + slot,
                    "FIGHT", font, new Color(1f, 0.36f, 0.28f));
                shop[slot] = CreateBadge(card, "PlayerShopIcon" + slot,
                    "SHOP", font, new Color(1f, 0.78f, 0.25f));
                death[slot] = CreateBadge(card, "PlayerDeathIcon" + slot,
                    "DOWN", font, new Color(0.73f, 0.74f, 0.79f));
            }
            root.AddComponent<BoardPlayerStatusBadges>().Configure(
                baseIcons, forced, combat, shop, death);
        }

        private static Text CreateBadge(Transform card, string name,
            string caption, Font font, Color color)
        {
            var label = NewUiObject(name, card, typeof(Text)).GetComponent<Text>();
            var rect = label.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = rect.anchorMin;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(288f, -52f);
            rect.sizeDelta = new Vector2(100f, 38f);
            label.font = font;
            label.fontSize = 16;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = color;
            label.text = caption;
            label.raycastTarget = false;
            label.gameObject.SetActive(false);
            return label;
        }

        private static GameObject CreatePanel(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 position,
            Vector2 size, Vector2 pivot)
        {
            var panel = NewUiObject(name, parent, typeof(Image));
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = panel.GetComponent<Image>();
            image.color = new Color(0.025f, 0.045f, 0.08f, 0.93f);
            image.raycastTarget = false;
            return panel;
        }

        private static Text CreateLabel(Transform parent, string name,
            string caption, Font font, Vector2 position, Vector2 size,
            int fontSize)
        {
            var label = NewUiObject(name, parent, typeof(Text)).GetComponent<Text>();
            var rect = label.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = rect.anchorMin;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            label.text = caption;
            label.font = font;
            label.fontSize = fontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }

        private static GameObject NewUiObject(string name, Transform parent,
            params Type[] components)
        {
            var result = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer));
            result.layer = LayerMask.NameToLayer("UI");
            result.transform.SetParent(parent, false);
            for (var index = 0; index < components.Length; index++)
            {
                result.AddComponent(components[index]);
            }
            return result;
        }

        private static Transform FindDescendant(Transform parent, string name)
        {
            if (parent == null) return null;
            var children = parent.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < children.Length; index++)
            {
                if (children[index].name == name) return children[index];
            }
            return null;
        }
    }
}
