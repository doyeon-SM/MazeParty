using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class CliffBarrageSceneContractTests
    {
        private const string ScenePath =
            "Assets/MazeParty/Scenes/Minigames/CliffBarrage.unity";

        [Test]
        public void Scene_BindsSharedCameraAndReusableHazardPools()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    ScenePath, OpenSceneMode.Additive);
            }

            try
            {
                var roots = scene.GetRootGameObjects();
                var state = roots.SelectMany(root =>
                    root.GetComponentsInChildren<NetworkCliffBarrageState>(
                        true)).SingleOrDefault();
                Assert.That(state, Is.Not.Null);
                var networkObject = state.GetComponent<NetworkObject>();
                Assert.That(networkObject, Is.Not.Null);
                Assert.That(networkObject.InScenePlaced, Is.True);
                Assert.That(networkObject.PrefabIdHash, Is.Not.EqualTo(0u));

                var view = state.GetComponent<CliffBarrageNetworkView>();
                Assert.That(view, Is.Not.Null);
                var serialized = new SerializedObject(view);
                foreach (var field in new[]
                         {
                             "state", "sharedCamera", "arenaPresentation",
                             "playerRoot"
                         })
                {
                    var property = serialized.FindProperty(field);
                    Assert.That(property, Is.Not.Null, field);
                    Assert.That(property.objectReferenceValue,
                        Is.Not.Null, field);
                }
                var arena = serialized.FindProperty("arenaPresentation")
                    .objectReferenceValue as GameObject;
                Assert.That(arena, Is.Not.Null);
                Assert.That(arena.transform.position.x,
                    Is.EqualTo(CliffBarrageNetworkView.ArenaCenterX));

                AssertPooledReferences(serialized, arena.transform,
                    "projectiles",
                    NetworkCliffBarrageState.ProjectilePoolSize);
                AssertPooledReferences(serialized, arena.transform,
                    "laserRoots",
                    NetworkCliffBarrageState.LaserPoolSize);
                AssertPooledReferences(serialized, arena.transform,
                    "warningBeams",
                    NetworkCliffBarrageState.LaserPoolSize);
                AssertPooledReferences(serialized, arena.transform,
                    "firingBeams",
                    NetworkCliffBarrageState.LaserPoolSize);

                var cameras = roots.SelectMany(root =>
                    root.GetComponentsInChildren<Component>(true))
                    .Where(component => component != null &&
                        component.GetType().FullName ==
                        "Unity.Cinemachine.CinemachineCamera")
                    .ToArray();
                Assert.That(cameras, Has.Length.EqualTo(1));
                Assert.That(roots.SelectMany(root =>
                    root.GetComponentsInChildren<Camera>(true)), Is.Empty);
                Assert.That(roots.SelectMany(root =>
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
        public void BuildSettings_EnableCliffBarrage()
        {
            var enabled = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            Assert.That(enabled, Does.Contain(ScenePath));
        }

        private static void AssertPooledReferences(
            SerializedObject serialized, Transform arena,
            string field, int expectedCount)
        {
            var property = serialized.FindProperty(field);
            Assert.That(property, Is.Not.Null, field);
            Assert.That(property.arraySize, Is.EqualTo(expectedCount), field);
            for (var index = 0; index < expectedCount; index++)
            {
                var reference = property.GetArrayElementAtIndex(index)
                    .objectReferenceValue;
                Assert.That(reference, Is.Not.Null,
                    field + "[" + index + "]");
                var gameObject = reference as GameObject;
                var transform = gameObject != null
                    ? gameObject.transform
                    : reference as Transform;
                Assert.That(transform, Is.Not.Null);
                Assert.That(transform.IsChildOf(arena), Is.True);
                Assert.That(transform.GetComponent<Collider>(), Is.Null);
            }
        }
    }
}
