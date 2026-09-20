using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public enum BoardLandingEffectType : byte
    {
        None,
        GoldGain,
        GoldLoss,
        ItemReward,
        Healing
    }

    public enum PlayerBoardActionState : byte
    {
        Hidden,
        Dice,
        Moving,
        Arrived,
        Fighting
    }

    public static class PlayerStatRules
    {
        public const int DefaultMaxHealth = 100;
        public const int DefaultStartingKeys = 0;
        public const int DefaultStartingGold = 10;
        public const int KeyShopGoldPrice = 20;

        public static int ClampHealth(int currentHealth, int maxHealth)
        {
            return Mathf.Clamp(currentHealth, 0, Mathf.Max(1, maxHealth));
        }

        public static int ApplyGoldDelta(int currentGold, int delta)
        {
            var safeCurrent = Math.Max(0, currentGold);
            var result = (long)safeCurrent + delta;
            return (int)Math.Min(int.MaxValue, Math.Max(0L, result));
        }

        public static int AddKeys(int currentKeys, int amount)
        {
            var safeCurrent = Math.Max(0, currentKeys);
            var result = (long)safeCurrent + Math.Max(0, amount);
            return (int)Math.Min(int.MaxValue, result);
        }

        public static bool CanPurchaseKey(int currentGold, int price = KeyShopGoldPrice)
        {
            return price > 0 && currentGold >= price;
        }
    }

    /// <summary>
    /// Deterministically assigns landing effects from a server-provided seed.
    /// Spawn-marked rooms are normal effect candidates; Respawn rooms are excluded.
    /// </summary>
    public sealed class BoardLandingEffectLayout
    {
        public const int GoldGainAmount = 3;
        public const int GoldLossAmount = 3;
        public const int HealingAmount = 50;
        public const int GainWeight = 5;
        public const int LossWeight = 3;
        public const int ItemRewardWeight = 1;
        public const int HealingWeight = 1;

        private readonly Dictionary<Vector2Int, BoardLandingEffectType> _effects;

        private BoardLandingEffectLayout(
            int seed,
            Dictionary<Vector2Int, BoardLandingEffectType> effects,
            int gainCount,
            int lossCount,
            int itemRewardCount,
            int healingCount)
        {
            Seed = seed;
            _effects = effects;
            GainCount = gainCount;
            LossCount = lossCount;
            ItemRewardCount = itemRewardCount;
            HealingCount = healingCount;
        }

        public int Seed { get; }
        public int GainCount { get; }
        public int LossCount { get; }
        public int ItemRewardCount { get; }
        public int HealingCount { get; }
        public int EligibleCount => GainCount + LossCount + ItemRewardCount + HealingCount;
        public IReadOnlyDictionary<Vector2Int, BoardLandingEffectType> Effects => _effects;

        public static BoardLandingEffectLayout Create(
            IReadOnlyList<BoardTile> tiles,
            int seed)
        {
            var eligible = new List<BoardTile>();
            if (tiles != null)
            {
                for (var i = 0; i < tiles.Count; i++)
                {
                    var tile = tiles[i];
                    if (tile != null && tile.TileType != BoardTileType.Respawn)
                    {
                        eligible.Add(tile);
                    }
                }
            }

            eligible.Sort(CompareCoordinates);
            var random = new System.Random(seed);
            for (var i = eligible.Count - 1; i > 0; i--)
            {
                var swapIndex = random.Next(i + 1);
                (eligible[i], eligible[swapIndex]) = (eligible[swapIndex], eligible[i]);
            }

            var counts = AllocateCounts(eligible.Count);
            var gainCount = counts[0];
            var lossCount = counts[1];
            var itemRewardCount = counts[2];
            var effects = new Dictionary<Vector2Int, BoardLandingEffectType>(eligible.Count);
            for (var i = 0; i < eligible.Count; i++)
            {
                effects[eligible[i].Coordinate] = i < gainCount
                    ? BoardLandingEffectType.GoldGain
                    : i < gainCount + lossCount
                        ? BoardLandingEffectType.GoldLoss
                        : i < gainCount + lossCount + itemRewardCount
                            ? BoardLandingEffectType.ItemReward
                            : BoardLandingEffectType.Healing;
            }

            return new BoardLandingEffectLayout(
                seed,
                effects,
                gainCount,
                lossCount,
                itemRewardCount,
                counts[3]);
        }

        public bool TryGetEffect(
            Vector2Int coordinate,
            out BoardLandingEffectType effect)
        {
            return _effects.TryGetValue(coordinate, out effect);
        }

        public int GetGoldDelta(Vector2Int coordinate)
        {
            return TryGetEffect(coordinate, out var effect)
                ? GetGoldDelta(effect)
                : 0;
        }

        public static int GetGoldDelta(BoardLandingEffectType effect)
        {
            switch (effect)
            {
                case BoardLandingEffectType.GoldGain:
                    return GoldGainAmount;
                case BoardLandingEffectType.GoldLoss:
                    return -GoldLossAmount;
                default:
                    return 0;
            }
        }

        private static int CompareCoordinates(BoardTile left, BoardTile right)
        {
            var x = left.Coordinate.x.CompareTo(right.Coordinate.x);
            return x != 0 ? x : left.Coordinate.y.CompareTo(right.Coordinate.y);
        }

        private static int[] AllocateCounts(int total)
        {
            var weights = new[] { GainWeight, LossWeight, ItemRewardWeight, HealingWeight };
            var counts = new int[weights.Length];
            var remainders = new int[weights.Length];
            var totalWeight = GainWeight + LossWeight + ItemRewardWeight + HealingWeight;
            var allocated = 0;
            for (var i = 0; i < weights.Length; i++)
            {
                var weighted = total * weights[i];
                counts[i] = weighted / totalWeight;
                remainders[i] = weighted % totalWeight;
                allocated += counts[i];
            }

            while (allocated < total)
            {
                var bestIndex = 0;
                for (var i = 1; i < remainders.Length; i++)
                {
                    if (remainders[i] > remainders[bestIndex])
                    {
                        bestIndex = i;
                    }
                }

                counts[bestIndex]++;
                remainders[bestIndex] = -1;
                allocated++;
            }

            return counts;
        }
    }
}
