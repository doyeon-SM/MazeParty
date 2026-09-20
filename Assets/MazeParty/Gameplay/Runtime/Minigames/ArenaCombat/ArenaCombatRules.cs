using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay.Minigames.ArenaCombat
{
    public readonly struct ArenaCombatRankingEntry
    {
        public ArenaCombatRankingEntry(
            int slot,
            int remainingHealth,
            double eliminatedAt)
        {
            Slot = slot;
            RemainingHealth = Math.Max(0, remainingHealth);
            EliminatedAt = eliminatedAt;
        }

        public int Slot { get; }
        public int RemainingHealth { get; }
        public double EliminatedAt { get; }
        public bool IsEliminated => !double.IsNaN(EliminatedAt);
    }

    /// <summary>
    /// Arena-only rules. Combat damage, reach, knockback and cooldown are
    /// deliberately inherited from the existing board combat rules.
    /// </summary>
    public static class ArenaCombatRules
    {
        public const int PlayerCount = 4;
        public const double CountdownSeconds = 3d;
        public const double ResultSeconds = 4d;
        public const float ArenaCenterX = 1620f;
        public const float ArenaHalfWidth = 9f;
        public const float ArenaHalfDepth = 9f;
        public const float ArenaHalfExtent = 9f;

        public static double MatchDurationSeconds =>
            BoardCombatRules.FightDurationSeconds;

        public static bool IsValidSlot(int slot)
        {
            return slot >= 0 && slot < PlayerCount;
        }

        public static Vector3 GetSpawnPosition(int slot)
        {
            switch (slot)
            {
                case 0: return new Vector3(ArenaCenterX - 4f, 1f, -4f);
                case 1: return new Vector3(ArenaCenterX + 4f, 1f, -4f);
                case 2: return new Vector3(ArenaCenterX - 4f, 1f, 4f);
                case 3: return new Vector3(ArenaCenterX + 4f, 1f, 4f);
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        public static bool ShouldEndMatch(double elapsedSeconds, int survivorCount)
        {
            return survivorCount <= 1 ||
                elapsedSeconds >= MatchDurationSeconds;
        }

        /// <summary>
        /// Survivors rank by remaining HP; defeated players rank by later
        /// elimination. Equal facts fall back to player slot deterministically.
        /// </summary>
        public static int[] ResolveRanks(
            IReadOnlyList<ArenaCombatRankingEntry> entries)
        {
            if (entries == null || entries.Count != PlayerCount)
            {
                throw new ArgumentException(
                    "Exactly four combatants are required.",
                    nameof(entries));
            }

            var ordered = new ArenaCombatRankingEntry[PlayerCount];
            var seenMask = 0;
            for (var index = 0; index < PlayerCount; index++)
            {
                var entry = entries[index];
                if (!IsValidSlot(entry.Slot) ||
                    (seenMask & (1 << entry.Slot)) != 0)
                {
                    throw new ArgumentException(
                        "Combatant slots must be unique and valid.",
                        nameof(entries));
                }

                seenMask |= 1 << entry.Slot;
                ordered[index] = entry;
            }

            Array.Sort(ordered, CompareRankingEntries);
            var ranks = new int[PlayerCount];
            for (var index = 0; index < ordered.Length; index++)
            {
                ranks[ordered[index].Slot] = index + 1;
            }
            return ranks;
        }

        private static int CompareRankingEntries(
            ArenaCombatRankingEntry left,
            ArenaCombatRankingEntry right)
        {
            if (left.IsEliminated != right.IsEliminated)
            {
                return left.IsEliminated ? 1 : -1;
            }

            var primary = left.IsEliminated
                ? right.EliminatedAt.CompareTo(left.EliminatedAt)
                : right.RemainingHealth.CompareTo(left.RemainingHealth);
            return primary != 0
                ? primary
                : left.Slot.CompareTo(right.Slot);
        }
    }
}
