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
            "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigameCommonHud.prefab";
        private const string BoardScenePath =
            "Assets/MazeParty/Scenes/Board/Board.unity";

        [Test]
        public void CountdownPrefab_UsesSerializedNonBlockingOverlayBindings()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);
            var view = prefab.GetComponent<MinigameStartCountdownView>();
            var common = prefab.GetComponent<MinigameCommonHudView>();
            var canvas = prefab.GetComponent<Canvas>();
            var numeral = prefab.GetComponentsInChildren<Text>(true)
                .Single(text => text.gameObject.name == "Numeral");
            Assert.That(view, Is.Not.Null);
            Assert.That(view.HasRequiredReferences, Is.True);
            Assert.That(common, Is.Not.Null);
            Assert.That(common.HasRequiredReferences, Is.True);
            Assert.That(common.RootCanvas, Is.SameAs(canvas));
            Assert.That(common.TimerDial, Is.Not.Null);
            Assert.That(common.RoundText, Is.Not.Null);
            Assert.That(common.TaggerAimRoot, Is.Not.Null);
            Assert.That(common.TaggerAimRoot.transform.IsChildOf(
                prefab.transform), Is.True);
            var aimGraphics = common.TaggerAimRoot
                .GetComponentsInChildren<Graphic>(true);
            Assert.That(aimGraphics, Is.Not.Empty);
            Assert.That(aimGraphics.All(graphic => !graphic.raycastTarget),
                Is.True);
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
                var runtimeCommon = instance.GetComponent<
                    MinigameCommonHudView>();
                var runtimeNumeral = instance.GetComponentsInChildren<Text>(true)
                    .Single(text => text.gameObject.name == "Numeral");
                Assert.That(runtimeCommon.TaggerAimRoot.activeSelf, Is.False);
                runtimeCommon.SetTaggerAimVisible(true);
                Assert.That(runtimeCommon.TaggerAimRoot.activeSelf, Is.True);
                runtimeCommon.SetTaggerAimVisible(false);
                Assert.That(runtimeCommon.TaggerAimRoot.activeSelf, Is.False);
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
                Assert.That(
                    view.transform.localScale,
                    Is.EqualTo(Vector3.one),
                    "The standalone HUD must not retain a zero-scale scene override.");
                Assert.That(view.HasRequiredReferences, Is.True);
                Assert.That(view.GetComponent<MinigameCommonHudView>()
                    .HasRequiredReferences, Is.True);
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
