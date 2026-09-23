using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    // Values 1-3 belonged to retired prototypes; never reuse serialized IDs.
    public enum PrototypeItemId : byte
    {
        None = 0, DoubleDice = 4, Pistol = 5, Sniper = 6, Grenade = 7, Mine = 8, LowDice = 9, HighDice = 10, PositionSwapper = 11, Cloak = 12
    }

    public static class PrototypeItemCatalog
    {
        private static BoardItemDefinition[] _definitions;
        public static IReadOnlyList<BoardItemDefinition> All => Definitions;
        private static BoardItemDefinition[] Definitions
        {
            get
            {
                if (_definitions == null)
                {
                    _definitions = Resources.LoadAll<BoardItemDefinition>("MazeParty/Items");
                    Array.Sort(_definitions, (a, b) => a.Id.CompareTo(b.Id));
                    if (_definitions.Length != 9) throw new InvalidOperationException("Expected nine board item SOs in Resources/MazeParty/Items.");
                }
                return _definitions;
            }
        }
        public static int Count => Definitions.Length;
        public static BoardItemDefinition Get(PrototypeItemId id)
        {
            foreach (var item in Definitions) if (item.Id == id) return item;
            throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown board item.");
        }
        public static PrototypeItemId GetRandomId(System.Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            int total = 0;
            foreach (var item in Definitions) total = checked(total + Mathf.Max(0, item.SpawnWeight));
            if (total == 0) throw new InvalidOperationException("At least one item needs a positive spawn weight.");
            int selected = random.Next(total);
            foreach (var item in Definitions)
            {
                selected -= Mathf.Max(0, item.SpawnWeight);
                if (selected < 0) return item.Id;
            }
            throw new InvalidOperationException("Invalid item weights.");
        }
        public static bool IsValid(PrototypeItemId id) => id >= PrototypeItemId.DoubleDice && id <= PrototypeItemId.Cloak;
    }

    public static class ItemShopRules
    {
        public const int ShopCount = 2;
        public const int OfferCount = 5;
        public const int TurnsBeforeRefresh = 5;
        public const float InteractionDistance = 5.5f;
    }

    public sealed class ItemShopStock
    {
        private readonly PrototypeItemId[] _offers =
            new PrototypeItemId[ItemShopRules.OfferCount];

        public ItemShopStock(int seed)
        {
            Seed = seed;
            var random = new System.Random(seed);
            for (var i = 0; i < _offers.Length; i++)
            {
                _offers[i] = PrototypeItemCatalog.GetRandomId(random);
            }
        }

        public int Seed { get; }
        public byte SoldMask { get; private set; }
        public bool IsSoldOut => SoldMask == (1 << ItemShopRules.OfferCount) - 1;

        public PrototypeItemId GetOffer(int index)
        {
            if (index < 0 || index >= _offers.Length)
            {
                return PrototypeItemId.None;
            }

            return _offers[index];
        }

        public bool IsSold(int index)
        {
            return index < 0 || index >= _offers.Length ||
                   (SoldMask & (1 << index)) != 0;
        }

        public bool TrySell(int index)
        {
            if (IsSold(index) || !PrototypeItemCatalog.IsValid(GetOffer(index)))
            {
                return false;
            }

            SoldMask = (byte)(SoldMask | (1 << index));
            return true;
        }

        public void RestoreSoldMask(byte soldMask)
        {
            SoldMask = (byte)(soldMask & ((1 << ItemShopRules.OfferCount) - 1));
        }
    }

    public static class ItemShopPlacementPolicy
    {
        public static List<BoardTile> BuildCandidates(
            IReadOnlyList<BoardTile> tiles,
            IReadOnlyCollection<Vector2Int> occupiedCoordinates,
            IReadOnlyCollection<Vector2Int> reservedCoordinates,
            bool excludePreviousLocation = false,
            Vector2Int previousLocation = default)
        {
            if (tiles == null)
            {
                throw new ArgumentNullException(nameof(tiles));
            }

            var occupied = occupiedCoordinates != null
                ? new HashSet<Vector2Int>(occupiedCoordinates)
                : new HashSet<Vector2Int>();
            var reserved = reservedCoordinates != null
                ? new HashSet<Vector2Int>(reservedCoordinates)
                : new HashSet<Vector2Int>();
            var seen = new HashSet<Vector2Int>();
            var candidates = new List<BoardTile>();
            for (var i = 0; i < tiles.Count; i++)
            {
                var tile = tiles[i];
                if (tile == null || tile.TileType == BoardTileType.Respawn ||
                    !seen.Add(tile.Coordinate) || occupied.Contains(tile.Coordinate) ||
                    reserved.Contains(tile.Coordinate) ||
                    excludePreviousLocation && tile.Coordinate == previousLocation)
                {
                    continue;
                }

                candidates.Add(tile);
            }

            return candidates;
        }

        public static bool TryChoose(
            IReadOnlyList<BoardTile> tiles,
            IReadOnlyCollection<Vector2Int> occupiedCoordinates,
            IReadOnlyCollection<Vector2Int> reservedCoordinates,
            IKeyShopRandomSource randomSource,
            out BoardTile selectedTile,
            bool excludePreviousLocation = false,
            Vector2Int previousLocation = default)
        {
            return KeyShopPlacementPolicy.TryChooseCandidate(
                BuildCandidates(
                    tiles,
                    occupiedCoordinates,
                    reservedCoordinates,
                    excludePreviousLocation,
                    previousLocation),
                randomSource,
                out selectedTile);
        }
    }

    public readonly struct PlayerRankingStats
    {
        public PlayerRankingStats(int keys, int gold, int minigameWins)
        {
            Keys = Math.Max(0, keys);
            Gold = Math.Max(0, gold);
            MinigameWins = Math.Max(0, minigameWins);
        }

        public int Keys { get; }
        public int Gold { get; }
        public int MinigameWins { get; }
    }

    public static class PlayerRankingRules
    {
        /// <summary>
        /// Returns competition ranks in input order: equal scores produce 1,1,3,4.
        /// </summary>
        public static int[] Calculate(IReadOnlyList<PlayerRankingStats> players)
        {
            if (players == null)
            {
                throw new ArgumentNullException(nameof(players));
            }

            var order = new int[players.Count];
            var ranks = new int[players.Count];
            for (var i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            Array.Sort(order, (left, right) => Compare(players[left], players[right]));
            for (var sortedIndex = 0; sortedIndex < order.Length; sortedIndex++)
            {
                var inputIndex = order[sortedIndex];
                ranks[inputIndex] = sortedIndex > 0 &&
                                    Compare(
                                        players[order[sortedIndex - 1]],
                                        players[inputIndex]) == 0
                    ? ranks[order[sortedIndex - 1]]
                    : sortedIndex + 1;
            }

            return ranks;
        }

        private static int Compare(PlayerRankingStats left, PlayerRankingStats right)
        {
            var keys = right.Keys.CompareTo(left.Keys);
            if (keys != 0)
            {
                return keys;
            }

            var gold = right.Gold.CompareTo(left.Gold);
            return gold != 0
                ? gold
                : right.MinigameWins.CompareTo(left.MinigameWins);
        }
    }
}
