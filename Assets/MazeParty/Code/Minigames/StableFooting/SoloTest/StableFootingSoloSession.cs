using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.StableFooting;
using MazeParty.Multiplayer;

namespace MazeParty.Dev.MinigameSoloTest
{
    public enum StableFootingSoloPhase : byte
    {
        Countdown,
        Running,
        RoundResult,
        Complete
    }

    /// <summary>
    /// Local clock and player-tile adapter around the production Stable Footing
    /// schedule, fall resolution and three-round scoring rules. The controller
    /// owns transforms; this class keeps only deterministic tile occupancy.
    /// </summary>
    public sealed class StableFootingSoloSession
    {
        public const int LocalPlayerSlot = 0;
        public const double CountdownSeconds =
            NetworkStableFootingState.CountdownSeconds;
        public const double ResultSeconds =
            NetworkStableFootingState.RoundResultSeconds;

        private readonly StableFootingRoundResult[] _roundResults =
            new StableFootingRoundResult[StableFootingRules.RoundCount];
        private readonly int[] _playerTileIndices =
            new int[StableFootingRules.PlayerCount];

        private IReadOnlyList<StableFootingLeaderboardEntry> _leaderboard;
        private IReadOnlyList<StableFootingFallResolution> _lastFalls =
            Array.Empty<StableFootingFallResolution>();
        private int _nextDropCycleIndex;

        public int Seed { get; private set; }
        public int RoundNumber { get; private set; }
        public StableFootingSoloPhase Phase { get; private set; }
        public double RemainingSeconds { get; private set; }
        public double RunningElapsedSeconds { get; private set; }
        public StableFootingRoundState RoundState { get; private set; }
        public StableFootingCycle PresentationCycle { get; private set; }
        public StableFootingCyclePhase PresentationCyclePhase
        {
            get;
            private set;
        }
        public IReadOnlyList<StableFootingLeaderboardEntry> Leaderboard =>
            _leaderboard;
        public IReadOnlyList<StableFootingFallResolution> LastFalls =>
            _lastFalls;

        public StableFootingCycle CurrentCycle =>
            Phase == StableFootingSoloPhase.Running && RoundState != null
                ? RoundState.Schedule.GetCycleAt(RunningElapsedSeconds)
                : null;

        public StableFootingCyclePhase CurrentCyclePhase =>
            Phase == StableFootingSoloPhase.Running && RoundState != null
                ? RoundState.Schedule.GetPhaseAt(RunningElapsedSeconds)
                : StableFootingCyclePhase.ShuffleReveal;

        public StableFootingPlayerRoundState LocalPlayer =>
            RoundState?.GetPlayer(LocalPlayerSlot);

        public void Begin(int seed)
        {
            Seed = seed;
            Array.Clear(_roundResults, 0, _roundResults.Length);
            _leaderboard = null;
            RoundNumber = 1;
            EnterCountdown();
        }

        public void Tick(float unscaledDeltaTime)
        {
            if (float.IsNaN(unscaledDeltaTime) ||
                float.IsInfinity(unscaledDeltaTime))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(unscaledDeltaTime));
            }

