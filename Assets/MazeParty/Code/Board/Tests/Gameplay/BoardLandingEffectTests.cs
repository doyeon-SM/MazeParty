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
            var eligibleTileCount = BoardLandingEffectLayout.TotalWeight * 2;
            for (var index = 0; index < eligibleTileCount; index++)
            {
                var type = index == 0
                    ? BoardTileType.Start
                    : BoardTileType.Normal;
                tiles.Add(CreateTile(new Vector2Int(index, 0), type));
            }

            var respawn = CreateTile(
                new Vector2Int(eligibleTileCount, 0),
                BoardTileType.Respawn);
            tiles.Add(respawn);
            var first = BoardLandingEffectLayout.Create(tiles, 12345);
            tiles.Reverse();
            var repeated = BoardLandingEffectLayout.Create(tiles, 12345);

            Assert.That(
                new[]
                {
                    (byte)BoardLandingEffectType.None,
                    (byte)BoardLandingEffectType.GoldGain,
                    (byte)BoardLandingEffectType.GoldLoss,
                    (byte)BoardLandingEffectType.ItemReward,
                    (byte)BoardLandingEffectType.Healing20,
                    (byte)BoardLandingEffectType.Healing10,
                    (byte)BoardLandingEffectType.Damage40,
                    (byte)BoardLandingEffectType.Damage20,
                    (byte)BoardLandingEffectType.SpecialEvent
                },
                Is.EqualTo(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }));
            Assert.That(
                first.EligibleCount,
                Is.EqualTo(eligibleTileCount));
            Assert.That(
                new[]
                {
                    first.GainCount,
                    first.LossCount,
                    first.ItemRewardCount,
                    first.Healing20Count,
                    first.Healing10Count,
                    first.Damage40Count,
                    first.Damage20Count,
                    first.SpecialEventCount
                },
                Is.EqualTo(new[] { 10, 6, 2, 2, 2, 2, 2, 2 }));
            Assert.That(first.TryGetEffect(respawn.Coordinate, out _), Is.False);
            foreach (var pair in first.Effects)
                Assert.That(repeated.Effects[pair.Key], Is.EqualTo(pair.Value));

            Assert.That(PlayerStatRules.ApplyGoldDelta(2, -3), Is.Zero);
            Assert.That(PlayerStatRules.ApplyKeyDelta(2, -3), Is.Zero);
            Assert.That(
                PlayerStatRules.ApplyKeyDelta(int.MaxValue, 1),
                Is.EqualTo(int.MaxValue));
            Assert.That(PlayerStatRules.CanPurchaseKey(19), Is.False);
            Assert.That(PlayerStatRules.CanPurchaseKey(20), Is.True);
            Assert.That(PlayerStatRules.ClampHealth(130, 100), Is.EqualTo(100));
        }

        [Test]
        public void LandingEffectDeltasAndDurations_MatchTheBoardContract()
        {
            var effects = new[]
            {
                BoardLandingEffectType.Healing20,
                BoardLandingEffectType.Healing10,
                BoardLandingEffectType.Damage40,
                BoardLandingEffectType.Damage20
            };
            var expectedHealthDeltas = new[] { 20, 10, -40, -20 };
            for (var index = 0; index < effects.Length; index++)
            {
                Assert.That(
                    BoardLandingEffectLayout.GetHealthDelta(effects[index]),
                    Is.EqualTo(expectedHealthDeltas[index]));
            }

            var turnEffects = new[]
            {
                BoardLandingEffectType.GoldGain,
                BoardLandingEffectType.SpecialEvent,
                BoardLandingEffectType.Damage20,
                BoardLandingEffectType.None
            };
            Assert.That(
                BoardLandingEffectLayout.GetTotalDurationSeconds(turnEffects),
                Is.EqualTo(7d));
            Assert.That(
                BoardLandingEffectLayout.GetTotalDurationSeconds(
                    turnEffects.Length,
                    index => turnEffects[index]),
                Is.EqualTo(7d));
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
