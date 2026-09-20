using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MinigameResultCanvasContractTests
    {
        private const string ResultPrefabPath =
            "Assets/MazeParty/UI/Prefabs/MinigameResultCanvas.prefab";
        private const string BoardPrefabPath =
            "Assets/MazeParty/UI/Prefabs/BoardCanvas.prefab";
        private const string BoardScenePath =
            "Assets/MazeParty/Scenes/Board.unity";
        private const string TestbedScenePath =
            "Assets/MazeParty/Dev/BoardFlowTestbed/BoardFlowTestbed.unity";

        [Test]
        public void FinalResult_HasOneDedicatedAuthoredCanvas()
        {
            var resultPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ResultPrefabPath);
            var boardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                BoardPrefabPath);
            Assert.That(resultPrefab, Is.Not.Null, ResultPrefabPath);
            Assert.That(boardPrefab, Is.Not.Null, BoardPrefabPath);

            var result = resultPrefab.GetComponent<
                MinigameResultCanvasBindings>();
            var board = boardPrefab.GetComponent<BoardCanvasBindings>();
            Assert.That(result, Is.Not.Null);
            Assert.That(result.HasRequiredReferences, Is.True);
            Assert.That(result.RootCanvas,
                Is.SameAs(resultPrefab.GetComponent<Canvas>()));
            Assert.That(result.RootCanvas.renderMode,
                Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(resultPrefab.GetComponentsInChildren<Canvas>(true),
                Has.Length.EqualTo(1));
            Assert.That(resultPrefab.GetComponent<CanvasScaler>(),
                Is.Not.Null);
            Assert.That(result.ResultPanel.activeSelf, Is.False);
            Assert.That(board, Is.Not.Null);
            Assert.That(board.HasRequiredReferences, Is.True);
            Assert.That(result.RootCanvas.sortingOrder,
                Is.GreaterThan(board.RootCanvas.sortingOrder));
            Assert.That(boardPrefab.GetComponentsInChildren<
                    MinigameResultCanvasBindings>(true),
                Is.Empty);
            Assert.That(boardPrefab.GetComponentsInChildren<Transform>(true)
                    .Any(child => child.name == "SkippedResultPanel"),
                Is.False);
        }

        [TestCase(BoardScenePath, false)]
        [TestCase(TestbedScenePath, true)]
        public void Scene_UsesResultPrefabRootAndSerializedPresenterBinding(
            string path,
            bool isTestbed)
        {
            var scene = SceneManager.GetSceneByPath(path);
            var openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere)
            {
                scene = EditorSceneManager.OpenScene(
                    path,
                    OpenSceneMode.Additive);
            }

            try
            {
                var roots = scene.GetRootGameObjects();
                var results = roots.SelectMany(root =>
                        root.GetComponentsInChildren<
                            MinigameResultCanvasBindings>(true))
                    .ToArray();
                Assert.That(results, Has.Length.EqualTo(1));
                var result = results[0];
                Assert.That(result.HasRequiredReferences, Is.True);
                Assert.That(result.transform.parent, Is.Null);
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        result.gameObject),
                    Is.EqualTo(ResultPrefabPath));

                if (isTestbed)
                {
                    var simulators = roots.SelectMany(root =>
                            root.GetComponentsInChildren<MonoBehaviour>(true))
                        .Where(component => component != null &&
                            component.GetType().FullName ==
                            "MazeParty.Gameplay.BoardFlowTestbed." +
                            "BoardFlowLocalSimulator")
                        .ToArray();
                    Assert.That(simulators, Has.Length.EqualTo(1));
                    var serialized = new SerializedObject(simulators[0]);
                    Assert.That(serialized.FindProperty("resultUiBindings")
                            .objectReferenceValue,
                        Is.SameAs(result));
                }
                else
                {
                    var boardViews = roots.SelectMany(root =>
                            root.GetComponentsInChildren<BoardFlowView>(true))
                        .ToArray();
                    Assert.That(boardViews, Has.Length.EqualTo(1));
                    Assert.That(boardViews[0].ResultUiBindings,
                        Is.SameAs(result));
                }
            }
            finally
            {
                if (openedHere)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
