using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardHitFlashUiTests
    {
        private const string PrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardHitFlash.prefab";
        private const string ScenePath =
            "Assets/MazeParty/Scenes/Board/Board.unity";
        [Test]
        public void BoardHitFlashPrefab_HasRequiredNonBlockingBindings()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);

            var view = prefab.GetComponent<ArenaCombatHitFlashView>();
            Assert.That(view, Is.Not.Null);
            Assert.That(view.HasRequiredReferences, Is.True);
            Assert.That(prefab.GetComponent<Canvas>(), Is.Not.Null);
            Assert.That(
                prefab.GetComponentsInChildren<GraphicRaycaster>(true),
                Is.Empty);

            var graphics = prefab.GetComponentsInChildren<Graphic>(true);
            Assert.That(graphics, Is.Not.Empty);
            Assert.That(
                graphics.All(graphic => !graphic.raycastTarget),
                Is.True,
                "The board hit flash must never consume pointer input.");
            Assert.That(
                prefab.GetComponentsInChildren<CanvasGroup>(true)
                    .All(group => !group.blocksRaycasts),
                Is.True,
                "The board hit flash CanvasGroups must not block pointer input.");
        }

        [Test]
        public void BoardScene_HasOneStandaloneHitFlashPrefabInstance()
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
                var views = scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<ArenaCombatHitFlashView>(true))
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
                if (openedForTest && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

    }
}
