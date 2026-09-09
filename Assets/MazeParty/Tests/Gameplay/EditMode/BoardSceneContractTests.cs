using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardSceneContractTests
    {
        private const string BoardScenePath = "Assets/MazeParty/Scenes/Board.unity";

        [Test]
        public void GeneratedBoardScene_MatchesPrototypeTopologyContract()
        {
            var scene = SceneManager.GetSceneByPath(BoardScenePath);
            var wasAlreadyLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasAlreadyLoaded)
            {
                scene = EditorSceneManager.OpenScene(BoardScenePath, OpenSceneMode.Additive);
            }

            try
            {
                var topology = FindTopology(scene);
                Assert.That(topology, Is.Not.Null, "Board scene must contain one BoardTopology.");
                topology.RebuildIndex();

                var cameraDirector = scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<GameplayCameraDirector>(true))
                    .SingleOrDefault();
                Assert.That(cameraDirector, Is.Not.Null);
                Assert.That(
                    cameraDirector.SmoothCameraTransitions,
                    Is.False,
                    "Build cameras must cut immediately to avoid motion-sickness-inducing blends.");

                var tiles = topology.Tiles.Where(tile => tile != null).ToArray();
                var gates = topology.Gates.Where(gate => gate != null).ToArray();
                var starts = tiles.Where(tile => tile.TileType == BoardTileType.Start).ToArray();

                Assert.That(tiles, Has.Length.EqualTo(32));
                Assert.That(gates, Has.Length.EqualTo(36));
                Assert.That(starts, Has.Length.EqualTo(4));
                Assert.That(
                    starts.Select(tile => tile.Coordinate),
                    Is.EquivalentTo(new[]
                    {
                        new Vector2Int(1, 1),
                        new Vector2Int(5, 1),
                        new Vector2Int(5, 5),
                        new Vector2Int(1, 5)
                    }));
                Assert.That(tiles.Min(tile => tile.Coordinate.x), Is.EqualTo(0));
                Assert.That(tiles.Max(tile => tile.Coordinate.x), Is.EqualTo(6));
                Assert.That(tiles.Min(tile => tile.Coordinate.y), Is.EqualTo(0));
                Assert.That(tiles.Max(tile => tile.Coordinate.y), Is.EqualTo(6));
                Assert.That(
                    gates.All(gate => Mathf.Approximately(gate.GateWidth, BoardTile.RoomSize)),
                    Is.True,
                    "A blue reusable boundary opens the complete side of its room.");
                Assert.That(
                    tiles.Count(tile => tile.TileType == BoardTileType.KeyShop),
                    Is.Zero,
                    "The unique Key Shop is placed dynamically at turn-two overview.");

                var validation = topology.ValidateTopology();
                Assert.That(
                    validation.IsValid,
                    Is.True,
                    string.Join(Environment.NewLine, validation.Issues.Select(issue => issue.Message)));

                foreach (var tile in tiles)
                {
                    Assert.That(
                        topology.GetOutgoingGates(tile),
                        Is.Not.Empty,
                        "Every room must retain at least one directed exit: " + tile.Coordinate);
                }

                foreach (var start in starts)
                {
                    Assert.That(
                        CountReachableTiles(topology, start),
                        Is.EqualTo(tiles.Length),
                        "Every start must be able to reach the full directed board: " + start.Coordinate);
                }
            }
            finally
            {
                if (!wasAlreadyLoaded && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static BoardTopology FindTopology(Scene scene)
        {
            BoardTopology found = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var candidate = root.GetComponentInChildren<BoardTopology>(true);
                if (candidate == null)
                {
                    continue;
                }

                Assert.That(found, Is.Null, "Board scene contains more than one BoardTopology.");
                found = candidate;
            }

            return found;
        }

        private static int CountReachableTiles(BoardTopology topology, BoardTile start)
        {
            var visited = new HashSet<BoardTile> { start };
            var pending = new Queue<BoardTile>();
            pending.Enqueue(start);

            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                var outgoing = topology.GetOutgoingGates(current);
                for (var i = 0; i < outgoing.Count; i++)
                {
                    var destination = outgoing[i].Destination;
                    if (destination != null && visited.Add(destination))
                    {
                        pending.Enqueue(destination);
                    }
                }
            }

            return visited.Count;
        }
    }
}
