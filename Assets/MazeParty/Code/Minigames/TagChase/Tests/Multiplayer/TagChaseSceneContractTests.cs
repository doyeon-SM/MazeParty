using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
            "Assets/MazeParty/Scenes/Minigames/TagChase/TagChase.unity";
        private const string EnvironmentPrefabPath =
            "Assets/MazeParty/Prefabs/Minigames/TagChase/" +
            "TagChaseEnvironment.prefab";
        private const float GeometryTolerance = 0.001f;

        [Test]
        public void AttackPresentationRpc_IsReliablePerEvent()
        {
            var rpc = typeof(NetworkTagChaseState).GetMethod(
                "PlayAttackPresentationRpc",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(rpc, Is.Not.Null);
            Assert.That(rpc.GetCustomAttribute<RpcAttribute>()?.Delivery,
                Is.EqualTo(RpcDelivery.Reliable));
            var parameters = rpc.GetParameters();
            Assert.That(parameters, Has.Length.EqualTo(1));
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(byte)));
        }

        [Test]
        public void Scene_PreservesArenaAndRoleCameras()
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

                var environmentRoot = roots
                    .SelectMany(root =>
                        root.GetComponentsInChildren<Transform>(true))
                    .SingleOrDefault(transform =>
                        transform.name == "Tag Chase Environment");
                Assert.That(environmentRoot, Is.Not.Null);
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        environmentRoot.gameObject),
                    Is.EqualTo(EnvironmentPrefabPath));
                Assert.That(
                    Vector3.Distance(
                        environmentRoot.position,
                        Vector3.zero),
                    Is.LessThan(GeometryTolerance),
                    "The world-space collision layout requires the " +
                    "environment instance to remain at the origin.");
                Assert.That(
                    Quaternion.Angle(
                        environmentRoot.rotation,
                        Quaternion.identity),
                    Is.LessThan(GeometryTolerance),
                    "The world-space collision layout requires identity " +
                    "environment rotation.");
                Assert.That(
                    Vector3.Distance(
                        environmentRoot.lossyScale,
                        Vector3.one),
                    Is.LessThan(GeometryTolerance),
                    "The world-space collision layout requires unit " +
                    "environment scale.");

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
                var sharedRunnerCamera = cameras.Single(camera =>
                    camera.name == "CM_TagChaseRunners");
                Assert.That(
                    serialized.FindProperty("sharedRunnerCamera")
                        .objectReferenceValue,
                    Is.SameAs(sharedRunnerCamera));
                Assert.That(
                    Vector3.Distance(
                        sharedRunnerCamera.transform.position,
                        TagChaseNetworkView
                            .InitialSharedCameraPosition),
                    Is.LessThan(GeometryTolerance),
                    "Runner camera position must remain the shared " +
                    "fixed overview position.");
                Assert.That(
                    Quaternion.Angle(
                        sharedRunnerCamera.transform.rotation,
                        TagChaseNetworkView
                            .InitialSharedCameraRotation),
                    Is.LessThan(GeometryTolerance),
                    "Runner camera rotation must remain fixed on the " +
                    "arena center.");
                Assert.That(
                    GetLensFieldOfView(sharedRunnerCamera),
                    Is.EqualTo(TagChaseNetworkView.SharedFieldOfView)
                        .Within(GeometryTolerance),
                    "Runner camera zoom must remain fixed.");
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

        [Test]
        public void EnvironmentBlockingVisuals_MatchBakedCollisionLayout()
        {
            var environment =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    EnvironmentPrefabPath);
            Assert.That(environment, Is.Not.Null);

            var authoredRects = environment
                .GetComponentsInChildren<Renderer>(true)
                .Where(IsInternalBlockingVisual)
                .Select(renderer =>
                {
                    var bounds = renderer.bounds;
                    return new Rect(
                        bounds.min.x,
                        bounds.min.z,
                        bounds.size.x,
                        bounds.size.z);
                })
                .ToList();

            Assert.That(
                authoredRects,
                Has.Count.EqualTo(30),
                "The authored maze should contain 16 internal walls " +
                "and 14 pillars.");
            Assert.That(
                TagChaseArenaCollisionLayout.BlockingRectCount,
                Is.EqualTo(authoredRects.Count));

            var unmatched = new List<Rect>(authoredRects);
            foreach (var bakedRect in
                     TagChaseArenaCollisionLayout.BlockingRects)
            {
                var matchIndex = unmatched.FindIndex(
                    authoredRect => RectApproximatelyEquals(
                        authoredRect,
                        bakedRect));
                Assert.That(
                    matchIndex,
                    Is.GreaterThanOrEqualTo(0),
                    "No authored wall or pillar matches baked rect " +
                    FormatRect(bakedRect) + ".");
                unmatched.RemoveAt(matchIndex);
            }

            Assert.That(
                unmatched,
                Is.Empty,
                "Every authored internal wall and pillar must be " +
                "represented by the deterministic collision layout.");
        }

        [Test]
        public void RoundStartPositions_ClearBakedCollisionLayout()
        {
            for (var taggerSlot = 0;
                 taggerSlot < TagChaseRules.PlayerCount;
                 taggerSlot++)
            {
                for (var playerSlot = 0;
                     playerSlot < TagChaseRules.PlayerCount;
                     playerSlot++)
                {
                    var position =
                        NetworkTagChaseState.GetRoundStartPosition(
                            taggerSlot,
                            playerSlot);
                    Assert.That(
                        TagChaseArenaCollisionLayout.BlocksCircle(
                            position,
                            NetworkTagChaseState
                                .PlayerCollisionRadius),
                        Is.False,
                        "P" + (playerSlot + 1) +
                        " starts inside maze collision when P" +
                        (taggerSlot + 1) + " is the tagger.");
                }
            }
        }

        private static bool IsInternalBlockingVisual(Renderer renderer)
        {
            var isWall = renderer.name.StartsWith(
                "Wall_3M",
                StringComparison.Ordinal);
            var isPillar = renderer.name.StartsWith(
                               "Pilar",
                               StringComparison.Ordinal) ||
                           renderer.name.StartsWith(
                               "Pillar",
                               StringComparison.Ordinal);
            if (!isWall && !isPillar)
            {
                return false;
            }

            var center = renderer.bounds.center;
            return Mathf.Abs(
                       center.x -
                       NetworkTagChaseState.ArenaCenterX) <
                   NetworkTagChaseState.ArenaHalfWidth - 0.5f &&
                   Mathf.Abs(center.z) <
                   NetworkTagChaseState.ArenaHalfDepth - 0.5f;
        }

        private static bool RectApproximatelyEquals(
            Rect left,
            Rect right)
        {
            return Mathf.Abs(left.xMin - right.xMin) <=
                   GeometryTolerance &&
                   Mathf.Abs(left.yMin - right.yMin) <=
                   GeometryTolerance &&
                   Mathf.Abs(left.width - right.width) <=
                   GeometryTolerance &&
                   Mathf.Abs(left.height - right.height) <=
                   GeometryTolerance;
        }

        private static string FormatRect(Rect rect)
        {
            return $"({rect.xMin:F3}, {rect.yMin:F3}, " +
                   $"{rect.width:F3}, {rect.height:F3})";
        }

        private static float GetLensFieldOfView(Component camera)
        {
            var lensField = camera.GetType().GetField("Lens");
            Assert.That(lensField, Is.Not.Null);
            var lens = lensField.GetValue(camera);
            Assert.That(lens, Is.Not.Null);
            var fieldOfView = lens.GetType().GetField("FieldOfView");
            Assert.That(fieldOfView, Is.Not.Null);
            return (float)fieldOfView.GetValue(lens);
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
