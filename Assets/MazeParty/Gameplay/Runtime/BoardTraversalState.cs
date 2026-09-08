using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public sealed class BoardTraversalState : IDisposable
    {
        public BoardTile CurrentTile { get; private set; }
        public BoardTile LastValidTile { get; private set; }
        public int RemainingMoves { get; private set; }
        public bool IsInitialized => CurrentTile != null;
        public bool HasRemainingMoves => RemainingMoves > 0;

        public void Begin(BoardTile startTile, int remainingMoves)
        {
            if (startTile == null)
                throw new ArgumentNullException(nameof(startTile));

            CurrentTile?.Unregister(this);
            CurrentTile = startTile;
            LastValidTile = startTile;
            RemainingMoves = Mathf.Max(0, remainingMoves);
            CurrentTile.Register(this);
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
            return true;
        }

        public void Dispose()
        {
            CurrentTile?.Unregister(this);
            CurrentTile = null;
            LastValidTile = null;
            RemainingMoves = 0;
        }
    }
}
