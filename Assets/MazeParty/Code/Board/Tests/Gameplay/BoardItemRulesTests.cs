using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardItemRulesTests
    {
        [TestCase(0, 12, 1, 12, 24)]
        [TestCase(1, 1, 0, 1, 2)]
        [TestCase(0, 3, 1, 8, 11)]
        public void TwoDice_WaitForBoth_RejectDuplicates(int firstIndex, int firstFace, int secondIndex, int secondFace, int sum)
        {
            Assert.That(BoardDiceProgress.TrySettle(true, 0, 0, firstIndex, firstFace, out var a, out var b, out var total), Is.True);
            Assert.That(total, Is.Zero);
            Assert.That(BoardDiceProgress.TrySettle(true, a, b, firstIndex, 6, out _, out _, out _), Is.False);
            Assert.That(BoardDiceProgress.TrySettle(true, a, b, secondIndex, secondFace, out a, out b, out total), Is.True);
            Assert.That(total, Is.EqualTo(sum));
            Assert.That(BoardDiceProgress.TrySettle(true, a, b, secondIndex, 6, out _, out _, out _), Is.False);
        }
        [TestCase(true, 0, 0, -1)]
        [TestCase(true, 5, 0, 1)]
        [TestCase(true, 0, 5, 0)]
        [TestCase(true, 5, 5, -1)]
        [TestCase(false, 0, 0, -1)]
        public void Timeout_OnlyCompletesOneMissingDie(bool doubled, int a, int b, int missing)
        { Assert.That(BoardDiceProgress.MissingDieOnTimeout(doubled, a, b), Is.EqualTo(missing)); }

        [Test]
        public void ItemCasts_PassBoardBarriers_StopAtOrdinaryWall_AndOccludeBlast()
        {
            var root = new GameObject("Physics contract");
            try
            {
                var barrier = GameObject.CreatePrimitive(PrimitiveType.Cube);
                barrier.transform.SetParent(root.transform); barrier.transform.position = new Vector3(0, 100, 2);
                barrier.AddComponent<BoardBoundaryWallVisual>();
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.SetParent(root.transform); wall.transform.position = new Vector3(0, 100, 4);
                Physics.SyncTransforms();
                var origin = new Vector3(0, 100, 0);
                Assert.That(BoardItemPhysics.Cast(origin, Vector3.forward, 10, null, out var hit), Is.True);
                Assert.That(hit.collider.gameObject, Is.EqualTo(wall));
                Assert.That(BoardItemPhysics.HasBlastLineOfSight(origin, new Vector3(0, 100, 6)), Is.False);
                var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                player.transform.SetParent(root.transform);
                player.transform.position = new Vector3(0, 100, 3);
                player.AddComponent<PlayerHitZoneOwner>();
                player.AddComponent<PlayerBoardBoundaryWalls>();
                Physics.SyncTransforms();
                Assert.That(BoardItemPhysics.Cast(origin, Vector3.forward, 10, null, out hit), Is.True);
                Assert.That(hit.collider.gameObject, Is.EqualTo(player), "A player's wall controller must not make the player bulletproof.");
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(wall);
                Physics.SyncTransforms();
                Assert.That(BoardItemPhysics.HasBlastLineOfSight(origin, new Vector3(0, 100, 6)), Is.True);
                Assert.That(BoardItemPhysics.Cast(origin, Vector3.forward, 10, null, out _, .12f), Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
