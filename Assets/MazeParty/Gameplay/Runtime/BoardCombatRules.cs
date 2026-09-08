using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public static class BoardCombatRules
    {
        public const int TemporaryHealth = 100;
        public const int PunchDamage = 5;
        public const double FightDurationSeconds = 60d;
        public const double NextActionItemProtectionSeconds = 30d;
        public const float PunchRange = 2f;
        public const float PunchRadius = 0.35f;
        public const double PunchCooldownSeconds = 0.65d;
        public const float PunchKnockbackSpeed = 3.5f;
        public const int MaximumFightsPerTurn = 32;

        public static IReadOnlyList<BoardCombatGroup> BuildInitialQueue(
            IReadOnlyList<BoardCombatPlacement> placements,
            IReadOnlyList<int> overallRanks)
        {
            if (placements == null)
            {
                throw new ArgumentNullException(nameof(placements));
            }
            if (overallRanks == null ||
                overallRanks.Count < BoardFlowStateMachine.RequiredPlayerCount)
            {
                throw new ArgumentException(
                    "Every placement needs an overall rank.",
                    nameof(overallRanks));
            }

            var byCoordinate = new Dictionary<Vector2Int, List<BoardCombatPlacement>>();
            var lowestOverallRank = 1;
            for (var i = 0; i < placements.Count; i++)
            {
                var placement = placements[i];
                if (placement.Slot < 0 || placement.Slot >= BoardFlowStateMachine.RequiredPlayerCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(placements),
                        "Combat placement slots must be between zero and three.");
                }

                lowestOverallRank = Math.Max(
                    lowestOverallRank,
                    overallRanks[placement.Slot]);
                if (!byCoordinate.TryGetValue(placement.Coordinate, out var group))
                {
                    group = new List<BoardCombatPlacement>();
                    byCoordinate.Add(placement.Coordinate, group);
                }
                group.Add(placement);
            }

            var queue = new List<BoardCombatGroup>();
            foreach (var pair in byCoordinate)
            {
                if (pair.Value.Count < 2)
                {
                    continue;
                }

                pair.Value.Sort((left, right) =>
                {
                    var arrival = SanitizeTimestamp(left.ArrivalTime)
                        .CompareTo(SanitizeTimestamp(right.ArrivalTime));
                    return arrival != 0 ? arrival : left.Slot.CompareTo(right.Slot);
                });

                byte participantMask = 0;
                var containsLowestRank = false;
                for (var i = 0; i < pair.Value.Count; i++)
                {
                    var placement = pair.Value[i];
                    participantMask = (byte)(participantMask | (1 << placement.Slot));
                    containsLowestRank |=
                        overallRanks[placement.Slot] == lowestOverallRank;
                }

                queue.Add(new BoardCombatGroup(
                    pair.Key,
                    participantMask,
                    SanitizeTimestamp(pair.Value[1].ArrivalTime),
                    containsLowestRank));
            }

            queue.Sort(CompareGroups);
            return queue;
        }

        public static BoardCombatStanding[] ResolveStandings(
            IReadOnlyList<BoardCombatRankingEntry> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            var ordered = new List<BoardCombatRankingEntry>(entries.Count);
            for (var i = 0; i < entries.Count; i++)
            {
                ordered.Add(entries[i]);
            }
            ordered.Sort(CompareRankingEntries);

            var standings = new BoardCombatStanding[ordered.Count];
            for (var i = 0; i < ordered.Count; i++)
            {
                standings[i] = new BoardCombatStanding(
                    ordered[i].Slot,
                    i + 1,
                    RetreatDistanceForRank(i + 1));
            }
            return standings;
        }

        public static int RetreatDistanceForRank(int rank)
        {
            return Math.Max(0, rank - 1);
        }

        public static bool IsFightComplete(byte participantMask, byte aliveMask)
        {
            var remaining = (byte)(participantMask & aliveMask);
            return remaining == 0 || (remaining & (remaining - 1)) == 0;
        }

        private static int CompareGroups(BoardCombatGroup left, BoardCombatGroup right)
        {
            var formation = left.FormedAt.CompareTo(right.FormedAt);
            if (formation != 0)
            {
                return formation;
            }
            if (left.ContainsLowestOverallRank != right.ContainsLowestOverallRank)
            {
                return left.ContainsLowestOverallRank ? -1 : 1;
            }

            var x = left.Coordinate.x.CompareTo(right.Coordinate.x);
            return x != 0 ? x : left.Coordinate.y.CompareTo(right.Coordinate.y);
        }

        private static int CompareRankingEntries(
            BoardCombatRankingEntry left,
            BoardCombatRankingEntry right)
        {
            if (left.IsEliminated != right.IsEliminated)
            {
                return left.IsEliminated ? 1 : -1;
            }

            if (left.IsEliminated)
            {
                var elimination = SanitizeTimestamp(right.EliminatedAt)
                    .CompareTo(SanitizeTimestamp(left.EliminatedAt));
                if (elimination != 0)
                {
                    return elimination;
                }
            }
            else
            {
                var health = right.RemainingHealth.CompareTo(left.RemainingHealth);
                if (health != 0)
                {
                    return health;
                }
            }

            var arrival = SanitizeTimestamp(left.ArrivalTime)
                .CompareTo(SanitizeTimestamp(right.ArrivalTime));
            if (arrival != 0)
            {
                return arrival;
            }

            // A larger competition-rank number is currently further behind and
            // receives the deterministic rubber-band advantage.
            var overallRank = right.OverallRank.CompareTo(left.OverallRank);
            return overallRank != 0
                ? overallRank
                : left.Slot.CompareTo(right.Slot);
        }

        private static double SanitizeTimestamp(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? double.MaxValue
                : value;
        }
    }

    public readonly struct BoardCombatPlacement
    {
        public BoardCombatPlacement(int slot, Vector2Int coordinate, double arrivalTime)
        {
            Slot = slot;
            Coordinate = coordinate;
            ArrivalTime = arrivalTime;
        }

        public int Slot { get; }
        public Vector2Int Coordinate { get; }
        public double ArrivalTime { get; }
    }

    public readonly struct BoardCombatGroup
    {
        public BoardCombatGroup(
            Vector2Int coordinate,
            byte participantMask,
            double formedAt,
            bool containsLowestOverallRank)
        {
            Coordinate = coordinate;
            ParticipantMask = participantMask;
            FormedAt = formedAt;
            ContainsLowestOverallRank = containsLowestOverallRank;
        }

        public Vector2Int Coordinate { get; }
        public byte ParticipantMask { get; }
        public double FormedAt { get; }
        public bool ContainsLowestOverallRank { get; }
    }

    public readonly struct BoardCombatRankingEntry
    {
        public BoardCombatRankingEntry(
            int slot,
            int remainingHealth,
            double eliminatedAt,
            double arrivalTime,
            int overallRank)
        {
            Slot = slot;
            RemainingHealth = Math.Max(0, remainingHealth);
            EliminatedAt = eliminatedAt;
            ArrivalTime = arrivalTime;
            OverallRank = Math.Max(1, overallRank);
        }

        public int Slot { get; }
        public int RemainingHealth { get; }
        public double EliminatedAt { get; }
        public double ArrivalTime { get; }
        public int OverallRank { get; }
        public bool IsEliminated => !double.IsNaN(EliminatedAt);
    }

    public readonly struct BoardCombatStanding
    {
        public BoardCombatStanding(int slot, int rank, int retreatDistance)
        {
            Slot = slot;
            Rank = rank;
            RetreatDistance = retreatDistance;
        }

        public int Slot { get; }
        public int Rank { get; }
        public int RetreatDistance { get; }
    }
}
