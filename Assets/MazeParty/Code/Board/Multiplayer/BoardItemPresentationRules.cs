using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Pure presentation policy shared by the board item runtime and its
    /// long-lived boundary tests.
    /// </summary>
    public static class BoardItemPresentationRules
    {
        public static bool IsFirearm(PrototypeItemId item)
        {
            return item == PrototypeItemId.Pistol ||
                   item == PrototypeItemId.Sniper;
        }

        public static bool ShouldHighlightFirearmTarget(
            bool hitPlayer,
            int targetHealth,
            bool isCloaked,
            bool openingProtected,
            double personalProtectionRemaining)
        {
            return hitPlayer &&
                   targetHealth > 0 &&
                   !isCloaked &&
                   !openingProtected &&
                   personalProtectionRemaining <= 0d;
        }

        public static bool ShouldShowGrenadeRange(
            bool isOwner,
            bool canAcceptActionInput,
            int currentHealth,
            ItemChoiceResolution choice,
            PrototypeItemId equippedItem,
            int charges,
            bool localUsePending)
        {
            return isOwner &&
                   canAcceptActionInput &&
                   currentHealth > 0 &&
                   choice == ItemChoiceResolution.ItemSelected &&
                   equippedItem == PrototypeItemId.Grenade &&
                   charges > 0 &&
                   !localUsePending;
        }
    }

    /// <summary>
    /// Keeps the local grenade preview hidden from input until the server
    /// rejects that request or the authoritative selection becomes unavailable.
    /// </summary>
    public sealed class GrenadeRangeUseState
    {
        private uint _nextRequestId;
        private uint _pendingRequestId;

        public bool IsPending { get; private set; }

        public bool TryBegin(out uint requestId)
        {
            if (IsPending)
            {
                requestId = 0u;
                return false;
            }

            unchecked
            {
                _nextRequestId++;
                if (_nextRequestId == 0u)
                {
                    _nextRequestId = 1u;
                }
            }

            IsPending = true;
            _pendingRequestId = _nextRequestId;
            requestId = _pendingRequestId;
            return true;
        }

        public void Resolve(uint requestId, bool accepted)
        {
            if (!IsPending || requestId == 0u ||
                requestId != _pendingRequestId)
            {
                return;
            }

            if (!accepted)
            {
                Clear();
            }
        }

        public void ClearWhenUnavailable(bool selectionAvailable)
        {
            if (!selectionAvailable)
            {
                Clear();
            }
        }

        public void Clear()
        {
            IsPending = false;
            _pendingRequestId = 0u;
        }
    }

    /// <summary>
    /// Reconciles authoritative grenade snapshots by stable identity and samples
    /// their delayed presentation positions without changing gameplay state.
    /// </summary>
    public sealed class GrenadePresentationTrackSet
    {
        private sealed class Track
        {
            public Vector3 PreviousPosition;
            public Vector3 LatestPosition;
            public Vector3 LatestVelocity;
            public double PreviousTime;
            public double LatestTime;
            public bool HasLatest;
            public bool HasPrevious;
            public bool IsPendingRemoval;
            public double RemovalTime;

            public void Push(
                Vector3 position,
                Vector3 velocity,
                double snapshotTime)
            {
                if (IsPendingRemoval && snapshotTime < RemovalTime)
                {
                    return;
                }

                if (!HasLatest)
                {
                    PreviousPosition = position;
                    LatestPosition = position;
                    LatestVelocity = velocity;
                    PreviousTime = snapshotTime;
                    LatestTime = snapshotTime;
                    HasLatest = true;
                    IsPendingRemoval = false;
                    RemovalTime = 0d;
                    return;
                }

                if (snapshotTime < LatestTime)
                {
                    return;
                }

                IsPendingRemoval = false;
                RemovalTime = 0d;

                if (snapshotTime > LatestTime)
                {
                    PreviousPosition = LatestPosition;
                    PreviousTime = LatestTime;
                    HasPrevious = true;
                }

                LatestPosition = position;
                LatestVelocity = velocity;
                LatestTime = snapshotTime;
            }

            public void MarkPendingRemoval(double snapshotTime)
            {
                if (IsPendingRemoval || snapshotTime < LatestTime)
                {
                    return;
                }

                IsPendingRemoval = true;
                RemovalTime = snapshotTime;
            }

            public bool IsExpired(double now, double interpolationDelay)
            {
                return IsPendingRemoval &&
                       now - interpolationDelay >= RemovalTime - 1e-9d;
            }

            public Vector3 Sample(
                double now,
                double interpolationDelay,
                double maxExtrapolation)
            {
                var presentationTime = now - interpolationDelay;
                if (HasPrevious && presentationTime < LatestTime)
                {
                    if (presentationTime <= PreviousTime)
                    {
                        return PreviousPosition;
                    }

                    var duration = LatestTime - PreviousTime;
                    if (duration > 0d)
                    {
                        var progress = (float)((presentationTime - PreviousTime) /
                                               duration);
                        return Vector3.LerpUnclamped(
                            PreviousPosition,
                            LatestPosition,
                            progress);
                    }
                }

                var extrapolation = Math.Min(
                    Math.Max(0d, presentationTime - LatestTime),
                    maxExtrapolation);
                return LatestPosition + LatestVelocity * (float)extrapolation;
            }
        }

        private readonly Dictionary<uint, Track> _tracks =
            new Dictionary<uint, Track>();
        private readonly HashSet<uint> _snapshotIds = new HashSet<uint>();
        private readonly List<uint> _staleIds = new List<uint>();
        private readonly double _interpolationDelay;
        private readonly double _maxExtrapolation;

        public GrenadePresentationTrackSet(
            double interpolationDelay,
            double maxExtrapolation)
        {
            _interpolationDelay = Math.Max(0d, interpolationDelay);
            _maxExtrapolation = Math.Max(0d, maxExtrapolation);
        }

        public bool ApplySnapshot(
            IReadOnlyList<uint> ids,
            IReadOnlyList<Vector3> positions,
            IReadOnlyList<Vector3> velocities,
            double snapshotTime,
            List<uint> addedIds,
            List<uint> removedIds)
        {
            if (addedIds == null || removedIds == null)
            {
                throw new ArgumentNullException(
                    addedIds == null ? nameof(addedIds) : nameof(removedIds));
            }

            addedIds.Clear();
            removedIds.Clear();
            if (ids == null || positions == null || velocities == null ||
                ids.Count != positions.Count ||
                ids.Count != velocities.Count ||
                double.IsNaN(snapshotTime) ||
                double.IsInfinity(snapshotTime))
            {
                return false;
            }

            _snapshotIds.Clear();
            for (var index = 0; index < ids.Count; index++)
            {
                if (ids[index] == 0u ||
                    !_snapshotIds.Add(ids[index]) ||
                    !IsFinite(positions[index]) ||
                    !IsFinite(velocities[index]))
                {
                    _snapshotIds.Clear();
                    return false;
                }
            }

            for (var index = 0; index < ids.Count; index++)
            {
                var id = ids[index];
                if (!_tracks.TryGetValue(id, out var track))
                {
                    track = new Track();
                    _tracks.Add(id, track);
                    addedIds.Add(id);
                }

                track.Push(
                    positions[index],
                    velocities[index],
                    snapshotTime);
            }

            _staleIds.Clear();
            foreach (var pair in _tracks)
            {
                if (!_snapshotIds.Contains(pair.Key))
                {
                    pair.Value.MarkPendingRemoval(snapshotTime);
                }
            }

            _snapshotIds.Clear();
            _staleIds.Clear();
            return true;
        }

        public bool RemoveExpired(double now, List<uint> removedIds)
        {
            if (removedIds == null)
            {
                throw new ArgumentNullException(nameof(removedIds));
            }

            removedIds.Clear();
            if (double.IsNaN(now) || double.IsInfinity(now))
            {
                return false;
            }

            _staleIds.Clear();
            foreach (var pair in _tracks)
            {
                if (pair.Value.IsExpired(now, _interpolationDelay))
                {
                    _staleIds.Add(pair.Key);
                }
            }

            foreach (var id in _staleIds)
            {
                _tracks.Remove(id);
                removedIds.Add(id);
            }

            _staleIds.Clear();
            return true;
        }

        public bool TrySample(uint id, double now, out Vector3 position)
        {
            if (_tracks.TryGetValue(id, out var track) &&
                !track.IsExpired(now, _interpolationDelay))
            {
                position = track.Sample(
                    now,
                    _interpolationDelay,
                    _maxExtrapolation);
                return true;
            }

            position = default;
            return false;
        }

        public void Clear()
        {
            _tracks.Clear();
            _snapshotIds.Clear();
            _staleIds.Clear();
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
