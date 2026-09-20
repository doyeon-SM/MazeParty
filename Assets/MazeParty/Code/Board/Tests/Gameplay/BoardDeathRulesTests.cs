using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardDeathRulesTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var instance in _objects)
            {
                Object.DestroyImmediate(instance);
            }
            _objects.Clear();
        }

        [Test]
        public void GoldDropAndRespawn_UseFloorWorldDistanceAndCoordinateTieBreak()
        {
            Assert.That(BoardDeathRules.DroppedGold(0), Is.Zero);
            Assert.That(BoardDeathRules.DroppedGold(3), Is.Zero);
            Assert.That(BoardDeathRules.DroppedGold(10), Is.EqualTo(3));
            Assert.That(BoardDeathRules.DroppedGold(99), Is.EqualTo(29));

            var left = Create(new Vector2Int(0, 2), new Vector3(-2f, 0f, 0f));
            var right = Create(new Vector2Int(2, 2), new Vector3(2f, 0f, 0f));
            var elevated = Create(new Vector2Int(1, 0), new Vector3(0f, 6f, 0f));
            var tiles = new[] { right, elevated, left };
            Assert.That(BoardDeathRules.NearestRespawn(tiles, Vector3.zero), Is.SameAs(left));
            Assert.That(BoardDeathRules.NearestRespawn(tiles, new Vector3(1.5f, 0f, 0f)),
                Is.SameAs(right));
        }

        [Test]
        public void CoincidentTombstones_ReceiveDistinctAimableWorldPositions()
        {
            var positions = Enumerable.Range(0, 16)
                .Select(index => BoardTombstoneRules.DisplayPosition(Vector3.zero, index))
                .ToArray();
            Assert.That(positions.Distinct().Count(), Is.EqualTo(positions.Length));
            Assert.That(positions[0], Is.EqualTo(Vector3.zero));
        }

        private BoardTile Create(Vector2Int coordinate, Vector3 position)
        {
            var instance = new GameObject("Respawn " + coordinate);
            _objects.Add(instance);
            instance.transform.position = position;
            var tile = instance.AddComponent<BoardTile>();
            tile.Configure(coordinate, BoardTileType.Respawn);
            return tile;
        }
    }
}
