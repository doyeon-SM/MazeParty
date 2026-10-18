using System;
using System.Collections.Generic;

namespace MazeParty.Gameplay.Minigames
{
    public static class CompetitionRanking
    {
        public static int Rank(IReadOnlyList<int> scores, int slot)
        {
            if (scores == null) throw new ArgumentNullException(nameof(scores));
            if (slot < 0 || slot >= scores.Count) throw new ArgumentOutOfRangeException(nameof(slot));
            var rank = 1;
            for (var other = 0; other < scores.Count; other++)
                if (scores[other] > scores[slot]) rank++;
            return rank;
        }
    }
}
