using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardTopologyTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<BoardTraversalState> _traversals = new List<BoardTraversalState>();

        [TearDown]
        public void TearDown()
        {
            foreach (var traversal in _traversals)
                traversal.Dispose();
            _traversals.Clear();

            for (var index = _objects.Count - 1; index >= 0; index--)
                Object.DestroyImmediate(_objects[index]);
            _objects.Clear();
        }

        [Test]
        public void Gate_CommitsOnlyAfterCapsuleFullyClearsBoundary()
        {
            var source = CreateTile("Source", Vector2Int.zero, BoardTileType.Start, Vector3.zero);
            var destination = CreateTile(
                "Destination",
                Vector2Int.right,
                BoardTileType.Normal,
                Vector3.right * BoardTile.RoomSize);
            var gate = CreateGate(source, destination);
            var controller = CreateController(new Vector3(4.25f, 0f, 0f));
            var traversal = CreateTraversal(source, 2);

            Assert.That(
                gate.TryTraverse(traversal, controller),
                Is.EqualTo(BoardGateTraversalOutcome.PartialCrossing));
            Assert.That(traversal.CurrentTile, Is.SameAs(source));
            Assert.That(traversal.RemainingMoves, Is.EqualTo(2));

            controller.transform.position = new Vector3(4.51f, 0f, 0f);
            Assert.That(
                gate.TryTraverse(traversal, controller),
                Is.EqualTo(BoardGateTraversalOutcome.Committed));
            Assert.That(traversal.CurrentTile, Is.SameAs(destination));
            Assert.That(traversal.RemainingMoves, Is.EqualTo(1));
        }

        [Test]
        public void TraversalHistory_RetreatsInReverseArrivalOrder()
        {
            var start = CreateTile("Start", Vector2Int.zero, BoardTileType.Start, Vector3.zero);
            var middle = CreateTile(
                "Middle",
                Vector2Int.right,
                BoardTileType.Normal,
                Vector3.right * BoardTile.RoomSize);
            var end = CreateTile(
                "End",
                Vector2Int.right * 2,
                BoardTileType.Normal,
                Vector3.right * BoardTile.RoomSize * 2f);
            var firstGate = CreateGate(start, middle);
            var secondGate = CreateGate(middle, end);
            var controller = CreateController(new Vector3(4.51f, 0f, 0f));
            var traversal = CreateTraversal(start, 2);

            Assert.That(firstGate.TryTraverse(traversal, controller), Is.EqualTo(BoardGateTraversalOutcome.Committed));
            controller.transform.position = new Vector3(12.51f, 0f, 0f);
            Assert.That(secondGate.TryTraverse(traversal, controller), Is.EqualTo(BoardGateTraversalOutcome.Committed));

            Assert.That(traversal.Retreat(2), Is.EqualTo(new[] { middle, start }));
            Assert.That(traversal.CurrentTile, Is.SameAs(start));
            Assert.That(start.IsOccupiedBy(traversal), Is.True);
        }

        [Test]
        public void WrongWayAndExhaustedTraversal_AreBlockedAtLastValidTile()
        {
            var source = CreateTile("Source", Vector2Int.zero, BoardTileType.Start, new Vector3(2f, 0f, 3f));
            var destination = CreateTile("Destination", Vector2Int.right, BoardTileType.Normal, new Vector3(10f, 0f, 3f));
            var gate = CreateGate(source, destination);
            var topology = CreateTopology(source, destination, gate);

            var reverse = CreateTraversal(destination, 2);
            var reverseController = CreateController(new Vector3(5f, 0f, 3f));
            Assert.That(gate.TryTraverse(reverse, reverseController), Is.EqualTo(BoardGateTraversalOutcome.NotFromSource));

            var exhausted = CreateTraversal(source, 0);
            var exhaustedController = CreateController(new Vector3(6.75f, 0f, 3f));
            Assert.That(
                topology.ResolveGate(gate, exhausted, exhaustedController, true, 0.25f),
                Is.EqualTo(BoardGateTraversalOutcome.BlockedNoMoves));
            Assert.That(exhausted.CurrentTile, Is.SameAs(source));
            Assert.That(exhaustedController.transform.position, Is.EqualTo(new Vector3(2f, 0.25f, 3f)));
        }

        [Test]
        public void SharedOccupancyAndOutOfBoundsRecovery_PreserveCommittedState()
        {
            var source = CreateTile("Source", Vector2Int.zero, BoardTileType.Start, Vector3.zero);
            var destination = CreateTile(
                "Destination",
                Vector2Int.right,
                BoardTileType.Respawn,
                Vector3.right * BoardTile.RoomSize);
            var gate = CreateGate(source, destination);
            var topology = CreateTopology(source, destination, gate);
            var first = CreateTraversal(source, 1);
            var second = CreateTraversal(source, 3);
            Assert.That(source.OccupancyCount, Is.EqualTo(2));

            var controller = CreateController(new Vector3(4.75f, 0f, 0f));
            Assert.That(gate.TryTraverse(first, controller), Is.EqualTo(BoardGateTraversalOutcome.Committed));
            controller.transform.position = new Vector3(100f, -20f, 100f);
            Assert.That(topology.TryRecoverOutOfBounds(controller, first, 0.5f), Is.True);
            Assert.That(controller.transform.position, Is.EqualTo(new Vector3(8f, 0.5f, 0f)));
            Assert.That(first.CurrentTile, Is.SameAs(destination));
            Assert.That(source.IsOccupiedBy(second), Is.True);
        }

        [Test]
        public void SeparatedFreeformTiles_UseGateWidthCorridorAcrossTheGap()
        {
            var source = CreateTile(
                "Separated Source",
                Vector2Int.zero,
                BoardTileType.Start,
                Vector3.zero);
            source.gameObject.AddComponent<BoardTileFootprint>().Configure(
                SquareFootprint(2f));
            var destination = CreateTile(
                "Separated Destination",
                Vector2Int.right,
                BoardTileType.Normal,
                Vector3.right * 12f);
            destination.gameObject.AddComponent<BoardTileFootprint>().Configure(
                SquareFootprint(2f));
            var gate = CreateGate(source, destination);
            var topology = CreateTopology(source, destination, gate);

            var validation = topology.ValidateTopology();
            Assert.That(
                validation.IsValid,
                Is.True,
                string.Join("\n", validation.Issues));
            Assert.That(
                gate.TryGetEndpointCorridorLimit(source, out var sourceLimit),
                Is.True);
            Assert.That(sourceLimit, Is.EqualTo(-6f).Within(0.0001f));
            Assert.That(
                gate.TryGetEndpointCorridorLimit(
                    destination,
                    out var destinationLimit),
                Is.True);
            Assert.That(destinationLimit, Is.EqualTo(6f).Within(0.0001f));

            Assert.That(
                topology.ContainsTraversablePoint(
                    source,
                    new Vector3(4f, 0f, 0f)),
                Is.True,
                "The source-side dirt-path gap remains traversable.");
            Assert.That(
                topology.ContainsTraversablePoint(
                    destination,
                    new Vector3(8f, 0f, 0f)),
                Is.True,
                "The incoming corridor remains valid after the gate commits.");
            Assert.That(
                topology.ContainsTraversablePoint(
                    source,
                    new Vector3(4f, 0f, 2f)),
                Is.False,
                "The connection must not make the entire inter-tile gap playable.");

            var traversal = CreateTraversal(source, 1);
            var controller = CreateController(new Vector3(4f, 0f, 0f));
            Assert.That(
                topology.TryRecoverOutOfBounds(controller, traversal, 0.5f),
                Is.False,
                "Normal out-of-bounds recovery must preserve an authored corridor.");
            controller.transform.position = new Vector3(4f, 0f, 0.8f);
            Assert.That(
                topology.ContainsTraversableCapsule(source, controller),
                Is.False,
                "The complete player capsule must fit inside the dirt path.");
            controller.transform.position = new Vector3(4f, 0f, 0.7f);
            Assert.That(
                topology.ContainsTraversableCapsule(source, controller),
                Is.True);
            controller.transform.position = new Vector3(4f, 0f, 0f);
            Assert.That(
                gate.TryTraverse(traversal, controller),
                Is.EqualTo(BoardGateTraversalOutcome.SourceSide));
            controller.transform.position = new Vector3(6f, 0f, 0f);
            Assert.That(
                gate.TryTraverse(traversal, controller),
                Is.EqualTo(BoardGateTraversalOutcome.PartialCrossing));
            controller.transform.position = new Vector3(6.51f, 0f, 0f);
            Assert.That(
                gate.TryTraverse(traversal, controller),
                Is.EqualTo(BoardGateTraversalOutcome.Committed));
            Assert.That(traversal.CurrentTile, Is.SameAs(destination));
            Assert.That(
                topology.ContainsTraversablePoint(
                    traversal.CurrentTile,
                    new Vector3(8f, 0f, 0f)),
                Is.True);
        }

        [Test]
        public void TopologyValidation_RejectsSafeEndpointPastGatePlane()
        {
            var source = CreateTile(
                "Missed Source",
                Vector2Int.zero,
                BoardTileType.Start,
                Vector3.zero);
            source.gameObject.AddComponent<BoardTileFootprint>().Configure(new[]
            {
                new Vector2(7f, -1f),
                new Vector2(9f, -1f),
                new Vector2(9f, 1f),
                new Vector2(7f, 1f)
            });
            var destination = CreateTile(
                "Missed Destination",
                Vector2Int.right,
                BoardTileType.Normal,
                Vector3.right * 12f);
            destination.gameObject.AddComponent<BoardTileFootprint>().Configure(
                SquareFootprint(2f));
            var gateObject = CreateObject("Missed Corridor Gate");
            gateObject.transform.position = new Vector3(6f, 0f, 0f);
            gateObject.transform.rotation = Quaternion.LookRotation(
                Vector3.right,
                Vector3.up);
            var gate = gateObject.AddComponent<BoardGate>();
            gate.Configure(source, destination, 1f);

            var validation = BoardTopologyValidator.Validate(
                new[] { source, destination },
                new[] { gate });

            Assert.That(validation.IsValid, Is.False);
            Assert.That(
                validation.Issues.Select(issue => issue.Code),
                Does.Contain(BoardTopologyIssueCode.GateCorridorDisconnected));
        }

        [Test]
        public void TopologyValidation_RejectsGateNarrowerThanPlayerDiameter()
        {
            var source = CreateTile(
                "Narrow Source",
                Vector2Int.zero,
                BoardTileType.Start,
                Vector3.zero);
            var destination = CreateTile(
                "Narrow Destination",
                Vector2Int.right,
                BoardTileType.Normal,
                Vector3.right * BoardTile.RoomSize);
            var gate = CreateGate(source, destination);
            gate.Configure(source, destination, 0.75f);

            var validation = BoardTopologyValidator.Validate(
                new[] { source, destination },
                new[] { gate });

            Assert.That(
                validation.Issues.Select(issue => issue.Code),
                Does.Contain(BoardTopologyIssueCode.GateCorridorTooNarrow));
            var controller = CreateController(gate.PlanePoint);
            Assert.That(gate.IsCapsuleWithinGateSpan(controller), Is.False);
        }

        [Test]
        public void SlantedTriangleEdge_HasContinuousCenterlineCorridor()
        {
            var source = CreateTile(
                "Slanted Triangle",
                Vector2Int.zero,
                BoardTileType.Start,
                Vector3.zero);
            source.gameObject.AddComponent<BoardTileFootprint>().Configure(new[]
            {
                new Vector2(-2f, -2f),
                new Vector2(3f, -1f),
                new Vector2(-1f, 3f)
            });
            var destination = CreateTile(
                "Triangle Destination",
                Vector2Int.right,
                BoardTileType.Normal,
                Vector3.right * 12f);
            destination.gameObject.AddComponent<BoardTileFootprint>().Configure(
                SquareFootprint(2f));
            var gate = CreateGate(source, destination);
            var topology = CreateTopology(source, destination, gate);
            var controller = CreateController(new Vector3(2.25f, 0f, 0f));

            Assert.That(source.ContainsHorizontalPoint(
                controller.transform.TransformPoint(controller.center)), Is.False);
            Assert.That(topology.ValidateTopology().IsValid, Is.True);
            Assert.That(
                topology.ContainsTraversableCapsule(source, controller),
                Is.True,
                "A slanted polygon mouth must not leave a centerline gap before the dirt path.");
        }

        [Test]
        public void SkewedFootprint_UsesCapsuleSafeInteriorForTraversalAndRecovery()
        {
            var source = CreateTile(
                "Skewed Source",
                Vector2Int.zero,
                BoardTileType.Start,
                Vector3.zero);
            var footprint = source.gameObject.AddComponent<BoardTileFootprint>();
            footprint.Configure(new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1.3f, 0f),
                new Vector2(0f, 20f)
            });
            var destination = CreateTile(
                "Skewed Destination",
                Vector2Int.right,
                BoardTileType.Normal,
                Vector3.right * 12f);
            destination.gameObject.AddComponent<BoardTileFootprint>().Configure(
                SquareFootprint(2f));
            var gate = CreateGate(source, destination);
            var topology = CreateTopology(source, destination, gate);
            var traversal = CreateTraversal(source, 1);
            var controller = CreateController(new Vector3(0.65f, 0f, 0.1f));

            var rawCenter = source.GetRecoveryCenter();
            var capsuleSafeCenter = source.GetRecoveryCenter(
                horizontalInset: controller.radius);
            Assert.That(footprint.CanContainInset(controller.radius), Is.True);
            Assert.That(
                source.ContainsHorizontalDisc(rawCenter, controller.radius),
                Is.False,
                "A valid skewed polygon's centroid need not fit the player capsule.");
            Assert.That(
                source.ContainsHorizontalDisc(capsuleSafeCenter, controller.radius),
                Is.True);
            Assert.That(
                gate.TryGetEndpointCorridorLimit(
                    source,
                    out var corridorLimit,
                    controller.radius),
                Is.True);
            Assert.That(
                corridorLimit,
                Is.EqualTo(gate.GetSignedDistance(capsuleSafeCenter)).Within(0.0001f));

            var controllerCenter = controller.transform.TransformPoint(controller.center);
            Assert.That(source.ContainsHorizontalPoint(controllerCenter), Is.True);
            Assert.That(
                topology.ContainsTraversableCapsule(source, controller),
                Is.False,
                "A center inside the polygon is insufficient when the capsule crosses an edge.");
            Assert.That(
                topology.TryRecoverOutOfBounds(controller, traversal, 0.25f),
                Is.True);
            Assert.That(
                Vector3.Distance(
                    controller.transform.position,
                    capsuleSafeCenter + source.transform.up * 0.25f),
                Is.LessThan(0.0001f));
            Assert.That(topology.ContainsTraversableCapsule(source, controller), Is.True);
            Assert.That(topology.ValidateTopology().IsValid, Is.True);
        }

        [Test]
        public void TopologyValidation_RejectsFootprintTooNarrowForPlayerCapsule()
        {
            var source = CreateTile(
                "Too Narrow Source",
                Vector2Int.zero,
                BoardTileType.Start,
                Vector3.zero);
            source.gameObject.AddComponent<BoardTileFootprint>().Configure(
                SquareFootprint(0.4f));
            var destination = CreateTile(
                "Narrow Footprint Destination",
                Vector2Int.right,
                BoardTileType.Normal,
                Vector3.right * BoardTile.RoomSize);
            var gate = CreateGate(source, destination);

            var validation = BoardTopologyValidator.Validate(
                new[] { source, destination },
                new[] { gate });

            Assert.That(validation.IsValid, Is.False);
            Assert.That(
                validation.Issues.Select(issue => issue.Code),
                Does.Contain(BoardTopologyIssueCode.TileFootprintTooNarrow));
        }

        [Test]
        public void ForcedAdvance_FollowsShortestDirectedRouteToKeyShop()
        {
            var source = CreateTile("Source", Vector2Int.zero, BoardTileType.Start, Vector3.zero);
            var north = CreateTile("North", Vector2Int.up, BoardTileType.Normal, Vector3.forward * 8f);
            var northwest = CreateTile("Northwest", new Vector2Int(-1, 1), BoardTileType.Normal,
                new Vector3(-8f, 0f, 8f));
            var west = CreateTile("West", Vector2Int.left, BoardTileType.Normal, Vector3.left * 8f);
            var east = CreateTile("East", Vector2Int.right, BoardTileType.Normal, Vector3.right * 8f);
            var shop = CreateTile("Shop", new Vector2Int(1, 1), BoardTileType.KeyShop,
                new Vector3(8f, 0f, 8f));
            var longFirst = CreateGate(source, north);
            var shortSecond = CreateGate(source, east);
            var topology = CreateObject("Topology").AddComponent<BoardTopology>();
            topology.Configure(
                new[] { source, north, northwest, west, east, shop },
                new[]
                {
                    longFirst,
                    shortSecond,
                    CreateGate(north, northwest),
                    CreateGate(northwest, west),
                    CreateGate(west, source),
                    CreateGate(east, shop),
                    CreateGate(shop, east)
                });

            Assert.That(
                topology.SelectForcedAdvanceGate(source, null, Vector3.forward, shop),
                Is.SameAs(shortSecond));
            Assert.That(
                topology.SelectForcedAdvanceGate(shop, east, Vector3.forward, shop),
                Is.Null,
                "A player already at the shop stays on that tile.");
        }

        [Test]
        public void ForcedAdvance_ContinuesStraightThenUsesFirstNonBacktrackingExit()
        {
            var west = CreateTile("West", Vector2Int.left, BoardTileType.Normal, Vector3.left * 8f);
            var source = CreateTile("Source", Vector2Int.zero, BoardTileType.Start, Vector3.zero);
            var north = CreateTile("North", Vector2Int.up, BoardTileType.Normal, Vector3.forward * 8f);
            var east = CreateTile("East", Vector2Int.right, BoardTileType.Normal, Vector3.right * 8f);
            var south = CreateTile("South", Vector2Int.down, BoardTileType.Normal, Vector3.back * 8f);
            var unreachableShop = CreateTile(
                "Unreachable Shop", new Vector2Int(2, 2), BoardTileType.KeyShop,
                new Vector3(16f, 0f, 16f));
            var fromWest = CreateGate(west, source);
            var toNorth = CreateGate(source, north);
            var toEast = CreateGate(source, east);
            var toSouth = CreateGate(source, south);
            var backtrack = CreateGate(source, west);
            var topology = CreateObject("Topology").AddComponent<BoardTopology>();
            topology.Configure(
                new[] { west, source, north, east, south, unreachableShop },
                new[] { fromWest, toNorth, toEast, toSouth, backtrack });
            var traversal = CreateTraversal(west, 1);
            Assert.That(traversal.TryForceCommit(fromWest), Is.True);

            Assert.That(
                topology.SelectForcedAdvanceGate(source, west, Vector3.forward, null),
                Is.SameAs(toEast),
                "The arrival path takes precedence over the avatar's facing.");
            Assert.That(
                topology.SelectForcedAdvanceGate(source, west, Vector3.forward, unreachableShop),
                Is.SameAs(toEast),
                "An unreachable shop uses the same straight fallback.");
            Assert.That(traversal.CurrentTile, Is.SameAs(source));
            Assert.That(traversal.RemainingMoves, Is.Zero);
            Assert.That(traversal.History, Is.EqualTo(new[] { west, source }));
            Assert.That(
                topology.SelectForcedAdvanceGate(source, null, Vector3.forward, null),
                Is.SameAs(toNorth),
                "Without an arrival path, facing determines straight ahead.");

            topology.Configure(
                new[] { west, source, north, south },
                new[] { fromWest, backtrack, toNorth, toSouth });
            Assert.That(
                topology.SelectForcedAdvanceGate(source, west, Vector3.right, null),
                Is.SameAs(toNorth));
            topology.Configure(new[] { west, source }, new[] { fromWest, backtrack });
            Assert.That(
                topology.SelectForcedAdvanceGate(source, west, Vector3.right, null),
                Is.Null);
        }

        [Test]
        public void ForcedAdvancePath_UsesEveryRemainingStepAndPassesThroughShop()
        {
            var start = CreateTile("Start", Vector2Int.zero, BoardTileType.Start, Vector3.zero);
            var shop = CreateTile("Shop", Vector2Int.right, BoardTileType.KeyShop, Vector3.right * 8f);
            var east = CreateTile("East", Vector2Int.right * 2, BoardTileType.Normal, Vector3.right * 16f);
            var north = CreateTile("North", new Vector2Int(2, 1), BoardTileType.Normal,
                new Vector3(16f, 0f, 8f));
            var toShop = CreateGate(start, shop);
            var throughShop = CreateGate(shop, east);
            var afterShop = CreateGate(east, north);
            var topology = CreateObject("Topology").AddComponent<BoardTopology>();
            topology.Configure(
                new[] { start, shop, east, north },
                new[] { toShop, throughShop, afterShop });

            var path = topology.PlanForcedAdvancePath(start, null, Vector3.right, shop, 3);
            Assert.That(path, Is.EqualTo(new[] { toShop, throughShop, afterShop }));
            var traversal = CreateTraversal(start, 3);
            foreach (var gate in path)
            {
                Assert.That(traversal.TryForceCommit(gate), Is.True);
            }
            Assert.That(traversal.History, Is.EqualTo(new[] { start, shop, east, north }));
            Assert.That(traversal.CurrentTile, Is.SameAs(north));
            Assert.That(traversal.RemainingMoves, Is.Zero);
            Assert.That(
                topology.PlanForcedAdvancePath(shop, start, Vector3.right, shop, 3),
                Is.Empty,
                "A player already on the shop tile stays there.");
        }

        [Test]
        public void ForcedAdvancePath_ChoosesRightmostOfEqualShortestRoutes()
        {
            var south = CreateTile("South", Vector2Int.down, BoardTileType.Normal, Vector3.back * 8f);
            var source = CreateTile("Source", Vector2Int.zero, BoardTileType.Start, Vector3.zero);
            var west = CreateTile("West", Vector2Int.left, BoardTileType.Normal, Vector3.left * 8f);
            var east = CreateTile("East", Vector2Int.right, BoardTileType.Normal, Vector3.right * 8f);
            var northwest = CreateTile("Northwest", new Vector2Int(-1, 1), BoardTileType.Normal,
                new Vector3(-8f, 0f, 8f));
            var northeast = CreateTile("Northeast", new Vector2Int(1, 1), BoardTileType.Normal,
                new Vector3(8f, 0f, 8f));
            var shop = CreateTile("Shop", Vector2Int.up, BoardTileType.KeyShop, Vector3.forward * 8f);
            var fromSouth = CreateGate(south, source);
            var leftFirst = CreateGate(source, west);
            var rightSecond = CreateGate(source, east);
            var toNorthwest = CreateGate(west, northwest);
            var toNortheast = CreateGate(east, northeast);
            var fromNorthwest = CreateGate(northwest, shop);
            var fromNortheast = CreateGate(northeast, shop);
            var topology = CreateObject("Topology").AddComponent<BoardTopology>();
            topology.Configure(
                new[] { south, source, west, east, northwest, northeast, shop },
                new[]
                {
                    fromSouth, leftFirst, rightSecond, toNorthwest,
                    toNortheast, fromNorthwest, fromNortheast
                });

            Assert.That(
                topology.SelectForcedAdvanceGate(source, south, Vector3.back, shop),
                Is.SameAs(rightSecond));
            Assert.That(
                topology.PlanForcedAdvancePath(source, south, Vector3.back, shop, 3),
                Is.EqualTo(new[] { rightSecond, toNortheast, fromNortheast }));
        }
        private BoardTile CreateTile(string name, Vector2Int coordinate, BoardTileType type, Vector3 position)
        {
            var gameObject = CreateObject(name);
            gameObject.transform.position = position;
            var tile = gameObject.AddComponent<BoardTile>();
            tile.Configure(coordinate, type);
            return tile;
        }

        private BoardGate CreateGate(BoardTile source, BoardTile destination)
        {
            var gameObject = CreateObject(source.name + " -> " + destination.name);
            gameObject.transform.position = (source.WorldCenter + destination.WorldCenter) * 0.5f;
            gameObject.transform.rotation = Quaternion.LookRotation(
                (destination.WorldCenter - source.WorldCenter).normalized,
                Vector3.up);
            var gate = gameObject.AddComponent<BoardGate>();
            gate.Configure(source, destination);
            return gate;
        }

        private CharacterController CreateController(Vector3 position)
        {
            var gameObject = CreateObject("Controller");
            gameObject.transform.position = position;
            var controller = gameObject.AddComponent<CharacterController>();
            controller.center = Vector3.up;
            controller.height = 2f;
            controller.radius = 0.5f;
            return controller;
        }

        private BoardTraversalState CreateTraversal(BoardTile start, int remainingMoves)
        {
            var traversal = new BoardTraversalState();
            traversal.Begin(start, remainingMoves);
            _traversals.Add(traversal);
            return traversal;
        }

        private BoardTopology CreateTopology(BoardTile source, BoardTile destination, BoardGate gate)
        {
            var topology = CreateObject("Topology").AddComponent<BoardTopology>();
            topology.Configure(new[] { source, destination }, new[] { gate });
            return topology;
        }

        private static Vector2[] SquareFootprint(float halfExtent)
        {
            return new[]
            {
                new Vector2(-halfExtent, -halfExtent),
                new Vector2(halfExtent, -halfExtent),
                new Vector2(halfExtent, halfExtent),
                new Vector2(-halfExtent, halfExtent)
            };
        }

        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name);
            _objects.Add(gameObject);
            return gameObject;
        }
    }
}
