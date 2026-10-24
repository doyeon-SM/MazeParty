using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    [Serializable]
    public sealed class StatisticsPlayer
    {
        public string name;
        public Color color = Color.white;
        public int rank, keys, gold, peakGold, earnedGold, wins, lastPlaces, itemUses, received, dealt;
        public int bonusMask, moves;
        public byte eye, mouth, hat, expression;
    }

    [Serializable]
    public struct StatisticsBalance
    {
        public int slot, turn, sequence, keys, gold, earned;
    }

    [Serializable]
    public struct StatisticsDamage
    {
        // source -1 is environment; item -1 is a punch, 0 is environment/unknown.
        public int source, target, turn, item, amount;
    }

    /// <summary>One match, server-owned. Points are mutations, never periodic samples.</summary>
    [Serializable]
    public sealed class MatchStatistics
    {
        public const int MaxEvents = 20000;
        public const int MaxJsonLength = 8 * 1024 * 1024;
        public int version = 1, turns, sequence;
        public bool historyIncomplete, frozen;
        public StatisticsPlayer[] players = new StatisticsPlayer[4];
        public List<StatisticsBalance> balances = new List<StatisticsBalance>();
        public List<StatisticsDamage> damage = new List<StatisticsDamage>();
        public int[] placements = new int[16];
        public int[] lastPlacementTurn = new int[4];
        public int[] bonusCategories = new int[2];

        public static MatchStatistics Begin(int turns)
        {
            var result = new MatchStatistics { turns = turns };
            for (var i = 0; i < 4; i++) result.players[i] = new StatisticsPlayer();
            return result;
        }

        public void RecordBalance(int slot, int turn, int keys, int gold, int earned)
        {
            if (frozen || slot < 0 || slot > 3 || turn < 0 || turn > turns) return;
            for (var i = balances.Count - 1; i >= 0; i--)
                if (balances[i].slot == slot)
                {
                    var previous = balances[i];
                    if (previous.keys == keys && previous.gold == gold && previous.earned == earned) return;
                    break;
                }
            if (balances.Count >= MaxEvents) { historyIncomplete = true; return; }
            balances.Add(new StatisticsBalance { slot = slot, turn = turn, sequence = ++sequence,
                keys = Math.Max(0, keys), gold = Math.Max(0, gold), earned = Math.Max(0, earned) });
        }

        public void RecordPlacement(int slot, int turn, int rank)
        {
            if (frozen || slot < 0 || slot > 3 || rank < 1 || rank > 4 || turn < 1 || turn > turns ||
                turn <= lastPlacementTurn[slot]) return;
            placements[slot * 4 + rank - 1]++;
            lastPlacementTurn[slot] = turn;
        }

        public void RecordDamage(int source, int target, int turn, int item, int amount)
        {
            if (frozen || source < -1 || source > 3 || target < 0 || target > 3 || amount <= 0 ||
                turn < 1 || turn > turns) return;
            if (damage.Count >= MaxEvents) { historyIncomplete = true; return; }
            damage.Add(new StatisticsDamage { source = source, target = target, turn = turn, item = item, amount = amount });
        }

        public bool IsValid()
        {
            if (version != 1 || turns < 1 || turns > 20 || sequence < 0 || players == null || players.Length != 4 ||
                balances == null || balances.Count > MaxEvents || damage == null || damage.Count > MaxEvents ||
                placements == null || placements.Length != 16 || lastPlacementTurn == null || lastPlacementTurn.Length != 4 ||
                bonusCategories == null || bonusCategories.Length != 2) return false;
            foreach (var category in bonusCategories) if (category < 0 || category >= MatchAwardRules.CategoryCount) return false;
            for (var slot = 0; slot < 4; slot++)
            {
                var p = players[slot];
                if (p == null || p.rank < 0 || p.rank > 4 || (frozen && p.rank == 0) || p.keys < 0 || p.gold < 0 ||
                    p.peakGold < 0 || p.earnedGold < 0 || p.wins < 0 || p.lastPlaces < 0 || p.itemUses < 0 ||
                    p.received < 0 || p.dealt < 0 || p.moves < 0 || p.bonusMask < 0 || p.bonusMask > 3 ||
                    (p.name != null && p.name.Length > 256) || lastPlacementTurn[slot] < 0 || lastPlacementTurn[slot] > turns) return false;
                var count = 0;
                for (var rank = 0; rank < 4; rank++)
                {
                    var value = placements[slot * 4 + rank];
                    if (value < 0 || value > turns) return false;
                    count += value;
                }
                if (count > turns || count > lastPlacementTurn[slot]) return false;
            }
            var last = 0;
            var lastTurn = 0;
            foreach (var e in balances)
            {
                if (e.slot < 0 || e.slot > 3 || e.turn < lastTurn || e.turn > turns || e.sequence <= last || e.sequence > sequence ||
                    e.keys < 0 || e.gold < 0 || e.earned < 0) return false;
                last = e.sequence; lastTurn = e.turn;
            }
            lastTurn = 0;
            foreach (var e in damage)
            {
                if (e.source < -1 || e.source > 3 || e.target < 0 || e.target > 3 || e.turn < 1 || e.turn < lastTurn ||
                    e.turn > turns || e.amount <= 0 || e.item < -1 || e.item > 12 || (e.item > 0 && e.item < 4)) return false;
                lastTurn = e.turn;
            }
            return true;
        }

        public static bool TryDecode(string json, out MatchStatistics result)
        {
            result = null;
            if (string.IsNullOrEmpty(json) || json.Length > MaxJsonLength) return false;
            try
            {
                var decoded = JsonUtility.FromJson<MatchStatistics>(json);
                if (decoded == null || !decoded.IsValid()) return false;
                result = decoded;
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        public bool FitsCheckpoint(int turn, int scheduledTurns)
        {
            if (frozen || turns != scheduledTurns || turn < 1 || turn > turns) return false;
            foreach (var e in balances) if (e.turn > turn) return false;
            foreach (var e in damage) if (e.turn > turn) return false;
            foreach (var settled in lastPlacementTurn) if (settled > turn) return false;
            return true;
        }

        public void RemapSlots(int[] savedToCurrent)
        {
            if (savedToCurrent == null || savedToCurrent.Length != 4) throw new ArgumentException("Four slots required.");
            var seen = 0;
            foreach (var slot in savedToCurrent)
            {
                if (slot < 0 || slot > 3 || (seen & (1 << slot)) != 0) throw new ArgumentException("Invalid slot mapping.");
                seen |= 1 << slot;
            }
            var mapped = new StatisticsPlayer[4]; var counts = new int[16]; var last = new int[4];
            for (var slot = 0; slot < 4; slot++)
            {
                mapped[savedToCurrent[slot]] = players[slot];
                last[savedToCurrent[slot]] = lastPlacementTurn[slot];
                for (var rank = 0; rank < 4; rank++) counts[savedToCurrent[slot] * 4 + rank] = placements[slot * 4 + rank];
            }
            players = mapped; placements = counts; lastPlacementTurn = last;
            for (var i = 0; i < balances.Count; i++) { var e = balances[i]; e.slot = savedToCurrent[e.slot]; balances[i] = e; }
            for (var i = 0; i < damage.Count; i++)
            {
                var e = damage[i]; e.target = savedToCurrent[e.target];
                if (e.source >= 0) e.source = savedToCurrent[e.source];
                damage[i] = e;
            }
        }
    }
}
