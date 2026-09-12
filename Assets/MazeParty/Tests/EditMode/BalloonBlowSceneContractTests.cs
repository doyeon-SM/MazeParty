using System.Linq;
using MazeParty.Gameplay.Minigames.BalloonBlow;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BalloonBlowSceneContractTests
    {
        private const string ScenePath =
            "Assets/MazeParty/Scenes/BalloonBlow.unity";
        private const string HudPrefabPath =
            "Assets/MazeParty/UI/Prefabs/BalloonBlowHud.prefab";
        private const string LabelPrefabPath =
            "Assets/MazeParty/UI/Prefabs/BalloonBlowStationLabel.prefab";

        [Test]
        public void Scene_PreservesFixedStationsSharedCameraAndPrefabUiContract()
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
                var roots = scene.GetRootGameObjects();
                var state = roots
                    .SelectMany(root => root.GetComponentsInChildren<
                        NetworkBalloonBlowState>(true))
                    .SingleOrDefault();
                Assert.That(state, Is.Not.Null);

                var networkObject = state.GetComponent<NetworkObject>();
                Assert.That(networkObject, Is.Not.Null);
                Assert.That(networkObject.InScenePlaced, Is.True);
                Assert.That(networkObject.PrefabIdHash, Is.Not.EqualTo(0u));

                var view = state.GetComponent<BalloonBlowNetworkView>();
                Assert.That(view, Is.Not.Null);
                var serializedView = new SerializedObject(view);
                foreach (var propertyName in new[]
                         {
                             "state",
                             "sharedCamera",
                             "playerRoot",
                             "arenaPresentation",
                             "cueAudioSource",
                             "hud"
                         })
                {
                    var property = serializedView.FindProperty(propertyName);
                    Assert.That(property, Is.Not.Null, propertyName);
                    Assert.That(
                        property.objectReferenceValue,
                        Is.Not.Null,
                        propertyName);
                }
                foreach (var propertyName in new[]
                         {
                             "playerAnchors",
                             "balloonAnchors",
                             "stationLabels"
                         })
                {
                    var property = serializedView.FindProperty(propertyName);
                    Assert.That(property, Is.Not.Null, propertyName);
                    Assert.That(
                        property.arraySize,
                        Is.EqualTo(BalloonBlowRules.PlayerCount),
                        propertyName);
                    for (var index = 0; index < property.arraySize; index++)
                    {
                        Assert.That(
                            property.GetArrayElementAtIndex(index)
                                .objectReferenceValue,
                            Is.Not.Null,
                            propertyName + "[" + index + "]");
                    }
                }

                var playerAnchors = FindDescendant(
                    state.transform,
                    "Player Anchors");
                var balloonAnchors = FindDescendant(
                    state.transform,
                    "Balloon Anchors");
                Assert.That(playerAnchors, Is.Not.Null);
                Assert.That(balloonAnchors, Is.Not.Null);
                Assert.That(
                    playerAnchors.childCount,
                    Is.EqualTo(BalloonBlowRules.PlayerCount));
                Assert.That(
                    balloonAnchors.childCount,
                    Is.EqualTo(BalloonBlowRules.PlayerCount));
                for (var slot = 0;
                     slot < BalloonBlowRules.PlayerCount;
                     slot++)
                {
                    var balloon = balloonAnchors.Find(
                        "Balloon Anchor " + (slot + 1));
                    Assert.That(balloon, Is.Not.Null);
                    Assert.That(balloon.Find("Balloon Body"), Is.Not.Null);
                    Assert.That(balloon.Find("Balloon Knot"), Is.Not.Null);
                }

                var labels = state.GetComponentsInChildren<
                    BalloonBlowStationLabel>(true);
                Assert.That(
                    labels,
                    Has.Length.EqualTo(BalloonBlowRules.PlayerCount));
                foreach (var label in labels)
                {
                    Assert.That(label.HasRequiredReferences, Is.True);
                    Assert.That(
                        PrefabUtility
                            .GetPrefabAssetPathOfNearestInstanceRoot(
                                label.gameObject),
                        Is.EqualTo(LabelPrefabPath));
                }

                var hud = state.GetComponentInChildren<
                    BalloonBlowHudBindings>(true);
                Assert.That(hud, Is.Not.Null);
                Assert.That(hud.HasRequiredReferences, Is.True);
                Assert.That(
                    hud.transform.localScale,
                    Is.EqualTo(Vector3.one),
                    "The prefab-authored HUD root must remain renderable.");
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        hud.gameObject),
                    Is.EqualTo(HudPrefabPath));

                var cameras = state.GetComponentsInChildren<Component>(true)
                    .Where(component =>
                        component != null &&
                        component.GetType().FullName ==
                        "Unity.Cinemachine.CinemachineCamera")
                    .ToArray();
                Assert.That(cameras, Has.Length.EqualTo(1));
                Assert.That(cameras[0].name, Is.EqualTo(
                    "CM_BalloonBlowShared"));
                Assert.That(
                    roots.SelectMany(root =>
                        root.GetComponentsInChildren<Camera>(true)),
                    Is.Empty,
                    "The additive scene must reuse Board's output Camera.");
                Assert.That(
                    roots.SelectMany(root =>
                        root.GetComponentsInChildren<AudioListener>(true)),
                    Is.Empty);
            }
            finally
            {
                if (openedForTest && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static Transform FindDescendant(
            Transform root,
            string childName)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == childName)
            {
                return root;
            }
            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindDescendant(root.GetChild(index), childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
