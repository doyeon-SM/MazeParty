using System;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    /// <summary>
    /// Maintains the already-authored map and HUD status-badge prefabs without
    /// recreating the deleted legacy overview grid.
    /// </summary>
    public static class BoardMapPrefabAuthoring
    {
        private const string MapPrefabPath =
            BoardCanvasSplitProjectSetup.MapPrefabPath;
        private const string HudPrefabPath =
            BoardCanvasSplitProjectSetup.HudPrefabPath;

        [MenuItem("MazeParty/Board/Upgrade Board Map UI Prefab")]
        public static void UpgradePrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(MapPrefabPath);
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
                PrefabUtility.SaveAsPrefabAsset(root, MapPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            var hud = PrefabUtility.LoadPrefabContents(HudPrefabPath);
            try
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                Ensure(hud, font);
                PrefabUtility.SaveAsPrefabAsset(hud, HudPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(hud);
            }

            AssetDatabase.SaveAssets();
        }

        public static void Ensure(GameObject root, Font font)
        {
            if (root.GetComponent<BoardMapView>() != null)
            {
                BoardMinimapPrefabAuthoring.Ensure(root, font);
                return;
            }

            if (FindDescendant(root.transform, "PlayerCard0") != null)
            {
                if (root.GetComponent<BoardPlayerStatusBadges>() == null)
                {
                    CreateStatusBadges(root, font);
                }
                return;
            }

            throw new InvalidOperationException(
                root.name + " is neither the authored BoardMapCanvas nor " +
                "BoardHudCanvas prefab.");
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
            label.fontStyle = FontStyle.Normal;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = color;
            label.text = caption;
            label.raycastTarget = false;
            label.gameObject.SetActive(false);
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
