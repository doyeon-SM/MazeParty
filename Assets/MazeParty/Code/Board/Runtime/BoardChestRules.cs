using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public static class BoardChestRules
    {
        public const int Count = 2;
        public static bool ShouldRefresh(int turn, int appearedTurn) =>
            turn >= 2 && turn % 2 == 0 && appearedTurn < turn;

        public static Vector2Int[] ChooseLocations(IEnumerable<BoardTile> tiles,
            ICollection<Vector2Int> shops, IReadOnlyList<Vector2Int> previous, int seed)
        {
            var candidates = new List<Vector2Int>();
            foreach (var tile in tiles)
                if (tile != null && tile.TileType != BoardTileType.Respawn &&
                    !shops.Contains(tile.Coordinate) && !candidates.Contains(tile.Coordinate))
                    candidates.Add(tile.Coordinate);
            // Sort before drawing so hierarchy order cannot change seeded placement.
            candidates.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            if (candidates.Count < Count)
                throw new InvalidOperationException("The board needs two eligible chest tiles.");
            var random = new System.Random(seed);
            var result = new Vector2Int[Count];
            for (var i = 0; i < Count; i++)
            {
                var choices = new List<Vector2Int>(candidates);
                if (previous != null && previous.Count > i && choices.Count > 1)
                    choices.Remove(previous[i]);
                result[i] = choices[random.Next(choices.Count)];
                candidates.Remove(result[i]);
            }
            return result;
        }

        public static PrototypeItemId DrawItem(IReadOnlyList<BoardItemDefinition> items, int seed, string mapId = null)
        {
            var total = 0;
            foreach (var item in items)
                if (item.UseTiming == BoardItemUseTiming.Immediate && item.IsAvailableOnMap(mapId))
                    total = checked(total + Mathf.Max(0, item.SpawnWeight));
            if (total == 0)
                throw new InvalidOperationException("A chest needs an immediate-use item with positive weight.");
            var selected = new System.Random(seed).Next(total);
            foreach (var item in items)
            {
                if (item.UseTiming != BoardItemUseTiming.Immediate || !item.IsAvailableOnMap(mapId)) continue;
                selected -= Mathf.Max(0, item.SpawnWeight);
                if (selected < 0) return item.Id;
            }
            throw new InvalidOperationException("Invalid chest item weights.");
        }
    }
}
