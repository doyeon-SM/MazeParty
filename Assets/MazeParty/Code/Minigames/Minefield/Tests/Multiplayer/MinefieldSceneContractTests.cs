using System.Linq;
using System.Reflection;
using MazeParty.Gameplay.Minigames.Minefield;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MinefieldSceneContractTests
    {
        private const string ScenePath =
            "Assets/MazeParty/Scenes/Minigames/Minefield/Minefield.unity";
        private const string SonarPulsePrefabPath =
            "Assets/MazeParty/Prefabs/Minigames/Minefield/SonarPulse.prefab";

        [Test]
        public void HazardPresentationRpc_IsReliablePerEvent()
        {
            var rpc = typeof(NetworkMinefieldState).GetMethod(
                "PlayHazardVfxRpc",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(rpc, Is.Not.Null);
            Assert.That(rpc.GetCustomAttribute<RpcAttribute>()?.Delivery,
                Is.EqualTo(RpcDelivery.Reliable));
            var parameters = rpc.GetParameters();
            Assert.That(parameters, Has.Length.EqualTo(3));
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(byte)));
            Assert.That(parameters[1].ParameterType, Is.EqualTo(typeof(byte)));
            Assert.That(parameters[2].ParameterType,
                Is.EqualTo(typeof(Vector3)));
        }

        [Test]
        public void MinefieldScene_PreservesAuthoritativeLayoutAndAdditiveContract()
        {
            AssertAuthoritativeLayoutContract();

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
                        NetworkMinefieldState>(true))
                    .SingleOrDefault();
                Assert.That(state, Is.Not.Null);

                var view = state.GetComponent<MinefieldNetworkView>();
                Assert.That(view, Is.Not.Null);
                Assert.That(
                    state.GetComponentsInChildren<Canvas>(true),
                    Is.Empty,
                    "Minefield communicates state through its world " +
                    "presentation and owns no dedicated Canvas HUD.");

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
                    FindDescendant(state.transform, "Crusher Placeholder"),
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

        [Test]
        public void SonarPulsePrefab_MatchesAuthoritativeDetectionRadius()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                SonarPulsePrefabPath);
            Assert.That(prefab, Is.Not.Null, SonarPulsePrefabPath);

            var pulse = prefab.GetComponent<LineRenderer>();
            Assert.That(pulse, Is.Not.Null, SonarPulsePrefabPath);
            Assert.That(pulse.loop, Is.True);
            Assert.That(pulse.useWorldSpace, Is.False);
            Assert.That(pulse.positionCount, Is.GreaterThanOrEqualTo(16));

            var positions = new Vector3[pulse.positionCount];
            pulse.GetPositions(positions);
            for (var index = 0; index < positions.Length; index++)
            {
                Assert.That(
                    positions[index].y,
                    Is.EqualTo(0f).Within(0.0001f),
                    "Point " + index + " must stay on the authored XZ plane.");
                Assert.That(
                    new Vector2(positions[index].x, positions[index].z).magnitude,
                    Is.EqualTo(NetworkMinefieldState.SonarRadius).Within(0.001f),
                    "Point " + index + " must match the authoritative radius.");
            }
        }

        private static void AssertAuthoritativeLayoutContract()
        {
            Assert.That(NetworkMinefieldState.GridWidth, Is.EqualTo(3));
            Assert.That(NetworkMinefieldState.MineCount, Is.EqualTo(20));
            Assert.That(NetworkMinefieldState.SonarRadius, Is.EqualTo(3f));
            Assert.That(
                NetworkMinefieldState.SonarDetectionSeconds,
                Is.EqualTo(0.75d));

            var positions = NetworkMinefieldState.GenerateMineWorldPositions(
                0x123456789ABCDEF0UL,
                1);
            Assert.That(
                NetworkMinefieldState.GenerateMineWorldPositions(
                    0x123456789ABCDEF0UL,
                    1),
                Is.EqualTo(positions));
            Assert.That(positions, Has.Length.EqualTo(20));
            for (var index = 0; index < positions.Length; index++)
            {
                Assert.That(
                    positions[index].x,
                    Is.InRange(
                        NetworkMinefieldState.ArenaMinX +
                        NetworkMinefieldState.MineSpawnHorizontalPadding,
                        NetworkMinefieldState.ArenaMaxX -
                        NetworkMinefieldState.MineSpawnHorizontalPadding));
                Assert.That(
                    positions[index].z,
                    Is.InRange(
                        NetworkMinefieldState.ArenaMinZ +
                        NetworkMinefieldState.MineSafeZoneDepth,
                        NetworkMinefieldState.ArenaMaxZ -
                        NetworkMinefieldState.MineSafeZoneDepth));
                for (var other = index + 1;
                     other < positions.Length;
                     other++)
                {
                    Assert.That(
                        Vector3.Distance(positions[index], positions[other]),
                        Is.GreaterThanOrEqualTo(
                            NetworkMinefieldState.MinimumMineSpacing -
                            0.0001f));
                }
            }

            const BindingFlags privateInstance =
                BindingFlags.Instance | BindingFlags.NonPublic;
            var stateType = typeof(NetworkMinefieldState);
            Assert.That(
                stateType.GetField("_serverSeed", privateInstance)?.FieldType,
                Is.EqualTo(typeof(ulong)),
                "The complete layout must remain server-local.");
            Assert.That(
                stateType.GetField(
                    "_detonatedMineMask",
                    privateInstance)?.FieldType,
                Is.EqualTo(typeof(uint)));
            Assert.That(
                stateType.GetField(
                    "_revealedMinePositions",
                    privateInstance)?.FieldType,
                Is.EqualTo(typeof(NetworkList<Vector3>)),
                "Only sonar-authorized positions may replicate.");
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
                var found = FindDescendant(root.GetChild(index), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
