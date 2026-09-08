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
            }

            for (var i = 0; i < gates.Length; i++)
            {
                var gate = gates[i];
                if (gate == null || gate.Source == null)
                    continue;

                if (_outgoingGates.TryGetValue(gate.Source, out var outgoing))
                    outgoing.Add(gate);
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
