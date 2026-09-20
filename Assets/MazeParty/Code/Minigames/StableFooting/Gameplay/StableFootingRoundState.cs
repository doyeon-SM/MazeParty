using System;
using System.Collections.Generic;

namespace MazeParty.Gameplay.Minigames.StableFooting
{
    public enum StableFootingRoundEndReason : byte
    {
        None,
        LastSurvivor,
        AllEliminated,
        TimeLimit
    }

    public readonly struct StableFootingFallResolution
    {
        internal StableFootingFallResolution(
            int playerSlot,
            bool wasApplied,
            double eliminatedAtSeconds,
            ulong serverEventOrder)
        {
            PlayerSlot = playerSlot;
            WasApplied = wasApplied;
            EliminatedAtSeconds = eliminatedAtSeconds;
            ServerEventOrder = serverEventOrder;
        }

        public int PlayerSlot { get; }
        public bool WasApplied { get; }
        public double EliminatedAtSeconds { get; }
        public ulong ServerEventOrder { get; }
    }

    public sealed class StableFootingPlayerRoundState
    {
        internal StableFootingPlayerRoundState(int playerSlot)
        {
            PlayerSlot = playerSlot;
        }

        public int PlayerSlot { get; }
        public bool IsEliminated { get; private set; }
        public bool IsAlive => !IsEliminated;
        public double EliminatedAtSeconds { get; private set; }
        public ulong EliminationOrder { get; private set; }

        internal StableFootingFallResolution Eliminate(
            double roundElapsedSeconds,
            ulong serverEventOrder)
        {
            if (IsEliminated)
            {
                return new StableFootingFallResolution(
                    PlayerSlot,
                    false,
                    EliminatedAtSeconds,
                    EliminationOrder);
            }

            IsEliminated = true;
            EliminatedAtSeconds = roundElapsedSeconds;
            EliminationOrder = serverEventOrder;
            return new StableFootingFallResolution(
                PlayerSlot,
                true,
                EliminatedAtSeconds,
                EliminationOrder);
        }

        internal StableFootingRoundOutcome CaptureOutcome()
        {
            return IsAlive
                ? StableFootingRoundOutcome.Survive(PlayerSlot)
                : StableFootingRoundOutcome.Eliminate(
                    PlayerSlot,
                    EliminatedAtSeconds,
                    EliminationOrder);
        }
    }

    /// <summary>
    /// Pure authoritative elimination and completion state for one round.
    /// World movement and tile occupancy remain presentation/runtime concerns.
    /// </summary>
    public sealed class StableFootingRoundState
    {
        private readonly StableFootingPlayerRoundState[] _players;
        private ulong _nextServerEventOrder = 1UL;

        public StableFootingRoundState(
            ulong serverSeed,
            int roundNumber)
        {
            StableFootingRules.ValidateRoundNumber(roundNumber);
            ServerSeed = serverSeed;
            RoundNumber = roundNumber;
            Schedule = StableFootingCycleScheduleGenerator.Generate(
                serverSeed,
                roundNumber);
            _players =
                new StableFootingPlayerRoundState[
                    StableFootingRules.PlayerCount];
            for (var playerSlot = 0;
                 playerSlot < _players.Length;
                 playerSlot++)
            {
                _players[playerSlot] =
                    new StableFootingPlayerRoundState(playerSlot);
            }
        }

        public ulong ServerSeed { get; }
        public int RoundNumber { get; }
        public StableFootingRoundSchedule Schedule { get; }
        public bool IsComplete { get; private set; }
        public StableFootingRoundEndReason EndReason { get; private set; }
        public StableFootingRoundResult Result { get; private set; }

        public int AlivePlayerCount
        {
            get
            {
                var count = 0;
                for (var index = 0; index < _players.Length; index++)
                {
                    if (_players[index].IsAlive)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public StableFootingPlayerRoundState GetPlayer(int playerSlot)
        {
            if (!StableFootingRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerSlot),
                    playerSlot,
                    "Player slot must be between 0 and 3.");
            }

            return _players[playerSlot];
        }

        /// <summary>
        /// Resolves everyone falling during one authoritative simulation step.
        /// Input order is the server event order used for same-time ranking.
        /// </summary>
        public IReadOnlyList<StableFootingFallResolution> ResolveFalls(
            IReadOnlyList<int> playerSlotsInServerOrder,
            double roundElapsedSeconds)
        {
            if (playerSlotsInServerOrder == null)
            {
                throw new ArgumentNullException(
                    nameof(playerSlotsInServerOrder));
            }

            StableFootingRules.ValidateRoundElapsedSeconds(
                roundElapsedSeconds);
            var seenSlots = new bool[StableFootingRules.PlayerCount];
            for (var index = 0;
                 index < playerSlotsInServerOrder.Count;
                 index++)
            {
                var playerSlot = playerSlotsInServerOrder[index];
                if (!StableFootingRules.IsValidPlayerSlot(playerSlot))
                {
                    throw new ArgumentException(
                        "Fall list contains an invalid player slot.",
                        nameof(playerSlotsInServerOrder));
                }

                if (seenSlots[playerSlot])
                {
                    throw new ArgumentException(
                        "Fall list contains a duplicate player slot.",
                        nameof(playerSlotsInServerOrder));
                }

                seenSlots[playerSlot] = true;
            }

            var resolutions =
                new StableFootingFallResolution[
                    playerSlotsInServerOrder.Count];
            if (IsComplete ||
                roundElapsedSeconds >= StableFootingRules.RoundSeconds)
            {
                if (!IsComplete)
                {
                    TryEndForTimeout(roundElapsedSeconds);
                }

                for (var index = 0;
                     index < playerSlotsInServerOrder.Count;
                     index++)
                {
                    var player = GetPlayer(
                        playerSlotsInServerOrder[index]);
                    resolutions[index] =
                        new StableFootingFallResolution(
                            player.PlayerSlot,
                            false,
                            player.EliminatedAtSeconds,
                            player.EliminationOrder);
                }

                return Array.AsReadOnly(resolutions);
            }

            for (var index = 0;
                 index < playerSlotsInServerOrder.Count;
                 index++)
            {
                var player = GetPlayer(playerSlotsInServerOrder[index]);
                resolutions[index] = player.IsEliminated
                    ? player.Eliminate(
                        player.EliminatedAtSeconds,
                        player.EliminationOrder)
                    : player.Eliminate(
                        roundElapsedSeconds,
                        _nextServerEventOrder++);
            }

            if (AlivePlayerCount == 0)
            {
                Complete(StableFootingRoundEndReason.AllEliminated);
            }
            else if (AlivePlayerCount == 1)
            {
                Complete(StableFootingRoundEndReason.LastSurvivor);
            }

            return Array.AsReadOnly(resolutions);
        }

        public bool TryEndForTimeout(double roundElapsedSeconds)
        {
            StableFootingRules.ValidateRoundElapsedSeconds(
                roundElapsedSeconds);
            if (IsComplete ||
                roundElapsedSeconds < StableFootingRules.RoundSeconds)
            {
                return false;
            }

            Complete(StableFootingRoundEndReason.TimeLimit);
            return true;
        }

        private void Complete(StableFootingRoundEndReason reason)
        {
            var outcomes =
                new StableFootingRoundOutcome[
                    StableFootingRules.PlayerCount];
            for (var index = 0; index < _players.Length; index++)
            {
                outcomes[index] = _players[index].CaptureOutcome();
            }

            Result = StableFootingRoundScoring.Score(outcomes);
            EndReason = reason;
            IsComplete = true;
        }
    }
}
