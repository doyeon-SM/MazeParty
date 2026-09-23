using System;
using System.Collections.Generic;

namespace MazeParty.Gameplay
{
    public enum MatchAwardCategory : byte
    {
        PeakGoldHeld,
        TotalGoldEarned,
        MinigameWins,
        MinigameLastPlaces,
        ItemUses,
        DamageTaken,
        PlayerDamageDealt
    }

    public readonly struct MatchAwardStats
    {
        public MatchAwardStats(
            int peakGoldHeld,
            int totalGoldEarned,
            int minigameWins,
            int minigameLastPlaces,
            int itemUses,
            int damageTaken,
            int playerDamageDealt)
        {
            PeakGoldHeld = Math.Max(0, peakGoldHeld);
            TotalGoldEarned = Math.Max(0, totalGoldEarned);
            MinigameWins = Math.Max(0, minigameWins);
            MinigameLastPlaces = Math.Max(0, minigameLastPlaces);
            ItemUses = Math.Max(0, itemUses);
            DamageTaken = Math.Max(0, damageTaken);
            PlayerDamageDealt = Math.Max(0, playerDamageDealt);
        }

        public int PeakGoldHeld { get; }
        public int TotalGoldEarned { get; }
        public int MinigameWins { get; }
        public int MinigameLastPlaces { get; }
        public int ItemUses { get; }
        public int DamageTaken { get; }
        public int PlayerDamageDealt { get; }
    }

    /// <summary>
    /// Server-owned counters accumulated for one board match. The regular
    /// minigame-win NetworkVariable remains the source of truth for wins and is
    /// supplied only when an immutable award snapshot is requested.
    /// </summary>
    public struct MatchAwardProgress
    {
        public int PeakGoldHeld { get; private set; }
        public int TotalGoldEarned { get; private set; }
        public int MinigameLastPlaces { get; private set; }
        public int ItemUses { get; private set; }
        public int DamageTaken { get; private set; }
        public int PlayerDamageDealt { get; private set; }

        public void Reset(int startingGold)
        {
            PeakGoldHeld = Math.Max(0, startingGold);
            TotalGoldEarned = 0;
            MinigameLastPlaces = 0;
            ItemUses = 0;
            DamageTaken = 0;
            PlayerDamageDealt = 0;
        }

        public void RecordGoldBalance(int previousGold, int currentGold)
        {
            var safePrevious = Math.Max(0, previousGold);
            var safeCurrent = Math.Max(0, currentGold);
            if (safeCurrent > safePrevious)
            {
                TotalGoldEarned = SaturatingAdd(
                    TotalGoldEarned,
                    safeCurrent - safePrevious);
            }

            PeakGoldHeld = Math.Max(PeakGoldHeld, safeCurrent);
        }

        public void RecordMinigameLastPlace()
        {
            MinigameLastPlaces = SaturatingAdd(MinigameLastPlaces, 1);
        }

        public void RecordItemUse()
        {
            ItemUses = SaturatingAdd(ItemUses, 1);
        }

        public void RecordDamageTaken(int amount)
        {
            DamageTaken = SaturatingAdd(DamageTaken, amount);
        }

        public void RecordPlayerDamageDealt(int amount)
        {
            PlayerDamageDealt = SaturatingAdd(PlayerDamageDealt, amount);
        }

        public MatchAwardStats ToStats(int minigameWins)
        {
            return new MatchAwardStats(
                PeakGoldHeld,
                TotalGoldEarned,
                minigameWins,
                MinigameLastPlaces,
                ItemUses,
                DamageTaken,
                PlayerDamageDealt);
        }

        public MatchAwardProgress Sanitized()
        {
            return new MatchAwardProgress
            {
                PeakGoldHeld = Math.Max(0, PeakGoldHeld),
                TotalGoldEarned = Math.Max(0, TotalGoldEarned),
                MinigameLastPlaces = Math.Max(0, MinigameLastPlaces),
                ItemUses = Math.Max(0, ItemUses),
                DamageTaken = Math.Max(0, DamageTaken),
                PlayerDamageDealt = Math.Max(0, PlayerDamageDealt)
            };
        }

        private static int SaturatingAdd(int current, int amount)
        {
            if (amount <= 0)
            {
                return Math.Max(0, current);
            }

            return (int)Math.Min(
                int.MaxValue,
                (long)Math.Max(0, current) + amount);
        }
    }

    public static class MatchAwardRules
    {
        public const int CategoryCount = 7;
        public const int AwardCount = 2;
        private const int MaximumWinnerMaskPlayers = 8;

