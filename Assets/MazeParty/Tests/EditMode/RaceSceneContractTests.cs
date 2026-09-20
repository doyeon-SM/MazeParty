using System.Linq;
using MazeParty.Gameplay.Minigames.Race;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class RaceSceneContractTests
    {
        private const string ScenePath =
            "Assets/MazeParty/Scenes/Race.unity";
        [Test]
        public void Scene_PreservesFourLaneSharedCamera()
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
                        NetworkRaceState>(true))
                    .SingleOrDefault();
                Assert.That(state, Is.Not.Null);
                var networkObject = state.GetComponent<NetworkObject>();
                Assert.That(networkObject, Is.Not.Null);
                Assert.That(networkObject.InScenePlaced, Is.True);
                Assert.That(networkObject.PrefabIdHash, Is.Not.EqualTo(0u));

                var view = state.GetComponent<RaceNetworkView>();
                Assert.That(view, Is.Not.Null);
                var serialized = new SerializedObject(view);
                foreach (var propertyName in new[]
                         {
                             "state",
                             "sharedCamera",
                             "playerRoot",
                             "arenaPresentation"
                         })
                {
                    var property = serialized.FindProperty(propertyName);
                    Assert.That(property, Is.Not.Null, propertyName);
                    Assert.That(
                        property.objectReferenceValue,
                        Is.Not.Null,
                        propertyName);
                }

                Assert.That(
                    FindDescendant(state.transform, "Race Track"),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(state.transform, "Start Line"),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(state.transform, "Finish Line"),
                    Is.Not.Null);
                for (var divider = 1;
                     divider < RaceRules.PlayerCount;
                     divider++)
                {
                    Assert.That(
                        FindDescendant(
                            state.transform,
                            "Lane Divider " + divider),
                        Is.Not.Null);
                }
                for (var slot = 1; slot <= RaceRules.PlayerCount; slot++)
                {
                    Assert.That(
                        FindDescendant(
                            state.transform,
                            "Start Marker " + slot),
                        Is.Not.Null);
                }

                var cameras = state.GetComponentsInChildren<Component>(true)
                    .Where(component => component != null &&
                        component.GetType().FullName ==
                        "Unity.Cinemachine.CinemachineCamera")
                    .ToArray();
                Assert.That(cameras, Has.Length.EqualTo(1));
                Assert.That(cameras[0].name, Is.EqualTo("CM_RaceShared"));
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

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == name)
            {
                return root;
            }
            for (var index = 0; index < root.childCount; index++)
            {
                var result = FindDescendant(root.GetChild(index), name);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }
    }
}
