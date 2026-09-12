using System;
using System.Linq;
using MazeParty.Gameplay.Minigames.WrongWay;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class WrongWaySceneContractTests
    {
        private const string ScenePath =
            "Assets/MazeParty/Scenes/WrongWay.unity";
        private const string HudPrefabPath =
            "Assets/MazeParty/UI/Prefabs/WrongWayHud.prefab";

        [Test]
        public void WrongWayScene_PreservesNetworkHudCourseAndAdditiveContract()
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
                        NetworkWrongWayState>(true))
                    .SingleOrDefault();
                Assert.That(state, Is.Not.Null);

                var view = state.GetComponent<WrongWayNetworkView>();
                Assert.That(view, Is.Not.Null);
                var serializedView = new SerializedObject(view);
                var hud = serializedView.FindProperty("hud")
                    ?.objectReferenceValue as WrongWayHudBindings;
                Assert.That(hud, Is.Not.Null);
                Assert.That(hud.HasRequiredReferences, Is.True);
                Assert.That(
                    hud.ProgressRows,
                    Has.Length.EqualTo(WrongWayRules.PlayerCount));
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        hud.gameObject),
                    Is.EqualTo(HudPrefabPath));

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
                Assert.That(
                    FindDescendant(state.transform, "Arena Presentation"),
                    Is.Not.Null);
                Assert.That(
                    state.GetComponentsInChildren<Transform>(true).Any(
                        value => value.name.IndexOf(
                            "Crusher",
                            StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.False,
                    "WrongWay must not inherit Minefield's death device.");

                for (var slot = 0;
                     slot < WrongWayRules.PlayerCount;
                     slot++)
                {
                    var lane = roots
                        .Select(root => FindDescendant(
                            root.transform,
                            "Lane " + (slot + 1)))
                        .FirstOrDefault(value => value != null);
                    Assert.That(lane, Is.Not.Null, "Lane " + (slot + 1));

                    var steps = Enumerable.Range(0, lane.childCount)
                        .Select(lane.GetChild)
                        .Where(child => child.name.StartsWith(
                            "Step ",
                            StringComparison.Ordinal))
                        .ToArray();
                    Assert.That(
                        steps,
                        Has.Length.EqualTo(WrongWayRules.StepCount));
                    Assert.That(
                        steps.Select(step => step.name).Distinct().Count(),
                        Is.EqualTo(WrongWayRules.StepCount));
                    Assert.That(
                        steps.All(step =>
                            step.GetComponent<Collider>() == null),
                        Is.True);
                }
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
