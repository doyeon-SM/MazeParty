using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using Unity.Netcode;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        private readonly NetworkVariable<int> _configuredSeriesRound = new NetworkVariable<int>(1);
        private readonly NetworkVariable<uint> _seriesFinalRanks = new NetworkVariable<uint>();
        private readonly NetworkVariable<uint> _seriesPoints = new NetworkVariable<uint>();
        private MinigameRoundSeries _configuredSeries;
        public bool HasConfiguredSeries => UsesConfiguredRoundSeries && GameSettings.Get(CurrentMinigame).Rounds > 1;
        public int GetSeriesFinalRank(int slot) => slot >= 0 && slot < 4
            ? (int)((_seriesFinalRanks.Value >> (slot * 8)) & 255U) : 0;
        public int GetSeriesPoints(int slot) => slot >= 0 && slot < 4
            ? (int)((_seriesPoints.Value >> (slot * 8)) & 255U) : 0;
        private bool _nextConfiguredRoundPending;
        public int ConfiguredSeriesRound => _configuredSeriesRound.Value;
        public bool UsesConfiguredRoundSeries => IsSingleRoundGame(CurrentMinigame);
        private static bool IsSingleRoundGame(ScheduledMinigameId id) =>
            id == ScheduledMinigameId.TerritoryPaint || id == ScheduledMinigameId.BombPassing ||
            id == ScheduledMinigameId.ArenaCombat || id == ScheduledMinigameId.Flight;
        private void BeginConfiguredSeries()
        {
            _configuredSeriesRound.Value = 1; _nextConfiguredRoundPending = false;
            _seriesFinalRanks.Value = 0; _seriesPoints.Value = 0;
            _configuredSeries = IsSingleRoundGame(CurrentMinigame) && GameSettings.Get(CurrentMinigame).Rounds > 1
                ? new MinigameRoundSeries(GameSettings.Get(CurrentMinigame).Rounds) : null;
        }
        private bool ContinueConfiguredSeries(ref PlayerPlacementSet placements)
        {
            if (_configuredSeries == null) return false;
            if (_nextConfiguredRoundPending) return true;
            if (!_configuredSeries.TryRecord(_configuredSeriesRound.Value, placements))
                return true;
            uint points = 0;
            for (var slot = 0; slot < 4; slot++)
                points |= (uint)_configuredSeries.Points(slot) << (slot * 8);
            _seriesPoints.Value = points;
            if (_configuredSeries.Complete)
            {
                placements = _configuredSeries.FinalPlacements();
                uint ranks = 0;
                for (var slot = 0; slot < 4; slot++)
                    ranks |= (uint)placements.GetRankForSlot(slot) << (slot * 8);
                _seriesFinalRanks.Value = ranks;
                return false;
            }
            _nextConfiguredRoundPending = true;
            return true;
        }
        private void TickConfiguredSeries()
        {
            if (!_nextConfiguredRoundPending || FlowState != BoardFlowState.MinigamePlaying ||
                IsSimulationSuspended || !HasFourBoardReadyPlayers() ||
                !TryGetCurrentMinigameRuntime(out var runtime) || !runtime.IsSpawned) return;
            // Defer until the previous state's completion callback has returned.
            _nextConfiguredRoundPending = false;
            _configuredSeriesRound.Value++;
            runtime.EndMatchOnServer();
            runtime.BeginMatchOnServer(unchecked(_currentMinigameSeed.Value +
                0x9E3779B97F4A7C15UL * (ulong)(_configuredSeriesRound.Value - 1)));
        }
    }
}
