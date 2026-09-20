using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public static class BoardDeathRules
    {
        public const double DeathPresentationSeconds = 1d;
        public const double RespawnProtectionSeconds = 10d;
        public const int DroppedGoldPercent = 30;

        public static int DroppedGold(int gold)
        {
            return (int)((long)Math.Max(0, gold) * DroppedGoldPercent / 100L);
        }

        public static BoardTile NearestRespawn(
            IReadOnlyList<BoardTile> tiles,
            Vector3 deathPosition)
        {
            if (tiles == null)
            {
                return null;
            }

            BoardTile nearest = null;
            var nearestDistance = float.PositiveInfinity;
            for (var index = 0; index < tiles.Count; index++)
            {
                var tile = tiles[index];
                if (tile == null || tile.TileType != BoardTileType.Respawn)
                {
                    continue;
                }

                var distance = (tile.WorldCenter - deathPosition).sqrMagnitude;
                if (distance < nearestDistance ||
                    distance == nearestDistance &&
                    (nearest == null ||
                     tile.Coordinate.x < nearest.Coordinate.x ||
                     tile.Coordinate.x == nearest.Coordinate.x &&
                     tile.Coordinate.y < nearest.Coordinate.y))
                {
                    nearest = tile;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }
    }
}
