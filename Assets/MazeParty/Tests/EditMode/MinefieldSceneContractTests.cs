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
            "Assets/MazeParty/Scenes/Minefield.unity";
        private const string HudPrefabPath =
            "Assets/MazeParty/UI/Prefabs/MinefieldHud.prefab";

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
                var serializedView = new SerializedObject(view);
                var hud = serializedView.FindProperty("hud")
                    ?.objectReferenceValue as MinefieldHudBindings;
                Assert.That(hud, Is.Not.Null);
                Assert.That(hud.HasRequiredReferences, Is.True);
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

        private static void AssertAuthoritativeLayoutContract()
        {
            Assert.That(NetworkMinefieldState.GridWidth, Is.EqualTo(3));
            Assert.That(NetworkMinefieldState.MineCount, Is.EqualTo(20));

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
