using System.Collections.Generic;
using System.Linq;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardResourceTransferPresentationTests
    {
        private const string BoardScenePath =
            "Assets/MazeParty/Scenes/Board/Board.unity";
        private const string CoinSourcePath =
            "Assets/Ignore/BTM_Assets/BTM_Items_Gems/Prefabs/Coin.prefab";
        private const string KeySourcePath =
            "Assets/Ignore/BTM_Assets/BTM_Items_Gems/Prefabs/Key.prefab";
        private const string CoinPrefabPath =
            "Assets/MazeParty/Prefabs/Board/World/BoardEventCoin.prefab";
        private const string KeyPrefabPath =
            "Assets/MazeParty/Prefabs/Board/World/BoardEventKey.prefab";

        [Test]
        public void TransferTimeline_UsesResultThenSourceThenDestinationAtExactBoundaries()
        {
            var snapshot = new BoardResourceTransferSnapshot
            {
                Active = true,
                Revision = 7,
                SourceSlot = 1,
                DestinationSlot = 3,
                Resource = (byte)BoardSpecialEventResource.Gold,
                Amount = 20,
                StartedAt = 100d
            };
            var cases = new[]
            {
                (99.5d, BoardResourceTransferPhase.Result, 0f),
                (101.999d, BoardResourceTransferPhase.Result, 0f),
                (102d, BoardResourceTransferPhase.Source, 0f),
                (102.4d, BoardResourceTransferPhase.Source, 0f),
                (103.999d, BoardResourceTransferPhase.Source, 0.99f),
                (104d, BoardResourceTransferPhase.Destination, 0f),
                (104.4d, BoardResourceTransferPhase.Destination, 0f),
                (105.999d, BoardResourceTransferPhase.Destination, 0.99f),
                (106d, BoardResourceTransferPhase.Complete, 1f)
            };

            foreach (var testCase in cases)
            {
                var phase = BoardResourceTransferPresentationRules.GetPhase(
                    snapshot,
                    testCase.Item1,
                    out _,
                    out var motionProgress);
                Assert.That(phase, Is.EqualTo(testCase.Item2),
                    "time=" + testCase.Item1);
                Assert.That(motionProgress, Is.GreaterThanOrEqualTo(testCase.Item3),
                    "time=" + testCase.Item1);
            }

            snapshot.Active = false;
            Assert.That(
                BoardResourceTransferPresentationRules.GetPhase(
                    snapshot,
                    100d,
                    out _,
                    out _),
                Is.EqualTo(BoardResourceTransferPhase.None));

            snapshot.Active = true;
            snapshot.Amount = 0;
            Assert.That(
                BoardResourceTransferPresentationRules.GetPhase(
                    snapshot,
                    103d,
                    out _,
                    out _),
                Is.EqualTo(BoardResourceTransferPhase.None),
                "A zero-value result must never create a resource or camera shot.");
        }

        [Test]
        public void BoardScene_UsesAuthoredCoinAndKeyWrappersWithoutGameplayComponents()
        {
            var expected = new[]
            {
                (CoinPrefabPath, CoinSourcePath),
                (KeyPrefabPath, KeySourcePath)
            };
            var prefabs = new List<GameObject>();
            foreach (var pair in expected)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pair.Item1);
                Assert.That(prefab, Is.Not.Null, pair.Item1);
                prefabs.Add(prefab);
                Assert.That(
                    AssetDatabase.GetDependencies(pair.Item1, true),
                    Does.Contain(pair.Item2),
                    pair.Item1);
                Assert.That(
                    prefab.GetComponentsInChildren<Collider>(true),
                    Is.Empty,
                    pair.Item1);
                Assert.That(
                    prefab.GetComponentsInChildren<NetworkObject>(true),
                    Is.Empty,
                    pair.Item1);
                Assert.That(
                    prefab.GetComponentsInChildren<MonoBehaviour>(true)
                        .Where(component => component != null),
                    Is.All.Matches<MonoBehaviour>(component => !component.enabled),
                    "The vendor floating script must not fight the authored transfer motion.");
            }

            var previousActiveScene = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(BoardScenePath);
            var wasAlreadyLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasAlreadyLoaded)
            {
                scene = EditorSceneManager.OpenScene(
                    BoardScenePath,
                    OpenSceneMode.Additive);
            }
            SceneManager.SetActiveScene(scene);
            try
            {
                var presenter = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<
                        BoardFlowCameraPresenter>(true))
                    .Single();
                var serialized = new SerializedObject(presenter);
                Assert.That(
                    serialized.FindProperty("resourceTransferCoinPrefab")
                        .objectReferenceValue,
                    Is.SameAs(prefabs[0]));
                Assert.That(
                    serialized.FindProperty("resourceTransferKeyPrefab")
                        .objectReferenceValue,
                    Is.SameAs(prefabs[1]));
            }
            finally
            {
                if (previousActiveScene.IsValid() &&
                    previousActiveScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActiveScene);
                }
                if (!wasAlreadyLoaded && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
