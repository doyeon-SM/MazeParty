using System;

namespace MazeParty.Gameplay.Minigames
{
    /// <summary>Additional rounds for originally single-round games; rewards settle once.</summary>
    public sealed class MinigameRoundSeries
    {
        private readonly int[] _points = new int[4];
        public MinigameRoundSeries(int rounds)
        {
            if (rounds < 1 || rounds > 5) throw new ArgumentOutOfRangeException(nameof(rounds));
            Rounds = rounds;
        }
        public int Rounds { get; }
        public int CompletedRounds { get; private set; }
        public bool Complete => CompletedRounds == Rounds;
        public int Points(int slot) => slot >= 0 && slot < 4 ? _points[slot] : 0;
        public bool TryRecord(int round, PlayerPlacementSet placements)
        {
            if (Complete || round != CompletedRounds + 1 || placements.Count != 4) return false;
            for (var slot = 0; slot < 4; slot++) _points[slot] += 4 - placements.GetRankForSlot(slot);
            CompletedRounds++; return true;
        }
        public PlayerPlacementSet FinalPlacements()
        {
            if (!Complete) throw new InvalidOperationException("The series has not finished.");
            var values = new PlayerPlacement[4];
            for (var slot=0;slot<4;slot++)
            {
                var rank=1; for(var other=0;other<4;other++) if(_points[other]>_points[slot]) rank++;
                values[slot]=new PlayerPlacement(slot,rank);
            }
            if (!PlayerPlacementSet.TryCreate(values,out var result,out _)) throw new InvalidOperationException();
            return result;
        }
    }
}
