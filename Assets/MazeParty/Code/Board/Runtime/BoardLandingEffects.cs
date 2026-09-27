using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public enum BoardLandingEffectType : byte
    {
        None = 0,
        GoldGain = 1,
        GoldLoss = 2,
        ItemReward = 3,
        Healing20 = 4,
        Healing10 = 5,
        Damage40 = 6,
        Damage20 = 7,
        SpecialEvent = 8
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
        public const int UnrolledActionTimeoutGoldPenalty = 5;

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
            return ApplyKeyDelta(currentKeys, Math.Max(0, amount));
        }

        public static int ApplyKeyDelta(int currentKeys, int delta)
        {
            var safeCurrent = Math.Max(0, currentKeys);
            var result = (long)safeCurrent + delta;
            return (int)Math.Min(int.MaxValue, Math.Max(0L, result));
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
        public const int Healing20Amount = 20;
        public const int Healing10Amount = 10;
        public const int Damage40Amount = 40;
        public const int Damage20Amount = 20;

        public const int GoldGainWeight = 5;
        public const int GoldLossWeight = 3;
        public const int ItemRewardWeight = 1;
        public const int Healing20Weight = 1;
        public const int Healing10Weight = 1;
        public const int Damage40Weight = 1;
        public const int Damage20Weight = 1;
        public const int SpecialEventWeight = 1;
        public const int TotalWeight =
            GoldGainWeight + GoldLossWeight + ItemRewardWeight +
            Healing20Weight + Healing10Weight + Damage40Weight +
            Damage20Weight + SpecialEventWeight;

        public const double StandardEffectDurationSeconds = 1d;
        public const double SpecialEventTargetDurationSeconds = 1d;
        public const double SpecialEventResourceDurationSeconds = 1d;
        public const double SpecialEventOperationDurationSeconds = 1d;
        public const double SpecialEventResultDurationSeconds = 1d;
        public const double SpecialEventDurationSeconds =
            SpecialEventTargetDurationSeconds +
            SpecialEventResourceDurationSeconds +
            SpecialEventOperationDurationSeconds +
            SpecialEventResultDurationSeconds;

        private static readonly BoardLandingEffectType[] AssignableEffects =
        {
            BoardLandingEffectType.GoldGain,
            BoardLandingEffectType.GoldLoss,
            BoardLandingEffectType.ItemReward,
            BoardLandingEffectType.Healing20,
            BoardLandingEffectType.Healing10,
            BoardLandingEffectType.Damage40,
            BoardLandingEffectType.Damage20,
            BoardLandingEffectType.SpecialEvent
        };

        private static readonly int[] AssignableWeights =
        {
            GoldGainWeight,
            GoldLossWeight,
            ItemRewardWeight,
            Healing20Weight,
            Healing10Weight,
            Damage40Weight,
            Damage20Weight,
            SpecialEventWeight
        };

        private readonly Dictionary<Vector2Int, BoardLandingEffectType> _effects;
        private readonly int[] _counts;

        private BoardLandingEffectLayout(
            int seed,
            Dictionary<Vector2Int, BoardLandingEffectType> effects,
            int[] counts)
        {
            Seed = seed;
            _effects = effects;
            _counts = counts;
        }

        public int Seed { get; }
        public int GainCount => GetCount(BoardLandingEffectType.GoldGain);
        public int LossCount => GetCount(BoardLandingEffectType.GoldLoss);
        public int ItemRewardCount => GetCount(BoardLandingEffectType.ItemReward);
        public int Healing20Count => GetCount(BoardLandingEffectType.Healing20);
        public int Healing10Count => GetCount(BoardLandingEffectType.Healing10);
        public int Damage40Count => GetCount(BoardLandingEffectType.Damage40);
        public int Damage20Count => GetCount(BoardLandingEffectType.Damage20);
        public int SpecialEventCount => GetCount(BoardLandingEffectType.SpecialEvent);
        public int EligibleCount => _effects.Count;
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
            var random = new StableRandom(seed);
            for (var i = eligible.Count - 1; i > 0; i--)
            {
                var swapIndex = random.Next(i + 1);
                (eligible[i], eligible[swapIndex]) =
                    (eligible[swapIndex], eligible[i]);
            }

            var allocatedCounts = AllocateCounts(eligible.Count);
            var countsByType = new int[(int)BoardLandingEffectType.SpecialEvent + 1];
            var effects = new Dictionary<Vector2Int, BoardLandingEffectType>(
                eligible.Count);
            var tileIndex = 0;
            for (var effectIndex = 0;
                 effectIndex < AssignableEffects.Length;
                 effectIndex++)
            {
                var effect = AssignableEffects[effectIndex];
                var count = allocatedCounts[effectIndex];
                countsByType[(int)effect] = count;
                for (var countIndex = 0; countIndex < count; countIndex++)
                {
                    effects[eligible[tileIndex++].Coordinate] = effect;
                }
            }

            return new BoardLandingEffectLayout(seed, effects, countsByType);
        }

        public bool TryGetEffect(
            Vector2Int coordinate,
            out BoardLandingEffectType effect)
        {
            return _effects.TryGetValue(coordinate, out effect);
        }

        public int GetCount(BoardLandingEffectType effect)
        {
            var index = (int)effect;
            return index >= 0 && index < _counts.Length ? _counts[index] : 0;
        }

        public int GetGoldDelta(Vector2Int coordinate)
        {
            return TryGetEffect(coordinate, out var effect)
                ? GetGoldDelta(effect)
                : 0;
        }

        public int GetHealthDelta(Vector2Int coordinate)
        {
            return TryGetEffect(coordinate, out var effect)
                ? GetHealthDelta(effect)
                : 0;
        }

        public static int GetWeight(BoardLandingEffectType effect)
        {
            switch (effect)
            {
                case BoardLandingEffectType.GoldGain:
                    return GoldGainWeight;
                case BoardLandingEffectType.GoldLoss:
                    return GoldLossWeight;
                case BoardLandingEffectType.ItemReward:
                    return ItemRewardWeight;
                case BoardLandingEffectType.Healing20:
                    return Healing20Weight;
                case BoardLandingEffectType.Healing10:
                    return Healing10Weight;
                case BoardLandingEffectType.Damage40:
                    return Damage40Weight;
                case BoardLandingEffectType.Damage20:
                    return Damage20Weight;
                case BoardLandingEffectType.SpecialEvent:
                    return SpecialEventWeight;
                default:
                    return 0;
            }
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

        public static int GetHealthDelta(BoardLandingEffectType effect)
        {
            switch (effect)
            {
                case BoardLandingEffectType.Healing20:
                    return Healing20Amount;
                case BoardLandingEffectType.Healing10:
                    return Healing10Amount;
                case BoardLandingEffectType.Damage40:
                    return -Damage40Amount;
                case BoardLandingEffectType.Damage20:
                    return -Damage20Amount;
                default:
                    return 0;
            }
        }

        public static double GetDurationSeconds(BoardLandingEffectType effect)
        {
            return effect == BoardLandingEffectType.SpecialEvent
                ? SpecialEventDurationSeconds
                : StandardEffectDurationSeconds;
        }

        public static double GetTotalDurationSeconds(
            IReadOnlyList<BoardLandingEffectType> effects)
        {
            if (effects == null)
            {
                throw new ArgumentNullException(nameof(effects));
            }

            return GetTotalDurationSeconds(effects.Count, index => effects[index]);
        }

        public static double GetTotalDurationSeconds(
            int effectCount,
            Func<int, BoardLandingEffectType> getEffect)
        {
            if (effectCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(effectCount));
            }

            if (getEffect == null)
            {
                throw new ArgumentNullException(nameof(getEffect));
            }

            var duration = 0d;
            for (var index = 0; index < effectCount; index++)
            {
                duration += GetDurationSeconds(getEffect(index));
            }

            return duration;
        }

        private static int CompareCoordinates(BoardTile left, BoardTile right)
        {
            var x = left.Coordinate.x.CompareTo(right.Coordinate.x);
            return x != 0 ? x : left.Coordinate.y.CompareTo(right.Coordinate.y);
        }

        private static int[] AllocateCounts(int total)
        {
            var counts = new int[AssignableWeights.Length];
            var remainders = new int[AssignableWeights.Length];
            var allocated = 0;
            for (var i = 0; i < AssignableWeights.Length; i++)
            {
                var weighted = total * AssignableWeights[i];
                counts[i] = weighted / TotalWeight;
                remainders[i] = weighted % TotalWeight;
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

        private struct StableRandom
        {
            private uint _state;

            public StableRandom(int seed)
            {
                _state = unchecked((uint)seed) ^ 0xA511E9B3u;
                if (_state == 0u)
                {
                    _state = 0x6D2B79F5u;
                }
            }

            public int Next(int exclusiveMaximum)
            {
                if (exclusiveMaximum <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
                }

                var bound = (uint)exclusiveMaximum;
                var threshold = unchecked(0u - bound) % bound;
                uint value;
                do
                {
                    value = NextUInt();
                }
                while (value < threshold);

                return (int)(value % bound);
            }

            private uint NextUInt()
            {
                var value = _state;
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                _state = value;
                return value;
            }
        }
    }
}
