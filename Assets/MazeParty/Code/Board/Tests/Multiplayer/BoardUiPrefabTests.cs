using System.Collections.Generic;
using MazeParty.Gameplay.Minigames;
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
            "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab";
        private const string BoardScenePath =
            "Assets/MazeParty/Scenes/Board/Board.unity";

        [Test]
        public void ReadyPlayerStates_AreBoundOnBoardCanvasPrefabAndSceneInstance()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);
            var bindings = prefab.GetComponent<BoardCanvasBindings>();
            Assert.That(bindings, Is.Not.Null);
            Assert.That(bindings.HasRequiredReferences, Is.True);
            Assert.That(prefab.GetComponent<BoardUtilityItemView>().HasRequiredReferences, Is.True);
            Assert.That(bindings.MinigameReadyPlayerStates.Length,
                Is.EqualTo(MultiplayerConstants.MaxPlayers));
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                var playerState = bindings.MinigameReadyPlayerStates[slot];
                Assert.That(playerState, Is.Not.Null, "READY slot " + slot);
                Assert.That(playerState.transform.IsChildOf(
                    bindings.MinigameReadyPanel.transform), Is.True);
                Assert.That(playerState.name,
                    Is.EqualTo("MinigameReadyPlayerState" + slot));
            }

            var scene = SceneManager.GetSceneByPath(BoardScenePath);
            var openedForTest = !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    BoardScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                BoardCanvasBindings sceneBindings = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    sceneBindings = root.GetComponentInChildren<BoardCanvasBindings>(true);
                    if (sceneBindings != null)
                    {
                        break;
                    }
                }

                Assert.That(sceneBindings, Is.Not.Null);
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        sceneBindings.gameObject),
                    Is.EqualTo(PrefabPath));
                Assert.That(sceneBindings.HasRequiredReferences, Is.True);
                var utility = sceneBindings.GetComponent<BoardUtilityItemView>();
                Assert.That(utility.HasRequiredReferences, Is.True);
                Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(utility), Is.Not.Null);
            }
            finally
            {
                if (openedForTest)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

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

        [Test]
        public void BoardCanvas_ProvidesDistinctRuleCardsForEveryMinigame()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);

            var bindings = prefab.GetComponent<BoardCanvasBindings>();
            Assert.That(bindings, Is.Not.Null);
            Assert.That(bindings.MinigameRuleImage, Is.Not.Null);
            Assert.That(bindings.MinigameRuleImage.preserveAspect, Is.True);
            Assert.That(bindings.GetMinigameRuleCard(ScheduledMinigameId.Skip),
                Is.Null);

            var uniqueCards = new HashSet<Sprite>();
            foreach (var game in MinigameCatalog.RegisteredMinigames)
            {
                var sprite = bindings.GetMinigameRuleCard(game.Id);
                Assert.That(sprite, Is.Not.Null, game.DisplayName);
                Assert.That(AssetDatabase.GetAssetPath(sprite),
                    Does.StartWith("Assets/MazeParty/Art/Minigames/RuleCards/"),
                    game.DisplayName);
                Assert.That(uniqueCards.Add(sprite), Is.True, game.DisplayName);
            }

            Assert.That(uniqueCards.Count, Is.EqualTo(MinigameCatalog.RegisteredCount));
        }
    }
}
