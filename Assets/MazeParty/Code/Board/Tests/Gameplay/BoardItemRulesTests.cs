using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardItemRulesTests
    {
        [Test]
        public void MineSimulationDelta_StopsAtActionLogicalBoundary()
        {
            var cases = new[]
            {
                (185d, 0.02f, 0.02f),
                (186.0625d, 0.125f, 0.0625f),
                (186.125d, 0.125f, 0f)
            };

            foreach (var testCase in cases)
            {
                var flow = new BoardFlowStateMachine();
                flow.Start(0d);
                flow.Tick(6d);
                Assert.That(flow.State, Is.EqualTo(BoardFlowState.Action));

                Assert.That(
                    BoardItemLifecycleRules.GetMineSimulationDelta(
                        flow,
                        testCase.Item1,
                        testCase.Item2),
                    Is.EqualTo(testCase.Item3).Within(0.000001f));
            }
        }

        [Test]
        public void MineSimulationDelta_UsesEarlierArrivalGraceBoundary()
        {
            var cases = new[]
            {
                (20.1d, 0.1f, 20.04d, 0.04f),
                (20.1d, 0.1f, 20d, 0f),
                (20.1d, 0.1f, 0d, 0.1f)
            };

            foreach (var testCase in cases)
            {
                var flow = new BoardFlowStateMachine();
                flow.Start(0d);
                flow.Tick(6d);

                Assert.That(
                    BoardItemLifecycleRules.GetMineSimulationDelta(
                        flow,
                        testCase.Item1,
                        testCase.Item2,
                        testCase.Item3),
                    Is.EqualTo(testCase.Item4).Within(0.000001f));
            }
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

        [Test]
        public void TwoDice_WaitForBoth_RejectDuplicates()
        {
            var cases = new[]
            {
                (0, 12, 1, 12, 24),
                (1, 1, 0, 1, 2),
                (0, 3, 1, 8, 11)
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    BoardDiceProgress.TrySettle(
                        true, 0, 0, testCase.Item1, testCase.Item2,
                        out var a, out var b, out var total),
                    Is.True);
                Assert.That(total, Is.Zero);
                Assert.That(
                    BoardDiceProgress.TrySettle(
                        true, a, b, testCase.Item1, 6,
                        out _, out _, out _),
                    Is.False);
                Assert.That(
                    BoardDiceProgress.TrySettle(
                        true, a, b, testCase.Item3, testCase.Item4,
                        out a, out b, out total),
                    Is.True);
                Assert.That(total, Is.EqualTo(testCase.Item5));
                Assert.That(
                    BoardDiceProgress.TrySettle(
                        true, a, b, testCase.Item3, 6,
                        out _, out _, out _),
                    Is.False);
            }
        }

        [Test]
        public void Timeout_OnlyCompletesOneMissingDie()
        {
            var cases = new[]
            {
                (true, 0, 0, -1),
                (true, 5, 0, 1),
                (true, 0, 5, 0),
                (true, 5, 5, -1),
                (false, 0, 0, -1)
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    BoardDiceProgress.MissingDieOnTimeout(
                        testCase.Item1,
                        testCase.Item2,
                        testCase.Item3),
                    Is.EqualTo(testCase.Item4));
            }
        }

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

                var outOfRangePlayer = GameObject.CreatePrimitive(
                    PrimitiveType.Capsule);
                outOfRangePlayer.transform.SetParent(root.transform);
                outOfRangePlayer.transform.position =
                    new Vector3(0, 100, 12);
                outOfRangePlayer.AddComponent<PlayerHitZoneOwner>();
                Physics.SyncTransforms();
                Assert.That(
                    BoardItemPhysics.Cast(
                        origin,
                        Vector3.forward,
                        10,
                        null,
                        out _),
                    Is.False,
                    "A player beyond the authored item range must not be hit.");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
