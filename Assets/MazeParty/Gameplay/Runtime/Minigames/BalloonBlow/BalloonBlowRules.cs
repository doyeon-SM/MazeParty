using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MazeParty.Gameplay.Minigames.BalloonBlow
{
    /// <summary>
    /// Fixed rules shared by the authoritative server and Solo mode.
    /// All timing values use the active 30-second round clock.
    /// </summary>
    public static class BalloonBlowRules
    {
        public const int PlayerCount = 4;
        public const int RoundCount = 3;
        public const double RoundSeconds = 30d;
        public const float MinimumProgressPercent = 0f;
        public const float MaxProgressPercent = 100f;
        public const float InflatePercentPerSecond = 10f;
        public const float DeflatePercentPerSecond = 3f;
        public const double MaxContinuousInflateSeconds = 2d;
        public const double ReleaseCooldownSeconds = 1d;
        public const double OverholdCooldownSeconds = 1.5d;

        public static int GetPointsForRank(int rank)
        {
            if (rank < 1 || rank > PlayerCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rank), rank, "Rank must be between 1 and 4.");
            }

            return PlayerCount - rank;
        }

        public static bool IsValidPlayerSlot(int playerSlot)
        {
            return playerSlot >= 0 && playerSlot < PlayerCount;
        }

        internal static void ValidateRoundNumber(int roundNumber)
        {
            if (roundNumber < 1 || roundNumber > RoundCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(roundNumber),
                    roundNumber,
                    "Round number must be between 1 and 3.");
            }
        }

        internal static void ValidateActiveElapsedSeconds(double value)
        {
            if (double.IsNaN(value) ||
                double.IsInfinity(value) ||
                value < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Active elapsed time must be finite and non-negative.");
            }
        }

        internal static void ValidateProgressPercent(float value)
        {
            if (float.IsNaN(value) ||
                float.IsInfinity(value) ||
                value < MinimumProgressPercent ||
                value > MaxProgressPercent)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Progress must be between 0 and 100 percent.");
            }
        }
    }

    public readonly struct BalloonBlowRoundOutcome
    {
        private BalloonBlowRoundOutcome(
            int playerSlot,
            bool isPopped,
            double poppedAtSeconds,
            ulong popOrder,
            float progressPercent)
        {
            if (!BalloonBlowRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerSlot),
                    playerSlot,
                    "Player slot must be between 0 and 3.");
            }

            BalloonBlowRules.ValidateProgressPercent(progressPercent);
            if (isPopped)
            {
                BalloonBlowRules.ValidateActiveElapsedSeconds(
                    poppedAtSeconds);
                if (poppedAtSeconds > BalloonBlowRules.RoundSeconds)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(poppedAtSeconds));
                }

                if (popOrder == 0UL)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(popOrder),
                        "A popped balloon must have a server order.");
                }
            }
            else if (progressPercent >=
                     BalloonBlowRules.MaxProgressPercent)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(progressPercent),
                    "An unfinished balloon must be below 100 percent.");
            }

            PlayerSlot = playerSlot;
            IsPopped = isPopped;
            PoppedAtSeconds = poppedAtSeconds;
            PopOrder = popOrder;
            ProgressPercent = progressPercent;
        }

        public int PlayerSlot { get; }
        public bool IsPopped { get; }
        public double PoppedAtSeconds { get; }
        public ulong PopOrder { get; }
        public float ProgressPercent { get; }

        public static BalloonBlowRoundOutcome Popped(
            int playerSlot,
            double poppedAtSeconds,
            ulong popOrder)
        {
            return new BalloonBlowRoundOutcome(
                playerSlot,
                true,
                poppedAtSeconds,
                popOrder,
                BalloonBlowRules.MaxProgressPercent);
        }

        public static BalloonBlowRoundOutcome Incomplete(
            int playerSlot,
            float progressPercent)
        {
            return new BalloonBlowRoundOutcome(
                playerSlot,
                false,
                0d,
                0UL,
                progressPercent);
        }
    }

    public readonly struct BalloonBlowRoundStanding
    {
        internal BalloonBlowRoundStanding(
            int rank,
            int points,
            BalloonBlowRoundOutcome outcome)
        {
            Rank = rank;
            Points = points;
            Outcome = outcome;
        }

        public int PlayerSlot => Outcome.PlayerSlot;
        public int Rank { get; }
        public int Points { get; }
        public BalloonBlowRoundOutcome Outcome { get; }
        public bool IsPopped => Outcome.IsPopped;
        public float ProgressPercent => Outcome.ProgressPercent;
    }

    public sealed class BalloonBlowRoundResult
    {
        private readonly ReadOnlyCollection<BalloonBlowRoundStanding>
            _standings;

        internal BalloonBlowRoundResult(
            BalloonBlowRoundStanding[] standings)
        {
            _standings = Array.AsReadOnly(standings);
        }

        public IReadOnlyList<BalloonBlowRoundStanding> Standings =>
            _standings;

        public BalloonBlowRoundStanding GetStandingForSlot(
            int playerSlot)
        {
            if (!BalloonBlowRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerSlot));
            }

            for (var index = 0; index < _standings.Count; index++)
            {
                if (_standings[index].PlayerSlot == playerSlot)
                {
                    return _standings[index];
                }
            }

            throw new InvalidOperationException(
                "Round result does not contain the requested player.");
        }
    }

    public static class BalloonBlowRoundScoring
    {
        public static BalloonBlowRoundResult Score(
            IReadOnlyList<BalloonBlowRoundOutcome> outcomes)
        {
            if (outcomes == null)
            {
                throw new ArgumentNullException(nameof(outcomes));
            }

            if (outcomes.Count != BalloonBlowRules.PlayerCount)
            {
                throw new ArgumentException(
                    "A completed round must contain exactly four outcomes.",
                    nameof(outcomes));
            }

            var ordered = new BalloonBlowRoundOutcome[
                BalloonBlowRules.PlayerCount];
            var seenSlots = new bool[BalloonBlowRules.PlayerCount];
            for (var index = 0; index < outcomes.Count; index++)
            {
                var outcome = outcomes[index];
                if (!BalloonBlowRules.IsValidPlayerSlot(
                        outcome.PlayerSlot))
                {
                    throw new ArgumentException(
                        "Outcome contains an invalid player slot.",
                        nameof(outcomes));
                }

                if (seenSlots[outcome.PlayerSlot])
                {
                    throw new ArgumentException(
                        "Outcome contains a duplicate player slot.",
                        nameof(outcomes));
                }

                seenSlots[outcome.PlayerSlot] = true;
                ordered[index] = outcome;
            }

            Array.Sort(ordered, CompareOutcomes);
            var standings = new BalloonBlowRoundStanding[
                BalloonBlowRules.PlayerCount];
            for (var index = 0; index < ordered.Length; index++)
            {
                var rank = index + 1;
                standings[index] = new BalloonBlowRoundStanding(
                    rank,
                    BalloonBlowRules.GetPointsForRank(rank),
                    ordered[index]);
            }

            return new BalloonBlowRoundResult(standings);
        }

        private static int CompareOutcomes(
            BalloonBlowRoundOutcome left,
            BalloonBlowRoundOutcome right)
        {
            if (left.IsPopped != right.IsPopped)
            {
                return left.IsPopped ? -1 : 1;
            }

            if (left.IsPopped)
            {
                var time = left.PoppedAtSeconds.CompareTo(
                    right.PoppedAtSeconds);
                if (time != 0)
                {
                    return time;
                }

                var order = left.PopOrder.CompareTo(right.PopOrder);
                if (order != 0)
                {
                    return order;
                }
            }
            else
            {
                var progress = right.ProgressPercent.CompareTo(
                    left.ProgressPercent);
                if (progress != 0)
                {
                    return progress;
                }
            }

            return left.PlayerSlot.CompareTo(right.PlayerSlot);
        }
    }

    public readonly struct BalloonBlowLeaderboardEntry
    {
        internal BalloonBlowLeaderboardEntry(
            int playerSlot,
            int rank,
            int totalPoints,
            int finalRoundRank)
        {
            PlayerSlot = playerSlot;
            Rank = rank;
            TotalPoints = totalPoints;
            FinalRoundRank = finalRoundRank;
        }

        public int PlayerSlot { get; }
        public int Rank { get; }
        public int TotalPoints { get; }
        public int FinalRoundRank { get; }
    }

    public static class BalloonBlowMatchScoring
    {
        public static IReadOnlyList<BalloonBlowLeaderboardEntry>
            BuildLeaderboard(
                IReadOnlyList<BalloonBlowRoundResult> rounds)
        {
            if (rounds == null)
            {
                throw new ArgumentNullException(nameof(rounds));
            }

            if (rounds.Count != BalloonBlowRules.RoundCount)
            {
                throw new ArgumentException(
                    "A completed match must contain exactly three rounds.",
                    nameof(rounds));
            }

            var totalPoints = new int[BalloonBlowRules.PlayerCount];
            var finalRoundRanks = new int[BalloonBlowRules.PlayerCount];
            for (var roundIndex = 0;
                 roundIndex < rounds.Count;
                 roundIndex++)
            {
                var round = rounds[roundIndex];
                if (round == null)
                {
                    throw new ArgumentException(
                        "Round results cannot contain null.",
                        nameof(rounds));
                }

                for (var standingIndex = 0;
                     standingIndex < round.Standings.Count;
                     standingIndex++)
                {
                    var standing = round.Standings[standingIndex];
                    totalPoints[standing.PlayerSlot] += standing.Points;
                    if (roundIndex == BalloonBlowRules.RoundCount - 1)
                    {
                        finalRoundRanks[standing.PlayerSlot] =
                            standing.Rank;
                    }
                }
            }

            var slots = new int[BalloonBlowRules.PlayerCount];
            for (var slot = 0; slot < slots.Length; slot++)
            {
                slots[slot] = slot;
            }

            Array.Sort(
                slots,
                (left, right) =>
                {
                    var points = totalPoints[right].CompareTo(
                        totalPoints[left]);
                    if (points != 0)
                    {
                        return points;
                    }

                    var finalRound = finalRoundRanks[left].CompareTo(
                        finalRoundRanks[right]);
                    return finalRound != 0
                        ? finalRound
                        : left.CompareTo(right);
                });

            var leaderboard = new BalloonBlowLeaderboardEntry[
                BalloonBlowRules.PlayerCount];
            for (var index = 0; index < slots.Length; index++)
            {
                var slot = slots[index];
                leaderboard[index] = new BalloonBlowLeaderboardEntry(
                    slot,
                    index + 1,
                    totalPoints[slot],
                    finalRoundRanks[slot]);
            }

            return Array.AsReadOnly(leaderboard);
        }
    }
}
