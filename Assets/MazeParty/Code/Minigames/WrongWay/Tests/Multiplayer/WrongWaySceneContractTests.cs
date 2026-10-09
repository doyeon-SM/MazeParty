using System;
using System.Linq;
using MazeParty.Gameplay.Minigames.WrongWay;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class WrongWaySceneContractTests
    {
        private const string ScenePath =
            "Assets/MazeParty/Scenes/Minigames/WrongWay/WrongWay.unity";
        private const string HudPrefabPath =
            "Assets/MazeParty/Prefabs/Minigames/WrongWay/UI/WrongWayHud.prefab";
        [Test]
        public void WrongWayScene_PreservesMinimalHudCourseAndAdditiveContract()
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
                    hud.GetComponentsInChildren<Text>(true),
                    Is.Empty,
                    "WrongWay gameplay shows the local direction icon alone.");
                Assert.That(hud.DirectionIcon, Is.Not.Null);
                var directionSprites = new[]
                {
                    hud.UpIcon,
                    hud.DownIcon,
                    hud.LeftIcon,
                    hud.RightIcon
                };
                Assert.That(directionSprites, Is.All.Not.Null);
                Assert.That(
                    directionSprites.Distinct().Count(),
                    Is.EqualTo(directionSprites.Length),
                    "Each direction must retain a distinct authored sprite.");
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

                    AssertRunnerMatchesSurface(
                        slot,
                        0,
                        FindDescendant(lane, "Start Platform"));
                    for (var progress = 1;
                         progress <= WrongWayRules.StepCount;
                         progress++)
                    {
                        AssertRunnerMatchesSurface(
                            slot,
                            progress,
                            FindDescendant(
                                lane,
                                "Step " + progress.ToString("00")));
                    }
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

        private static void AssertRunnerMatchesSurface(
            int slot,
            int progress,
            Transform surface)
        {
            Assert.That(surface, Is.Not.Null);
            var renderers = surface.GetComponentsInChildren<Renderer>(true);
            Assert.That(
                renderers,
                Is.Not.Empty,
                surface.name + " must have authored renderers.");

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            var runnerPosition = WrongWayNetworkView.GetRunnerWorldPosition(
                slot,
                progress);
            Assert.That(
                runnerPosition.x,
                Is.EqualTo(bounds.center.x).Within(0.001f),
                surface.name + " lane center");
            Assert.That(
                runnerPosition.y,
                Is.EqualTo(bounds.max.y).Within(0.001f),
                surface.name + " top surface");
            Assert.That(
                runnerPosition.z,
                Is.EqualTo(bounds.center.z).Within(0.001f),
                surface.name + " tread center");
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
