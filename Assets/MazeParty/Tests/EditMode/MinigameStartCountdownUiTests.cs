using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MinigameStartCountdownUiTests
    {
        private const string PrefabPath =
            "Assets/MazeParty/UI/Prefabs/MinigameStartCountdown.prefab";
        private const string BoardScenePath =
            "Assets/MazeParty/Scenes/Board.unity";

        [Test]
        public void CountdownPrefab_UsesSerializedNonBlockingOverlayBindings()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);
            var view = prefab.GetComponent<MinigameStartCountdownView>();
            var canvas = prefab.GetComponent<Canvas>();
            var numeral = prefab.GetComponentInChildren<Text>(true);
            Assert.That(view, Is.Not.Null);
            Assert.That(view.HasRequiredReferences, Is.True);
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(canvas.sortingOrder, Is.GreaterThanOrEqualTo(500));
            Assert.That(prefab.GetComponent<GraphicRaycaster>(), Is.Null);
            Assert.That(numeral, Is.Not.Null);
            Assert.That(numeral.raycastTarget, Is.False);

            var instance = Object.Instantiate(prefab);
            try
            {
                var runtimeView = instance.GetComponent<MinigameStartCountdownView>();
                var runtimeNumeral = instance.GetComponentInChildren<Text>(true);
                runtimeView.SetCountdown(3, true);
                Assert.That(runtimeView.IsVisible, Is.True);
                Assert.That(runtimeNumeral.text, Is.EqualTo("3"));
                runtimeView.SetCountdown(2, true);
                Assert.That(runtimeNumeral.text, Is.EqualTo("2"));
                runtimeView.SetCountdown(1, true);
                Assert.That(runtimeNumeral.text, Is.EqualTo("1"));
                runtimeView.SetCountdown(0, false);
                Assert.That(runtimeView.IsVisible, Is.False);
                Assert.That(runtimeNumeral.transform.parent.gameObject.activeSelf,
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void BoardScene_HasOneStandaloneCountdownPrefabWithMatchBinding()
        {
            var scene = SceneManager.GetSceneByPath(BoardScenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    BoardScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                var views = scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<MinigameStartCountdownView>(
                            true))
                    .ToArray();
                Assert.That(views, Has.Length.EqualTo(1));
                var view = views[0];
                Assert.That(view.transform.parent, Is.Null);
                Assert.That(view.HasRequiredReferences, Is.True);
                Assert.That(view.MatchState, Is.Not.Null);
                Assert.That(view.MatchState.gameObject.scene, Is.EqualTo(scene));
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        view.gameObject),
                    Is.EqualTo(PrefabPath));
            }
            finally
            {
                if (openedForTest)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
