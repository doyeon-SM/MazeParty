using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay.Minigames
{
    /// <summary>
    /// Ties the host's saved minigame schedule to the players it was made for.
    /// The saved order exists so the same players can restart an interrupted
    /// match (crash, network failure) with the same minigames. Any other group
    /// of players, in any room, starts from a fresh schedule.
    /// </summary>
    public static class MinigameScheduleRoster
    {
        /// <summary>
        /// Order-independent fingerprint of the account IDs of the players in
        /// the match; seats may differ between restarts. Only the hash is
        /// stored. Returns an empty string when no player ID is known.
        /// </summary>
        public static string CreateKey(IEnumerable<string> playerIds)
        {
            if (playerIds == null)
            {
                return string.Empty;
            }

            var ids = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var playerId in playerIds)
            {
                if (!string.IsNullOrWhiteSpace(playerId))
                {
                    ids.Add(playerId.Trim());
                }
            }

            return ids.Count == 0
                ? string.Empty
                : Hash128.Compute(string.Join("\n", ids)).ToString();
        }

        /// <summary>
        /// The saved schedule is reused only for exactly the same players. An
        /// unknown roster, or a schedule saved before rosters were recorded,
        /// is never reused.
        /// </summary>
        public static bool CanReuse(string savedRosterKey, string currentRosterKey)
        {
            return !string.IsNullOrEmpty(currentRosterKey) &&
                   string.Equals(
                       savedRosterKey,
                       currentRosterKey,
                       StringComparison.Ordinal);
        }
    }
}
