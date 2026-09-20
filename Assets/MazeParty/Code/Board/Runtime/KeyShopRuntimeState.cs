using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public enum KeyShopLifecycleState : byte
    {
        Inactive,
        Preparing,
        Appearing,
        Active
    }

    public enum KeyShopPlacementReason : byte
    {
        None,
        InitialTurnTwo,
        PurchaseRelocation
    }

    public readonly struct KeyShopLifecycleEvent
    {
        public KeyShopLifecycleEvent(
            KeyShopLifecycleState previousState,
            KeyShopLifecycleState state,
            KeyShopPlacementReason reason,
            int revision,
            bool hasPreviousLocation,
            Vector2Int previousLocation,
            bool hasTargetLocation,
            Vector2Int targetLocation)
        {
            PreviousState = previousState;
            State = state;
            Reason = reason;
            Revision = revision;
            HasPreviousLocation = hasPreviousLocation;
            PreviousLocation = previousLocation;
            HasTargetLocation = hasTargetLocation;
            TargetLocation = targetLocation;
        }

        public KeyShopLifecycleState PreviousState { get; }
        public KeyShopLifecycleState State { get; }
        public KeyShopPlacementReason Reason { get; }
        public int Revision { get; }
        public bool HasPreviousLocation { get; }
        public Vector2Int PreviousLocation { get; }
        public bool HasTargetLocation { get; }
        public Vector2Int TargetLocation { get; }
    }

    /// <summary>
    /// Authoritative key-shop lifecycle state. A networking owner invokes the
    /// placement methods on the server and mirrors State, location, and revision
    /// to clients. Visual presenters subscribe to the three stage hooks.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyShopRuntimeState : MonoBehaviour
    {
        public const int InitialPlacementTurn = 2;

        [SerializeField] private KeyShopLifecycleState state = KeyShopLifecycleState.Inactive;
        [SerializeField] private bool hasLocation;
        [SerializeField] private Vector2Int location;
        [SerializeField, Min(0)] private int placementRevision;

        private BoardTile _currentTile;
        private KeyShopPlacementReason _placementReason;
        private bool _hasPreviousLocation;
        private Vector2Int _previousLocation;

        public event Action<KeyShopLifecycleEvent> StateChanged;
        public event Action<KeyShopLifecycleEvent> Preparing;
        public event Action<KeyShopLifecycleEvent> Appearing;
        public event Action<KeyShopLifecycleEvent> Activated;

        public KeyShopLifecycleState State => state;
        public bool HasLocation => hasLocation;
        public Vector2Int Location => location;
        public BoardTile CurrentTile => _currentTile;
        public int PlacementRevision => placementRevision;
        public bool IsActive => state == KeyShopLifecycleState.Active && hasLocation;

        public bool TryBeginInitialPlacement(
            int currentTurn,
            IReadOnlyList<BoardTile> tiles,
            IReadOnlyCollection<Vector2Int> occupiedCoordinates,
            out BoardTile selectedTile)
        {
            return TryBeginInitialPlacement(
                currentTurn,
                tiles,
                occupiedCoordinates,
                UnityKeyShopRandomSource.Shared,
                out selectedTile);
        }

        public bool TryBeginInitialPlacement(
            int currentTurn,
            IReadOnlyList<BoardTile> tiles,
            IReadOnlyCollection<Vector2Int> occupiedCoordinates,
            IKeyShopRandomSource randomSource,
            out BoardTile selectedTile)
        {
            selectedTile = null;
            if (currentTurn != InitialPlacementTurn ||
                state != KeyShopLifecycleState.Inactive || hasLocation)
            {
                return false;
            }

            return TryBeginPlacement(
                KeyShopPlacementReason.InitialTurnTwo,
                tiles,
                occupiedCoordinates,
                randomSource,
                false,
                default,
                out selectedTile);
        }

        public bool TryBeginPurchaseRelocation(
            IReadOnlyList<BoardTile> tiles,
            IReadOnlyCollection<Vector2Int> occupiedCoordinates,
            out BoardTile selectedTile)
        {
            return TryBeginPurchaseRelocation(
                tiles,
                occupiedCoordinates,
                UnityKeyShopRandomSource.Shared,
                out selectedTile);
        }

        public bool TryBeginPurchaseRelocation(
            IReadOnlyList<BoardTile> tiles,
            IReadOnlyCollection<Vector2Int> occupiedCoordinates,
            IKeyShopRandomSource randomSource,
            out BoardTile selectedTile)
        {
            selectedTile = null;
            if (!IsActive)
                return false;

            return TryBeginPlacement(
                KeyShopPlacementReason.PurchaseRelocation,
                tiles,
                occupiedCoordinates,
                randomSource,
                true,
                location,
                out selectedTile);
        }

        /// <summary>
        /// Called by the current immediate presenter, or later by an animation
        /// presenter when its appearance sequence has completed.
        /// </summary>
        public bool TryCompleteAppearance()
        {
            if (state != KeyShopLifecycleState.Appearing || !hasLocation)
                return false;

            TransitionTo(KeyShopLifecycleState.Active);
            return true;
        }

        /// <summary>
        /// Clears match-scoped state. The authoritative integration should invoke
        /// this before turn one when the same scene instance is reused.
        /// </summary>
        public void ResetToInactive()
        {
            var previousState = state;
            var hadMatchState = state != KeyShopLifecycleState.Inactive || hasLocation;
            _hasPreviousLocation = hasLocation;
            _previousLocation = location;
            _placementReason = KeyShopPlacementReason.None;
            hasLocation = false;
            location = default;
            _currentTile = null;
            state = KeyShopLifecycleState.Inactive;
            if (hadMatchState)
                placementRevision++;

            var lifecycleEvent = CreateEvent(previousState);
            StateChanged?.Invoke(lifecycleEvent);
        }

        private bool TryBeginPlacement(
            KeyShopPlacementReason reason,
            IReadOnlyList<BoardTile> tiles,
            IReadOnlyCollection<Vector2Int> occupiedCoordinates,
            IKeyShopRandomSource randomSource,
            bool excludePreviousLocation,
            Vector2Int previousLocation,
            out BoardTile selectedTile)
        {
            selectedTile = null;
            var candidates = KeyShopPlacementPolicy.BuildCandidates(
                tiles,
                occupiedCoordinates,
                excludePreviousLocation,
                previousLocation);
            if (candidates.Count == 0)
                return false;

            _placementReason = reason;
            _hasPreviousLocation = hasLocation;
            _previousLocation = location;
            placementRevision++;

            hasLocation = false;
            _currentTile = null;
            TransitionTo(KeyShopLifecycleState.Preparing);

            if (!KeyShopPlacementPolicy.TryChooseCandidate(
                    candidates,
                    randomSource,
                    out selectedTile))
            {
                throw new InvalidOperationException(
                    "A non-empty key-shop candidate set could not produce a selection.");
            }

            location = selectedTile.Coordinate;
            hasLocation = true;
            _currentTile = selectedTile;
            TransitionTo(KeyShopLifecycleState.Appearing);
            return true;
        }

        private void TransitionTo(KeyShopLifecycleState nextState)
        {
            var previousState = state;
            state = nextState;
            var lifecycleEvent = CreateEvent(previousState);
            StateChanged?.Invoke(lifecycleEvent);

            switch (nextState)
            {
                case KeyShopLifecycleState.Preparing:
                    Preparing?.Invoke(lifecycleEvent);
                    break;
                case KeyShopLifecycleState.Appearing:
                    Appearing?.Invoke(lifecycleEvent);
                    break;
                case KeyShopLifecycleState.Active:
                    Activated?.Invoke(lifecycleEvent);
                    break;
            }
        }

        private KeyShopLifecycleEvent CreateEvent(KeyShopLifecycleState previousState)
        {
            var hasTarget = state == KeyShopLifecycleState.Appearing ||
                            state == KeyShopLifecycleState.Active;
            return new KeyShopLifecycleEvent(
                previousState,
                state,
                _placementReason,
                placementRevision,
                _hasPreviousLocation,
                _previousLocation,
                hasTarget && hasLocation,
                location);
        }
    }
}
