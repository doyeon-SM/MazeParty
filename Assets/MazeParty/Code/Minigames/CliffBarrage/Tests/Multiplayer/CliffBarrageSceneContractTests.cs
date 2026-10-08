using System.Linq;
using System.Reflection;
using MazeParty.Gameplay.Minigames.CliffBarrage;
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
            "Assets/MazeParty/Scenes/Minigames/CliffBarrage/CliffBarrage.unity";

        [Test]
        public void DamagePresentationRpc_IsReliablePerEvent()
        {
            var rpc = typeof(NetworkCliffBarrageState).GetMethod(
                "PlayDamagePresentationRpc",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(rpc, Is.Not.Null);
            Assert.That(rpc.GetCustomAttribute<RpcAttribute>()?.Delivery,
                Is.EqualTo(RpcDelivery.Reliable));
            var parameters = rpc.GetParameters();
            Assert.That(parameters, Has.Length.EqualTo(2));
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(byte)));
            Assert.That(parameters[1].ParameterType,
                Is.EqualTo(typeof(Vector2)));
        }

        [Test]
        public void PushPresentationRpc_IsReliablePerEvent()
        {
            var rpc = typeof(NetworkCliffBarrageState).GetMethod(
                "PlayPushPresentationRpc",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(rpc, Is.Not.Null);
            Assert.That(rpc.GetCustomAttribute<RpcAttribute>()?.Delivery,
                Is.EqualTo(RpcDelivery.Reliable));
            var parameters = rpc.GetParameters();
            Assert.That(parameters, Has.Length.EqualTo(1));
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(byte)));
        }

        [Test]
        public void CompletedRound_EntersSecondRoundCountdownForExactDuration()
        {
            var match = new CliffBarrageMatchState(
                20261009,
                projectileLimit: 0,
                laserLimit: 0);
            match.AdvanceTo(CliffBarrageRules.RoundDurationSeconds);
            Assert.That(match.IsRoundComplete, Is.True);
            Assert.That(match.IsComplete, Is.False);

            var root = new GameObject("CliffBarrage Round Countdown Test");
            try
            {
                var state = root.AddComponent<NetworkCliffBarrageState>();
                NetworkCountdownTestAccess.SetPrivateField(
                    state,
                    "_serverMatch",
                    match);
                NetworkCountdownTestAccess.SetNetworkValue(
                    state,
                    "_matchActive",
                    true);
                NetworkCountdownTestAccess.SetRoundNumber(state, 1);
                NetworkCountdownTestAccess.SetNetworkValue(
                    state,
                    "_phase",
                    (byte)NetworkCliffBarragePhase.RoundResult);

                const double transitionAt = 240d;
                NetworkCountdownTestAccess.InvokePrivate(
                    state,
                    "BeginNextRoundOnServer",
                    transitionAt);

                Assert.That(match.RoundNumber, Is.EqualTo(2));
                Assert.That(match.IsRoundComplete, Is.False);
                Assert.That(
                    state.Phase,
                    Is.EqualTo(NetworkCliffBarragePhase.Countdown));
                Assert.That(state.RoundNumber, Is.EqualTo(2));
                Assert.That(
                    NetworkCountdownTestAccess.GetNetworkValue<double>(
                        state,
                        "_phaseEndsAt"),
                    Is.EqualTo(
                        transitionAt +
                        CliffBarrageRules.CountdownSeconds)
                        .Within(0.000001d));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

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
