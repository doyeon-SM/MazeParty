using System;
using System.Collections.Generic;

namespace MazeParty.Gameplay.Minigames
{
    /// <summary>
    /// Stable identifiers used by the host-owned minigame schedule.
    /// Serialized numeric values must not be reordered or reused.
    /// </summary>
    public enum ScheduledMinigameId : byte
    {
        Skip = 0,
        Minefield = 1,
        WrongWay = 2
    }

    public static class MinigameScheduleRules
    {
        public const int DefaultTurnCount = 15;

        private static readonly ScheduledMinigameId[] RegisteredGames =
        {
            ScheduledMinigameId.Minefield,
            ScheduledMinigameId.WrongWay
        };

        public static int RegisteredGameCount => RegisteredGames.Length;

        public static ScheduledMinigameId GetRegisteredGame(int index)
        {
            if (index < 0 || index >= RegisteredGames.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return RegisteredGames[index];
        }

        public static bool IsRegisteredGame(ScheduledMinigameId minigame)
        {
            for (var index = 0; index < RegisteredGames.Length; index++)
            {
                if (RegisteredGames[index] == minigame)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsKnownValue(ScheduledMinigameId minigame)
        {
            return minigame == ScheduledMinigameId.Skip ||
                   IsRegisteredGame(minigame);
        }
    }

    /// <summary>
    /// Immutable, host-owned schedule for one match. The complete schedule contains
    /// unrevealed future turns and must never be replicated to clients. Replicate only
    /// the value returned for the current one-based turn.
    /// </summary>
    public sealed class HostMinigameSchedule
    {
        private readonly ScheduledMinigameId[] _entries;

        private HostMinigameSchedule(int seed, ScheduledMinigameId[] entries)
        {
            Seed = seed;
            _entries = entries;
        }

        public int Seed { get; }
        public int TurnCount => _entries.Length;

        public static HostMinigameSchedule Create(
            int seed,
            int totalTurns = MinigameScheduleRules.DefaultTurnCount)
        {
            if (totalTurns < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(totalTurns),
                    totalTurns,
                    "The schedule must contain at least one turn.");
            }

            var entries = new ScheduledMinigameId[totalTurns];
            var registered = new ScheduledMinigameId[
                MinigameScheduleRules.RegisteredGameCount];
            for (var index = 0; index < registered.Length; index++)
            {
                registered[index] =
                    MinigameScheduleRules.GetRegisteredGame(index);
            }

            var random = new StableScheduleRandom(seed);
            for (var index = registered.Length - 1; index > 0; index--)
            {
                var swapIndex = random.Next(index + 1);
                var swap = registered[index];
                registered[index] = registered[swapIndex];
                registered[swapIndex] = swap;
            }

            var scheduledGameCount =
                Math.Min(totalTurns, registered.Length);
            for (var index = 0; index < scheduledGameCount; index++)
            {
                entries[index] = registered[index];
            }

            for (var index = entries.Length - 1; index > 0; index--)
            {
                var swapIndex = random.Next(index + 1);
                var swap = entries[index];
                entries[index] = entries[swapIndex];
                entries[swapIndex] = swap;
            }

            return new HostMinigameSchedule(seed, entries);
        }

        public ScheduledMinigameId GetMinigameForTurn(int oneBasedTurn)
        {
            if (oneBasedTurn < 1 || oneBasedTurn > _entries.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(oneBasedTurn));
            }

            return _entries[oneBasedTurn - 1];
        }

        public bool TryGetMinigameForTurn(
            int oneBasedTurn,
            out ScheduledMinigameId minigame)
        {
            if (oneBasedTurn < 1 || oneBasedTurn > _entries.Length)
            {
                minigame = ScheduledMinigameId.Skip;
                return false;
            }

            minigame = _entries[oneBasedTurn - 1];
            return true;
        }

        internal static HostMinigameSchedule Restore(
            int seed,
            IReadOnlyList<ScheduledMinigameId> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            if (entries.Count < 1)
            {
                throw new ArgumentException(
                    "The stored schedule is empty.",
                    nameof(entries));
            }

            var restored = new ScheduledMinigameId[entries.Count];
            var registeredSeen =
                new bool[MinigameScheduleRules.RegisteredGameCount];
            var scheduledGameCount = 0;

            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                if (!MinigameScheduleRules.IsKnownValue(entry))
                {
                    throw new ArgumentException(
                        "The stored schedule contains an unknown minigame id.",
                        nameof(entries));
                }

                restored[index] = entry;
                for (var gameIndex = 0;
                     gameIndex < MinigameScheduleRules.RegisteredGameCount;
                     gameIndex++)
                {
                    if (entry == MinigameScheduleRules.GetRegisteredGame(gameIndex))
                    {
                        if (registeredSeen[gameIndex])
                        {
                            throw new ArgumentException(
                                "A registered minigame occurs more than once.",
                                nameof(entries));
                        }

                        registeredSeen[gameIndex] = true;
                        scheduledGameCount++;
                        break;
                    }
                }
            }

            var expectedGameCount = Math.Min(
                entries.Count,
                MinigameScheduleRules.RegisteredGameCount);
            if (scheduledGameCount != expectedGameCount)
            {
                throw new ArgumentException(
                    "The stored schedule does not contain the expected " +
                    "number of distinct registered minigames.",
                    nameof(entries));
            }

            return new HostMinigameSchedule(seed, restored);
        }

        internal ScheduledMinigameId[] CopyEntriesForHostPersistence()
        {
            return (ScheduledMinigameId[])_entries.Clone();
        }

        private struct StableScheduleRandom
        {
            private uint _state;

            public StableScheduleRandom(int seed)
            {
                _state = unchecked((uint)seed) ^ 0xA511E9B3u;
                if (_state == 0u)
                {
                    _state = 0x6D2B79F5u;
                }
            }

            public int Next(int exclusiveMaximum)
            {
                if (exclusiveMaximum <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
                }

                var bound = (uint)exclusiveMaximum;
                var threshold = unchecked(0u - bound) % bound;
                uint value;
                do
                {
                    value = NextUInt();
                }
                while (value < threshold);

                return (int)(value % bound);
            }

            private uint NextUInt()
            {
                var value = _state;
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                _state = value;
                return value;
            }
        }
    }
}
