using System;
using System.Collections.Generic;

namespace MazeParty.Gameplay.Minigames
{
    /// <summary>
    /// Board-facing final placement for one minigame player. Minigame-specific
    /// scores and tie breakers are resolved before crossing this boundary.
    /// </summary>
    public readonly struct PlayerPlacement
    {
        public PlayerPlacement(int playerSlot, int rank)
        {
            PlayerSlot = playerSlot;
            Rank = rank;
        }

        public int PlayerSlot { get; }
        public int Rank { get; }
    }

    public enum PlayerPlacementValidationError
    {
        None = 0,
        MissingPlacements = 1,
        IncorrectPlacementCount = 2,
        PlayerSlotOutOfRange = 3,
        RankOutOfRange = 4,
        DuplicatePlayerSlot = 5,
        DuplicateRank = 6,
        IncompletePlacements = 7
    }

    /// <summary>
    /// Validated, immutable four-player placement table normalized by slot.
    /// </summary>
    public readonly struct PlayerPlacementSet
    {
        private readonly uint _packedRanksBySlot;

        private PlayerPlacementSet(uint packedRanksBySlot)
        {
            _packedRanksBySlot = packedRanksBySlot;
        }

        public int Count => MinigameRewardRules.PlacementCount;

        public int GetRankForSlot(int playerSlot)
        {
            if (playerSlot < 0 || playerSlot >= Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerSlot),
                    playerSlot,
                    "Player slot must be between 0 and 3.");
            }

            return (int)((_packedRanksBySlot >> (playerSlot * 8)) & 0xffU);
        }

        public static bool TryCreate(
            IReadOnlyList<PlayerPlacement> placements,
            out PlayerPlacementSet placementSet,
            out PlayerPlacementValidationError error)
        {
            placementSet = default;
            if (placements == null)
            {
                error = PlayerPlacementValidationError.MissingPlacements;
                return false;
            }

            var playerCount = MinigameRewardRules.PlacementCount;
            if (placements.Count != playerCount)
            {
                error =
                    PlayerPlacementValidationError.IncorrectPlacementCount;
                return false;
            }

            var seenSlots = 0;
            var seenRanks = 0;
            uint packedRanks = 0U;
            for (var index = 0; index < placements.Count; index++)
            {
                var placement = placements[index];
                if (placement.PlayerSlot < 0 ||
                    placement.PlayerSlot >= playerCount)
                {
                    error =
                        PlayerPlacementValidationError.PlayerSlotOutOfRange;
                    return false;
                }

                if (placement.Rank < 1 || placement.Rank > playerCount)
                {
                    error = PlayerPlacementValidationError.RankOutOfRange;
                    return false;
                }

                var slotBit = 1 << placement.PlayerSlot;
                if ((seenSlots & slotBit) != 0)
                {
                    error =
                        PlayerPlacementValidationError.DuplicatePlayerSlot;
                    return false;
                }

                var rankBit = 1 << (placement.Rank - 1);
                if ((seenRanks & rankBit) != 0)
                {
                    error = PlayerPlacementValidationError.DuplicateRank;
                    return false;
                }

                seenSlots |= slotBit;
                seenRanks |= rankBit;
                packedRanks |=
                    (uint)placement.Rank << (placement.PlayerSlot * 8);
            }

            var completeMask = (1 << playerCount) - 1;
            if (seenSlots != completeMask || seenRanks != completeMask)
            {
                error = PlayerPlacementValidationError.IncompletePlacements;
                return false;
            }

            placementSet = new PlayerPlacementSet(packedRanks);
            error = PlayerPlacementValidationError.None;
            return true;
        }
    }
}