            var deltaTime = Math.Max(0d, unscaledDeltaTime);
            switch (Phase)
            {
                case StableFootingSoloPhase.Countdown:
                    RemainingSeconds = Math.Max(
                        0d,
                        RemainingSeconds - deltaTime);
                    if (RemainingSeconds <= 0d)
                    {
                        Phase = StableFootingSoloPhase.Running;
                        RunningElapsedSeconds = 0d;
                        RemainingSeconds = StableFootingRules.RoundSeconds;
                    }
                    break;

                case StableFootingSoloPhase.Running:
                    RunningElapsedSeconds = Math.Min(
                        StableFootingRules.RoundSeconds,
                        RunningElapsedSeconds + deltaTime);
                    RemainingSeconds = Math.Max(
                        0d,
                        StableFootingRules.RoundSeconds -
                        RunningElapsedSeconds);
                    RefreshPresentationForElapsed();

                    ResolveUnsupportedPlayers();
                    if (RoundState.IsComplete)
                    {
                        EnterRoundResult();
                        break;
                    }

                    ResolveCrossedDropBoundaries();
                    if (RoundState.IsComplete)
                    {
                        EnterRoundResult();
                        break;
                    }

                    if (RoundState.TryEndForTimeout(
                            RunningElapsedSeconds))
                    {
                        EnterRoundResult();
                    }
                    break;

                case StableFootingSoloPhase.RoundResult:
                    RemainingSeconds = Math.Max(
                        0d,
                        RemainingSeconds - deltaTime);
                    if (RemainingSeconds <= 0d)
                    {
                        AdvanceAfterRoundResult();
                    }
                    break;
            }
        }

        public bool SetPlayerTile(int playerSlot, int tileIndex)
        {
            ValidatePlayerSlot(playerSlot);
            if (tileIndex < -1 ||
                tileIndex >= StableFootingRules.TileCount ||
                RoundState == null ||
                RoundState.GetPlayer(playerSlot).IsEliminated)
            {
                return false;
            }

            _playerTileIndices[playerSlot] = tileIndex;
            return true;
        }

        public int GetPlayerTile(int playerSlot)
        {
            ValidatePlayerSlot(playerSlot);
            return _playerTileIndices[playerSlot];
        }

        public void RestartCurrentRound()
        {
            EnsureStarted();
            for (var index = RoundNumber - 1;
                 index < _roundResults.Length;
                 index++)
            {
                _roundResults[index] = null;
            }

            _leaderboard = null;
            EnterCountdown();
        }

        public StableFootingRoundResult GetRoundResult(int roundNumber)
        {
            if (roundNumber < 1 ||
                roundNumber > StableFootingRules.RoundCount)
            {
                throw new ArgumentOutOfRangeException(nameof(roundNumber));
            }
            return _roundResults[roundNumber - 1];
        }

        private void ResolveCrossedDropBoundaries()
        {
            var cycles = RoundState.Schedule.Cycles;
            while (_nextDropCycleIndex < cycles.Count)
            {
                var cycle = cycles[_nextDropCycleIndex];
                if (cycle.MoveEndsAtSeconds > RunningElapsedSeconds)
                {
                    break;
                }

                PresentationCycle = cycle;
                PresentationCyclePhase = StableFootingCyclePhase.Drop;

                var unsafeSlots = new List<int>(
                    StableFootingRules.PlayerCount);
                for (var slot = 0;
                     slot < StableFootingRules.PlayerCount;
                     slot++)
                {
                    var player = RoundState.GetPlayer(slot);
                    if (player.IsAlive &&
                        (_playerTileIndices[slot] < 0 ||
                         !cycle.IsTileActive(_playerTileIndices[slot]) ||
                         !cycle.IsTileSafe(_playerTileIndices[slot])))
                    {
                        unsafeSlots.Add(slot);
                    }
                }

                _lastFalls = RoundState.ResolveFalls(
                    unsafeSlots,
                    cycle.MoveEndsAtSeconds);
                _nextDropCycleIndex++;

                if (RoundState.IsComplete)
                {
                    return;
                }
            }

            RefreshPresentationForElapsed();
        }

        private void RefreshPresentationForElapsed()
        {
            if (RoundState == null)
            {
                return;
            }

            var cycle = RoundState.Schedule.GetCycleAt(
                RunningElapsedSeconds);
            if (cycle == null)
            {
                return;
            }

            PresentationCycle = cycle;
            PresentationCyclePhase = cycle.GetPhaseAt(
                RunningElapsedSeconds);
        }

        private void ResolveUnsupportedPlayers()
        {
            var cycle = RoundState.Schedule.GetCycleAt(
                RunningElapsedSeconds);
            if (cycle == null ||
                RoundState.Schedule.GetPhaseAt(RunningElapsedSeconds) !=
                    StableFootingCyclePhase.Move)
            {
                return;
            }

            var unsupportedSlots = new List<int>(
                StableFootingRules.PlayerCount);
            for (var slot = 0;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                var player = RoundState.GetPlayer(slot);
                var tileIndex = _playerTileIndices[slot];
                if (player.IsAlive &&
                    (tileIndex < 0 || !cycle.IsTileActive(tileIndex)))
                {
                    unsupportedSlots.Add(slot);
                }
            }

            if (unsupportedSlots.Count > 0)
            {
                _lastFalls = RoundState.ResolveFalls(
                    unsupportedSlots,
                    RunningElapsedSeconds);
            }
        }

        private void EnterCountdown()
        {
            RoundState = new StableFootingRoundState(
                unchecked((ulong)(uint)Seed),
                RoundNumber);
            for (var slot = 0;
                 slot < _playerTileIndices.Length;
                 slot++)
            {
                _playerTileIndices[slot] =
                    NetworkStableFootingState.GetStartTileIndex(slot);
            }

            Phase = StableFootingSoloPhase.Countdown;
            RemainingSeconds = CountdownSeconds;
            RunningElapsedSeconds = 0d;
            _nextDropCycleIndex = 0;
            _lastFalls = Array.Empty<StableFootingFallResolution>();
            PresentationCycle = RoundState.Schedule.Cycles[0];
            PresentationCyclePhase =
                StableFootingCyclePhase.ShuffleReveal;
        }

        private void EnterRoundResult()
        {
            if (RoundState == null || !RoundState.IsComplete)
            {
                throw new InvalidOperationException(
                    "The round must be complete before showing results.");
            }

            _roundResults[RoundNumber - 1] = RoundState.Result;
            Phase = StableFootingSoloPhase.RoundResult;
            RemainingSeconds = ResultSeconds;
        }

        private void AdvanceAfterRoundResult()
        {
            if (RoundNumber >= StableFootingRules.RoundCount)
            {
                _leaderboard =
                    StableFootingMatchScoring.BuildLeaderboard(
                        _roundResults);
                Phase = StableFootingSoloPhase.Complete;
                RemainingSeconds = 0d;
                return;
            }

            RoundNumber++;
            EnterCountdown();
        }

        private void EnsureStarted()
        {
            if (RoundState == null ||
                RoundNumber < 1 ||
                RoundNumber > StableFootingRules.RoundCount)
            {
                throw new InvalidOperationException(
                    "Start the Stable Footing solo session first.");
            }
        }

        private static void ValidatePlayerSlot(int playerSlot)
        {
            if (playerSlot < 0 ||
                playerSlot >= StableFootingRules.PlayerCount)
            {
                throw new ArgumentOutOfRangeException(nameof(playerSlot));
            }
        }
    }
}
