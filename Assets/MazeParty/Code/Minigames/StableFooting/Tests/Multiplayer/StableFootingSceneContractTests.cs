using System.Linq;
using System.Reflection;
using MazeParty.Gameplay.Minigames.StableFooting;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class StableFootingSceneContractTests
    {
        private const string ScenePath =
            "Assets/MazeParty/Scenes/Minigames/StableFooting/StableFooting.unity";
        private const string StableFootingPrefabFolder =
            "Assets/MazeParty/Prefabs/Minigames/StableFooting/";
        private const string HeartSpritePath =
            "Assets/Ignore/Modern UI Pack/Textures/Icon/Common/Heart Filled.png";
        private const string StarSpritePath =
            "Assets/Ignore/Modern UI Pack/Textures/Icon/Common/Star Filled.png";
        private const string SunSpritePath =
            "Assets/Ignore/Modern UI Pack/Textures/Icon/Weather/Sun Filled.png";

        [Test]
        public void PushPresentationRpc_IsReliablePerEvent()
        {
            var rpc = typeof(NetworkStableFootingState).GetMethod(
                "PlayPushPresentationRpc",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(rpc, Is.Not.Null);
            Assert.That(rpc.GetCustomAttribute<RpcAttribute>()?.Delivery,
                Is.EqualTo(RpcDelivery.Reliable));
            var parameters = rpc.GetParameters();
            Assert.That(parameters, Has.Length.EqualTo(3));
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(byte)));
            Assert.That(parameters[1].ParameterType, Is.EqualTo(typeof(byte)));
            Assert.That(parameters[2].ParameterType,
                Is.EqualTo(typeof(Vector3)));
        }

        [Test]
        public void SymbolPrefabs_UseWhiteModernUiSprites()
        {
            AssertSymbolPrefab(
                StableFootingPrefabFolder + "Tile.prefab");
            AssertSymbolPrefab(
                StableFootingPrefabFolder + "SafeSymbolDisplay.prefab");
        }

        [TestCase(StableFootingSymbol.Cross)]
        [TestCase(StableFootingSymbol.Circle)]
        [TestCase(StableFootingSymbol.Square)]
        public void SafeSymbolDisplay_ShowsCorrectSymbolGreenAtCenter(
            StableFootingSymbol safeSymbol)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                StableFootingPrefabFolder + "SafeSymbolDisplay.prefab");
            var instance = Object.Instantiate(prefab);
            try
            {
                var renderers = new[]
                {
                    FindDescendant(instance.transform, "Cross Mark")
                        .GetComponent<SpriteRenderer>(),
                    FindDescendant(instance.transform, "Circle Mark")
                        .GetComponent<SpriteRenderer>(),
                    FindDescendant(instance.transform, "Square Mark")
                        .GetComponent<SpriteRenderer>()
                };
                var authoredSlots = renderers
                    .Select(renderer => renderer.transform.localPosition)
                    .OrderBy(position => position.x)
                    .ToArray();
                var presenter = new StableFootingSafeSymbolPresenter(
                    renderers[0],
                    renderers[1],
                    renderers[2]);

                presenter.Apply(safeSymbol, true);

                var safeIndex = (int)safeSymbol;
                Assert.That(
                    renderers[safeIndex].transform.localPosition,
                    Is.EqualTo(authoredSlots[1]));
                Assert.That(
                    renderers[safeIndex].color,
                    Is.EqualTo(Color.green));
                Assert.That(
                    renderers.All(renderer =>
                        renderer.gameObject.activeInHierarchy),
                    Is.True);

                var distractors = renderers
                    .Where((renderer, index) => index != safeIndex)
                    .ToArray();
                Assert.That(
                    distractors.Select(renderer => renderer.color),
                    Is.All.EqualTo(Color.white));
                CollectionAssert.AreEquivalent(
                    new[] { authoredSlots[0], authoredSlots[2] },
                    distractors.Select(renderer =>
                        renderer.transform.localPosition).ToArray());
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void Scene_PreservesArenaNetworkSharedCameraAndWorldCueContract()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    ScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                var roots = scene.GetRootGameObjects();
                var state = roots
                    .SelectMany(root => root.GetComponentsInChildren<
                        NetworkStableFootingState>(true))
                    .SingleOrDefault();
                Assert.That(state, Is.Not.Null);

                var view = state.GetComponent<StableFootingNetworkView>();
                Assert.That(view, Is.Not.Null);
                var serializedView = new SerializedObject(view);
                var requiredReferences = new[]
                {
                    "state",
                    "sharedCamera",
                    "runnerRoot",
                    "tileRoot",
                    "arenaPresentation",
                    "safeSymbolCrossRenderer",
                    "safeSymbolCircleRenderer",
                    "safeSymbolSquareRenderer",
                    "cueAudioSource"
                };
                foreach (var propertyName in requiredReferences)
                {
                    var property = serializedView.FindProperty(propertyName);
                    Assert.That(property, Is.Not.Null, propertyName);
                    Assert.That(
                        property.objectReferenceValue,
                        Is.Not.Null,
                        propertyName);
                }
                var safeSymbolReferences = new[]
                {
                    "safeSymbolCrossRenderer",
                    "safeSymbolCircleRenderer",
                    "safeSymbolSquareRenderer"
                };
                foreach (var propertyName in safeSymbolReferences)
                {
                    Assert.That(
                        serializedView.FindProperty(propertyName)
                            .objectReferenceValue,
                        Is.InstanceOf<SpriteRenderer>(),
                        propertyName);
                }
                Assert.That(
                    serializedView.FindProperty("hud"),
                    Is.Null,
                    "Stable Footing must not retain a dedicated HUD field.");
                Assert.That(
                    roots.SelectMany(root =>
                        root.GetComponentsInChildren<Canvas>(true)),
                    Is.Empty,
                    "Stable Footing must not add a dedicated screen Canvas.");

                var networkObject = state.GetComponent<NetworkObject>();
                Assert.That(networkObject, Is.Not.Null);
                Assert.That(networkObject.InScenePlaced, Is.True);
                Assert.That(networkObject.PrefabIdHash, Is.Not.EqualTo(0u));

                var cameras = state.GetComponentsInChildren<Component>(true)
                    .Where(component =>
                        component != null &&
                        component.GetType().FullName ==
                        "Unity.Cinemachine.CinemachineCamera")
                    .ToArray();
                Assert.That(cameras, Has.Length.EqualTo(1));
                Assert.That(cameras[0].name, Is.EqualTo(
                    "CM_StableFootingShared"));
                Assert.That(
                    roots.SelectMany(root =>
                        root.GetComponentsInChildren<Camera>(true)),
                    Is.Empty,
                    "The additive scene must reuse Board's output Camera.");
                Assert.That(
                    roots.SelectMany(root =>
                        root.GetComponentsInChildren<AudioListener>(true)),
                    Is.Empty);

                var tileRoot = FindDescendant(
                    state.transform,
                    "Tile Anchors");
                Assert.That(tileRoot, Is.Not.Null);
                Assert.That(
                    tileRoot.childCount,
                    Is.EqualTo(StableFootingRules.TileCount));
                var sampleTile = tileRoot.Find("Tile Anchor 00");
                Assert.That(sampleTile, Is.Not.Null);
                Assert.That(sampleTile.Find("Tile Surface"), Is.Not.Null);
                Assert.That(sampleTile.Find("Cross Mark"), Is.Not.Null);
                Assert.That(sampleTile.Find("Circle Mark"), Is.Not.Null);
                Assert.That(sampleTile.Find("Square Mark"), Is.Not.Null);

                var playerAnchors = FindDescendant(
                    state.transform,
                    "Player Anchors");
                Assert.That(playerAnchors, Is.Not.Null);
                Assert.That(
                    playerAnchors.childCount,
                    Is.EqualTo(StableFootingRules.PlayerCount));
                for (var slot = 0;
                     slot < StableFootingRules.PlayerCount;
                     slot++)
                {
                    var anchor = playerAnchors.Find(
                        "Player Anchor " + (slot + 1));
                    Assert.That(anchor, Is.Not.Null, slot.ToString());
                    var expected = NetworkStableFootingState.GetTileCenter(
                        NetworkStableFootingState.GetStartTileIndex(slot));
                    Assert.That(anchor.position.x,
                        Is.EqualTo(expected.x).Within(0.001f));
                    Assert.That(anchor.position.y,
                        Is.EqualTo(
                            StableFootingNetworkView
                                .RunnerPresentationHeight)
                            .Within(0.001f));
                    Assert.That(anchor.position.z,
                        Is.EqualTo(expected.z).Within(0.001f));
                }

                var safeDisplay = FindDescendant(
                    state.transform,
                    "Safe Symbol Display");
                Assert.That(safeDisplay, Is.Not.Null);
                Assert.That(safeDisplay.Find("Cross Mark"), Is.Not.Null);
                Assert.That(safeDisplay.Find("Circle Mark"), Is.Not.Null);
                Assert.That(safeDisplay.Find("Square Mark"), Is.Not.Null);

                const int sweepRow = 3;
                const int startColumn = 1;
                const int missingColumn = 2;
                var sweepStart = NetworkStableFootingState.GetTileCenter(
                    sweepRow * StableFootingRules.BoardWidth + startColumn) +
                    Vector3.right * 0.2f;
                var sweepEnd = sweepStart + Vector3.right *
                    (NetworkStableFootingState.TileSize *
                     StableFootingRules.PushDistanceInTiles);
                var missingTile = sweepRow *
                    StableFootingRules.BoardWidth + missingColumn;
                var fullMask =
                    (1UL << StableFootingRules.TileCount) - 1UL;
                Assert.That(
                    NetworkStableFootingState.TryFindFirstUnsupportedPoint(
                        sweepStart,
                        sweepEnd,
                        fullMask & ~(1UL << missingTile),
                        out var firstGapPoint),
                    Is.True,
                    "A push must not teleport across a removed platform.");
                Assert.That(
                    NetworkStableFootingState.TryGetTileIndex(
                        firstGapPoint,
                        out var firstGapTile),
                    Is.True);
                Assert.That(firstGapTile, Is.EqualTo(missingTile));
            }
            finally
            {
                if (openedForTest && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void AssertSymbolPrefab(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, prefabPath);

            AssertSymbol(
                prefab.transform,
                "Cross Mark",
                HeartSpritePath,
                prefabPath);
            AssertSymbol(
                prefab.transform,
                "Circle Mark",
                StarSpritePath,
                prefabPath);
            AssertSymbol(
                prefab.transform,
                "Square Mark",
                SunSpritePath,
                prefabPath);
        }

        private static void AssertSymbol(
            Transform prefabRoot,
            string symbolName,
            string spritePath,
            string prefabPath)
        {
            var symbol = FindDescendant(prefabRoot, symbolName);
            Assert.That(symbol, Is.Not.Null,
                prefabPath + " / " + symbolName);

            var renderer = symbol.GetComponent<SpriteRenderer>();
            Assert.That(renderer, Is.Not.Null,
                prefabPath + " / " + symbolName);

            var expectedSprite =
                AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            Assert.That(expectedSprite, Is.Not.Null, spritePath);
            Assert.That(renderer.sprite, Is.EqualTo(expectedSprite),
                prefabPath + " / " + symbolName);
            Assert.That(renderer.color, Is.EqualTo(Color.white),
                prefabPath + " / " + symbolName);
        }

        private static Transform FindDescendant(
            Transform root,
            string childName)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == childName)
            {
                return root;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var result = FindDescendant(
                    root.GetChild(index),
                    childName);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }
    }
}
