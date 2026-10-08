using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Owner-local knowledge used by board map presentation. It remembers the last
    /// observed logical tile separately from the players visible in the current
    /// observation frame, so losing sight hides a minimap marker without erasing the
    /// full-map history.
    /// </summary>
    public sealed class BoardPlayerMapKnowledge
    {
        private readonly Vector2Int[] _lastKnown =
            new Vector2Int[PlayerSlotRules.Count];
        private readonly Vector2Int[] _observed =
            new Vector2Int[PlayerSlotRules.Count];

        private byte _knownMask;
        private byte _currentVisibleMask;
        private byte _observedMask;
        private bool _observationFrameOpen;
        private int _revision;

        public byte KnownMask => _knownMask;
        public byte CurrentVisibleMask => _currentVisibleMask;
        public int Revision => _revision;
        public bool IsObservationFrameOpen => _observationFrameOpen;

        public void Reset()
        {
            var changed = _knownMask != 0 || _currentVisibleMask != 0;
            Array.Clear(_lastKnown, 0, _lastKnown.Length);
            Array.Clear(_observed, 0, _observed.Length);
            _knownMask = 0;
            _currentVisibleMask = 0;
            _observedMask = 0;
            _observationFrameOpen = false;
            if (changed)
            {
                IncrementRevision();
            }
        }

        /// <summary>
        /// Records initial knowledge without replacing a position learned later.
        /// Seeding never makes the player currently visible.
        /// </summary>
        public bool SeedIfUnknown(int slot, Vector2Int coordinate)
        {
            PlayerSlotRules.Validate(slot);
            var bit = BitFor(slot);
            if ((_knownMask & bit) != 0)
            {
                return false;
            }

            _lastKnown[slot] = coordinate;
            _knownMask |= bit;
            IncrementRevision();
            return true;
        }

        /// <summary>
        /// Starts an atomic visibility sample. Call <see cref="Observe"/> for every
        /// player seen this frame, then <see cref="EndObservationFrame"/> once.
        /// </summary>
        public void BeginObservationFrame()
        {
            _observedMask = 0;
            _observationFrameOpen = true;
        }

        /// <summary>
        /// Stages a server-authored logical tile for a player visible this frame.
        /// Repeated observations of one slot keep the latest coordinate.
        /// </summary>
        public void Observe(int slot, Vector2Int coordinate)
        {
            PlayerSlotRules.Validate(slot);
            if (!_observationFrameOpen)
            {
                throw new InvalidOperationException(
                    "BeginObservationFrame must be called before Observe.");
            }

            _observed[slot] = coordinate;
            _observedMask |= BitFor(slot);
        }

        /// <summary>
        /// Commits one observation frame. The revision changes at most once even when
        /// several players move or visibility changes together.
        /// </summary>
        public bool EndObservationFrame()
        {
            if (!_observationFrameOpen)
            {
                return false;
            }

            var changed = _currentVisibleMask != _observedMask;
            for (var slot = 0; slot < PlayerSlotRules.Count; slot++)
            {
                var bit = BitFor(slot);
                if ((_observedMask & bit) == 0)
                {
                    continue;
                }

                if ((_knownMask & bit) == 0 ||
                    _lastKnown[slot] != _observed[slot])
                {
                    _lastKnown[slot] = _observed[slot];
                    _knownMask |= bit;
                    changed = true;
                }
            }

            _currentVisibleMask = _observedMask;
            _observedMask = 0;
            _observationFrameOpen = false;
            if (changed)
            {
                IncrementRevision();
            }

            return changed;
        }

        public bool TryGetLastKnown(int slot, out Vector2Int coordinate)
        {
            PlayerSlotRules.Validate(slot);
            if ((_knownMask & BitFor(slot)) == 0)
            {
                coordinate = default;
                return false;
            }

            coordinate = _lastKnown[slot];
            return true;
        }

        public bool IsCurrentlyVisible(int slot)
        {
            PlayerSlotRules.Validate(slot);
            return (_currentVisibleMask & BitFor(slot)) != 0;
        }

        private static byte BitFor(int slot)
        {
            return (byte)(1 << slot);
        }

        private void IncrementRevision()
        {
            unchecked
            {
                _revision++;
            }
        }
    }
}
