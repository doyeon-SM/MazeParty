using System;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardBombardmentRulesTests
    {
        [Test]
        public void DamageBands_IncludeEachBoundary_AndRejectOutsideOrNonfiniteDistances()
        {
            var distances = new[] { -1f, 0f, 3f, 3.001f, 7f, 7.001f, 10f, 10.001f, float.NaN, float.PositiveInfinity };
            var damage = new[] { 0, 80, 80, 50, 50, 20, 20, 0, 0, 0 };
            for (int i = 0; i < distances.Length; i++)
                Assert.That(BoardBombardmentRules.DamageAtDistance(distances[i]), Is.EqualTo(damage[i]), distances[i].ToString());
        }

        [Test]
        public void Salvo_WaitsOneSecond_ThenHalfSeconds_PausesAndFiresDuplicatesExactlyOnce()
        {
            var point = new Vector3(3, .06f, 2);
            var input = new[] { point, point, point };
            var salvo = new BoardBombardmentSequence(input, 10d);
            input[0] = Vector3.one * 100; // Snapshot must own its confirmed coordinates.
            salvo.Tick(10.999d, false);
            Assert.That(salvo.TryTakeDue(out _), Is.False);
            salvo.Tick(11d, false);
            Assert.That(salvo.TryTakeDue(out var first), Is.True);
            Assert.That(first, Is.EqualTo(point));
            Assert.That(salvo.TryTakeDue(out _), Is.False);
            salvo.Tick(20d, true);
            salvo.Tick(20.499d, false);
            Assert.That(salvo.TryTakeDue(out _), Is.False);
            salvo.Tick(20.5d, false);
            Assert.That(salvo.TryTakeDue(out var second), Is.True);
            Assert.That(second, Is.EqualTo(point));
            salvo.Tick(21d, false);
            Assert.That(salvo.TryTakeDue(out var third), Is.True);
            Assert.That(third, Is.EqualTo(point));
            Assert.That(salvo.Complete, Is.True);
            salvo.Tick(50d, false);
            Assert.That(salvo.TryTakeDue(out _), Is.False);

            var catchup = new BoardBombardmentSequence(new[] { point, point, point }, 0d);
            catchup.Tick(2d, false);
            var count = 0;
            while (catchup.TryTakeDue(out _)) count++;
            Assert.That(count, Is.EqualTo(3));
            Assert.Throws<ArgumentException>(() => new BoardBombardmentSequence(new[] { point, point }, 0d));
            Assert.Throws<ArgumentException>(() => new BoardBombardmentSequence(new[] { point, point, new Vector3(float.NaN, 0, 0) }, 0d));
        }

        [Test]
        public void Targeting_RecastsGround_RejectsWallsVoidAndOffBoard()
        {
            var root = new GameObject("Bombardment surface fixture");
            root.transform.position = new Vector3(2000, 0, 2000);
            try
            {
                var tile = root.AddComponent<BoardTile>();
                tile.Configure(Vector2Int.zero, BoardTileType.Normal);
                var topology = root.AddComponent<BoardTopology>();
                topology.Configure(new[] { tile }, Array.Empty<BoardGate>());
                var floor = new GameObject("Floor");
                floor.transform.SetParent(root.transform, false);
                var collider = floor.AddComponent<BoxCollider>();
                collider.center = Vector3.down * .1f;
                collider.size = new Vector3(6, .2f, 6);
                Physics.SyncTransforms();
                var center = tile.WorldCenter;
                Assert.That(BoardBombardmentRules.TryGetGroundPosition(topology, center + Vector3.up * 20, out var point), Is.True);
                Assert.That(point.y, Is.EqualTo(.06f).Within(.001f));
                Assert.That(BoardBombardmentRules.TryGetGroundPosition(topology, center + Vector3.right * 20, out _), Is.False);
                Assert.That(BoardBombardmentRules.TryGetGroundPosition(topology, new Vector3(float.NaN, 0, 0), out _), Is.False);
                collider.enabled = false;
                Physics.SyncTransforms();
                Assert.That(BoardBombardmentRules.TryGetGroundPosition(topology, center, out _), Is.False);
                collider.enabled = true;
                collider.center = Vector3.up * 2f;
                collider.size = new Vector3(1, 4, 1);
                Physics.SyncTransforms();
                Assert.That(BoardBombardmentRules.TryGetGroundPosition(topology, center, out _), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
