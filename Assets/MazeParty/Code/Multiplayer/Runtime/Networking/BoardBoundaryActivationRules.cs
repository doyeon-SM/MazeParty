using System;

namespace MazeParty.Multiplayer
{
    internal static class BoardBoundaryActivationRules
    {
        public static bool IsLobbyPhase(string sessionPhase)
        {
            return string.Equals(
                sessionPhase,
                MultiplayerConstants.LobbyPhase,
                StringComparison.Ordinal);
        }

        public static bool ShouldActivate(
            string sessionPhase,
            bool gameplayEnabled,
            bool isActionPhase,
            bool isCombatPhase,
            bool combatBoundaryActive,
            bool hasLogicalTile)
        {
            if (!string.Equals(
                    sessionPhase,
                    MultiplayerConstants.PlayingPhase,
                    StringComparison.Ordinal) ||
                !gameplayEnabled ||
                !hasLogicalTile)
            {
                return false;
            }

            return isActionPhase ||
                   (isCombatPhase && combatBoundaryActive);
        }
    }
}
