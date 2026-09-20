using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class BoardTopology : MonoBehaviour
    {
        [SerializeField] private BoardTile[] tiles = Array.Empty<BoardTile>();
        [SerializeField] private BoardGate[] gates = Array.Empty<BoardGate>();

        private readonly Dictionary<Vector2Int, BoardTile> _tilesByCoordinate =
            new Dictionary<Vector2Int, BoardTile>();
        private readonly Dictionary<BoardTile, List<BoardGate>> _outgoingGates =
            new Dictionary<BoardTile, List<BoardGate>>();
        private readonly Dictionary<BoardTile, List<BoardGate>> _incomingGates =
            new Dictionary<BoardTile, List<BoardGate>>();
        private readonly HashSet<BoardTile> _registeredTiles = new HashSet<BoardTile>();

        public IReadOnlyList<BoardTile> Tiles => tiles;
        public IReadOnlyList<BoardGate> Gates => gates;

        private void Awake()
        {
            RebuildIndex();
        }

        public void Configure(BoardTile[] boardTiles, BoardGate[] boardGates)
        {
            tiles = boardTiles != null ? (BoardTile[])boardTiles.Clone() : Array.Empty<BoardTile>();
            gates = boardGates != null ? (BoardGate[])boardGates.Clone() : Array.Empty<BoardGate>();
            RebuildIndex();
        }

        public void RebuildIndex()
        {
            _tilesByCoordinate.Clear();
            _outgoingGates.Clear();
            _incomingGates.Clear();
            _registeredTiles.Clear();

            for (var i = 0; i < tiles.Length; i++)
            {
                var tile = tiles[i];
                if (tile == null)
                    continue;

                _registeredTiles.Add(tile);
                if (!_tilesByCoordinate.ContainsKey(tile.Coordinate))
                    _tilesByCoordinate.Add(tile.Coordinate, tile);
                _outgoingGates[tile] = new List<BoardGate>();
                _incomingGates[tile] = new List<BoardGate>();
            }

            for (var i = 0; i < gates.Length; i++)
            {
                var gate = gates[i];
                if (gate == null || gate.Source == null)
                    continue;

                if (_outgoingGates.TryGetValue(gate.Source, out var outgoing))
                    outgoing.Add(gate);
                if (gate.Destination != null &&
                    _incomingGates.TryGetValue(gate.Destination, out var incoming))
                    incoming.Add(gate);
            }
        }

        public bool TryGetTile(Vector2Int coordinate, out BoardTile tile)
        {
            return _tilesByCoordinate.TryGetValue(coordinate, out tile);
        }

        public IReadOnlyList<BoardGate> GetOutgoingGates(BoardTile tile)
        {
            return tile != null && _outgoingGates.TryGetValue(tile, out var outgoing)
                ? outgoing
                : Array.Empty<BoardGate>();
        }

        public IReadOnlyList<BoardGate> GetIncomingGates(BoardTile tile)
        {
            return tile != null && _incomingGates.TryGetValue(tile, out var incoming)
                ? incoming
                : Array.Empty<BoardGate>();
        }

        /// <summary>
        /// Selects the next directed gate for a timed-out player. An available key
        /// shop takes priority; otherwise continue straight from the arrival path,
        /// falling back to the first exit that does not return to the previous tile.
        /// </summary>
        public BoardGate SelectForcedAdvanceGate(
            BoardTile source,
            BoardTile previousTile,
            Vector3 facingDirection,
            BoardTile keyShopTile)
        {
            var outgoing = GetOutgoingGates(source);
            if (outgoing.Count == 0)
            {
                return null;
            }

            var forward = previousTile != null
                ? source.WorldCenter - previousTile.WorldCenter
                : facingDirection;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
            {
                forward.Normalize();
            }

            if (keyShopTile != null)
            {
                if (source == keyShopTile)
                {
                    return null;
                }

                var distances = new Dictionary<BoardTile, int>
                {
                    [keyShopTile] = 0
                };
                var pending = new Queue<BoardTile>();
                pending.Enqueue(keyShopTile);
                while (pending.Count > 0)
                {
                    var current = pending.Dequeue();
                    var incoming = GetIncomingGates(current);
                    for (var index = 0; index < incoming.Count; index++)
                    {
                        var from = incoming[index].Source;
                        if (from == null || distances.ContainsKey(from))
                        {
                            continue;
                        }

                        distances.Add(from, distances[current] + 1);
                        pending.Enqueue(from);
                    }
                }

                BoardGate shortestGate = null;
                var shortestDistance = int.MaxValue;
                var rightmostScore = float.NegativeInfinity;
                for (var index = 0; index < outgoing.Count; index++)
                {
                    var gate = outgoing[index];
                    if (gate == null || gate.Destination == null ||
                        !distances.TryGetValue(gate.Destination, out var distance) ||
                        distance > shortestDistance)
                    {
                        continue;
                    }

                    var direction = gate.Destination.WorldCenter - source.WorldCenter;
                    direction.y = 0f;
                    var rightTurnScore = forward.sqrMagnitude > 0.0001f &&
                                         direction.sqrMagnitude > 0.0001f
                        ? Vector3.SignedAngle(
                            forward, direction.normalized, Vector3.up)
                        : 0f;
                    if (distance == shortestDistance &&
                        rightTurnScore <= rightmostScore + 0.0001f)
                    {
                        continue;
                    }

                    shortestGate = gate;
                    shortestDistance = distance;
                    rightmostScore = rightTurnScore;
                }

                if (shortestGate != null)
                {
                    return shortestGate;
                }
            }

            if (forward.sqrMagnitude > 0.0001f)
            {
                for (var index = 0; index < outgoing.Count; index++)
                {
                    var gate = outgoing[index];
                    if (gate == null || gate.Destination == null ||
                        gate.Destination == previousTile)
                    {
                        continue;
                    }

                    var direction = gate.Destination.WorldCenter - source.WorldCenter;
                    direction.y = 0f;
                    if (direction.sqrMagnitude > 0.0001f &&
                        Vector3.Dot(forward, direction.normalized) > 0.999f)
                    {
                        return gate;
                    }
                }
            }

            for (var index = 0; index < outgoing.Count; index++)
            {
                var gate = outgoing[index];
                if (gate != null && gate.Destination != null &&
                    gate.Destination != previousTile)
                {
                    return gate;
                }
            }

            return null;
        }

        public IReadOnlyList<BoardGate> PlanForcedAdvancePath(
            BoardTile source,
            BoardTile previousTile,
            Vector3 facingDirection,
            BoardTile keyShopTile,
            int remainingMoves)
        {
            var path = new List<BoardGate>(Mathf.Max(0, remainingMoves));
            if (source == null || remainingMoves <= 0 || source == keyShopTile)
            {
                return path;
            }

            var current = source;
            var previous = previousTile;
            for (var step = 0; step < remainingMoves; step++)
            {
                // A shop crossed mid-path does not stop the settlement. Once
                // there, route by the same straight/default exit rule.
                var target = current == keyShopTile ? null : keyShopTile;
                var gate = SelectForcedAdvanceGate(
                    current, previous, facingDirection, target);
                if (gate == null)
                {
                    break;
                }

                path.Add(gate);
                previous = current;
                current = gate.Destination;
            }

            return path;
        }

        public BoardTile FindContainingTile(Vector3 worldPoint, float tolerance = 0f)
        {
            for (var i = 0; i < tiles.Length; i++)
            {
                var tile = tiles[i];
                if (tile != null && tile.ContainsHorizontalPoint(worldPoint, tolerance))
                    return tile;
            }

            return null;
        }

        public BoardGateTraversalOutcome ResolveGate(
            BoardGate gate,
            BoardTraversalState traversal,
            CharacterController controller,
            bool recoverWhenBlocked = true,
            float recoveryVerticalOffset = 0f)
        {
            if (gate == null)
                return BoardGateTraversalOutcome.InvalidConfiguration;

            var outcome = gate.TryTraverse(traversal, controller);
            if (outcome == BoardGateTraversalOutcome.BlockedNoMoves && recoverWhenBlocked)
                RecoverToLastValidCenter(controller, traversal, recoveryVerticalOffset);
            return outcome;
        }

        public bool TryGetLastValidCenter(BoardTraversalState traversal, out Vector3 center)
        {
            if (traversal == null ||
                traversal.LastValidTile == null ||
                !_registeredTiles.Contains(traversal.LastValidTile))
            {
                center = default;
                return false;
            }

            center = traversal.LastValidTile.WorldCenter;
            return true;
        }

        public bool TryRecoverOutOfBounds(
            CharacterController controller,
            BoardTraversalState traversal,
            float recoveryVerticalOffset = 0f)
        {
            if (controller == null)
                return false;

            var capsuleCenter = controller.transform.TransformPoint(controller.center);
            if (FindContainingTile(capsuleCenter) != null)
                return false;

            return RecoverToLastValidCenter(controller, traversal, recoveryVerticalOffset);
        }

        public bool RecoverToLastValidCenter(
            CharacterController controller,
            BoardTraversalState traversal,
            float recoveryVerticalOffset = 0f)
        {
            if (controller == null ||
                traversal == null ||
                traversal.LastValidTile == null ||
                !_registeredTiles.Contains(traversal.LastValidTile))
                return false;

            var target = traversal.LastValidTile.GetRecoveryCenter(recoveryVerticalOffset);
            var wasEnabled = controller.enabled;
            if (wasEnabled)
                controller.enabled = false;
            controller.transform.position = target;
            if (wasEnabled)
                controller.enabled = true;
            return true;
        }

        public BoardTopologyValidationResult ValidateTopology()
        {
            return BoardTopologyValidator.Validate(tiles, gates);
        }
    }
}
