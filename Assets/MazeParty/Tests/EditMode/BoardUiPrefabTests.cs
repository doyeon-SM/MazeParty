using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardUiPrefabTests
    {
        private const string PrefabPath =
            "Assets/MazeParty/UI/Prefabs/BoardCanvas.prefab";

        [Test]
        public void BoardCanvas_CanHideAllBoardUiDuringMinigameGameplay()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);

            var instance = Object.Instantiate(prefab);
            try
            {
                var view = instance.GetComponent<BoardFlowView>();
                var canvas = instance.GetComponent<Canvas>();
                var raycaster = instance.GetComponent<GraphicRaycaster>();
                Assert.That(view, Is.Not.Null);
                Assert.That(canvas, Is.Not.Null);
                Assert.That(raycaster, Is.Not.Null);

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
    }
}
