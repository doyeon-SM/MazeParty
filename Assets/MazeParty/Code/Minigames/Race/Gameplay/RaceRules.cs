using System;
using System.Collections.Generic;

namespace MazeParty.Gameplay.Minigames.Race
{
    public enum RaceStepInput : byte
    {
        None = 0,
        Left = 1,
        Right = 2
    }

    public readonly struct RaceRoundEntry
    {
        public RaceRoundEntry(int playerSlot, int progress, int rank)
        {
            PlayerSlot = playerSlot;
            Progress = progress;
            Rank = rank;
        }

        public int PlayerSlot { get; }
        public int Progress { get; }
        public int Rank { get; }
    }

    public readonly struct RaceLeaderboardEntry
    {
        public RaceLeaderboardEntry(int playerSlot, int totalPoints, int rank)
        {
            PlayerSlot = playerSlot;
            TotalPoints = totalPoints;
            Rank = rank;
        }

        public int PlayerSlot { get; }
        public int TotalPoints { get; }
        public int Rank { get; }
    }

    public static class RaceRules
    {
        public const int PlayerCount = 4;
        public const int RoundCount = 3;
        public const int RequiredSteps = 500;
        public const double RoundSeconds = 60d;

        public static bool IsValidPlayerSlot(int slot)
        {
            return slot >= 0 && slot < PlayerCount;
        }

        public static bool IsValidStepInput(RaceStepInput input)
        {
            return input == RaceStepInput.Left ||
                   input == RaceStepInput.Right;
        }

        public static bool IsAlternatingStep(
            RaceStepInput previous,
            RaceStepInput current)
        {
            return IsValidStepInput(current) &&
                   (previous == RaceStepInput.None || previous != current);
        }

        public static IReadOnlyList<RaceRoundEntry> BuildRoundLeaderboard(
            IReadOnlyList<int> progress,
            IReadOnlyList<ulong> progressOrder)
        {
            ValidateFour(progress, nameof(progress));
            ValidateFour(progressOrder, nameof(progressOrder));
            var slots = new[] { 0, 1, 2, 3 };
            Array.Sort(
                slots,
                (left, right) =>
                {
                    var progressCompare =
                        progress[right].CompareTo(progress[left]);
                    if (progressCompare != 0)
                    {
                        return progressCompare;
                    }

                    var leftOrder = progressOrder[left];
                    var rightOrder = progressOrder[right];
                    if (leftOrder != rightOrder)
                    {
                        if (leftOrder == 0UL) return 1;
                        if (rightOrder == 0UL) return -1;
                        return leftOrder.CompareTo(rightOrder);
                    }
                    return left.CompareTo(right);
                });

            var result = new RaceRoundEntry[PlayerCount];
            for (var index = 0; index < slots.Length; index++)
            {
                var slot = slots[index];
                result[index] = new RaceRoundEntry(
                    slot,
                    Math.Max(0, Math.Min(RequiredSteps, progress[slot])),
                    index + 1);
            }
            return result;
        }

        public static int[] BuildRoundPoints(
            IReadOnlyList<RaceRoundEntry> leaderboard)
        {
            ValidateFour(leaderboard, nameof(leaderboard));
            var result = new int[PlayerCount];
            var seenSlots = 0;
            var seenRanks = 0;
            for (var index = 0; index < leaderboard.Count; index++)
            {
                var entry = leaderboard[index];
                if (!IsValidPlayerSlot(entry.PlayerSlot) ||
                    entry.Rank < 1 || entry.Rank > PlayerCount)
                {
                    throw new ArgumentException(
                        "Leaderboard entries must use unique valid slots and ranks.",
                        nameof(leaderboard));
                }

                var slotBit = 1 << entry.PlayerSlot;
                var rankBit = 1 << (entry.Rank - 1);
                if ((seenSlots & slotBit) != 0 ||
                    (seenRanks & rankBit) != 0)
                {
                    throw new ArgumentException(
                        "Leaderboard entries must use unique valid slots and ranks.",
                        nameof(leaderboard));
                }
                seenSlots |= slotBit;
                seenRanks |= rankBit;
                result[entry.PlayerSlot] = GetPointsForRank(entry.Rank);
            }
            return result;
        }

        public static IReadOnlyList<RaceLeaderboardEntry>
            BuildFinalLeaderboard(IReadOnlyList<int> totalPoints)
        {
            ValidateFour(totalPoints, nameof(totalPoints));
            var slots = new[] { 0, 1, 2, 3 };
            Array.Sort(
                slots,
                (left, right) =>
                {
                    var scoreCompare =
                        totalPoints[right].CompareTo(totalPoints[left]);
                    return scoreCompare != 0
                        ? scoreCompare
                        : left.CompareTo(right);
                });

            var result = new RaceLeaderboardEntry[PlayerCount];
            for (var index = 0; index < slots.Length; index++)
            {
                var slot = slots[index];
                result[index] = new RaceLeaderboardEntry(
                    slot,
                    totalPoints[slot],
                    index + 1);
            }
            return result;
        }

        public static int GetPointsForRank(int rank)
        {
            return rank >= 1 && rank <= PlayerCount
                ? PlayerCount - rank
                : 0;
        }

        private static void ValidateFour<T>(
            IReadOnlyList<T> values,
            string parameterName)
        {
            if (values == null || values.Count != PlayerCount)
            {
                throw new ArgumentException(
                    "Exactly four player values are required.",
                    parameterName);
            }
        }
    }
}
