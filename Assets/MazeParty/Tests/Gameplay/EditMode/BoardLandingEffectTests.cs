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
            for (var i = _objects.Count - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(_objects[i]);
            }
            _objects.Clear();
        }

        [Test]
        public void Layout_AssignsExactFiveThreeOneOneRatio_AndExcludesRespawnTiles()
        {
            var tiles = new List<BoardTile>();
            for (var i = 0; i < 10; i++)
            {
                var type = i == 0
                    ? BoardTileType.Start
                    : i == 1 ? BoardTileType.KeyShop : BoardTileType.Normal;
                tiles.Add(CreateTile(new Vector2Int(i, 0), type));
            }
            var respawn = CreateTile(new Vector2Int(10, 0), BoardTileType.Respawn);
            tiles.Add(respawn);

            var layout = BoardLandingEffectLayout.Create(tiles, 12345);

            Assert.That(layout.EligibleCount, Is.EqualTo(10));
            Assert.That(layout.GainCount, Is.EqualTo(5));
            Assert.That(layout.LossCount, Is.EqualTo(3));
            Assert.That(layout.ItemRewardCount, Is.EqualTo(1));
            Assert.That(layout.HealingCount, Is.EqualTo(1));
            Assert.That(
                layout.TryGetEffect(respawn.Coordinate, out _),
                Is.False);
            Assert.That(
                layout.TryGetEffect(tiles[0].Coordinate, out var startEffect),
                Is.True,
                "A start marker is still a normal landing-effect room.");
            Assert.That(startEffect, Is.Not.EqualTo(BoardLandingEffectType.None));
        }

        [Test]
        public void Layout_SameSeedAndCoordinates_ProducesSameAssignments()
        {
            var tiles = new List<BoardTile>();
            for (var i = 0; i < 12; i++)
            {
                tiles.Add(CreateTile(new Vector2Int(i % 4, i / 4), BoardTileType.Normal));
            }

            var first = BoardLandingEffectLayout.Create(tiles, 7788);
            tiles.Reverse();
            var second = BoardLandingEffectLayout.Create(tiles, 7788);

            foreach (var pair in first.Effects)
            {
                Assert.That(second.Effects[pair.Key], Is.EqualTo(pair.Value));
            }
        }

        [Test]
        public void GoldLoss_ClampsAtZero()
        {
            Assert.That(PlayerStatRules.ApplyGoldDelta(2, -3), Is.Zero);
            Assert.That(PlayerStatRules.ApplyGoldDelta(0, -3), Is.Zero);
            Assert.That(PlayerStatRules.ApplyGoldDelta(10, 3), Is.EqualTo(13));
        }

        [Test]
        public void KeyShopPrice_RequiresTwentyGold()
        {
            Assert.That(PlayerStatRules.CanPurchaseKey(19), Is.False);
            Assert.That(PlayerStatRules.CanPurchaseKey(20), Is.True);
            Assert.That(PlayerStatRules.KeyShopGoldPrice, Is.EqualTo(20));
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
