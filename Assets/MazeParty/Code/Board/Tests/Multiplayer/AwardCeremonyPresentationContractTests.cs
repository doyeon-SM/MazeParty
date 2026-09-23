using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class AwardCeremonyPresentationContractTests
    {
        private const string CanvasPrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/AwardCeremonyCanvas.prefab";
        private const string StagePrefabPath =
            "Assets/MazeParty/Prefabs/Board/World/AwardCeremonyStage.prefab";
        private const string BoardScenePath =
            "Assets/MazeParty/Scenes/Board/Board.unity";

        [Test]
        public void CeremonyPrefabs_HaveCompleteAuthoredContracts()
        {
            var canvasPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                CanvasPrefabPath);
            Assert.That(canvasPrefab, Is.Not.Null);
            var canvasBindings = canvasPrefab.GetComponent<
                AwardCeremonyCanvasBindings>();
            var canvasView = canvasPrefab.GetComponent<AwardCeremonyView>();
            Assert.That(canvasBindings, Is.Not.Null);
            Assert.That(canvasBindings.HasRequiredReferences, Is.True);
            Assert.That(canvasView, Is.Not.Null);
            Assert.That(canvasView.Bindings, Is.SameAs(canvasBindings));
            Assert.That(
                canvasPrefab.GetComponentsInChildren<Canvas>(true),
                Has.Length.EqualTo(1));
            Assert.That(
                canvasPrefab.GetComponentsInChildren<GraphicRaycaster>(true),
                Has.Length.EqualTo(1));
            Assert.That(
                canvasBindings.BonusAwardAnimator.runtimeAnimatorController,
                Is.Not.Null);
            CollectionAssert.IsSubsetOf(
                new[] { "AwardOverlayVisible", "AwardOverlaySlideUp" },
                canvasBindings.BonusAwardAnimator.runtimeAnimatorController
                    .animationClips.Select(clip => clip.name).ToArray());
            Assert.That(canvasBindings.RootCanvas.enabled, Is.False);
            Assert.That(canvasBindings.RootRaycaster.enabled, Is.False);
            Assert.That(
                canvasBindings.FinalRankTexts,
                Has.Length.EqualTo(MultiplayerConstants.MaxPlayers));

            var stagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                StagePrefabPath);
            Assert.That(stagePrefab, Is.Not.Null);
            var stageBindings = stagePrefab.GetComponent<
                AwardCeremonyStageBindings>();
            var stagePresentation = stagePrefab.GetComponent<
                AwardCeremonyPresentation>();
            Assert.That(stageBindings, Is.Not.Null);
            Assert.That(stageBindings.HasRequiredReferences, Is.True);
            Assert.That(stagePresentation, Is.Not.Null);
            Assert.That(stagePresentation.Bindings, Is.SameAs(stageBindings));
            Assert.That(
                stageBindings.PodiumRoots,
                Has.Length.EqualTo(MultiplayerConstants.MaxPlayers));
            Assert.That(
                stageBindings.PlayerAnchors,
                Has.Length.EqualTo(MultiplayerConstants.MaxPlayers));
            Assert.That(
                stageBindings.WinnerSpotlights,
                Has.Length.EqualTo(MultiplayerConstants.MaxPlayers));
            Assert.That(
                stageBindings.WinnerSpotlights.All(light =>
                    light.type == LightType.Spot),
                Is.True);
            Assert.That(stageBindings.PresentationRoot.activeSelf, Is.False);
            Assert.That(
                stagePrefab.GetComponentsInChildren<Camera>(true),
                Is.Empty,
                "The ceremony must reuse the Board output Camera.");
            Assert.That(
                stagePrefab.GetComponentsInChildren<AudioListener>(true),
                Is.Empty);
            var sharedCameras = stagePrefab
                .GetComponentsInChildren<MonoBehaviour>(true)
                .Where(component => component != null &&
                    component.GetType().FullName ==
                    "Unity.Cinemachine.CinemachineCamera")
                .ToArray();
            Assert.That(sharedCameras, Has.Length.EqualTo(1));
            Assert.That(sharedCameras[0].gameObject.activeSelf, Is.False);
        }

        [Test]
        public void BoardScene_UsesExactlyOneInstanceOfEachCeremonyPrefab()
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
                var canvasBindings = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<
                        AwardCeremonyCanvasBindings>(true))
                    .ToArray();
                var stageBindings = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<
                        AwardCeremonyStageBindings>(true))
                    .ToArray();
                Assert.That(canvasBindings, Has.Length.EqualTo(1));
                Assert.That(stageBindings, Has.Length.EqualTo(1));
                Assert.That(canvasBindings[0].HasRequiredReferences, Is.True);
                Assert.That(stageBindings[0].HasRequiredReferences, Is.True);
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        canvasBindings[0].gameObject),
                    Is.EqualTo(CanvasPrefabPath));
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        stageBindings[0].gameObject),
                    Is.EqualTo(StagePrefabPath));
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
