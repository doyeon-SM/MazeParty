using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MinigameResultCanvasContractTests
    {
        private const string ResultPrefabPath =
            "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigameResultCanvas.prefab";
        private const string BoardPrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab";
        private const string BoardScenePath =
            "Assets/MazeParty/Scenes/Board/Board.unity";
        [Test]
        public void EveryMinigameResult_DisplaysAllTiedPlayersWithTheirSharedReward()
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var warning = typeof(Unity.Netcode.NetworkVariableBase).GetField("IgnoreInitializeWarning", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var previousWarning = (bool)warning.GetValue(null);
            warning.SetValue(null, true);
            var previousMatch = NetworkMatchState.Instance;
            typeof(NetworkMatchState).GetProperty("Instance").SetValue(null, null);
            var names = new[] { "Minefield", "WrongWay", "RedLightGreenLight", "StableFooting", "BalloonBlow", "GiftGrab", "TerritoryPaint", "TagChase", "Race", "SnowySpin", "ArenaCombat", "CliffBarrage", "BombPassing", "BouncingBalls", "SequenceMemory", "CoinCollect" };
            try
            {
                foreach (var name in names)
                {
                    var root = new GameObject("Result contract " + name);
                    try
                    {
                        var type = typeof(NetworkMatchState).Assembly.GetType("MazeParty.Multiplayer.Network" + name + "State");
                        var state = root.AddComponent(type);
                        var field = type.GetField("_finalRanks", flags) ?? type.GetField("_finalRanksPacked", flags);
                        System.Action<uint> setRanks;
                        if (state is NetworkCoinCollectState)
                        {
                            var players = (Unity.Netcode.NetworkList<CoinCollectPlayerNetworkSnapshot>)
                                type.GetField("_players", flags).GetValue(state);
                            setRanks = packed =>
                            {
                                players.Clear();
                                for (var slot = 0; slot < 4; slot++)
                                    players.Add(new CoinCollectPlayerNetworkSnapshot { Rank = (byte)(packed >> (slot * 8)) });
                            };
                        }
                        else
                        {
                            var ranks = (Unity.Netcode.NetworkVariable<uint>)field.GetValue(state);
                            setRanks = packed => ranks.Value = packed;
                        }
                        var method = typeof(BoardFlowView).GetMethod("Build" + name + "ResultSummary", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                        foreach (var input in new[] { new[] { 4, 2, 2, 1 }, new[] { 1, 1, 1, 1 } })
                        {
                            uint packed = 0;
                            for (var slot = 0; slot < 4; slot++) packed |= (uint)input[slot] << (slot * 8);
                            setRanks(packed);
                            var lines = ((string)method.Invoke(null, new object[] { state })).Split('\n');
                            Assert.That(lines.Length, Is.EqualTo(4), name);
                            var expected = input.OrderBy(value => value).ToArray();
                            for (var row = 0; row < 4; row++)
                            {
                                Assert.That(lines[row], Does.StartWith(expected[row] + "."), name);
                                Assert.That(lines[row], Does.Contain("+" + MazeParty.Gameplay.Minigames.MinigameRewardRules.GetFinalPlacementGold(expected[row])), name);
                            }
                        }
                        setRanks(0);
                        Assert.That((string)method.Invoke(null, new object[] { state }), Does.Not.Contain("\n"), name + " waits for a complete snapshot");
                    }
                    finally { Object.DestroyImmediate(root); }
                }
            }
            finally
            {
                warning.SetValue(null, previousWarning);
                typeof(NetworkMatchState).GetProperty("Instance").SetValue(null, previousMatch);
            }
        }

        [Test]
        public void FinalResult_HasOneDedicatedAuthoredCanvas()
        {
            var resultPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ResultPrefabPath);
            var boardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                BoardPrefabPath);
            Assert.That(resultPrefab, Is.Not.Null, ResultPrefabPath);
            Assert.That(boardPrefab, Is.Not.Null, BoardPrefabPath);

            var result = resultPrefab.GetComponent<
                MinigameResultCanvasBindings>();
            var board = boardPrefab.GetComponent<BoardCanvasBindings>();
            Assert.That(result, Is.Not.Null);
            Assert.That(result.HasRequiredReferences, Is.True);
            Assert.That(result.RootCanvas,
                Is.SameAs(resultPrefab.GetComponent<Canvas>()));
            Assert.That(result.RootCanvas.renderMode,
                Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(resultPrefab.GetComponentsInChildren<Canvas>(true),
                Has.Length.EqualTo(1));
            Assert.That(resultPrefab.GetComponent<CanvasScaler>(),
                Is.Not.Null);
            Assert.That(resultPrefab.transform.localScale,
                Is.EqualTo(Vector3.one));
            Assert.That(result.ResultPanel.activeSelf, Is.False);
            Assert.That(result.ResultPanel.name, Is.EqualTo("MinigameResultPanel"));
            Assert.That(result.ResultSummary.supportRichText, Is.True,
                "Player-color tags must render instead of appearing as text.");
            Assert.That(board, Is.Not.Null);
            Assert.That(board.HasRequiredReferences, Is.True);
            Assert.That(result.RootCanvas.sortingOrder,
                Is.GreaterThan(board.RootCanvas.sortingOrder));
            Assert.That(boardPrefab.GetComponentsInChildren<
                    MinigameResultCanvasBindings>(true),
                Is.Empty);
            // Neither the legacy (SkippedResultPanel) nor the current result
            // panel may remain inside BoardCanvas after the migration.
            Assert.That(boardPrefab.GetComponentsInChildren<Transform>(true)
                    .Any(child => child.name == "SkippedResultPanel" ||
                                  child.name == "MinigameResultPanel"),
                Is.False);
        }

        [Test]
        public void BoardScene_UsesResultPrefabRootAndSerializedPresenterBinding()
        {
            var scene = SceneManager.GetSceneByPath(BoardScenePath);
            var openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere)
            {
                scene = EditorSceneManager.OpenScene(
                    BoardScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                var roots = scene.GetRootGameObjects();
                var results = roots.SelectMany(root =>
                        root.GetComponentsInChildren<
                            MinigameResultCanvasBindings>(true))
                    .ToArray();
                Assert.That(results, Has.Length.EqualTo(1));
                var result = results[0];
                Assert.That(result.HasRequiredReferences, Is.True);
                Assert.That(result.transform.parent, Is.Null);
                Assert.That(result.transform.localScale,
                    Is.EqualTo(Vector3.one));
                Assert.That(result.ResultPanel.activeSelf, Is.False);
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        result.gameObject),
                    Is.EqualTo(ResultPrefabPath));

                var boardViews = roots.SelectMany(root =>
                        root.GetComponentsInChildren<BoardFlowView>(true))
                    .ToArray();
                Assert.That(boardViews, Has.Length.EqualTo(1));
                Assert.That(boardViews[0].ResultUiBindings,
                    Is.SameAs(result));
            }
            finally
            {
                if (openedHere)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
