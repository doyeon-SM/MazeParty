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
        public const int UncreditedDeathLoss = 6;
        public const int UncreditedDeathReward = 2;

        public static int GoldLoss(int gold, bool hasKiller) =>
            hasKiller ? DroppedGold(gold) : Math.Min(Math.Max(0, gold), UncreditedDeathLoss);

        public static int GoldReward(int gold, int victimSlot, int killerSlot, int recipientSlot)
        {
            if (victimSlot < 0 || victimSlot >= 4 || recipientSlot < 0 ||
                recipientSlot >= 4 || recipientSlot == victimSlot) return 0;
            var hasKiller = killerSlot >= 0 && killerSlot < 4 && killerSlot != victimSlot;
            return hasKiller
                ? (recipientSlot == killerSlot ? DroppedGold(gold) : 0)
                : UncreditedDeathReward;
        }

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
