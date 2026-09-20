using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public sealed class BoardTraversalState : IDisposable
    {
        private readonly List<BoardTile> _history = new List<BoardTile>();

        public BoardTile CurrentTile { get; private set; }
        public BoardTile LastValidTile { get; private set; }
        public int RemainingMoves { get; private set; }
        public bool IsInitialized => CurrentTile != null;
        public bool HasRemainingMoves => RemainingMoves > 0;
        public IReadOnlyList<BoardTile> History => _history;

        public void Begin(BoardTile startTile, int remainingMoves)
        {
            if (startTile == null)
                throw new ArgumentNullException(nameof(startTile));

            CurrentTile?.Unregister(this);
            CurrentTile = startTile;
            LastValidTile = startTile;
            RemainingMoves = Mathf.Max(0, remainingMoves);
            CurrentTile.Register(this);
            _history.Clear();
            _history.Add(CurrentTile);
        }

        public void ResetMoves(int remainingMoves)
        {
            if (!IsInitialized)
                throw new InvalidOperationException("Traversal must begin on a board tile before moves can be reset.");

            RemainingMoves = Mathf.Max(0, remainingMoves);
        }

        internal bool TryCommit(BoardGate gate)
        {
            if (gate == null || CurrentTile != gate.Source || gate.Destination == null || RemainingMoves <= 0)
                return false;

            CurrentTile.Unregister(this);
            CurrentTile = gate.Destination;
            LastValidTile = CurrentTile;
            RemainingMoves--;
            CurrentTile.Register(this);
            _history.Add(CurrentTile);
            return true;
        }

        public BoardTile[] Retreat(int requestedSteps)
        {
            if (!IsInitialized || requestedSteps <= 0 || _history.Count <= 1)
            {
                return Array.Empty<BoardTile>();
            }

            var actualSteps = Math.Min(requestedSteps, _history.Count - 1);
            var path = new BoardTile[actualSteps];
            CurrentTile.Unregister(this);
            for (var step = 0; step < actualSteps; step++)
            {
                _history.RemoveAt(_history.Count - 1);
                path[step] = _history[_history.Count - 1];
            }

            CurrentTile = path[path.Length - 1];
            LastValidTile = CurrentTile;
            RemainingMoves = 0;
            CurrentTile.Register(this);
            return path;
        }

        public Vector2Int[] CaptureHistoryCoordinates()
        {
            var result = new Vector2Int[_history.Count];
            for (var i = 0; i < _history.Count; i++)
            {
                result[i] = _history[i].Coordinate;
            }
            return result;
        }

        public bool RestoreHistory(
            BoardTopology topology,
            IReadOnlyList<Vector2Int> coordinates,
            int remainingMoves)
        {
            if (topology == null || coordinates == null || coordinates.Count == 0)
            {
                return false;
            }

            var restored = new List<BoardTile>(coordinates.Count);
            for (var i = 0; i < coordinates.Count; i++)
            {
                if (!topology.TryGetTile(coordinates[i], out var tile) || tile == null)
                {
                    return false;
                }
                restored.Add(tile);
            }

            CurrentTile?.Unregister(this);
            _history.Clear();
            _history.AddRange(restored);
            CurrentTile = restored[restored.Count - 1];
            LastValidTile = CurrentTile;
            RemainingMoves = Mathf.Max(0, remainingMoves);
            CurrentTile.Register(this);
            return true;
        }

        public void Relocate(BoardTile destination, int remainingMoves = 0)
        {
            Begin(
                destination ?? throw new ArgumentNullException(nameof(destination)),
                remainingMoves);
        }

        public void Dispose()
        {
            CurrentTile?.Unregister(this);
            CurrentTile = null;
            LastValidTile = null;
            RemainingMoves = 0;
            _history.Clear();
        }
    }
}
