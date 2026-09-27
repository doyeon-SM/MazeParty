using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardItemRulesTests
    {
        [TestCase(185d, 0.02f, 0.02f)]
        [TestCase(186.0625d, 0.125f, 0.0625f)]
        [TestCase(186.125d, 0.125f, 0f)]
        public void MineSimulationDelta_StopsAtActionLogicalBoundary(
            double synchronizedNow,
            float frameDelta,
            float expectedDelta)
        {
            var flow = new BoardFlowStateMachine();
            flow.Start(0d);
            flow.Tick(6d);
            Assert.That(flow.State, Is.EqualTo(BoardFlowState.Action));

            Assert.That(
                BoardItemLifecycleRules.GetMineSimulationDelta(
                    flow,
                    synchronizedNow,
                    frameDelta),
                Is.EqualTo(expectedDelta).Within(0.000001f));
        }

        [TestCase(20.1d, 0.1f, 20.04d, 0.04f)]
        [TestCase(20.1d, 0.1f, 20d, 0f)]
        [TestCase(20.1d, 0.1f, 0d, 0.1f)]
        public void MineSimulationDelta_UsesEarlierArrivalGraceBoundary(
            double synchronizedNow,
            float frameDelta,
            double arrivalGraceDeadline,
            float expectedDelta)
        {
            var flow = new BoardFlowStateMachine();
            flow.Start(0d);
            flow.Tick(6d);

            Assert.That(
                BoardItemLifecycleRules.GetMineSimulationDelta(
                    flow,
                    synchronizedNow,
                    frameDelta,
                    arrivalGraceDeadline),
                Is.EqualTo(expectedDelta).Within(0.000001f));
        }

        [Test]
        public void ProjectileSettlement_EndsAtLifetimeAndDoesNotActivateMines()
        {
            var lifetime = PrototypeItemCatalog.Get(
                PrototypeItemId.Grenade).ProjectileLifetime;
            Assert.That(lifetime, Is.EqualTo(5f));
            Assert.That(
                BoardFlowStateMachine.AscendingResolveDurationSeconds,
                Is.EqualTo(lifetime));

            Assert.That(
                BoardItemLifecycleRules.CanSimulateProjectile(
                    BoardFlowState.Action),
                Is.True);
            Assert.That(
                BoardItemLifecycleRules.CanSimulateProjectile(
                    BoardFlowState.AscendingResolve),
                Is.True);
            Assert.That(
                BoardItemLifecycleRules.CanSimulateProjectile(
                    BoardFlowState.CombatResolve),
                Is.False);
            Assert.That(
                BoardItemLifecycleRules.CanSimulateProjectile(
                    BoardFlowState.MinigameIntroReady),
                Is.False);

            Assert.That(
                BoardItemLifecycleRules.CanSimulateMine(
                    BoardFlowState.Action),
                Is.True);
            Assert.That(
                BoardItemLifecycleRules.CanSimulateMine(
                    BoardFlowState.AscendingResolve),
                Is.False);

            Assert.That(
                BoardItemLifecycleRules.HasReachedProjectileLifetime(
                    lifetime - 0.001f,
                    lifetime),
                Is.False);
            Assert.That(
                BoardItemLifecycleRules.HasReachedProjectileLifetime(
                    lifetime,
                    lifetime),
                Is.True);
        }

        [Test]
        public void AcceptedMinePlacement_UsesTheSamePositionContractAsRecovery()
        {
            var root = new GameObject("Mine position contract");
            try
            {
                var tileObject = new GameObject("Tile");
                tileObject.transform.SetParent(root.transform);
                tileObject.transform.position = new Vector3(2f, 0f, 3f);
                var tile = tileObject.AddComponent<BoardTile>();
                tile.Configure(Vector2Int.zero, BoardTileType.Normal);

                var topologyObject = new GameObject("Topology");
                topologyObject.transform.SetParent(root.transform);
                var topology = topologyObject.AddComponent<BoardTopology>();
                topology.Configure(new[] { tile }, System.Array.Empty<BoardGate>());

                Assert.That(
                    BoardItemLifecycleRules.TryGetMinePlacementPosition(
                        topology,
                        tile.WorldCenter,
                        Vector3.up,
                        out var position),
                    Is.True);
                Assert.That(
                    position,
                    Is.EqualTo(
                        tile.WorldCenter +
                        Vector3.up * BoardItemLifecycleRules.MineSurfaceOffset));
                Assert.That(
                    BoardItemLifecycleRules.IsValidMinePosition(
                        topology,
                        position),
                    Is.True);

                Assert.That(
                    BoardItemLifecycleRules.TryGetMinePlacementPosition(
                        topology,
                        tile.WorldCenter + Vector3.up * 2f,
                        Vector3.up,
                        out _),
                    Is.False);
                Assert.That(
                    BoardItemLifecycleRules.TryGetMinePlacementPosition(
                        topology,
                        tile.WorldCenter + Vector3.right * BoardTile.RoomSize,
                        Vector3.up,
                        out _),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

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
