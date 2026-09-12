using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class RedLightGreenLightSceneContractTests
    {
        private const string ScenePath =
            "Assets/MazeParty/Scenes/RedLightGreenLight.unity";
        private const string HudPrefabPath =
            "Assets/MazeParty/UI/Prefabs/RedLightGreenLightHud.prefab";

        [Test]
        public void Scene_PreservesNetworkHudArenaAndAdditiveContract()
        {
            var hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                HudPrefabPath);
            Assert.That(hudPrefab, Is.Not.Null, HudPrefabPath);
            var prefabBindings =
                hudPrefab.GetComponent<RedLightGreenLightHudBindings>();
            Assert.That(prefabBindings, Is.Not.Null);
            Assert.That(prefabBindings.HasRequiredReferences, Is.True);
            Assert.That(prefabBindings.PlayerRows, Has.Length.EqualTo(4));

            var labelBuilder = typeof(RedLightGreenLightNetworkView).GetMethod(
                "BuildSignalLabel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(labelBuilder, Is.Not.Null);
            Assert.That(
                labelBuilder.GetParameters()[1].ParameterType,
                Is.EqualTo(
                    typeof(RedLightGreenLightHudSignalStyle)
                        .MakeByRefType()),
                "The runtime view must select a prefab-owned semantic style, " +
                "not a hard-coded Color.");

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
                        NetworkRedLightGreenLightState>(true))
                    .SingleOrDefault();
                Assert.That(state, Is.Not.Null);

                var view =
                    state.GetComponent<RedLightGreenLightNetworkView>();
                Assert.That(view, Is.Not.Null);
                var serializedView = new SerializedObject(view);
                var hud = serializedView.FindProperty("hud")
                    ?.objectReferenceValue as
                    RedLightGreenLightHudBindings;
                Assert.That(hud, Is.Not.Null);
                Assert.That(hud.HasRequiredReferences, Is.True);
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        hud.gameObject),
                    Is.EqualTo(HudPrefabPath));

                var requiredReferences = new[]
                {
                    "state",
                    "topDownCamera",
                    "runnerRoot",
                    "arenaPresentation",
                    "observerHead",
                    "greenSignalRenderer",
                    "redSignalRenderer",
                    "greenSignalLight",
                    "redSignalLight",
                    "cueAudioSource",
                    "hud"
                };
                foreach (var propertyName in requiredReferences)
                {
                    var property = serializedView.FindProperty(propertyName);
                    Assert.That(property, Is.Not.Null, propertyName);
                    Assert.That(
                        property.objectReferenceValue,
                        Is.Not.Null,
                        propertyName);
                }

                var networkObject = state.GetComponent<NetworkObject>();
                Assert.That(networkObject, Is.Not.Null);
                Assert.That(networkObject.InScenePlaced, Is.True);
                Assert.That(networkObject.PrefabIdHash, Is.Not.EqualTo(0u));
                Assert.That(
                    state.GetComponentsInChildren<Component>(true).Any(
                        component => component != null &&
                                     component.GetType().FullName ==
                                     "Unity.Cinemachine.CinemachineCamera"),
                    Is.True);
                Assert.That(
                    roots.SelectMany(root =>
                        root.GetComponentsInChildren<Camera>(true)),
                    Is.Empty,
                    "The additive scene must reuse Board's output Camera.");
                Assert.That(
                    roots.SelectMany(root =>
                        root.GetComponentsInChildren<AudioListener>(true)),
                    Is.Empty);

                var floor = FindDescendant(state.transform, "Arena Floor");
                Assert.That(floor, Is.Not.Null);
                Assert.That(floor.localScale.x, Is.EqualTo(20f).Within(0.001f));
                Assert.That(floor.localScale.z, Is.EqualTo(44f).Within(0.001f));
                Assert.That(
                    FindDescendant(state.transform, "Start Line"),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(state.transform, "Finish Line"),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(state.transform, "West Wall")
                        ?.GetComponent<Collider>(),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(state.transform, "East Wall")
                        ?.GetComponent<Collider>(),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(state.transform, "Observer Placeholder"),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(state.transform, "Observer Head"),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(state.transform, "Green Signal")
                        ?.GetComponent<Renderer>(),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(state.transform, "Red Signal")
                        ?.GetComponent<Renderer>(),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(state.transform, "Green Signal Light")
                        ?.GetComponent<Light>(),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(state.transform, "Red Signal Light")
                        ?.GetComponent<Light>(),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(state.transform, "Signal Audio Anchor")
                        ?.GetComponent<AudioSource>(),
                    Is.Not.Null);
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
                var result = FindDescendant(
                    root.GetChild(index),
                    childName);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}
