using System.Collections.Generic;
using System.Linq;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
        private const string LandingFeedbackPrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardLandingEffectFeedback.prefab";
        private const string LightningPrefabPath =
            "Assets/MazeParty/Prefabs/Common/VFX/LightningStrike.prefab";

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
                (102.999d, BoardResourceTransferPhase.Result, 0f),
                (103d, BoardResourceTransferPhase.Source, 0f),
                (103.4d, BoardResourceTransferPhase.Source, 0f),
                (104.999d, BoardResourceTransferPhase.Source, 0.99f),
                (105d, BoardResourceTransferPhase.Destination, 0f),
                (105.4d, BoardResourceTransferPhase.Destination, 0f),
                (106.999d, BoardResourceTransferPhase.Destination, 0.99f),
                (107d, BoardResourceTransferPhase.Complete, 1f)
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
        public void LandingEffectFeedback_UsesSignedMapContentForExactlyOneSecond()
        {
            var previousLanguage = GameText.Language;
            GameText.SetLanguage(GameLanguage.English);
            try
            {
                var cases = new[]
                {
                    (BoardLandingEffectFeedbackKind.Gold, 3,
                        PrototypeItemId.None, BoardMapIconKind.GoldGain, "+3"),
                    (BoardLandingEffectFeedbackKind.Gold, -3,
                        PrototypeItemId.None, BoardMapIconKind.GoldLoss, "-3"),
                    (BoardLandingEffectFeedbackKind.Health, 20,
                        PrototypeItemId.None, BoardMapIconKind.Healing, "+20"),
                    (BoardLandingEffectFeedbackKind.Health, -40,
                        PrototypeItemId.None, BoardMapIconKind.Healing, "-40"),
                    (BoardLandingEffectFeedbackKind.Item, 0,
                        PrototypeItemId.Pistol, BoardMapIconKind.Item,
                        "+ " + GameText.T(
                            PrototypeItemCatalog.Get(
                                PrototypeItemId.Pistol).DisplayName))
                };

                for (var index = 0; index < cases.Length; index++)
                {
                    var testCase = cases[index];
                    var snapshot = new BoardLandingEffectFeedbackSnapshot
                    {
                        Active = true,
                        Revision = index + 1,
                        Slot = index % MultiplayerConstants.MaxPlayers,
                        Kind = (byte)testCase.Item1,
                        SignedAmount = testCase.Item2,
                        ItemId = (byte)testCase.Item3,
                        StartedAt = 100d
                    };
                    Assert.That(
                        BoardLandingEffectFeedbackRules.TryGetContent(
                            snapshot,
                            out var iconKind,
                            out var label),
                        Is.True);
                    Assert.That(iconKind, Is.EqualTo(testCase.Item4));
                    Assert.That(label, Is.EqualTo(testCase.Item5));
                }

                var exactWindow = new BoardLandingEffectFeedbackSnapshot
                {
                    Active = true,
                    Revision = 9,
                    Slot = 0,
                    Kind = (byte)BoardLandingEffectFeedbackKind.Event,
                    StartedAt = 100d
                };
                Assert.That(
                    BoardLandingEffectFeedbackRules.IsVisible(
                        exactWindow,
                        100.999999d),
                    Is.True);
                Assert.That(
                    BoardLandingEffectFeedbackRules.IsVisible(
                        exactWindow,
                        101d),
                    Is.False);
                Assert.That(
                    BoardLandingEffectFeedbackRules.GetRemainingSeconds(
                        exactWindow,
                        100d),
                    Is.EqualTo(1d));
                Assert.That(
                    BoardLandingEffectFeedbackRules.TryGetContent(
                        exactWindow,
                        out _,
                        out _),
                    Is.False,
                    "Events use one LightningStrike instead of overhead text.");
            }
            finally
            {
                GameText.SetLanguage(previousLanguage);
            }
        }

        [Test]
        public void BoardScene_UsesAuthoredResourceAndLandingFeedbackPrefabs()
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

            var feedbackPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                LandingFeedbackPrefabPath);
            Assert.That(feedbackPrefab, Is.Not.Null, LandingFeedbackPrefabPath);
            var feedback = feedbackPrefab.GetComponent<
                BoardLandingEffectFeedbackView>();
            Assert.That(feedback, Is.Not.Null);
            Assert.That(feedback.HasRequiredReferences, Is.True);
            Assert.That(
                feedbackPrefab.GetComponent<Canvas>().renderMode,
                Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(
                feedbackPrefab.GetComponentsInChildren<Text>(true)
                    .All(text => text.fontStyle == FontStyle.Normal),
                Is.True);
            var lightningPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                LightningPrefabPath);
            Assert.That(lightningPrefab, Is.Not.Null, LightningPrefabPath);

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
                Assert.That(
                    serialized.FindProperty("landingEffectFeedbackPrefab")
                        .objectReferenceValue,
                    Is.SameAs(feedback));
                Assert.That(
                    serialized.FindProperty("landingEventLightningPrefab")
                        .objectReferenceValue,
                    Is.SameAs(lightningPrefab));
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
