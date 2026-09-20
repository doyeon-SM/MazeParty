using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class ArenaCombatHitFlashUiTests
    {
        private const string PrefabPath =
            "Assets/MazeParty/UI/Prefabs/ArenaCombatHitFlash.prefab";
        private const string ScenePath =
            "Assets/MazeParty/Scenes/Minigames/ArenaCombat.unity";

        [Test]
        public void HitFlashPrefab_HasSerializedNonBlockingRedOverlayWithoutHealthUi()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);
            var view = prefab.GetComponent<ArenaCombatHitFlashView>();
            var canvas = prefab.GetComponent<Canvas>();
            var image = prefab.GetComponentInChildren<Image>(true);
            var group = prefab.GetComponentInChildren<CanvasGroup>(true);
            Assert.That(view, Is.Not.Null);
            Assert.That(view.HasRequiredReferences, Is.True);
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.renderMode,
                Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(canvas.sortingOrder, Is.GreaterThan(500));
            Assert.That(prefab.GetComponent<GraphicRaycaster>(), Is.Null);
            Assert.That(image, Is.Not.Null);
            Assert.That(image.color.r, Is.GreaterThan(image.color.g));
            Assert.That(image.color.r, Is.GreaterThan(image.color.b));
            Assert.That(image.raycastTarget, Is.False);
            Assert.That(group, Is.Not.Null);
            Assert.That(group.blocksRaycasts, Is.False);
            Assert.That(prefab.GetComponentsInChildren<Text>(true), Is.Empty);

            var instance = Object.Instantiate(prefab);
            try
            {
                var runtimeView = instance.GetComponent<ArenaCombatHitFlashView>();
                runtimeView.Flash();
                Assert.That(runtimeView.CurrentOpacity, Is.GreaterThan(0f));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ArenaCombatScene_HasOneStandaloneHitFlashPrefabInstance()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    ScenePath, OpenSceneMode.Additive);
            }

            try
            {
                var views = scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<ArenaCombatHitFlashView>(
                            true))
                    .ToArray();
                Assert.That(views, Has.Length.EqualTo(1));
                var view = views[0];
                Assert.That(view.transform.parent, Is.Null);
                Assert.That(view.HasRequiredReferences, Is.True);
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
