using System.Linq;
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
            "Assets/MazeParty/Scenes/Minigames/RedLightGreenLight/RedLightGreenLight.unity";
        private const string HudPrefabPath =
            "Assets/MazeParty/Prefabs/Minigames/RedLightGreenLight/UI/RedLightGreenLightHud.prefab";

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
            Assert.That(prefabBindings.SignalText, Is.Not.Null);
            Assert.That(prefabBindings.SignalText.supportRichText, Is.True);
            AssertLightSequence(hudPrefab);

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
                    Is.Null,
                    "The final authored scene uses only the HUD signal.");
                Assert.That(
                    FindDescendant(state.transform, "Observer Head"),
                    Is.Null);
                Assert.That(
                    FindDescendant(state.transform, "Signal Tower Placeholder"),
                    Is.Null,
                    "The final authored scene uses only the HUD signal.");
                Assert.That(
                    FindDescendant(state.transform, "Green Signal"),
                    Is.Null);
                Assert.That(
                    FindDescendant(state.transform, "Red Signal"),
                    Is.Null);
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

        private static void AssertLightSequence(GameObject hudPrefab)
        {
            var instance = Object.Instantiate(hudPrefab);
            try
            {
                var bindings =
                    instance.GetComponent<RedLightGreenLightHudBindings>();
                var redTag = "<color=#" +
                             ColorUtility.ToHtmlStringRGBA(
                                 bindings.RedSignalColor) + ">";
                var greenTag = "<color=#" +
                               ColorUtility.ToHtmlStringRGBA(
                                   bindings.GreenSignalColor) + ">";

                for (var redCount = 0; redCount <= 3; redCount++)
                {
                    bindings.SetSignalLights(redCount);
                    var lights = System.Text.RegularExpressions.Regex.Matches(
                            bindings.SignalText.text,
                            "<color=#[0-9A-Fa-f]{8}>●</color>")
                        .Cast<System.Text.RegularExpressions.Match>()
                        .Select(match => match.Value)
                        .ToArray();
                    Assert.That(lights, Has.Length.EqualTo(3));
                    for (var lightIndex = 0;
                         lightIndex < lights.Length;
                         lightIndex++)
                    {
                        Assert.That(
                            lights[lightIndex],
                            Does.StartWith(
                                lightIndex < redCount
                                    ? redTag
                                    : greenTag),
                            "Lights must turn red from left to right.");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
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
