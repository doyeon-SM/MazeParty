using System.Linq;
using MazeParty.Gameplay.Minigames.TagChase;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class TagChaseSceneContractTests
    {
        private const string ScenePath =
            "Assets/MazeParty/Scenes/TagChase.unity";
        private const string HudPrefabPath =
            "Assets/MazeParty/UI/Prefabs/TagChaseHud.prefab";

        [Test]
        public void Scene_PreservesArenaRoleCamerasAndTimerOnlyHudContract()
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
                    .SelectMany(root =>
                        root.GetComponentsInChildren<
                            NetworkTagChaseState>(true))
                    .SingleOrDefault();
                Assert.That(state, Is.Not.Null);

                var networkObject = state.GetComponent<NetworkObject>();
                Assert.That(networkObject, Is.Not.Null);
                Assert.That(networkObject.InScenePlaced, Is.True);
                Assert.That(networkObject.PrefabIdHash, Is.Not.EqualTo(0u));

                var view = state.GetComponent<TagChaseNetworkView>();
                Assert.That(view, Is.Not.Null);
                var serialized = new SerializedObject(view);
                foreach (var propertyName in new[]
                         {
                             "state",
                             "sharedRunnerCamera",
                             "taggerCamera",
                             "playerRoot",
                             "arenaPresentation",
                             "hud"
                         })
                {
                    var property = serialized.FindProperty(propertyName);
                    Assert.That(property, Is.Not.Null, propertyName);
                    Assert.That(
                        property.objectReferenceValue,
                        Is.Not.Null,
                        propertyName);
                }

                var hud = state.GetComponentInChildren<
                    TagChaseHudBindings>(true);
                Assert.That(hud, Is.Not.Null);
                Assert.That(hud.HasRequiredReferences, Is.True);
                Assert.That(
                    hud.GetComponentsInChildren<UnityEngine.UI.Text>(true),
                    Has.Length.EqualTo(1),
                    "The production HUD may show only the shared timer text.");
                Assert.That(
                    PrefabUtility
                        .GetPrefabAssetPathOfNearestInstanceRoot(
                            hud.gameObject),
                    Is.EqualTo(HudPrefabPath));

                Assert.That(
                    FindDescendant(state.transform, "Arena Floor"),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(state.transform, "North Boundary"),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(state.transform, "South Boundary"),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(state.transform, "West Boundary"),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(state.transform, "East Boundary"),
                    Is.Not.Null);
                for (var index = 0;
                     index < NetworkTagChaseState.ObstacleCount;
                     index++)
                {
                    Assert.That(
                        FindDescendant(
                            state.transform,
                            "Sight Blocker " + (index + 1)),
                        Is.Not.Null);
                }
                Assert.That(
                    FindDescendant(state.transform, "Tagger Start"),
                    Is.Not.Null);
                for (var runner = 0; runner < 3; runner++)
                {
                    Assert.That(
                        FindDescendant(
                            state.transform,
                            "Runner Start " + (runner + 1)),
                        Is.Not.Null);
                }

                var cameras = state
                    .GetComponentsInChildren<Component>(true)
                    .Where(component =>
                        component != null &&
                        component.GetType().FullName ==
                        "Unity.Cinemachine.CinemachineCamera")
                    .ToArray();
                Assert.That(cameras, Has.Length.EqualTo(2));
                Assert.That(
                    cameras.Select(camera => camera.name),
                    Is.EquivalentTo(new[]
                    {
                        "CM_TagChaseRunners",
                        "CM_TagChaseTagger"
                    }));
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
