using System;
using System.Collections.Generic;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Shared rules for the fixed four-player game. Keeping validation here
    /// prevents each board and minigame system from inventing its own bounds.
    /// </summary>
    public static class PlayerSlotRules
    {
        public const int Count = 4;

        public static bool IsValid(int slot)
        {
            return slot >= 0 && slot < Count;
        }

        public static void Validate(int slot, string parameterName = "slot")
        {
            if (!IsValid(slot))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    slot,
                    "A player slot must be between 0 and 3.");
            }
        }
    }

    /// <summary>
    /// A four-player bit set. Only the low four bits are retained so values
    /// received from byte-based persistence and networking boundaries cannot
    /// leak unrelated flags into player membership checks.
    /// </summary>
    public readonly struct PlayerMask4 : IEquatable<PlayerMask4>
    {
        public const byte ValidBits = (1 << PlayerSlotRules.Count) - 1;

        private readonly byte _bits;

        private PlayerMask4(byte bits)
        {
            _bits = (byte)(bits & ValidBits);
        }

        public static PlayerMask4 None => default;
        public static PlayerMask4 All => new PlayerMask4(ValidBits);

        public byte Bits => _bits;
        public bool IsEmpty => _bits == 0;
        public bool IsAll => _bits == ValidBits;

        public int Count =>
            (_bits & 1) +
            ((_bits >> 1) & 1) +
            ((_bits >> 2) & 1) +
            ((_bits >> 3) & 1);

        public static PlayerMask4 FromBits(byte bits)
        {
            return new PlayerMask4(bits);
        }

        public bool Contains(int slot)
        {
            return PlayerSlotRules.IsValid(slot) &&
                   (_bits & (1 << slot)) != 0;
        }

        public PlayerMask4 With(int slot, bool included = true)
        {
            PlayerSlotRules.Validate(slot);
            var bit = 1 << slot;
            return new PlayerMask4(
                included
                    ? (byte)(_bits | bit)
                    : (byte)(_bits & ~bit));
        }

        public PlayerMask4 Without(int slot)
        {
            return With(slot, false);
        }

        public PlayerMask4 Union(PlayerMask4 other)
        {
            return new PlayerMask4((byte)(_bits | other._bits));
        }

        public PlayerMask4 Intersect(PlayerMask4 other)
        {
            return new PlayerMask4((byte)(_bits & other._bits));
        }

        public bool Equals(PlayerMask4 other)
        {
            return _bits == other._bits;
        }

        public override bool Equals(object obj)
        {
            return obj is PlayerMask4 other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _bits;
        }

        public override string ToString()
        {
            return Convert.ToString(_bits, 2).PadLeft(PlayerSlotRules.Count, '0');
        }

        public static bool operator ==(PlayerMask4 left, PlayerMask4 right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PlayerMask4 left, PlayerMask4 right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Small immutable value collection for logical state that must contain
    /// exactly one value per player. Unity-serialized reference arrays and
    /// mutable high-frequency buffers should remain arrays.
    /// </summary>
    public readonly struct PlayerSlots<T> : IEquatable<PlayerSlots<T>>
    {
        private readonly T _slot0;
        private readonly T _slot1;
        private readonly T _slot2;
        private readonly T _slot3;

        public PlayerSlots(T slot0, T slot1, T slot2, T slot3)
        {
            _slot0 = slot0;
            _slot1 = slot1;
            _slot2 = slot2;
            _slot3 = slot3;
        }

        public T this[int slot]
        {
            get
            {
                PlayerSlotRules.Validate(slot);
                return slot switch
                {
                    0 => _slot0,
                    1 => _slot1,
                    2 => _slot2,
                    _ => _slot3
                };
            }
        }

        public static PlayerSlots<T> From(IReadOnlyList<T> values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (values.Count != PlayerSlotRules.Count)
            {
                throw new ArgumentException(
                    "Exactly four player-slot values are required.",
                    nameof(values));
            }

            return new PlayerSlots<T>(
                values[0],
                values[1],
                values[2],
                values[3]);
        }

        public PlayerSlots<T> With(int slot, T value)
        {
            PlayerSlotRules.Validate(slot);
            return slot switch
            {
                0 => new PlayerSlots<T>(value, _slot1, _slot2, _slot3),
                1 => new PlayerSlots<T>(_slot0, value, _slot2, _slot3),
                2 => new PlayerSlots<T>(_slot0, _slot1, value, _slot3),
                _ => new PlayerSlots<T>(_slot0, _slot1, _slot2, value)
            };
        }

        public T[] ToArray()
        {
            return new[] { _slot0, _slot1, _slot2, _slot3 };
        }

        public bool Equals(PlayerSlots<T> other)
        {
            var comparer = EqualityComparer<T>.Default;
            return comparer.Equals(_slot0, other._slot0) &&
                   comparer.Equals(_slot1, other._slot1) &&
                   comparer.Equals(_slot2, other._slot2) &&
                   comparer.Equals(_slot3, other._slot3);
        }

        public override bool Equals(object obj)
        {
            return obj is PlayerSlots<T> other && Equals(other);
        }

        public override int GetHashCode()
        {
            var comparer = EqualityComparer<T>.Default;
            unchecked
            {
                var hash = comparer.GetHashCode(_slot0);
                hash = (hash * 397) ^ comparer.GetHashCode(_slot1);
                hash = (hash * 397) ^ comparer.GetHashCode(_slot2);
                hash = (hash * 397) ^ comparer.GetHashCode(_slot3);
                return hash;
            }
        }

        public static bool operator ==(
            PlayerSlots<T> left,
            PlayerSlots<T> right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            PlayerSlots<T> left,
            PlayerSlots<T> right)
        {
            return !left.Equals(right);
        }
    }
}
