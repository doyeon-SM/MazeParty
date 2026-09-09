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
        private const string MinefieldScene =
            "Assets/MazeParty/Scenes/Minefield.unity";
        private const string WrongWayScene =
            "Assets/MazeParty/Scenes/WrongWay.unity";

        [Test]
        public void WrongWay_UsesFourParallelFiftyStepLanes()
        {
            Assert.That(WrongWayRules.PlayerCount, Is.EqualTo(4));
            Assert.That(WrongWayRules.StepCount, Is.EqualTo(50));
            Assert.That(WrongWayRules.RoundCount, Is.EqualTo(2));
            Assert.That(WrongWayRules.RoundSeconds, Is.EqualTo(60d));

            for (var slot = 1;
                 slot < WrongWayRules.PlayerCount;
                 slot++)
            {
                Assert.That(
                    WrongWayNetworkView.GetLaneX(slot),
                    Is.GreaterThan(
                        WrongWayNetworkView.GetLaneX(slot - 1)));
            }

            var start =
                WrongWayNetworkView.GetRunnerWorldPosition(0, 0);
            var finish =
                WrongWayNetworkView.GetRunnerWorldPosition(
                    0,
                    WrongWayRules.StepCount);
            Assert.That(start.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(
                finish.y,
                Is.EqualTo(WrongWayNetworkView.CourseHeight)
                    .Within(0.001f));
            Assert.That(finish.z, Is.GreaterThan(start.z));
        }

        [Test]
        public void WrongWay_IsEnabledImmediatelyAfterMinefield()
        {
            var scenes = EditorBuildSettings.scenes;
            var minefieldIndex = Array.FindIndex(
                scenes,
                scene => scene.path == MinefieldScene);
            var wrongWayIndex = Array.FindIndex(
                scenes,
                scene => scene.path == WrongWayScene);

            Assert.That(wrongWayIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(scenes[wrongWayIndex].enabled, Is.True);
            if (minefieldIndex >= 0)
            {
                Assert.That(
                    wrongWayIndex,
                    Is.EqualTo(minefieldIndex + 1));
            }
        }

        [Test]
        public void WrongWayScene_HasNetworkStateCameraAndNoOutputCamera()
        {
            var scene = SceneManager.GetSceneByPath(WrongWayScene);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    WrongWayScene,
                    OpenSceneMode.Additive);
            }

            try
            {
                var roots = scene.GetRootGameObjects();
                var state = roots
                    .SelectMany(root =>
                        root.GetComponentsInChildren<
                            NetworkWrongWayState>(true))
                    .SingleOrDefault();

                Assert.That(state, Is.Not.Null);
                Assert.That(
                    state.GetComponent<WrongWayNetworkView>(),
                    Is.Not.Null);

                var networkObject =
                    state.GetComponent<NetworkObject>();
                Assert.That(networkObject, Is.Not.Null);
                Assert.That(networkObject.InScenePlaced, Is.True);
                Assert.That(
                    networkObject.PrefabIdHash,
                    Is.Not.EqualTo(0u));

                Assert.That(
                    state.GetComponentsInChildren<Component>(true)
                        .Any(component =>
                            component != null &&
                            component.GetType().FullName ==
                            "Unity.Cinemachine.CinemachineCamera"),
                    Is.True);
                Assert.That(
                    roots.SelectMany(root =>
                        root.GetComponentsInChildren<Camera>(true)),
                    Is.Empty,
                    "The additive Board scene owns the output Camera.");
                Assert.That(
                    roots.SelectMany(root =>
                        root.GetComponentsInChildren<AudioListener>(true)),
                    Is.Empty);

                Assert.That(
                    FindDescendant(
                        state.transform,
                        "Arena Presentation"),
                    Is.Not.Null);
                Assert.That(
                    state.GetComponentsInChildren<Transform>(true)
                        .Any(value =>
                            value.name.IndexOf(
                                "Crusher",
                                StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.False,
                    "WrongWay has no crusher or other death device.");
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
        public void WrongWayScene_EachLaneContainsExactlyFiftySteps()
        {
            var scene = SceneManager.GetSceneByPath(WrongWayScene);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    WrongWayScene,
                    OpenSceneMode.Additive);
            }

            try
            {
                var roots = scene.GetRootGameObjects();
                Assert.That(roots, Is.Not.Empty);

                for (var slot = 0;
                     slot < WrongWayRules.PlayerCount;
                     slot++)
                {
                    var lane = roots
                        .Select(root => FindDescendant(
                            root.transform,
                            "Lane " + (slot + 1)))
                        .FirstOrDefault(value => value != null);
                    Assert.That(lane, Is.Not.Null);

                    var steps = Enumerable.Range(
                            0,
                            lane.childCount)
                        .Select(lane.GetChild)
                        .Where(child =>
                            child.name.StartsWith(
                                "Step ",
                                StringComparison.Ordinal))
                        .ToArray();

                    Assert.That(
                        steps.Length,
                        Is.EqualTo(WrongWayRules.StepCount));
                    Assert.That(
                        steps.Select(step => step.name).Distinct().Count(),
                        Is.EqualTo(WrongWayRules.StepCount));
                    Assert.That(
                        steps.All(step =>
                            step.GetComponent<Collider>() == null),
                        Is.True,
                        "The logical race does not need 200 physics colliders.");
                }
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
        public void RaceCamera_TracksLeaderAndKeepsThreeQuarterView()
        {
            var startFocus =
                WrongWayNetworkView.CalculateCameraFocus(0);
            var finishFocus =
                WrongWayNetworkView.CalculateCameraFocus(
                    WrongWayRules.StepCount);
            var startPosition =
                WrongWayNetworkView.CalculateCameraPosition(
                    startFocus);
            var startRotation =
                WrongWayNetworkView.CalculateCameraRotation(
                    startFocus);
            var forward = startRotation * Vector3.forward;

            Assert.That(
                finishFocus.y,
                Is.GreaterThan(startFocus.y));
            Assert.That(
                finishFocus.z,
                Is.GreaterThan(startFocus.z));
            Assert.That(
                startPosition.y,
                Is.GreaterThan(startFocus.y));
            Assert.That(
                startPosition.z,
                Is.LessThan(startFocus.z));
            Assert.That(
                Vector3.Dot(
                    forward,
                    (startFocus - startPosition).normalized),
                Is.EqualTo(1f).Within(0.001f));
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

            for (var index = 0;
                 index < root.childCount;
                 index++)
            {
                var found = FindDescendant(
                    root.GetChild(index),
                    childName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
