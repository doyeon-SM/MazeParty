using System.Linq;
using MazeParty.Gameplay;
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
            "Assets/MazeParty/Prefabs/Minigames/ArenaCombat/UI/ArenaCombatHitFlash.prefab";
        private const string ScenePath =
            "Assets/MazeParty/Scenes/Minigames/ArenaCombat/ArenaCombat.unity";

        [Test]
        public void HitFlashPrefab_HasNonBlockingBindingsAndReducedFlashSupport()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);
            var view = prefab.GetComponent<ArenaCombatHitFlashView>();
            Assert.That(view, Is.Not.Null);
            Assert.That(view.HasRequiredReferences, Is.True);
            Assert.That(prefab.GetComponent<GraphicRaycaster>(), Is.Null);
            var graphics = prefab.GetComponentsInChildren<Graphic>(true);
            Assert.That(graphics, Is.Not.Empty);
            Assert.That(
                graphics.All(graphic => !graphic.raycastTarget),
                Is.True);
            Assert.That(
                prefab.GetComponentsInChildren<CanvasGroup>(true)
                    .All(group => !group.blocksRaycasts),
                Is.True);

            var instance = Object.Instantiate(prefab);
            try
            {
                var runtimeView = instance.GetComponent<ArenaCombatHitFlashView>();
                PresentationAccessibility.Apply(false, false);
                runtimeView.Flash();
                var normalOpacity = runtimeView.CurrentOpacity;
                PresentationAccessibility.Apply(false, true);
                runtimeView.Flash();
                Assert.That(normalOpacity, Is.GreaterThan(0f));
                Assert.That(runtimeView.CurrentOpacity,
                    Is.EqualTo(
                        normalOpacity *
                        PresentationAccessibility.ReducedFlashIntensityScale)
                    .Within(0.0001f));
            }
            finally
            {
                PresentationAccessibility.Apply(false, false);
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
