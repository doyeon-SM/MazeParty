using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardCommerceRulesTests
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
        public void ItemShopStock_HasFiveSingleCopyOffersAndDeterministicSeed()
        {
            var first = new ItemShopStock(1250);
            var second = new ItemShopStock(1250);
            for (var i = 0; i < ItemShopRules.OfferCount; i++)
            {
                Assert.That(PrototypeItemCatalog.IsValid(first.GetOffer(i)), Is.True);
                Assert.That(second.GetOffer(i), Is.EqualTo(first.GetOffer(i)));
                Assert.That(first.IsSold(i), Is.False);
                Assert.That(first.TrySell(i), Is.True);
                Assert.That(first.TrySell(i), Is.False, "An offer has exactly one shared copy.");
            }

            Assert.That(first.IsSoldOut, Is.True);
        }

        [Test]
        public void PrototypePrices_MatchTestEconomy()
        {
            Assert.That(PrototypeItemCatalog.Get(PrototypeItemId.PulseBlaster).Price, Is.EqualTo(7));
            Assert.That(PrototypeItemCatalog.Get(PrototypeItemId.PushMine).Price, Is.EqualTo(5));
            Assert.That(PrototypeItemCatalog.Get(PrototypeItemId.MedKit).Price, Is.EqualTo(5));
        }

        [Test]
        public void ItemShopPlacement_ExcludesRespawnOccupiedReservedAndPreviousTiles()
        {
            var previous = CreateTile(new Vector2Int(0, 0), BoardTileType.Normal);
            var occupied = CreateTile(new Vector2Int(1, 0), BoardTileType.Normal);
            var reserved = CreateTile(new Vector2Int(2, 0), BoardTileType.Start);
            var respawn = CreateTile(new Vector2Int(3, 0), BoardTileType.Respawn);
            var valid = CreateTile(new Vector2Int(4, 0), BoardTileType.Normal);

            var selected = ItemShopPlacementPolicy.TryChoose(
                new[] { previous, occupied, reserved, respawn, valid },
                new[] { occupied.Coordinate },
                new[] { reserved.Coordinate },
                new FixedRandomSource(),
                out var selectedTile,
                true,
                previous.Coordinate);

            Assert.That(selected, Is.True);
            Assert.That(selectedTile, Is.SameAs(valid));
        }

        [Test]
        public void Ranking_UsesCompetitionRanksForExactTies()
        {
            var ranks = PlayerRankingRules.Calculate(new[]
            {
                new PlayerRankingStats(2, 10, 0),
                new PlayerRankingStats(2, 10, 0),
                new PlayerRankingStats(1, 100, 9),
                new PlayerRankingStats(0, 500, 20)
            });

            CollectionAssert.AreEqual(new[] { 1, 1, 3, 4 }, ranks);
        }

        [Test]
        public void Ranking_UsesKeysThenGoldThenMinigameWins()
        {
            var ranks = PlayerRankingRules.Calculate(new[]
            {
                new PlayerRankingStats(1, 0, 0),
                new PlayerRankingStats(0, 100, 100),
                new PlayerRankingStats(0, 10, 5),
                new PlayerRankingStats(0, 10, 1)
            });

            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, ranks);
        }

        [Test]
        public void HealingAmount_IsFiftyAndHealthStillClampsToMaximum()
        {
            Assert.That(BoardLandingEffectLayout.HealingAmount, Is.EqualTo(50));
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

        private sealed class FixedRandomSource : IKeyShopRandomSource
        {
            public int NextIndex(int exclusiveMaximum)
            {
                return 0;
            }
        }
    }
}
