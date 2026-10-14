using System;
using System.Linq;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    /// <summary>One-time authored asset upgrade. Never rebuilds the user's map panel.</summary>
    public static class BoardMapGuidancePrefabUpgrade
    {
        private const string MapPrefab =
            "Assets/MazeParty/Prefabs/Board/UI/BoardMapCanvas.prefab";
        private const string YellowPrefab =
            "Assets/MazeParty/Prefabs/Board/World/KeyShopRouteHemisphere.prefab";
        private const string BluePrefab =
            "Assets/MazeParty/Prefabs/Board/World/TravelRouteHemisphere.prefab";
        private const string YellowMaterial =
            "Assets/MazeParty/Board/Materials/KeyShopRouteYellow.mat";
        private const string BlueMaterial =
            "Assets/MazeParty/Board/Materials/TravelRouteBlue.mat";

        private static readonly string[] Descriptions =
        {
            ": YOU / FACING", ": OTHER PLAYER", ": RESPAWN",
            ": KEY SHOP", ": GOLD GAIN", ": GOLD LOSS",
            ": ITEM REWARD", ": HEALING", ": DAMAGE",
            ": SPECIAL EVENT", ": ONE-WAY PATH", ": YOUR MINE",
            ": ROUTE TO KEY SHOP", ": REACHABLE THIS TURN"
        };

        [MenuItem("MazeParty/Board/Add Map Icon Info And Travel Guidance")]
        public static void Apply()
        {
            var blue = EnsureBlueMarker();
            UpgradeMapPrefab();
            BindScene("Assets/MazeParty/Scenes/Board/Board.unity", blue);
            BindScene("Assets/MazeParty/Scenes/Board/Dev/BoardFlowTestbed.unity", blue);
            AssetDatabase.SaveAssets();
            Debug.Log("Map icon info, player direction and travel route assets are authored.");
        }

        private static GameObject EnsureBlueMarker()
        {
            var materialCreated = AssetDatabase.LoadAssetAtPath<Material>(
                BlueMaterial) == null;
            if (materialCreated)
                AssetDatabase.CopyAsset(YellowMaterial, BlueMaterial);
            var material = AssetDatabase.LoadAssetAtPath<Material>(BlueMaterial);
            if (materialCreated)
            {
                material.name = "TravelRouteBlue";
                material.SetColor("_BaseColor", new Color(.05f, .48f, 1f, 1f));
                material.SetColor("_EmissionColor", new Color(.02f, .18f, .6f, 1f));
                EditorUtility.SetDirty(material);
            }
            var prefabCreated = AssetDatabase.LoadAssetAtPath<GameObject>(
                BluePrefab) == null;
            if (prefabCreated)
                AssetDatabase.CopyAsset(YellowPrefab, BluePrefab);
            if (!prefabCreated)
                return AssetDatabase.LoadAssetAtPath<GameObject>(BluePrefab);
            var root = PrefabUtility.LoadPrefabContents(BluePrefab);
            try
            {
                root.name = "TravelRouteHemisphere";
                var particle = root.GetComponentInChildren<ParticleSystem>(true);
                var main = particle.main;
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(.04f, .48f, 1f, .4f));
                particle.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
                PrefabUtility.SaveAsPrefabAsset(root, BluePrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return AssetDatabase.LoadAssetAtPath<GameObject>(BluePrefab);
        }

        private static void UpgradeMapPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(MapPrefab);
            try
            {
                var views = root.GetComponentsInChildren<BoardMinimapView>(true);
                foreach (var view in views)
                    UpgradeView(view);
                var panel = root.GetComponentsInChildren<RectTransform>(true)
                    .FirstOrDefault(rect => rect.name == "BoardMapIconInfo");
                if (panel == null)
                    throw new InvalidOperationException("Authored BoardMapIconInfo is missing.");
                if (panel.childCount == 0)
                    AddLegend(panel, views.First(view => view.name == "Board Full Map"));
                PrefabUtility.SaveAsPrefabAsset(root, MapPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void UpgradeView(BoardMinimapView view)
        {
            var data = new SerializedObject(view);
            var rooms = data.FindProperty("rooms");
            for (var index = 0; index < rooms.arraySize; index++)
            {
                var floor = rooms.GetArrayElementAtIndex(index)
                    .FindPropertyRelative("Floor").objectReferenceValue as Image;
                if (floor == null) continue;
                foreach (var marker in floor.transform.parent
                             .GetComponentsInChildren<Text>(true)
                             .Where(text => text.name.StartsWith("Marker "))
                    .ToArray())
                    UnityEngine.Object.DestroyImmediate(marker.gameObject);
            }
            var yellow = data.FindProperty("shopRouteGraphic")
                .objectReferenceValue as BoardMapRouteGraphic;
            if (yellow == null) throw new InvalidOperationException("Shop route graphic missing.");
            var blue = data.FindProperty("travelRouteGraphic")
                .objectReferenceValue as BoardMapRouteGraphic;
            if (blue == null)
            {
                var clone = UnityEngine.Object.Instantiate(
                    yellow.gameObject, yellow.transform.parent, false);
                clone.name = "Travel Route Dots";
                blue = clone.GetComponent<BoardMapRouteGraphic>();
                blue.raycastTarget = false;
                blue.color = new Color(.04f, .32f, .65f, .65f);
                clone.transform.SetSiblingIndex(yellow.transform.GetSiblingIndex() + 1);
                data.FindProperty("travelRouteGraphic").objectReferenceValue = blue;
            }
            var preferred = data.FindProperty("preferredTravelRouteGraphic")
                .objectReferenceValue as BoardMapRouteGraphic;
            if (preferred == null)
            {
                var clone = UnityEngine.Object.Instantiate(
                    blue.gameObject, blue.transform.parent, false);
                clone.name = "Preferred Travel Route Dots";
                preferred = clone.GetComponent<BoardMapRouteGraphic>();
                preferred.raycastTarget = false;
                preferred.color = new Color(.06f, .55f, 1f, 1f);
                clone.transform.SetSiblingIndex(blue.transform.GetSiblingIndex() + 1);
                data.FindProperty("preferredTravelRouteGraphic")
                    .objectReferenceValue = preferred;
            }
            var players = data.FindProperty("players");
            var highlights = data.FindProperty("localHighlights");
            var directions = data.FindProperty("localDirections");
            directions.arraySize = players.arraySize;
            for (var slot = 0; slot < players.arraySize; slot++)
            {
                var marker = players.GetArrayElementAtIndex(slot)
                    .objectReferenceValue as Image;
                var ring = marker.GetComponentInChildren<BoardMapPlayerDirectionGraphic>(true);
                if (ring == null)
                {
                    var ringObject = new GameObject(
                        "Self Direction Ring", typeof(RectTransform),
                        typeof(CanvasRenderer), typeof(BoardMapPlayerDirectionGraphic));
                    ringObject.layer = marker.gameObject.layer;
                    ringObject.transform.SetParent(marker.transform, false);
                    var rect = (RectTransform)ringObject.transform;
                    rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
                    rect.anchoredPosition = Vector2.zero;
                    rect.sizeDelta = new Vector2(54f, 54f);
                    ring = ringObject.GetComponent<BoardMapPlayerDirectionGraphic>();
                    ring.color = Color.white;
                    ring.raycastTarget = false;
                    ringObject.SetActive(false);
                    var oldLabel = highlights.GetArrayElementAtIndex(slot)
                        .objectReferenceValue as GameObject;
                    if (oldLabel != null && oldLabel.TryGetComponent<Text>(out var text))
                        text.enabled = false;
                }
                directions.GetArrayElementAtIndex(slot).objectReferenceValue = ring;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddLegend(RectTransform panel, BoardMinimapView fullMap)
        {
            var data = new SerializedObject(fullMap);
            var sprites = new[]
            {
                data.FindProperty("locationMarkFilledIcon").objectReferenceValue as Sprite,
                data.FindProperty("locationMarkFilledIcon").objectReferenceValue as Sprite,
                data.FindProperty("homeFilledIcon").objectReferenceValue as Sprite,
                null,
                data.FindProperty("moneyFilledIcon").objectReferenceValue as Sprite,
                data.FindProperty("moneyFilledIcon").objectReferenceValue as Sprite,
                null,
                null,
                data.FindProperty("damageIcon").objectReferenceValue as Sprite,
                data.FindProperty("warningFilledIcon").objectReferenceValue as Sprite,
                data.FindProperty("arrowUpIcon").objectReferenceValue as Sprite,
                data.FindProperty("helpFilledIcon").objectReferenceValue as Sprite,
                null,
                null
            };
            var kinds = new[]
            {
                BoardMapIconKind.Room, BoardMapIconKind.Room,
                BoardMapIconKind.Respawn, BoardMapIconKind.Key,
                BoardMapIconKind.GoldGain, BoardMapIconKind.GoldLoss,
                BoardMapIconKind.Item, BoardMapIconKind.Healing,
                BoardMapIconKind.Damage, BoardMapIconKind.SpecialEvent,
                BoardMapIconKind.Arrow, BoardMapIconKind.Room,
                BoardMapIconKind.Room, BoardMapIconKind.Room
            };
            var colors = new[]
            {
                Color.white, Color.white, Color.white,
                new Color(1f, .82f, .12f), new Color(1f, .82f, .12f),
                new Color(1f, .2f, .18f), Color.white,
                new Color(.35f, 1f, .5f), new Color(1f, .2f, .18f),
                new Color(1f, .2f, .18f), new Color(.35f, .8f, 1f),
                new Color(1f, .2f, .18f), new Color(1f, .82f, .12f),
                new Color(.06f, .55f, 1f)
            };
            var sourceFont = panel.GetComponentInParent<Canvas>()
                .GetComponentInChildren<Text>(true).font;
            for (var index = 0; index < Descriptions.Length; index++)
            {
                var row = new GameObject("Icon Info " + index,
                    typeof(RectTransform));
                row.layer = panel.gameObject.layer;
                row.transform.SetParent(panel, false);
                var rowRect = (RectTransform)row.transform;
                rowRect.anchorMin = rowRect.anchorMax =
                    new Vector2(0f, 1f);
                rowRect.pivot = new Vector2(0f, 1f);
                rowRect.anchoredPosition = new Vector2(6f, -5f - index * 35f);
                rowRect.sizeDelta = new Vector2(190f, 34f);
                var icon = new GameObject("Icon", typeof(RectTransform),
                    typeof(CanvasRenderer));
                icon.layer = row.layer;
                icon.transform.SetParent(row.transform, false);
                var iconRect = (RectTransform)icon.transform;
                iconRect.anchorMin = iconRect.anchorMax =
                    new Vector2(0f, .5f);
                iconRect.pivot = new Vector2(.5f, .5f);
                iconRect.anchoredPosition = new Vector2(15f, 0f);
                iconRect.sizeDelta = new Vector2(22f, 22f);
                if (index >= 12)
                {
                    iconRect.sizeDelta = new Vector2(12f, 12f);
                    var dot = icon.AddComponent<BoardMapCircleGraphic>();
                    dot.color = colors[index];
                    dot.raycastTarget = false;
                }
                else if (index < 2)
                {
                    var image = icon.AddComponent<Image>();
                    image.sprite = sprites[index];
                    image.preserveAspect = true;
                    image.raycastTarget = false;
                    if (index == 0)
                    {
                        var ring = new GameObject("Facing Ring",
                            typeof(RectTransform), typeof(CanvasRenderer),
                            typeof(BoardMapPlayerDirectionGraphic));
                        ring.transform.SetParent(icon.transform, false);
                        var ringRect = (RectTransform)ring.transform;
                        ringRect.anchorMin = ringRect.anchorMax = ringRect.pivot =
                            Vector2.one * .5f;
                        ringRect.sizeDelta = new Vector2(34f, 34f);
                        ring.GetComponent<BoardMapPlayerDirectionGraphic>()
                            .raycastTarget = false;
                    }
                }
                else
                {
                    var graphic = icon.AddComponent<BoardMapIcon>();
                    graphic.SetIcon(kinds[index], sprites[index]);
                    graphic.color = colors[index];
                    graphic.raycastTarget = false;
                }
                var label = new GameObject("Description", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Text), typeof(LocalizedText));
                label.layer = row.layer;
                label.transform.SetParent(row.transform, false);
                var labelRect = (RectTransform)label.transform;
                labelRect.anchorMin = labelRect.anchorMax =
                    new Vector2(0f, .5f);
                labelRect.pivot = new Vector2(0f, .5f);
                labelRect.anchoredPosition = new Vector2(35f, 0f);
                labelRect.sizeDelta = new Vector2(153f, 33f);
                var text = label.GetComponent<Text>();
                text.font = sourceFont;
                text.fontSize = 12;
                text.alignment = TextAnchor.MiddleLeft;
                text.color = Color.white;
                text.raycastTarget = false;
                text.text = Descriptions[index];
                label.GetComponent<LocalizedText>().Configure(Descriptions[index]);
            }
        }

        private static void BindScene(string path, GameObject blue)
        {
            var scene = SceneManager.GetSceneByPath(path);
            var wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded)
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                foreach (var guide in root.GetComponentsInChildren<BoardShopRouteView>(true))
                {
                    var data = new SerializedObject(guide);
                    data.FindProperty("travelMarkerPrefab")
                        .objectReferenceValue = blue;
                    data.ApplyModifiedPropertiesWithoutUndo();
                    EditorSceneManager.MarkSceneDirty(scene);
                }
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (!wasLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
