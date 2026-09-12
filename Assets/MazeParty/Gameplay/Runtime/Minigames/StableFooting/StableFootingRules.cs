using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MazeParty.Gameplay.Minigames.StableFooting
{
    /// <summary>
    /// Fixed rules shared by the authoritative server, clients and solo session.
    /// </summary>
    public static class StableFootingRules
    {
        public const int PlayerCount = 4;
        public const int BoardWidth = 6;
        public const int BoardHeight = 8;
        public const int TileCount = BoardWidth * BoardHeight;
        public const int SymbolCount = 3;
        public const int RoundCount = 3;
        public const int PermanentTilesRemovedPerCycle = 2;
        public const double RoundSeconds = 60d;
        public const double ShuffleRevealSeconds = 1d;
        public const double InitialMoveSeconds = 4d;
        public const double MoveAccelerationPerCycleSeconds = 0.25d;
        public const double MinimumMoveSeconds = 2d;
        public const double DropSeconds = 2d;
        public const double RestoreSeconds = 1d;
        public const double PushCooldownSeconds = 0.65d;
        public const float PushDistanceInTiles = 1.5f;

        public static int GetPointsForRank(int rank)
        {
            if (rank < 1 || rank > PlayerCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rank),
                    rank,
                    "Rank must be between 1 and 4.");
            }

            return PlayerCount - rank;
        }

        public static double GetMoveSecondsForCycle(int cycleNumber)
        {
            if (cycleNumber < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cycleNumber),
                    cycleNumber,
                    "Cycle number must be positive.");
            }

            return Math.Max(
                MinimumMoveSeconds,
                InitialMoveSeconds -
                ((cycleNumber - 1) * MoveAccelerationPerCycleSeconds));
        }

        public static bool IsValidPlayerSlot(int playerSlot)
        {
            return playerSlot >= 0 && playerSlot < PlayerCount;
        }

        internal static void ValidateRoundNumber(int roundNumber)
        {
            if (roundNumber < 1 || roundNumber > RoundCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(roundNumber),
                    roundNumber,
                    "Round number must be between 1 and 3.");
            }
        }

        internal static void ValidateTileIndex(
            int tileIndex,
            string parameterName)
        {
            if (tileIndex < 0 || tileIndex >= TileCount)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    tileIndex,
                    "Tile index must be between 0 and 47.");
            }
        }

        internal static void ValidateRoundElapsedSeconds(double value)
        {
            if (double.IsNaN(value) ||
                double.IsInfinity(value) ||
                value < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Round time must be a finite non-negative value.");
            }
        }
    }

    public enum StableFootingSymbol : byte
    {
        Cross,
        Circle,
        Square
    }

    public enum StableFootingCyclePhase : byte
    {
        ShuffleReveal,
        Move,
        Drop,
        Restore,
        RoundComplete
    }

    public readonly struct StableFootingTileAssignment :
        IEquatable<StableFootingTileAssignment>
    {
        public StableFootingTileAssignment(
            int tileIndex,
            StableFootingSymbol symbol)
        {
            StableFootingRules.ValidateTileIndex(
                tileIndex,
                nameof(tileIndex));
            if (symbol < StableFootingSymbol.Cross ||
                symbol > StableFootingSymbol.Square)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(symbol),
                    symbol,
                    "Symbol must be Cross, Circle or Square.");
            }

            TileIndex = tileIndex;
            Symbol = symbol;
        }

        public int TileIndex { get; }
        public StableFootingSymbol Symbol { get; }

        public bool Equals(StableFootingTileAssignment other)
        {
            return TileIndex == other.TileIndex && Symbol == other.Symbol;
        }

        public override bool Equals(object obj)
        {
            return obj is StableFootingTileAssignment other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (TileIndex * 397) ^ (int)Symbol;
            }
        }

        public override string ToString()
        {
            return $"{TileIndex}:{Symbol}";
        }
    }

    /// <summary>
    /// Immutable authoritative instructions for one platform cycle.
    /// </summary>
    public sealed class StableFootingCycle
    {
        private readonly ReadOnlyCollection<StableFootingTileAssignment>
            _assignments;
        private readonly ReadOnlyCollection<int> _permanentlyRemovedTileIndices;
        private readonly bool[] _activeTiles;
        private readonly bool[] _permanentlyRemovedTiles;
        private readonly StableFootingSymbol[] _symbolsByTile;

        internal StableFootingCycle(
            int cycleNumber,
            double startsAtSeconds,
            double moveSeconds,
            StableFootingSymbol safeSymbol,
            StableFootingTileAssignment[] assignments,
            int[] permanentlyRemovedTileIndices)
        {
            CycleNumber = cycleNumber;
            StartsAtSeconds = startsAtSeconds;
            MoveSeconds = moveSeconds;
            SafeSymbol = safeSymbol;
            ShuffleRevealEndsAtSeconds =
                startsAtSeconds + StableFootingRules.ShuffleRevealSeconds;
            MoveEndsAtSeconds = ShuffleRevealEndsAtSeconds + moveSeconds;
            DropEndsAtSeconds =
                MoveEndsAtSeconds + StableFootingRules.DropSeconds;
            EndsAtSeconds =
                DropEndsAtSeconds + StableFootingRules.RestoreSeconds;

            _assignments = Array.AsReadOnly(assignments);
            _permanentlyRemovedTileIndices =
                Array.AsReadOnly(permanentlyRemovedTileIndices);
            _activeTiles = new bool[StableFootingRules.TileCount];
            _permanentlyRemovedTiles =
                new bool[StableFootingRules.TileCount];
            _symbolsByTile =
                new StableFootingSymbol[StableFootingRules.TileCount];

            for (var index = 0; index < assignments.Length; index++)
            {
                var assignment = assignments[index];
                _activeTiles[assignment.TileIndex] = true;
                _symbolsByTile[assignment.TileIndex] = assignment.Symbol;
            }

            for (var index = 0;
                 index < permanentlyRemovedTileIndices.Length;
                 index++)
            {
                _permanentlyRemovedTiles[
                    permanentlyRemovedTileIndices[index]] = true;
            }
        }

        public int CycleNumber { get; }
        public int ActiveTileCount => _assignments.Count;
        public double StartsAtSeconds { get; }
        public double ShuffleRevealEndsAtSeconds { get; }
        public double MoveEndsAtSeconds { get; }
        public double DropEndsAtSeconds { get; }
        public double EndsAtSeconds { get; }
        public double MoveSeconds { get; }
        public StableFootingSymbol SafeSymbol { get; }
        public IReadOnlyList<StableFootingTileAssignment> Assignments =>
            _assignments;
        public IReadOnlyList<int> PermanentlyRemovedTileIndices =>
            _permanentlyRemovedTileIndices;

        public bool IsTileActive(int tileIndex)
        {
            StableFootingRules.ValidateTileIndex(
                tileIndex,
                nameof(tileIndex));
            return _activeTiles[tileIndex];
        }

        public StableFootingSymbol GetSymbolForTile(int tileIndex)
        {
            StableFootingRules.ValidateTileIndex(
                tileIndex,
                nameof(tileIndex));
            if (!_activeTiles[tileIndex])
            {
                throw new InvalidOperationException(
                    "The requested tile is not active during this cycle.");
            }

            return _symbolsByTile[tileIndex];
        }

        public bool IsTileSafe(int tileIndex)
        {
            return GetSymbolForTile(tileIndex) == SafeSymbol;
        }

        public bool WillBePermanentlyRemoved(int tileIndex)
        {
            StableFootingRules.ValidateTileIndex(
                tileIndex,
                nameof(tileIndex));
            return _permanentlyRemovedTiles[tileIndex];
        }

        public StableFootingCyclePhase GetPhaseAt(
            double roundElapsedSeconds)
        {
            StableFootingRules.ValidateRoundElapsedSeconds(
                roundElapsedSeconds);
            if (roundElapsedSeconds < StartsAtSeconds ||
                roundElapsedSeconds >= EndsAtSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(roundElapsedSeconds),
                    roundElapsedSeconds,
                    "Time must fall inside this cycle.");
            }

            if (roundElapsedSeconds < ShuffleRevealEndsAtSeconds)
            {
                return StableFootingCyclePhase.ShuffleReveal;
            }

            if (roundElapsedSeconds < MoveEndsAtSeconds)
            {
                return StableFootingCyclePhase.Move;
            }

            return roundElapsedSeconds < DropEndsAtSeconds
                ? StableFootingCyclePhase.Drop
                : StableFootingCyclePhase.Restore;
        }
    }

    public sealed class StableFootingRoundSchedule
    {
        private readonly ReadOnlyCollection<StableFootingCycle> _cycles;

        internal StableFootingRoundSchedule(
            StableFootingCycle[] cycles,
            int finalTileIndex)
        {
            _cycles = Array.AsReadOnly(cycles);
            FinalTileIndex = finalTileIndex;
        }

        public IReadOnlyList<StableFootingCycle> Cycles => _cycles;
        public int FinalTileIndex { get; }

        public StableFootingCycle GetCycleAt(double roundElapsedSeconds)
        {
            StableFootingRules.ValidateRoundElapsedSeconds(
                roundElapsedSeconds);
            if (roundElapsedSeconds >= StableFootingRules.RoundSeconds)
            {
                return null;
            }

            for (var index = 0; index < _cycles.Count; index++)
            {
                var cycle = _cycles[index];
                if (roundElapsedSeconds >= cycle.StartsAtSeconds &&
                    roundElapsedSeconds < cycle.EndsAtSeconds)
                {
                    return cycle;
                }
            }

            return null;
        }

        public StableFootingCyclePhase GetPhaseAt(
            double roundElapsedSeconds)
        {
            StableFootingRules.ValidateRoundElapsedSeconds(
                roundElapsedSeconds);
            if (roundElapsedSeconds >= StableFootingRules.RoundSeconds)
            {
                return StableFootingCyclePhase.RoundComplete;
            }

            var cycle = GetCycleAt(roundElapsedSeconds);
            return cycle == null
                ? StableFootingCyclePhase.RoundComplete
                : cycle.GetPhaseAt(roundElapsedSeconds);
        }
    }

    /// <summary>
    /// Generates the same full collapse plan on every runtime from a server seed.
    /// Permanently removed tiles always come from the currently unsafe set, so a
    /// survivor standing on the announced safe symbol is never removed on restore.
    /// </summary>
    public static class StableFootingCycleScheduleGenerator
    {
        public static ulong DeriveRoundSeed(
            ulong serverSeed,
            int roundNumber)
        {
            StableFootingRules.ValidateRoundNumber(roundNumber);
            var mixer = new SplitMix64(
                serverSeed ^
                unchecked(0xA24BAED4963EE407UL * (ulong)roundNumber));
            return mixer.NextUInt64();
        }

        public static StableFootingRoundSchedule Generate(
            ulong serverSeed,
            int roundNumber)
        {
            StableFootingRules.ValidateRoundNumber(roundNumber);
            var random = new SplitMix64(
                DeriveRoundSeed(serverSeed, roundNumber));
            var activeTiles = new List<int>(StableFootingRules.TileCount);
            for (var tileIndex = 0;
                 tileIndex < StableFootingRules.TileCount;
                 tileIndex++)
            {
                activeTiles.Add(tileIndex);
            }

            var cycles = new List<StableFootingCycle>();
            var startsAtSeconds = 0d;
            while (activeTiles.Count > 1)
            {
                var cycleNumber = cycles.Count + 1;
                var shuffledTiles = activeTiles.ToArray();
                Shuffle(shuffledTiles, ref random);

                var safeSymbol =
                    (StableFootingSymbol)random.NextInt(
                        StableFootingRules.SymbolCount);
                var firstUnsafeSymbol =
                    (StableFootingSymbol)(((int)safeSymbol + 1) %
                        StableFootingRules.SymbolCount);
                var secondUnsafeSymbol =
                    (StableFootingSymbol)(((int)safeSymbol + 2) %
                        StableFootingRules.SymbolCount);
                var symbolsByTile =
                    new StableFootingSymbol[StableFootingRules.TileCount];

                symbolsByTile[shuffledTiles[0]] = safeSymbol;
                if (shuffledTiles.Length > 1)
                {
                    symbolsByTile[shuffledTiles[1]] = firstUnsafeSymbol;
                }

                if (shuffledTiles.Length > 2)
                {
                    symbolsByTile[shuffledTiles[2]] = secondUnsafeSymbol;
                }

                for (var index = 3;
                     index < shuffledTiles.Length;
                     index++)
                {
                    symbolsByTile[shuffledTiles[index]] =
                        (StableFootingSymbol)random.NextInt(
                            StableFootingRules.SymbolCount);
                }

                var assignments =
                    new StableFootingTileAssignment[activeTiles.Count];
                activeTiles.Sort();
                for (var index = 0; index < activeTiles.Count; index++)
                {
                    var tileIndex = activeTiles[index];
                    assignments[index] = new StableFootingTileAssignment(
                        tileIndex,
                        symbolsByTile[tileIndex]);
                }

                var removalCount = Math.Min(
                    StableFootingRules.PermanentTilesRemovedPerCycle,
                    activeTiles.Count - 1);
                var unsafeCandidates = new List<int>();
                for (var index = 0;
                     index < shuffledTiles.Length;
                     index++)
                {
                    var tileIndex = shuffledTiles[index];
                    if (symbolsByTile[tileIndex] != safeSymbol)
                    {
                        unsafeCandidates.Add(tileIndex);
                    }
                }

                var removedTiles = new int[removalCount];
                for (var index = 0; index < removalCount; index++)
                {
                    removedTiles[index] = unsafeCandidates[index];
                }

                Array.Sort(removedTiles);
                var moveSeconds =
                    StableFootingRules.GetMoveSecondsForCycle(cycleNumber);
                var cycle = new StableFootingCycle(
                    cycleNumber,
                    startsAtSeconds,
                    moveSeconds,
                    safeSymbol,
                    assignments,
                    removedTiles);
                cycles.Add(cycle);
                startsAtSeconds = cycle.EndsAtSeconds;

                for (var index = 0; index < removedTiles.Length; index++)
                {
                    activeTiles.Remove(removedTiles[index]);
                }
            }

            return new StableFootingRoundSchedule(
                cycles.ToArray(),
                activeTiles[0]);
        }

        private static void Shuffle(int[] values, ref SplitMix64 random)
        {
            for (var index = values.Length - 1; index > 0; index--)
            {
                var swapIndex = random.NextInt(index + 1);
                var temporary = values[index];
                values[index] = values[swapIndex];
                values[swapIndex] = temporary;
            }
        }

        private struct SplitMix64
        {
            private ulong _state;

            public SplitMix64(ulong seed)
            {
                _state = seed;
            }

            public ulong NextUInt64()
            {
                _state = unchecked(_state + 0x9E3779B97F4A7C15UL);
                var value = _state;
                value = unchecked(
                    (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
                value = unchecked(
                    (value ^ (value >> 27)) * 0x94D049BB133111EBUL);
                return value ^ (value >> 31);
            }

            public int NextInt(int exclusiveMaximum)
            {
                if (exclusiveMaximum <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(exclusiveMaximum));
                }

                var range = (ulong)exclusiveMaximum;
                var threshold = unchecked(0UL - range) % range;
                ulong value;
                do
                {
                    value = NextUInt64();
                }
                while (value < threshold);

                return (int)(value % range);
            }
        }
    }

    public readonly struct StableFootingRoundOutcome
    {
        private StableFootingRoundOutcome(
            int playerSlot,
            bool survived,
            double eliminatedAtSeconds,
            ulong eliminationOrder)
        {
            if (!StableFootingRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerSlot),
                    playerSlot,
                    "Player slot must be between 0 and 3.");
            }

            if (!survived)
            {
                StableFootingRules.ValidateRoundElapsedSeconds(
                    eliminatedAtSeconds);
                if (eliminatedAtSeconds > StableFootingRules.RoundSeconds)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(eliminatedAtSeconds));
                }

                if (eliminationOrder == 0UL)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(eliminationOrder),
                        "An elimination must have a non-zero server order.");
                }
            }

            PlayerSlot = playerSlot;
            Survived = survived;
            EliminatedAtSeconds = eliminatedAtSeconds;
            EliminationOrder = eliminationOrder;
        }

        public int PlayerSlot { get; }
        public bool Survived { get; }
        public double EliminatedAtSeconds { get; }
        public ulong EliminationOrder { get; }

        public static StableFootingRoundOutcome Survive(int playerSlot)
        {
            return new StableFootingRoundOutcome(
                playerSlot,
                true,
                0d,
                0UL);
        }

        public static StableFootingRoundOutcome Eliminate(
            int playerSlot,
            double eliminatedAtSeconds,
            ulong eliminationOrder)
        {
            return new StableFootingRoundOutcome(
                playerSlot,
                false,
                eliminatedAtSeconds,
                eliminationOrder);
        }
    }

    public readonly struct StableFootingRoundStanding
    {
        internal StableFootingRoundStanding(
            int rank,
            int points,
            StableFootingRoundOutcome outcome)
        {
            Rank = rank;
            Points = points;
            Outcome = outcome;
        }

        public int PlayerSlot => Outcome.PlayerSlot;
        public int Rank { get; }
        public int Points { get; }
        public StableFootingRoundOutcome Outcome { get; }
    }

    public sealed class StableFootingRoundResult
    {
        private readonly ReadOnlyCollection<StableFootingRoundStanding>
            _standings;

        internal StableFootingRoundResult(
            StableFootingRoundStanding[] standings)
        {
            _standings = Array.AsReadOnly(standings);
        }

        public IReadOnlyList<StableFootingRoundStanding> Standings =>
            _standings;

        public StableFootingRoundStanding GetStandingForSlot(int playerSlot)
        {
            if (!StableFootingRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(nameof(playerSlot));
            }

            for (var index = 0; index < _standings.Count; index++)
            {
                if (_standings[index].PlayerSlot == playerSlot)
                {
                    return _standings[index];
                }
            }

            throw new InvalidOperationException(
                "Round result does not contain the requested player.");
        }
    }

    public static class StableFootingRoundScoring
    {
        public static StableFootingRoundResult Score(
            IReadOnlyList<StableFootingRoundOutcome> outcomes)
        {
            if (outcomes == null)
            {
                throw new ArgumentNullException(nameof(outcomes));
            }

            if (outcomes.Count != StableFootingRules.PlayerCount)
            {
                throw new ArgumentException(
                    "A completed round must contain exactly four outcomes.",
                    nameof(outcomes));
            }

            var ordered =
                new StableFootingRoundOutcome[StableFootingRules.PlayerCount];
            var seenSlots = new bool[StableFootingRules.PlayerCount];
            for (var index = 0; index < outcomes.Count; index++)
            {
                var outcome = outcomes[index];
                if (!StableFootingRules.IsValidPlayerSlot(
                        outcome.PlayerSlot))
                {
                    throw new ArgumentException(
                        "Outcome contains an invalid player slot.",
                        nameof(outcomes));
                }

                if (seenSlots[outcome.PlayerSlot])
                {
                    throw new ArgumentException(
                        "Outcome contains a duplicate player slot.",
                        nameof(outcomes));
                }

                seenSlots[outcome.PlayerSlot] = true;
                ordered[index] = outcome;
            }

            Array.Sort(ordered, CompareOutcomes);
            var standings =
                new StableFootingRoundStanding[StableFootingRules.PlayerCount];
            for (var index = 0; index < ordered.Length; index++)
            {
                var rank = index + 1;
                standings[index] = new StableFootingRoundStanding(
                    rank,
                    StableFootingRules.GetPointsForRank(rank),
                    ordered[index]);
            }

            return new StableFootingRoundResult(standings);
        }

        private static int CompareOutcomes(
            StableFootingRoundOutcome left,
            StableFootingRoundOutcome right)
        {
            if (left.Survived != right.Survived)
            {
                return left.Survived ? -1 : 1;
            }

            if (left.Survived)
            {
                return left.PlayerSlot.CompareTo(right.PlayerSlot);
            }

            var time = right.EliminatedAtSeconds.CompareTo(
                left.EliminatedAtSeconds);
            if (time != 0)
            {
                return time;
            }

            var order = right.EliminationOrder.CompareTo(
                left.EliminationOrder);
            return order != 0
                ? order
                : left.PlayerSlot.CompareTo(right.PlayerSlot);
        }
    }

    public readonly struct StableFootingLeaderboardEntry
    {
        internal StableFootingLeaderboardEntry(
            int playerSlot,
            int rank,
            int totalPoints,
            int finalRoundRank)
        {
            PlayerSlot = playerSlot;
            Rank = rank;
            TotalPoints = totalPoints;
            FinalRoundRank = finalRoundRank;
        }

        public int PlayerSlot { get; }
        public int Rank { get; }
        public int TotalPoints { get; }
        public int FinalRoundRank { get; }
    }

    public static class StableFootingMatchScoring
    {
        public static IReadOnlyList<StableFootingLeaderboardEntry>
            BuildLeaderboard(
                IReadOnlyList<StableFootingRoundResult> rounds)
        {
            if (rounds == null)
            {
                throw new ArgumentNullException(nameof(rounds));
            }

            if (rounds.Count != StableFootingRules.RoundCount)
            {
                throw new ArgumentException(
                    "A completed match must contain exactly three rounds.",
                    nameof(rounds));
            }

            var totalPoints = new int[StableFootingRules.PlayerCount];
            var finalRoundRanks = new int[StableFootingRules.PlayerCount];
            for (var roundIndex = 0;
                 roundIndex < rounds.Count;
                 roundIndex++)
            {
                var round = rounds[roundIndex];
                if (round == null)
                {
                    throw new ArgumentException(
                        "Round results cannot contain null.",
                        nameof(rounds));
                }

                for (var standingIndex = 0;
                     standingIndex < round.Standings.Count;
                     standingIndex++)
                {
                    var standing = round.Standings[standingIndex];
                    totalPoints[standing.PlayerSlot] += standing.Points;
                    if (roundIndex == StableFootingRules.RoundCount - 1)
                    {
                        finalRoundRanks[standing.PlayerSlot] = standing.Rank;
                    }
                }
            }

            var slots = new int[StableFootingRules.PlayerCount];
            for (var slot = 0; slot < slots.Length; slot++)
            {
                slots[slot] = slot;
            }

            Array.Sort(
                slots,
                (left, right) =>
                {
                    var points = totalPoints[right].CompareTo(
                        totalPoints[left]);
                    if (points != 0)
                    {
                        return points;
                    }

                    var finalRound = finalRoundRanks[left].CompareTo(
                        finalRoundRanks[right]);
                    return finalRound != 0
                        ? finalRound
                        : left.CompareTo(right);
                });

            var leaderboard =
                new StableFootingLeaderboardEntry[
                    StableFootingRules.PlayerCount];
            for (var index = 0; index < slots.Length; index++)
            {
                var slot = slots[index];
                leaderboard[index] = new StableFootingLeaderboardEntry(
                    slot,
                    index + 1,
                    totalPoints[slot],
                    finalRoundRanks[slot]);
            }

            return Array.AsReadOnly(leaderboard);
        }
    }
}
