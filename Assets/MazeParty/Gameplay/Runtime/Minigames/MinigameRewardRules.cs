using System;

namespace MazeParty.Gameplay.Minigames
{
    /// <summary>
    /// Shared board-economy rewards granted once from a minigame's final
    /// leaderboard. Per-round points remain owned by each minigame and are only
    /// used to determine this final placement.
    /// </summary>
    public static class MinigameRewardRules
    {
        public const int PlacementCount = 4;
        public const string FinalPlacementGoldSchedule = "10 / 6 / 3 / 0";

        public static int GetFinalPlacementGold(int rank)
        {
            switch (rank)
            {
                case 1:
                    return 10;
                case 2:
                    return 6;
                case 3:
                    return 3;
                case 4:
                    return 0;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(rank),
                        rank,
                        "Rank must be between 1 and 4.");
            }
        }
    }
}
