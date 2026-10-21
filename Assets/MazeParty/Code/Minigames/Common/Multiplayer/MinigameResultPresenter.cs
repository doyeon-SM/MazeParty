using System;
using System.Text;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;

namespace MazeParty.Multiplayer
{
    internal readonly struct MinigameResultRow : IEquatable<MinigameResultRow>
    {
        public readonly int Rank;
        private readonly string _format;
        private readonly double _first, _second;
        private readonly int _metricCount;
        public MinigameResultRow(int rank, string format)
        { Rank = rank; _format = format; _first = _second = 0d; _metricCount = 0; }
        public MinigameResultRow(int rank, string format, double first) : this(rank, format)
        { _first = first; _metricCount = 1; }
        public MinigameResultRow(int rank, string format, double first, double second) : this(rank, format, first)
        { _second = second; _metricCount = 2; }
        public bool Equals(MinigameResultRow other) => Rank == other.Rank && _format == other._format &&
            _first.Equals(other._first) && _second.Equals(other._second) && _metricCount == other._metricCount;
        public string Format(string name)
        {
            var gold = MinigameRewardRules.GetFinalPlacementGold(Rank);
            return _metricCount == 0 ? GameText.F(_format, Rank, name, gold) :
                _metricCount == 1 ? GameText.F(_format, Rank, name, _first, gold) :
                GameText.F(_format, Rank, name, _first, _second, gold);
        }
    }

    /// <summary>One validated, cached result table. Reading presentation never settles rewards.</summary>
    internal sealed class MinigameResultPresenter
    {
        private readonly MinigameResultRow[] _rows = new MinigameResultRow[PlayerSlotRules.Count];
        private readonly string[] _names = new string[PlayerSlotRules.Count];
        private readonly PlayerPlacement[] _placements = new PlayerPlacement[PlayerSlotRules.Count];
        private readonly int[] _order = new int[PlayerSlotRules.Count];
        private GameLanguage _language;
        private string _summary;

        public string Present(IMinigameRuntimeAdapter runtime, NetworkMatchState match, Func<int, string> nameForSlot)
        {
            var changed = _summary == null || _language != GameText.Language;
            _language = GameText.Language;
            for (var slot = 0; slot < _rows.Length; slot++)
            {
                var row = runtime != null && runtime.UsesRoundSeriesResults && match != null && match.HasConfiguredSeries
                    ? new MinigameResultRow(match.GetSeriesFinalRank(slot), "{0}.  {1}  SCORE {2}  GOLD +{3}", match.GetSeriesPoints(slot))
                    : runtime != null ? runtime.GetResultRow(slot) : default;
                var name = nameForSlot(slot);
                changed |= !_rows[slot].Equals(row) || _names[slot] != name;
                _rows[slot] = row;
                _names[slot] = name;
                _placements[slot] = new PlayerPlacement(slot, row.Rank);
            }
            if (!changed) return _summary;
            if (!PlayerPlacementSet.TryCreate(_placements, out _, out _))
                return _summary = GameText.T("Final standings are synchronizing...");

            // Four fixed slots: stable insertion keeps the existing slot order for ties.
            for (var slot = 0; slot < _order.Length; slot++)
            {
                var index = slot;
                while (index > 0 && _rows[_order[index - 1]].Rank > _rows[slot].Rank)
                { _order[index] = _order[index - 1]; index--; }
                _order[index] = slot;
            }
            var builder = new StringBuilder();
            foreach (var slot in _order)
            {
                if (builder.Length > 0) builder.AppendLine();
                builder.Append(_rows[slot].Format(_names[slot]));
            }
            return _summary = builder.ToString();
        }
    }
}
