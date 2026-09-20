using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardLandingEffectTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (var index = _objects.Count - 1; index >= 0; index--)
                Object.DestroyImmediate(_objects[index]);
            _objects.Clear();
        }

        [Test]
        public void LandingLayoutAndEconomy_AreSeededAndRespectRewardBoundaries()
        {
            var tiles = new List<BoardTile>();
            for (var index = 0; index < 10; index++)
            {
                var type = index == 0
                    ? BoardTileType.Start
                    : BoardTileType.Normal;
                tiles.Add(CreateTile(new Vector2Int(index, 0), type));
            }

            var respawn = CreateTile(new Vector2Int(10, 0), BoardTileType.Respawn);
            tiles.Add(respawn);
            var first = BoardLandingEffectLayout.Create(tiles, 12345);
            tiles.Reverse();
            var repeated = BoardLandingEffectLayout.Create(tiles, 12345);

            Assert.That(first.EligibleCount, Is.EqualTo(10));
            Assert.That(
                new[] { first.GainCount, first.LossCount, first.ItemRewardCount, first.HealingCount },
                Is.EqualTo(new[] { 5, 3, 1, 1 }));
            Assert.That(first.TryGetEffect(respawn.Coordinate, out _), Is.False);
            foreach (var pair in first.Effects)
                Assert.That(repeated.Effects[pair.Key], Is.EqualTo(pair.Value));

            Assert.That(PlayerStatRules.ApplyGoldDelta(2, -3), Is.Zero);
            Assert.That(PlayerStatRules.CanPurchaseKey(19), Is.False);
            Assert.That(PlayerStatRules.CanPurchaseKey(20), Is.True);
            Assert.That(PlayerStatRules.ClampHealth(130, 100), Is.EqualTo(100));
        }

        private BoardTile CreateTile(Vector2Int coordinate, BoardTileType type)
        {
            var gameObject = new GameObject("Tile " + coordinate);
            _objects.Add(gameObject);
            var tile = gameObject.AddComponent<BoardTile>();
            tile.Configure(coordinate, type);
            return tile;
        }
    }
}
