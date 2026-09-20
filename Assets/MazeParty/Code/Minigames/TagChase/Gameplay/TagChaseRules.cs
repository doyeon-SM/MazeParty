using System;
using System.Collections.Generic;

namespace MazeParty.Gameplay.Minigames.TagChase
{
    public readonly struct TagChaseLeaderboardEntry
    {
        public TagChaseLeaderboardEntry(
            int playerSlot,
            int totalPoints,
            int rank)
        {
            PlayerSlot = playerSlot;
            TotalPoints = totalPoints;
            Rank = rank;
        }

        public int PlayerSlot { get; }
        public int TotalPoints { get; }
        public int Rank { get; }
    }

    public static class TagChaseRules
    {
        public const int PlayerCount = 4;
        public const int RoundCount = 4;
        public const double RoundSeconds = 60d;
        public const int TaggerWinPoints = 3;
        public const int TaggerLossPoints = 0;
        public const int RunnerSurvivalPoints = 2;
        public const int RunnerCaughtPoints = 1;

        public static bool IsValidPlayerSlot(int slot)
        {
            return slot >= 0 && slot < PlayerCount;
        }

        public static int[] BuildTaggerOrder(ulong seed)
        {
            var order = new[] { 0, 1, 2, 3 };
            var random = new StableRandom(seed);
            for (var index = order.Length - 1; index > 0; index--)
            {
                var swapIndex = random.Next(index + 1);
                var swap = order[index];
                order[index] = order[swapIndex];
                order[swapIndex] = swap;
            }

            return order;
        }

        public static bool AreAllRunnersCaught(
            int taggerSlot,
            byte caughtMask)
        {
            ValidateSlot(taggerSlot);
            var runnerMask =
                ((1 << PlayerCount) - 1) & ~(1 << taggerSlot);
            return (caughtMask & runnerMask) == runnerMask;
        }

        public static int[] BuildRoundPoints(
            int taggerSlot,
            byte caughtMask)
        {
            ValidateSlot(taggerSlot);
            var points = new int[PlayerCount];
            var taggerWon =
                AreAllRunnersCaught(taggerSlot, caughtMask);
            for (var slot = 0; slot < PlayerCount; slot++)
            {
                if (slot == taggerSlot)
                {
                    points[slot] = taggerWon
                        ? TaggerWinPoints
                        : TaggerLossPoints;
                    continue;
                }

                points[slot] =
                    (caughtMask & (1 << slot)) != 0
                        ? RunnerCaughtPoints
                        : RunnerSurvivalPoints;
            }

            if (taggerWon)
            {
                for (var slot = 0; slot < PlayerCount; slot++)
                {
                    if (slot != taggerSlot)
                    {
                        points[slot] = 0;
                    }
                }
            }

            return points;
        }

        public static IReadOnlyList<TagChaseLeaderboardEntry>
            BuildFinalLeaderboard(IReadOnlyList<int> totalPoints)
        {
            if (totalPoints == null ||
                totalPoints.Count != PlayerCount)
            {
                throw new ArgumentException(
                    "Exactly four total scores are required.",
                    nameof(totalPoints));
            }

            var slots = new int[PlayerCount];
            for (var slot = 0; slot < PlayerCount; slot++)
            {
                slots[slot] = slot;
            }

            Array.Sort(
                slots,
                (left, right) =>
                {
                    var scoreOrder =
                        totalPoints[right].CompareTo(totalPoints[left]);
                    return scoreOrder != 0
                        ? scoreOrder
                        : left.CompareTo(right);
                });

            var result =
                new TagChaseLeaderboardEntry[PlayerCount];
            for (var index = 0; index < slots.Length; index++)
            {
                var slot = slots[index];
                result[index] =
                    new TagChaseLeaderboardEntry(
                        slot,
                        totalPoints[slot],
                        index + 1);
            }

            return result;
        }

        private static void ValidateSlot(int slot)
        {
            if (!IsValidPlayerSlot(slot))
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        private struct StableRandom
        {
            private ulong _state;

            public StableRandom(ulong seed)
            {
                _state = seed ^ 0x9E3779B97F4A7C15UL;
                if (_state == 0UL)
                {
                    _state = 0xD1B54A32D192ED03UL;
                }
            }

            public int Next(int exclusiveMaximum)
            {
                if (exclusiveMaximum <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(exclusiveMaximum));
                }

                return (int)(NextUInt() %
                    (uint)exclusiveMaximum);
            }

            private uint NextUInt()
            {
                var value = _state;
                value ^= value >> 12;
                value ^= value << 25;
                value ^= value >> 27;
                _state = value;
                return (uint)(
                    value * 0x2545F4914F6CDD1DUL >> 32);
            }
        }
    }
}
