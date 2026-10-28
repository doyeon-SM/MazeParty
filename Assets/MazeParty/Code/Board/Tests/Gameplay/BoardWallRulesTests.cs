using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardWallRulesTests
    {
        private GameObject _root;
        private BoardTopology _topology;
        private BoardTile[] _tiles;
        private readonly List<BoardGate> _gates = new List<BoardGate>();

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Wall rules");
            _topology = _root.AddComponent<BoardTopology>();
            _tiles = new BoardTile[4];
            var coordinates = new[] { Vector2Int.zero, Vector2Int.right, Vector2Int.one, Vector2Int.up };
            for (var i = 0; i < 4; i++)
            {
                var go = new GameObject("Tile"); go.transform.SetParent(_root.transform);
                go.transform.position = new Vector3(coordinates[i].x * 8, 0, coordinates[i].y * 8);
                _tiles[i] = go.AddComponent<BoardTile>(); _tiles[i].Configure(coordinates[i], BoardTileType.Normal);
            }
            for (var i = 0; i < 4; i++)
            { AddGate(_tiles[i], _tiles[(i + 1) % 4]); AddGate(_tiles[(i + 1) % 4], _tiles[i]); }
            _topology.Configure(_tiles, _gates.ToArray());
        }
        [TearDown]
        public void TearDown() { Object.DestroyImmediate(_root); _gates.Clear(); }

        [Test]
        public void CombinedClosures_PreserveDirectedConnectivity_AndAimRequiresCurrentTileReciprocalPassage()
        {
            var first = _gates[0];
            Assert.That(BoardWallRules.CanClose(_topology, first), Is.True);
            var origin = _tiles[0].WorldCenter + Vector3.up;
            Assert.That(BoardWallRules.TryAim(_topology, _tiles[0], origin, Vector3.right, out var aimed, out _), Is.True);
            Assert.That(aimed, Is.SameAs(first));
            foreach (var invalid in new[] { Vector3.left, Vector3.up, Vector3.back })
                Assert.That(BoardWallRules.TryAim(_topology, _tiles[0], origin, invalid, out _, out _), Is.False);
            Assert.That(BoardWallRules.TryAim(_topology, _tiles[2], origin, Vector3.right, out _, out _), Is.False);
            first.SetTemporaryWallExpiry(6); _gates[1].SetTemporaryWallExpiry(6);
            Assert.That(BoardWallRules.CanClose(_topology, first), Is.False);
            Assert.That(BoardWallRules.CanClose(_topology, _gates[2]), Is.False, "A second cut would isolate rooms.");
            Assert.That(BoardWallRules.TryAim(_topology, _tiles[0], origin, Vector3.right, out _, out _), Is.False);
            first.SetTemporaryWallExpiry(0); _gates[1].SetTemporaryWallExpiry(0);
            Assert.That(BoardWallRules.CanClose(_topology, _gates[2]), Is.True);
            _gates.RemoveAt(1); _topology.Configure(_tiles, _gates.ToArray());
            Assert.That(BoardWallRules.CanClose(_topology, first), Is.False, "One-way passage cannot receive a wall.");
            Assert.That(BoardWallRules.TryAim(_topology, _tiles[0], origin, Vector3.right, out _, out _), Is.False);
        }

        [Test]
        public void BlockedPassage_StopsBothDirectionsForcedTravelAndRetreat_AndReopensWithoutSpendingMoves()
        {
            var first = _gates[0]; var reverse = _gates[1];
            using (var traversal = new BoardTraversalState())
            {
                traversal.Begin(_tiles[0], 2);
                Assert.That(traversal.TryForceCommit(first), Is.True);
                first.SetTemporaryWallExpiry(6); reverse.SetTemporaryWallExpiry(6);
                traversal.ResetMoves(2);
                Assert.That(traversal.Retreat(1, (from, to) => !BoardWallRules.IsPassageBlocked(_topology, from, to)), Is.Empty);
                Assert.That(traversal.CurrentTile, Is.SameAs(_tiles[1]));
                Assert.That(_topology.SelectForcedAdvanceGate(_tiles[0], null, Vector3.right, _tiles[1]), Is.SameAs(_gates[7]));
                var route = new List<BoardTile>();
                Assert.That(BoardMapRoute.TryFind(_topology, _tiles[0], _tiles[1], route), Is.True);
                Assert.That(route.Count, Is.EqualTo(4));
                foreach (var gate in new[] { first, reverse })
                {
                    traversal.Begin(gate.Source, 2);
                    Assert.That(traversal.TryForceCommit(gate), Is.False);
                    var body = new GameObject("Capsule"); body.transform.SetParent(_root.transform);
                    body.transform.position = gate.PlanePoint + gate.ForwardNormal;
                    var controller = body.AddComponent<CharacterController>(); controller.height = 2; controller.radius = .5f;
                    Assert.That(gate.TryTraverse(traversal, controller), Is.EqualTo(BoardGateTraversalOutcome.BlockedByWall));
                    Assert.That(traversal.RemainingMoves, Is.EqualTo(2));
                    gate.SetTemporaryWallExpiry(0);
                    Assert.That(gate.TryTraverse(traversal, controller), Is.EqualTo(BoardGateTraversalOutcome.Committed));
                    Assert.That(traversal.RemainingMoves, Is.EqualTo(1));
                }
            }
        }

        private void AddGate(BoardTile from, BoardTile to)
        {
            var go = new GameObject("Gate"); go.transform.SetParent(_root.transform);
            go.transform.position = (from.WorldCenter + to.WorldCenter) * .5f;
            go.transform.rotation = Quaternion.LookRotation(to.WorldCenter - from.WorldCenter);
            var gate = go.AddComponent<BoardGate>(); gate.Configure(from, to); _gates.Add(gate);
        }
    }
}
