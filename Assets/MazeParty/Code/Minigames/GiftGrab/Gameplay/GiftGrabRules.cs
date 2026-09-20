using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MazeParty.Gameplay.Minigames.GiftGrab
{
    /// <summary>
    /// Fixed values shared by the authoritative server and Solo mode.
    /// Every timestamp is measured against the active (unpaused) round clock.
    /// </summary>
    public static class GiftGrabRules
    {
        public const int PlayerCount = 4;
        public const int RoundCount = 2;
        public const double RoundSeconds = 60d;

        public const int InitialGiftCount = 10;
        public const int SpawnBatchSize = 3;
        public const int SpawnBatchCount = 3;
        public const double SpawnIntervalSeconds = 15d;
        public const int TotalGiftCount =
            InitialGiftCount + (SpawnBatchSize * SpawnBatchCount);

        public const float NormalMoveSpeed = 5f;
        public const float CarryMoveSpeed = 2.75f;
        public const float ThrowSpeed = 11f;
        public const double ThrowStunSeconds = 1d;
        public const double ThrowSelfHitImmunitySeconds = 0.25d;
        public const float PushRange = 1.6f;
        public const float PushRadius = 0.7f;
        public const float PushKnockbackDistance = 1.25f;
        public const double PushStunSeconds = 0.5d;
        public const double PushCooldownSeconds = 0.65d;
        public const double RegrabLockSeconds = 0.25d;

        public const int NoPlayerSlot = -1;
        public const int NoGiftId = -1;

        public static int GetPointsForRank(int rank)
        {
            if (rank < 1 || rank > PlayerCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rank),
                    rank,
                    "Rank must be between 1 and 4.");
            }

            return PlayerCount - rank;
        }

        public static bool IsValidPlayerSlot(int playerSlot)
        {
            return playerSlot >= 0 && playerSlot < PlayerCount;
        }

        public static bool IsValidGiftId(int giftId)
        {
            return giftId >= 0 && giftId < TotalGiftCount;
        }

        /// <summary>
        /// Returns the number of gifts that must exist at the supplied active
        /// time. Batches appear exactly at 15, 30 and 45 seconds.
        /// </summary>
        public static int GetScheduledGiftCount(double activeElapsedSeconds)
        {
            ValidateActiveElapsedSeconds(activeElapsedSeconds);
            var completedBatches = (int)Math.Floor(
                activeElapsedSeconds / SpawnIntervalSeconds);
            completedBatches = Math.Min(
                completedBatches,
                SpawnBatchCount);
            return InitialGiftCount +
                (completedBatches * SpawnBatchSize);
        }

        internal static void ValidateRoundNumber(int roundNumber)
        {
            if (roundNumber < 1 || roundNumber > RoundCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(roundNumber),
                    roundNumber,
                    "Round number must be between 1 and 2.");
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

        internal static void ValidateStoredGiftCount(int value)
        {
            if (value < 0 || value > TotalGiftCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Stored gift count must be between 0 and 19.");
            }
        }

        internal static void ValidateGiftSeconds(double value)
        {
            if (double.IsNaN(value) ||
                double.IsInfinity(value) ||
                value < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Gift-seconds must be finite and non-negative.");
            }
        }
    }

    public readonly struct GiftGrabRoundOutcome
    {
        private GiftGrabRoundOutcome(
            int playerSlot,
            int storedGiftCount,
            double cumulativeStoredGiftSeconds)
        {
            if (!GiftGrabRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(nameof(playerSlot));
            }

            GiftGrabRules.ValidateStoredGiftCount(storedGiftCount);
            GiftGrabRules.ValidateGiftSeconds(
                cumulativeStoredGiftSeconds);

            PlayerSlot = playerSlot;
            StoredGiftCount = storedGiftCount;
            CumulativeStoredGiftSeconds = cumulativeStoredGiftSeconds;
        }

        public int PlayerSlot { get; }
        public int StoredGiftCount { get; }
        public double CumulativeStoredGiftSeconds { get; }

        public static GiftGrabRoundOutcome Create(
            int playerSlot,
            int storedGiftCount,
            double cumulativeStoredGiftSeconds)
        {
            return new GiftGrabRoundOutcome(
                playerSlot,
                storedGiftCount,
                cumulativeStoredGiftSeconds);
        }
    }

    public readonly struct GiftGrabRoundStanding
    {
        internal GiftGrabRoundStanding(
            int rank,
            int points,
            GiftGrabRoundOutcome outcome)
        {
            Rank = rank;
            Points = points;
            Outcome = outcome;
        }

        public int PlayerSlot => Outcome.PlayerSlot;
        public int Rank { get; }
        public int Points { get; }
        public GiftGrabRoundOutcome Outcome { get; }
        public int StoredGiftCount => Outcome.StoredGiftCount;
        public double CumulativeStoredGiftSeconds =>
            Outcome.CumulativeStoredGiftSeconds;
    }

    public sealed class GiftGrabRoundResult
    {
        private readonly ReadOnlyCollection<GiftGrabRoundStanding>
            _standings;

        internal GiftGrabRoundResult(GiftGrabRoundStanding[] standings)
        {
            _standings = Array.AsReadOnly(standings);
        }

        public IReadOnlyList<GiftGrabRoundStanding> Standings =>
            _standings;

        public GiftGrabRoundStanding GetStandingForSlot(int playerSlot)
        {
            if (!GiftGrabRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(nameof(playerSlot));
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

    public static class GiftGrabRoundScoring
    {
        public static GiftGrabRoundResult Score(
            IReadOnlyList<GiftGrabRoundOutcome> outcomes)
        {
            if (outcomes == null)
            {
                throw new ArgumentNullException(nameof(outcomes));
            }

            if (outcomes.Count != GiftGrabRules.PlayerCount)
            {
                throw new ArgumentException(
                    "A completed round must contain exactly four outcomes.",
                    nameof(outcomes));
            }

            var ordered = new GiftGrabRoundOutcome[
                GiftGrabRules.PlayerCount];
            var seenSlots = new bool[GiftGrabRules.PlayerCount];
            for (var index = 0; index < outcomes.Count; index++)
            {
                var outcome = outcomes[index];
                if (!GiftGrabRules.IsValidPlayerSlot(outcome.PlayerSlot))
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
            var standings = new GiftGrabRoundStanding[
                GiftGrabRules.PlayerCount];
            for (var index = 0; index < ordered.Length; index++)
            {
                var rank = index + 1;
                standings[index] = new GiftGrabRoundStanding(
                    rank,
                    GiftGrabRules.GetPointsForRank(rank),
                    ordered[index]);
            }

            return new GiftGrabRoundResult(standings);
        }

        private static int CompareOutcomes(
            GiftGrabRoundOutcome left,
            GiftGrabRoundOutcome right)
        {
            var stored = right.StoredGiftCount.CompareTo(
                left.StoredGiftCount);
            if (stored != 0)
            {
                return stored;
            }

            var ownership = right.CumulativeStoredGiftSeconds.CompareTo(
                left.CumulativeStoredGiftSeconds);
            return ownership != 0
                ? ownership
                : left.PlayerSlot.CompareTo(right.PlayerSlot);
        }
    }

    public readonly struct GiftGrabLeaderboardEntry
    {
        internal GiftGrabLeaderboardEntry(
            int playerSlot,
            int rank,
            int totalPoints,
            int totalStoredGiftCount,
            int finalRoundRank)
        {
            PlayerSlot = playerSlot;
            Rank = rank;
            TotalPoints = totalPoints;
            TotalStoredGiftCount = totalStoredGiftCount;
            FinalRoundRank = finalRoundRank;
        }

        public int PlayerSlot { get; }
        public int Rank { get; }
        public int TotalPoints { get; }
        public int TotalStoredGiftCount { get; }
        public int FinalRoundRank { get; }
    }

    public static class GiftGrabMatchScoring
    {
        public static IReadOnlyList<GiftGrabLeaderboardEntry>
            BuildLeaderboard(IReadOnlyList<GiftGrabRoundResult> rounds)
        {
            if (rounds == null)
            {
                throw new ArgumentNullException(nameof(rounds));
            }

            if (rounds.Count != GiftGrabRules.RoundCount)
            {
                throw new ArgumentException(
                    "A completed match must contain exactly two rounds.",
                    nameof(rounds));
            }

            var totalPoints = new int[GiftGrabRules.PlayerCount];
            var totalStoredGiftCounts =
                new int[GiftGrabRules.PlayerCount];
            var finalRoundRanks = new int[GiftGrabRules.PlayerCount];
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
                    totalStoredGiftCounts[standing.PlayerSlot] +=
                        standing.StoredGiftCount;
                    if (roundIndex == GiftGrabRules.RoundCount - 1)
                    {
                        finalRoundRanks[standing.PlayerSlot] =
                            standing.Rank;
                    }
                }
            }

            var slots = new int[GiftGrabRules.PlayerCount];
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

                    var stored = totalStoredGiftCounts[right].CompareTo(
                        totalStoredGiftCounts[left]);
                    if (stored != 0)
                    {
                        return stored;
                    }

                    var finalRound = finalRoundRanks[left].CompareTo(
                        finalRoundRanks[right]);
                    return finalRound != 0
                        ? finalRound
                        : left.CompareTo(right);
                });

            var leaderboard = new GiftGrabLeaderboardEntry[
                GiftGrabRules.PlayerCount];
            for (var index = 0; index < slots.Length; index++)
            {
                var slot = slots[index];
                leaderboard[index] = new GiftGrabLeaderboardEntry(
                    slot,
                    index + 1,
                    totalPoints[slot],
                    totalStoredGiftCounts[slot],
                    finalRoundRanks[slot]);
            }

            return Array.AsReadOnly(leaderboard);
        }
    }
}
