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
            var serializedView = new SerializedObject(view);
            var prompt = serializedView.FindProperty("combatPromptText").objectReferenceValue as Text;
            Assert.That(prompt, Is.Not.Null);
            Assert.That(prompt.GetComponent<LocalizedText>(), Is.Not.Null);
            Assert.That(prompt.raycastTarget, Is.False);
            Assert.That(common, Is.Not.Null);
            Assert.That(common.HasRequiredReferences, Is.True);
            Assert.That(common.RootCanvas, Is.SameAs(canvas));
            Assert.That(common.TimerDial, Is.Not.Null);
            Assert.That(common.RoundText, Is.Not.Null);
            Assert.That(common.TaggerAimRoot, Is.Not.Null);
            Assert.That(common.SpectatorText, Is.Not.Null);
            Assert.That(common.SpectatorText.transform.IsChildOf(
                prefab.transform), Is.True);
            Assert.That(common.SpectatorText.raycastTarget, Is.False);
            Assert.That(common.HasSpectatorControls, Is.True);
            Assert.That(common.SpectatorSwitchIcon.raycastTarget, Is.False);
            Assert.That(common.SpectatorSwitchText.raycastTarget, Is.False);
            var inputSprites = AssetDatabase.LoadAssetAtPath<TMPro.TMP_SpriteAsset>(
                "Assets/Ignore/Input Sprites for TextMesh Pro/all input icons same size.asset");
            Assert.That(common.SpectatorSwitchIcon.texture, Is.SameAs(inputSprites.spriteSheet));
            Assert.That(common.TaggerAimRoot.transform.IsChildOf(
                prefab.transform), Is.True);
            var aimGraphics = common.TaggerAimRoot
                .GetComponentsInChildren<Graphic>(true);
            Assert.That(aimGraphics, Is.Not.Empty);
            var aimImage = common.TaggerAimRoot.GetComponent<Image>();
            Assert.That(aimImage, Is.Not.Null);
            Assert.That(aimImage.sprite, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(aimImage.sprite), Is.EqualTo(
                "Assets/Ignore/Modern UI Pack/Textures/Icon/Navigation/Add.png"));
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
                Assert.That(runtimeCommon.SpectatorText.gameObject.activeSelf,
                    Is.False);
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
