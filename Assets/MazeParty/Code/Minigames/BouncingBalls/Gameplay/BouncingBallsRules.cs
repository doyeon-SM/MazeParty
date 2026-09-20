using System;
using System.Collections.Generic;

namespace MazeParty.Gameplay.Minigames.BouncingBalls
{
    /// <summary>
    /// Authoritative two-dimensional arena dimensions and match timing.
    /// Slots are clockwise from the bottom: bottom, right, top, left.
    /// </summary>
    public static class BouncingBallsRules
    {
        public const int PlayerCount = 4;
        public const int BallCount = 3;
        public const int RoundCount = 2;
        public const double RoundSeconds = 60d;
        public const double CountdownSeconds = 3d;
        public const double ResultSeconds = 4d;
        public const int SimulationHz = 60;
        public const double SimulationStepSeconds = 1d / SimulationHz;

        public const double ArenaHalfExtent = 8d;
        public const double GoalHalfWidth = 3.1d;
        public const double ShieldRailDistance = 7.35d;
        public const double ShieldHalfWidth = 1.1d;
        public const double ShieldMaximumOffset = 1.85d;
        public const double ShieldSpeed = 5.4d;
        public const double BallRadius = 0.24d;
        public const double BallSpeed = 7.5d;
        public const double RespawnVariationRadians = 0.35d;
        public const int NoOwnerSlot = -1;

        public static bool IsValidPlayerSlot(int slot)
        {
            return slot >= 0 && slot < PlayerCount;
        }

        public static bool IsValidBallId(int ballId)
        {
            return ballId >= 0 && ballId < BallCount;
        }

        internal static void ValidateRoundElapsed(double seconds)
        {
            if (double.IsNaN(seconds) ||
                double.IsInfinity(seconds) ||
                seconds < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(seconds),
                    seconds,
                    "Active round time must be finite and non-negative.");
            }
        }
    }

    /// <summary>
    /// The match awards no per-round placement points. Cumulative goals
    /// decide rank, then fewer conceded goals and finally stable server slot.
    /// </summary>
    public static class BouncingBallsRanking
    {
        public static int[] BuildRanksBySlot(
            IReadOnlyList<int> scores,
            IReadOnlyList<int> conceded)
        {
            if (scores == null)
            {
                throw new ArgumentNullException(nameof(scores));
            }

            if (conceded == null)
            {
                throw new ArgumentNullException(nameof(conceded));
            }

            if (scores.Count != BouncingBallsRules.PlayerCount ||
                conceded.Count != BouncingBallsRules.PlayerCount)
            {
                throw new ArgumentException(
                    "Scores and conceded goals require four player slots.");
            }

            var slots = new int[BouncingBallsRules.PlayerCount];
            for (var slot = 0; slot < slots.Length; slot++)
            {
                if (scores[slot] < 0 || conceded[slot] < 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(scores),
                        "Goal counts cannot be negative.");
                }

                slots[slot] = slot;
            }

            Array.Sort(
                slots,
                (left, right) =>
                {
                    var scoreOrder = scores[right].CompareTo(scores[left]);
                    if (scoreOrder != 0)
                    {
                        return scoreOrder;
                    }

                    var concededOrder =
                        conceded[left].CompareTo(conceded[right]);
                    return concededOrder != 0
                        ? concededOrder
                        : left.CompareTo(right);
                });

            var ranks = new int[BouncingBallsRules.PlayerCount];
            for (var index = 0; index < slots.Length; index++)
            {
                ranks[slots[index]] = index + 1;
            }

            return ranks;
        }
    }
}
