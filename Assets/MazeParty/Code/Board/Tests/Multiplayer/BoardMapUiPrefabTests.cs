using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardMapUiPrefabTests
    {
        private const string PrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab";
        private const string ScenePath =
            "Assets/MazeParty/Scenes/Board/Board.unity";
        private const string ModernIconRoot =
            "Assets/Ignore/Modern UI Pack/Textures/Icon/";

        [Test]
        public void BoardMapIconKinds_KeepSerializedIdsWhenNewKindsAreAppended()
        {
            Assert.That(
                new[]
                {
                    (int)BoardMapIconKind.Room,
                    (int)BoardMapIconKind.Start,
                    (int)BoardMapIconKind.Respawn,
                    (int)BoardMapIconKind.Key,
                    (int)BoardMapIconKind.GoldGain,
                    (int)BoardMapIconKind.GoldLoss,
                    (int)BoardMapIconKind.Item,
                    (int)BoardMapIconKind.Healing,
                    (int)BoardMapIconKind.Arrow,
                    (int)BoardMapIconKind.Damage,
                    (int)BoardMapIconKind.SpecialEvent
                },
                Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }));
        }

        [Test]
        public void BoardMapAndStatusBadges_AreBoundOnPrefabAndBoardSceneInstance()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            AssertBound(prefab);

            var scene = SceneManager.GetSceneByPath(ScenePath);
            var wasAlreadyLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasAlreadyLoaded)
            {
                scene = EditorSceneManager.OpenScene(ScenePath,
                    OpenSceneMode.Additive);
            }
            try
            {
                BoardMapView sceneMap = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var map = root.GetComponentInChildren<BoardMapView>(true);
                    if (map == null) continue;
                    Assert.That(sceneMap, Is.Null,
                        "Board scene should contain exactly one board map.");
                    sceneMap = map;
                }
                Assert.That(sceneMap, Is.Not.Null);
                AssertBound(sceneMap.gameObject);
                var source = PrefabUtility.GetCorrespondingObjectFromSource(
                    sceneMap.gameObject);
                Assert.That(AssetDatabase.GetAssetPath(source),
                    Is.EqualTo(PrefabPath));
            }
            finally
            {
                if (!wasAlreadyLoaded && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void AssertBound(GameObject root)
        {
            var map = root.GetComponent<BoardMapView>();
            var badges = root.GetComponent<BoardPlayerStatusBadges>();
            Assert.That(map, Is.Not.Null);
            Assert.That(map.HasRequiredReferences, Is.True);
            var mapData = new SerializedObject(map);
            var fullMapPanel = mapData.FindProperty("fullMapPanel")
                .objectReferenceValue as GameObject;
            var fullMapCloseButton = mapData.FindProperty("fullMapCloseButton")
                .objectReferenceValue as UnityEngine.UI.Button;
            Assert.That(fullMapPanel, Is.Not.Null);
            Assert.That(fullMapCloseButton, Is.Not.Null);
            Assert.That(fullMapCloseButton.transform.IsChildOf(
                fullMapPanel.transform), Is.True,
                "The full-map close control must be authored inside the popup prefab.");
            var minimap = root.GetComponent<BoardMinimapView>();
            Assert.That(minimap, Is.Not.Null);
            Assert.That(minimap.HasRequiredReferences, Is.True);
            var maps = root.GetComponentsInChildren<BoardMinimapView>(true);
            Assert.That(maps.Length, Is.EqualTo(2));
            foreach (var itemMap in maps)
            {
                var data = new SerializedObject(itemMap);
                var isFullMap = Mathf.Approximately(
                    data.FindProperty("radiusInTiles").floatValue,
                    0f);
                Assert.That(data.FindProperty("currentTile").objectReferenceValue,
                    Is.Null);
                Assert.That(data.FindProperty("shopDistanceIcon").objectReferenceValue,
                    Is.Null);
                Assert.That(data.FindProperty("shopDistanceText").objectReferenceValue,
                    Is.Null);
                Assert.That(data.FindProperty("showKeyShopDetails").boolValue,
                    Is.False,
                    "Deleted map copy and key-shop details must remain optional.");
                AssertSpritePath(data, "homeFilledIcon",
                    ModernIconRoot + "Navigation/Home Filled.png");
                AssertSpritePath(data, "moneyFilledIcon",
                    ModernIconRoot + "Business & Commerce/Money Filled.png");
                AssertSpritePath(data, "damageIcon",
                    ModernIconRoot + "Navigation/Add.png", "Add");
                AssertSpritePath(data, "warningFilledIcon",
                    ModernIconRoot + "Navigation/Warning Filled.png");
                AssertSpritePath(data, "arrowUpIcon",
                    ModernIconRoot + "Navigation/Arrow Up.png");
                AssertSpritePath(data, "locationMarkFilledIcon",
                    ModernIconRoot + "Map/Location Mark Filled.png");
                AssertSpritePath(data, "helpFilledIcon",
                    ModernIconRoot + "Navigation/Help Filled.png");
                if (isFullMap)
                {
                    Assert.That(data.FindProperty("heading").objectReferenceValue,
                        Is.Null);
                    Assert.That(data.FindProperty("showTravelCounts").boolValue,
                        Is.True);
                    var rooms = data.FindProperty("rooms");
                    for (var roomIndex = 0;
                         roomIndex < rooms.arraySize;
                         roomIndex++)
                    {
                        var symbol = rooms.GetArrayElementAtIndex(roomIndex)
                            .FindPropertyRelative("Symbol")
                            .objectReferenceValue as UnityEngine.UI.Text;
                        Assert.That(symbol, Is.Not.Null);
                        Assert.That(symbol.alignment,
                            Is.EqualTo(TextAnchor.UpperLeft));
                    }
                }
                else
                {
                    Assert.That(data.FindProperty("heading").objectReferenceValue,
                        Is.Not.Null);
                    Assert.That(data.FindProperty("showTravelCounts").boolValue,
                        Is.False,
                        "The live minimap must not receive route-count labels.");
                }
                var mines = data.FindProperty("mineGraphic").objectReferenceValue as BoardMapMineGraphic;
                var route = data.FindProperty("shopRouteGraphic").objectReferenceValue as BoardMapRouteGraphic;
                Assert.That(mines, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(mines.IconSprite),
                    Is.EqualTo(
                        ModernIconRoot + "Navigation/Help Filled.png"));
                Assert.That(mines.color,
                    Is.EqualTo(new Color(1f, .2f, .18f, 1f)));
                Assert.That(mines.transform.parent,
                    Is.EqualTo(route.transform.parent));

                var effectLayer = data.FindProperty("landingEffectLayer")
                    .objectReferenceValue as RectTransform;
                Assert.That(effectLayer, Is.Not.Null);
                Assert.That(effectLayer.parent,
                    Is.EqualTo(route.transform.parent));
                var players = data.FindProperty("players");
                Assert.That(players.arraySize,
                    Is.EqualTo(MultiplayerConstants.MaxPlayers));
                for (var slot = 0; slot < players.arraySize; slot++)
                {
                    var marker = players.GetArrayElementAtIndex(slot)
                        .objectReferenceValue as UnityEngine.UI.Image;
                    Assert.That(marker, Is.Not.Null);
                    Assert.That(
                        AssetDatabase.GetAssetPath(marker.sprite),
                        Is.EqualTo(
                            ModernIconRoot +
                            "Map/Location Mark Filled.png"));
                    Assert.That(marker.preserveAspect, Is.True);
                    Assert.That(marker.transform.GetSiblingIndex(),
                        Is.GreaterThan(effectLayer.GetSiblingIndex()),
                        "Player markers must render above every tile icon layer.");
                }

                var shopColor = data.FindProperty("shopColor").colorValue;
                Assert.That(shopColor.r, Is.EqualTo(1f).Within(0.001f));
                Assert.That(shopColor.g, Is.EqualTo(0.82f).Within(0.001f));
                Assert.That(shopColor.b, Is.EqualTo(0.12f).Within(0.001f));

                var typeColors = data.FindProperty("typeIconColors");
                Assert.That(typeColors.GetArrayElementAtIndex(0).colorValue,
                    Is.EqualTo(typeColors.GetArrayElementAtIndex(1).colorValue),
                    "Start tiles use the normal-tile design.");
                var effectColors = data.FindProperty("effectIconColors");
                Assert.That(effectColors.GetArrayElementAtIndex(3).colorValue,
                    Is.EqualTo(Color.white), "Item reward stays white.");
                Assert.That(effectColors.GetArrayElementAtIndex(4).colorValue,
                    Is.EqualTo(effectColors.GetArrayElementAtIndex(5).colorValue),
                    "Both healing strengths use green.");
                Assert.That(effectColors.GetArrayElementAtIndex(6).colorValue,
                    Is.EqualTo(effectColors.GetArrayElementAtIndex(7).colorValue),
                    "Both damage strengths use red.");
                Assert.That(effectColors.GetArrayElementAtIndex(7).colorValue,
                    Is.EqualTo(effectColors.GetArrayElementAtIndex(8).colorValue),
                    "Damage and special events use the requested red.");
            }
            Assert.That(badges, Is.Not.Null);
            Assert.That(badges.HasRequiredReferences, Is.True);
        }

        private static void AssertSpritePath(
            SerializedObject data,
            string propertyName,
            string expectedPath,
            string expectedName = null)
        {
            var sprite = data.FindProperty(propertyName)
                .objectReferenceValue as Sprite;
            Assert.That(sprite, Is.Not.Null, propertyName);
            Assert.That(AssetDatabase.GetAssetPath(sprite),
                Is.EqualTo(expectedPath), propertyName);
            if (!string.IsNullOrEmpty(expectedName))
                Assert.That(sprite.name, Is.EqualTo(expectedName), propertyName);
        }
    }
}
