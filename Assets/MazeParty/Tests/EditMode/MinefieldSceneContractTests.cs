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
        public void Minefield_UsesThreeColumnsAndTwentyContinuousMines()
        {
            Assert.That(NetworkMinefieldState.GridWidth, Is.EqualTo(3));
            Assert.That(NetworkMinefieldState.MineCount, Is.EqualTo(20));

            var first = NetworkMinefieldState.GenerateMineWorldPositions(
                0x123456789ABCDEF0UL,
                1);
            var second = NetworkMinefieldState.GenerateMineWorldPositions(
                0x123456789ABCDEF0UL,
                1);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first.Length, Is.EqualTo(20));

            var cellWidth = (NetworkMinefieldState.ArenaMaxX -
                             NetworkMinefieldState.ArenaMinX) /
                            NetworkMinefieldState.GridWidth;
            var cellDepth = (NetworkMinefieldState.ArenaMaxZ -
                             NetworkMinefieldState.ArenaMinZ) /
                            NetworkMinefieldState.GridHeight;
            var occupiedCells = first.Select(position => new Vector2Int(
                Mathf.FloorToInt((position.x - NetworkMinefieldState.ArenaMinX) /
                                 cellWidth),
                Mathf.FloorToInt((position.z - NetworkMinefieldState.ArenaMinZ) /
                                 cellDepth))).ToArray();

            Assert.That(occupiedCells.Distinct().Count(), Is.LessThan(first.Length),
                "Continuous placement must permit more than one mine in a logical cell.");
            Assert.That(first.Any(position =>
                Mathf.Abs((position.x - NetworkMinefieldState.ArenaMinX) /
                          cellWidth - 0.5f -
                          Mathf.Floor((position.x - NetworkMinefieldState.ArenaMinX) /
                                      cellWidth)) > 0.01f),
                Is.True,
                "Mines must not be locked to cell centers.");

            for (var index = 0; index < first.Length; index++)
            {
                Assert.That(first[index].x, Is.InRange(
                    NetworkMinefieldState.ArenaMinX +
                    NetworkMinefieldState.MineSpawnHorizontalPadding,
                    NetworkMinefieldState.ArenaMaxX -
                    NetworkMinefieldState.MineSpawnHorizontalPadding));
                Assert.That(first[index].z, Is.InRange(
                    NetworkMinefieldState.ArenaMinZ +
                    NetworkMinefieldState.MineSafeZoneDepth,
                    NetworkMinefieldState.ArenaMaxZ -
                    NetworkMinefieldState.MineSafeZoneDepth));
                for (var other = index + 1; other < first.Length; other++)
                {
                    Assert.That(Vector3.Distance(first[index], first[other]),
                        Is.GreaterThanOrEqualTo(
                            NetworkMinefieldState.MinimumMineSpacing - 0.0001f));
                }
            }
        }

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

        [Test]
        public void PlayerCamera_IsLocalTopViewTiltedTenDegreesFromVertical()
        {
            var focus = new Vector3(12f, 0f, -5f);
            var position = MinefieldNetworkView.CalculatePlayerCameraPosition(focus);
            var rotation = MinefieldNetworkView.PlayerCameraRotation;
            var forward = rotation * Vector3.forward;

            Assert.That(position.y, Is.EqualTo(
                focus.y + MinefieldNetworkView.PlayerCameraHeight).Within(0.001f));
            Assert.That(position.z, Is.LessThan(focus.z));
            Assert.That(
                Vector3.Angle(forward, Vector3.down),
                Is.EqualTo(MinefieldNetworkView.PlayerCameraTiltDegrees).Within(0.001f));
            Assert.That(
                MinefieldNetworkView.PlayerCameraOrthographicSize,
                Is.LessThan(12f),
                "Each client should frame its own runner rather than the full course.");
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
