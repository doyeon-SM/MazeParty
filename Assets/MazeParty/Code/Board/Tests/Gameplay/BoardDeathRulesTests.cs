using System.Collections.Generic;
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
        public void DeathRewards_UseKillerThirtyPercentOrFixedSixWithoutPayingVictim()
        {
            foreach (var gold in new[] { 0, 3, 6, 10, 99 })
            {
                Assert.That(BoardDeathRules.GoldLoss(gold, true), Is.EqualTo(gold * 30 / 100));
                Assert.That(BoardDeathRules.GoldLoss(gold, false), Is.EqualTo(System.Math.Min(gold, 6)));
                for (var victim = 0; victim < 4; victim++)
                {
                    var killer = (victim + 1) % 4;
                    for (var recipient = 0; recipient < 4; recipient++)
                    {
                        Assert.That(BoardDeathRules.GoldReward(gold, victim, killer, recipient),
                            Is.EqualTo(recipient == killer ? gold * 30 / 100 : 0));
                        foreach (var uncredited in new[] { -1, victim })
                            Assert.That(BoardDeathRules.GoldReward(gold, victim, uncredited, recipient),
                                Is.EqualTo(recipient == victim ? 0 : 2));
                    }
                }
            }
        }

        [Test]
        public void GoldFlight_EndsExactlyAtLastArrivalAndTracksMovingRecipientThroughWalls()
        {
            foreach (var amount in new[] { 1, 2, 30, 200 })
            {
                var duration = BoardDeathGoldPresentationRules.Duration(amount);
                Assert.That(BoardDeathGoldPresentationRules.IsComplete(amount, duration - 0.000001d), Is.False);
                Assert.That(BoardDeathGoldPresentationRules.IsComplete(amount, duration), Is.True);
                var previous = 0d;
                for (var index = 0; index < amount; index++)
                {
                    var arrival = BoardDeathGoldPresentationRules.ArrivalTime(index, amount);
                    Assert.That(arrival, Is.GreaterThanOrEqualTo(previous));
                    var destination = new Vector3(10f + index, 1f, 8f);
                    Assert.That(BoardDeathGoldPresentationRules.Position(
                        Vector3.zero, destination, index, amount, arrival), Is.EqualTo(destination));
                    var moved = destination + Vector3.right * 5f;
                    var before = BoardDeathGoldPresentationRules.Position(
                        Vector3.zero, destination, index, amount, arrival - 0.2d);
                    var after = BoardDeathGoldPresentationRules.Position(
                        Vector3.zero, moved, index, amount, arrival - 0.2d);
                    Assert.That(after.x, Is.GreaterThan(before.x));
                    previous = arrival;
                }
            }
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
