using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardUiPrefabTests
    {
        private const string PrefabPath =
            "Assets/MazeParty/UI/Prefabs/BoardCanvas.prefab";
        private const string BoardScenePath =
            "Assets/MazeParty/Scenes/Board.unity";
        private const string TestbedScenePath =
            "Assets/MazeParty/Dev/BoardFlowTestbed/BoardFlowTestbed.unity";

        [Test]
        public void BoardCanvasPrefab_HasRuntimeDesignContract()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<Canvas>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<CanvasScaler>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<GraphicRaycaster>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<BoardEventSystemBootstrap>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<BoardFlowView>(), Is.Not.Null);

            var anchorNames = prefab.GetComponentsInChildren<Transform>(true)
                .Select(transform => transform.name)
                .ToArray();
            Assert.That(anchorNames, Does.Contain("ItemSelectionPanel"));
            Assert.That(anchorNames, Does.Contain("ItemShopPanel"));
            Assert.That(anchorNames, Does.Contain("ReconnectOverlay"));
            Assert.That(anchorNames, Does.Contain("PlayerRank0"));
            Assert.That(anchorNames, Does.Contain("ItemShopOffer4"));
        }

        [Test]
        public void BoardCanvas_CanHideAllBoardUiDuringMinefieldGameplay()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var instance = Object.Instantiate(prefab);
            try
            {
                var view = instance.GetComponent<BoardFlowView>();
                var canvas = instance.GetComponent<Canvas>();
                var raycaster = instance.GetComponent<GraphicRaycaster>();

                view.SetBoardUiVisible(false);
                Assert.That(view.BoardUiVisible, Is.False);
                Assert.That(canvas.enabled, Is.False);
                Assert.That(raycaster.enabled, Is.False);

                view.SetBoardUiVisible(true);
                Assert.That(view.BoardUiVisible, Is.True);
                Assert.That(canvas.enabled, Is.True);
                Assert.That(raycaster.enabled, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [TestCase(BoardScenePath)]
        [TestCase(TestbedScenePath)]
        public void GeneratedScene_UsesSharedBoardCanvasPrefab(string scenePath)
        {
            var scene = SceneManager.GetSceneByPath(scenePath);
            var wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded)
            {
                scene = EditorSceneManager.OpenScene(
                    scenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                var canvas = scene.GetRootGameObjects()
                    .FirstOrDefault(root => root.name == "Board Canvas");

                Assert.That(canvas, Is.Not.Null);
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(canvas),
                    Is.EqualTo(PrefabPath));
            }
            finally
            {
                if (!wasLoaded && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
