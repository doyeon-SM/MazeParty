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
            var minimap = root.GetComponent<BoardMinimapView>();
            Assert.That(minimap, Is.Not.Null);
            Assert.That(minimap.HasRequiredReferences, Is.True);
            var maps = root.GetComponentsInChildren<BoardMinimapView>(true);
            Assert.That(maps.Length, Is.EqualTo(2));
            foreach (var itemMap in maps)
            {
                var data = new SerializedObject(itemMap);
                var mines = data.FindProperty("mineGraphic").objectReferenceValue as BoardMapMineGraphic;
                var route = data.FindProperty("shopRouteGraphic").objectReferenceValue as BoardMapRouteGraphic;
                Assert.That(mines, Is.Not.Null);
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
                            "Assets/MazeParty/Resources/MazeParty/Expressions/WheelDot.png"));
                    Assert.That(marker.preserveAspect, Is.True);
                    Assert.That(effectLayer.GetSiblingIndex(),
                        Is.GreaterThan(marker.transform.GetSiblingIndex()));
                }
            }
            Assert.That(badges, Is.Not.Null);
            Assert.That(badges.HasRequiredReferences, Is.True);
        }
    }
}