        public static string GetDisplayName(MatchAwardCategory category)
        {
            switch (category)
            {
                case MatchAwardCategory.PeakGoldHeld:
                    return "MOST GOLD HELD";
                case MatchAwardCategory.TotalGoldEarned:
                    return "MOST GOLD EARNED";
                case MatchAwardCategory.MinigameWins:
                    return "MOST MINIGAME WINS";
                case MatchAwardCategory.MinigameLastPlaces:
                    return "MOST MINIGAME LAST PLACES";
                case MatchAwardCategory.ItemUses:
                    return "MOST ITEMS USED";
                case MatchAwardCategory.DamageTaken:
                    return "MOST DAMAGE TAKEN";
                case MatchAwardCategory.PlayerDamageDealt:
                    return "MOST PLAYER DAMAGE DEALT";
                default:
                    throw new ArgumentOutOfRangeException(nameof(category));
            }
        }

        public static int GetValue(
            MatchAwardStats stats,
            MatchAwardCategory category)
        {
            switch (category)
            {
                case MatchAwardCategory.PeakGoldHeld:
                    return stats.PeakGoldHeld;
                case MatchAwardCategory.TotalGoldEarned:
                    return stats.TotalGoldEarned;
                case MatchAwardCategory.MinigameWins:
                    return stats.MinigameWins;
                case MatchAwardCategory.MinigameLastPlaces:
                    return stats.MinigameLastPlaces;
                case MatchAwardCategory.ItemUses:
                    return stats.ItemUses;
                case MatchAwardCategory.DamageTaken:
                    return stats.DamageTaken;
                case MatchAwardCategory.PlayerDamageDealt:
                    return stats.PlayerDamageDealt;
                default:
                    throw new ArgumentOutOfRangeException(nameof(category));
            }
        }

        public static bool CountsAsPlayerDamage(
            DamageKind damageKind,
            bool hasPlayerSource,
            bool isSelfDamage)
        {
            return damageKind == DamageKind.Item &&
                   hasPlayerSource &&
                   !isSelfDamage;
        }

        public static int GetAppliedDamageAmount(
            int currentHealth,
            int requestedDamage)
        {
            return Math.Min(
                Math.Max(0, currentHealth),
                Math.Max(0, requestedDamage));
        }

        /// <summary>
        /// Returns every player tied at the maximum. An all-zero category is a
        /// valid fallback award and therefore returns every player as a winner.
        /// </summary>
        public static bool TryGetWinnerMask(
            IReadOnlyList<MatchAwardStats> players,
            MatchAwardCategory category,
            out byte winnerMask,
            out int winningValue)
        {
            if (players == null)
            {
                throw new ArgumentNullException(nameof(players));
            }
            if (players.Count > MaximumWinnerMaskPlayers)
            {
                throw new ArgumentException(
                    "A byte winner mask supports at most eight players.",
                    nameof(players));
            }

            winnerMask = 0;
            winningValue = 0;
            if (players.Count == 0)
            {
                return false;
            }

            winningValue = GetValue(players[0], category);
            winnerMask = 1;
            for (var slot = 1; slot < players.Count; slot++)
            {
                var value = GetValue(players[slot], category);
                if (value > winningValue)
                {
                    winningValue = value;
                    winnerMask = (byte)(1 << slot);
                }
                else if (value == winningValue)
                {
                    winnerMask = (byte)(winnerMask | (1 << slot));
                }
            }

            return true;
        }

        /// <summary>
        /// Selects two distinct seeded categories, preferring categories with a
        /// positive winning value. Zero-value categories fill any shortage so a
        /// non-empty completed match always receives two awards.
        /// </summary>
        public static bool TrySelectTwoDistinctCategories(
            IReadOnlyList<MatchAwardStats> players,
            int seed,
            out MatchAwardCategory first,
            out MatchAwardCategory second)
        {
            var positive = new List<MatchAwardCategory>(CategoryCount);
            var fallback = new List<MatchAwardCategory>(CategoryCount);
            for (var categoryIndex = 0;
                 categoryIndex < CategoryCount;
                 categoryIndex++)
            {
                var category = (MatchAwardCategory)categoryIndex;
                if (TryGetWinnerMask(
                        players,
                        category,
                        out _,
                        out var winningValue))
                {
                    (winningValue > 0 ? positive : fallback).Add(category);
                }
            }

            first = default;
            second = default;
            if (positive.Count + fallback.Count < AwardCount)
            {
                return false;
            }

            var randomState = unchecked((uint)seed) ^ 0x9E3779B9U;
            if (randomState == 0U)
            {
                randomState = 0xA341316CU;
            }

            Shuffle(positive, ref randomState);
            Shuffle(fallback, ref randomState);
            positive.AddRange(fallback);

            first = positive[0];
            second = positive[1];
            return true;
        }

        private static void Shuffle(
            IList<MatchAwardCategory> values,
            ref uint randomState)
        {
            for (var index = values.Count - 1; index > 0; index--)
            {
                randomState = NextRandom(randomState);
                var swapIndex = (int)(randomState % (uint)(index + 1));
                var value = values[index];
                values[index] = values[swapIndex];
                values[swapIndex] = value;
            }
        }

        private static uint NextRandom(uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            return state ^ (state << 5);
        }
    }
}
