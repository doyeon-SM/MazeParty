using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MinefieldSceneContractTests
    {
        private const string BootstrapScene =
            "Assets/MazeParty/Scenes/OnlineBootstrap.unity";
        private const string BoardScene =
            "Assets/MazeParty/Scenes/Board.unity";
        private const string MinefieldScene =
            "Assets/MazeParty/Scenes/Minefield.unity";
        private const string BoardCanvasPrefab =
            "Assets/MazeParty/UI/Prefabs/BoardCanvas.prefab";

        [Test]
        public void Minefield_IsThirdEnabledBuildScene()
        {
            var enabled = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .ToArray();

            Assert.That(enabled.Length, Is.GreaterThanOrEqualTo(3));
            Assert.That(enabled[0].path, Is.EqualTo(BootstrapScene));
            Assert.That(enabled[1].path, Is.EqualTo(BoardScene));
            Assert.That(enabled[2].path, Is.EqualTo(MinefieldScene));
        }

        [Test]
        public void MinefieldScene_HasStableNetworkStateAndAdditiveSafeTopView()
        {
            var scene = SceneManager.GetSceneByPath(MinefieldScene);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    MinefieldScene,
                    OpenSceneMode.Additive);
            }

            try
            {
                var roots = scene.GetRootGameObjects();
                var networkState = roots
                    .SelectMany(root =>
                        root.GetComponentsInChildren<NetworkMinefieldState>(true))
                    .SingleOrDefault();

                Assert.That(networkState, Is.Not.Null);
                Assert.That(networkState.GetComponent<MinefieldNetworkView>(), Is.Not.Null);
                var networkObject = networkState.GetComponent<NetworkObject>();
                Assert.That(networkObject, Is.Not.Null);
                Assert.That(networkObject.InScenePlaced, Is.True);
                Assert.That(networkObject.PrefabIdHash, Is.Not.EqualTo(0u));
                Assert.That(
                    networkState.GetComponentsInChildren<Component>(true).Any(
                        component => component != null &&
                                     component.GetType().FullName ==
                                     "Unity.Cinemachine.CinemachineCamera"),
                    Is.True);
                Assert.That(
                    roots.SelectMany(root => root.GetComponentsInChildren<Camera>(true)),
                    Is.Empty,
                    "The additive scene must reuse Board's output Camera.");
                Assert.That(
                    roots.SelectMany(root => root.GetComponentsInChildren<AudioListener>(true)),
                    Is.Empty);
                Assert.That(
                    FindDescendant(networkState.transform, "Crusher Placeholder"),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(networkState.transform, "Arena Presentation"),
                    Is.Not.Null,
                    "The retained additive scene needs one presentation root to hide on Board.");
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
        public void BoardCanvas_HasMinefieldRuleImageAndResultAnchors()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoardCanvasPrefab);
            Assert.That(prefab, Is.Not.Null);

            var ruleImage = FindDescendant(prefab.transform, "MinigameRuleImage");
            Assert.That(ruleImage, Is.Not.Null);
            Assert.That(ruleImage.GetComponent<Image>(), Is.Not.Null);
            Assert.That(ruleImage.GetComponent<Image>().preserveAspect, Is.True);
            Assert.That(
                FindDescendant(prefab.transform, "MinigameRulePlaceholderText")
                    ?.GetComponent<Text>(),
                Is.Not.Null);
            Assert.That(
                FindDescendant(prefab.transform, "MinigameReadyStatus")
                    ?.GetComponent<Text>(),
                Is.Not.Null);
            Assert.That(
                FindDescendant(prefab.transform, "MinefieldResultSummary")
                    ?.GetComponent<Text>(),
                Is.Not.Null);
        }

        [Test]
        public void NetworkState_KeepsAuthoritativeMineLayoutServerOnly()
        {
            const BindingFlags PrivateInstance =
                BindingFlags.Instance | BindingFlags.NonPublic;
            var stateType = typeof(NetworkMinefieldState);

            Assert.That(
                stateType.GetField("_serverSeed", PrivateInstance)?.FieldType,
                Is.EqualTo(typeof(ulong)),
                "The layout seed must not be a client-readable NetworkVariable.");
            Assert.That(
                stateType.GetField("_detonatedMineMask", PrivateInstance)?.FieldType,
                Is.EqualTo(typeof(uint)),
                "The authoritative mine mask must remain server-local.");
            Assert.That(
                stateType.GetField("_revealedMinePositions", PrivateInstance)?.FieldType,
                Is.EqualTo(typeof(NetworkList<Vector3>)),
                "Only sonar-authorized mine positions should be replicated.");
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
