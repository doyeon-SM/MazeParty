using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MazeParty.Gameplay.Minigames.Minefield
{
    /// <summary>
    /// Fixed match rules shared by the server-authoritative simulation and clients.
    /// </summary>
    public static class MinefieldRules
    {
        public const int PlayerCount = 4;
        public const int RoundCount = 3;
        public const int MineHitsToEliminate = 2;

        public static int GetPointsForRank(int rank)
        {
            if (rank < 1 || rank > PlayerCount)
            {
                throw new ArgumentOutOfRangeException(nameof(rank), rank, "Rank must be between 1 and 4.");
            }

            return PlayerCount - rank;
        }

        public static bool IsValidPlayerSlot(int playerSlot)
        {
            return playerSlot >= 0 && playerSlot < PlayerCount;
        }
    }

    public enum MinefieldPlayerState : byte
    {
        Healthy,
        Crippled,
        Eliminated,
        Finished
    }

    public readonly struct MinefieldHitResolution
    {
        public MinefieldHitResolution(
            bool wasApplied,
            MinefieldPlayerState previousState,
            MinefieldPlayerState currentState,
            int mineHitCount)
        {
            WasApplied = wasApplied;
            PreviousState = previousState;
            CurrentState = currentState;
            MineHitCount = mineHitCount;
        }

        public bool WasApplied { get; }
        public MinefieldPlayerState PreviousState { get; }
        public MinefieldPlayerState CurrentState { get; }
        public int MineHitCount { get; }
        public bool BecameCrippled =>
            WasApplied &&
            PreviousState == MinefieldPlayerState.Healthy &&
            CurrentState == MinefieldPlayerState.Crippled;
        public bool BecameEliminated =>
            WasApplied &&
            CurrentState == MinefieldPlayerState.Eliminated;
    }

    /// <summary>
    /// Pure per-round state. Presentation code can use ShouldHideTorso for the
    /// bloodless first-hit pose and MovementSpeedMultiplier for movement.
    /// </summary>
    public sealed class MinefieldPlayerRoundState
    {
        public MinefieldPlayerRoundState(int playerSlot)
        {
            if (!MinefieldRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerSlot),
                    playerSlot,
                    "Player slot must be between 0 and 3.");
            }

            PlayerSlot = playerSlot;
            State = MinefieldPlayerState.Healthy;
        }

        public int PlayerSlot { get; }
        public MinefieldPlayerState State { get; private set; }
        public int MineHitCount { get; private set; }
        public bool IsTerminal =>
            State == MinefieldPlayerState.Eliminated ||
            State == MinefieldPlayerState.Finished;
        public bool CanMove => !IsTerminal;
        public bool ShouldHideTorso => MineHitCount > 0;
        public float MovementSpeedMultiplier =>
            State == MinefieldPlayerState.Healthy
                ? 1f
                : State == MinefieldPlayerState.Crippled
                    ? FootstepRules.WalkSpeedMultiplier
                    : 0f;

        public MinefieldHitResolution ApplyMineHit()
        {
            var previousState = State;
            if (IsTerminal)
            {
                return new MinefieldHitResolution(
                    false,
                    previousState,
                    State,
                    MineHitCount);
            }

            MineHitCount++;
            State = MineHitCount >= MinefieldRules.MineHitsToEliminate
                ? MinefieldPlayerState.Eliminated
                : MinefieldPlayerState.Crippled;

            return new MinefieldHitResolution(
                true,
                previousState,
                State,
                MineHitCount);
        }

        public bool TryFinish()
        {
            if (IsTerminal)
            {
                return false;
            }

            State = MinefieldPlayerState.Finished;
            return true;
        }
    }

    public enum MinefieldTerminalKind : byte
    {
        Finished,
        Eliminated
    }

    /// <summary>
    /// Terminal result for one player. ResolutionOrder is a server-authored,
    /// monotonically increasing event order, not a local clock value.
    /// </summary>
    public readonly struct MinefieldRoundOutcome
    {
        private MinefieldRoundOutcome(
            int playerSlot,
            MinefieldTerminalKind terminalKind,
            ulong resolutionOrder)
        {
            if (!MinefieldRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerSlot),
                    playerSlot,
                    "Player slot must be between 0 and 3.");
            }

            PlayerSlot = playerSlot;
            TerminalKind = terminalKind;
            ResolutionOrder = resolutionOrder;
        }

        public int PlayerSlot { get; }
        public MinefieldTerminalKind TerminalKind { get; }
        public ulong ResolutionOrder { get; }

        public static MinefieldRoundOutcome Finish(int playerSlot, ulong arrivalOrder)
        {
            return new MinefieldRoundOutcome(
                playerSlot,
                MinefieldTerminalKind.Finished,
                arrivalOrder);
        }

        public static MinefieldRoundOutcome Eliminate(int playerSlot, ulong eliminationOrder)
        {
            return new MinefieldRoundOutcome(
                playerSlot,
                MinefieldTerminalKind.Eliminated,
                eliminationOrder);
        }
    }

    public readonly struct MinefieldRoundStanding
    {
        internal MinefieldRoundStanding(
            int rank,
            int points,
            MinefieldRoundOutcome outcome)
        {
            Rank = rank;
            Points = points;
            Outcome = outcome;
        }

        public int PlayerSlot => Outcome.PlayerSlot;
        public int Rank { get; }
        public int Points { get; }
        public MinefieldRoundOutcome Outcome { get; }
    }

    public sealed class MinefieldRoundResult
    {
        private readonly ReadOnlyCollection<MinefieldRoundStanding> _standings;

        internal MinefieldRoundResult(MinefieldRoundStanding[] standings)
        {
            _standings = Array.AsReadOnly(standings);
        }

        /// <summary>
        /// Standings are ordered from rank 1 through rank 4.
        /// </summary>
        public IReadOnlyList<MinefieldRoundStanding> Standings => _standings;

        public MinefieldRoundStanding GetStandingForSlot(int playerSlot)
        {
            if (!MinefieldRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(nameof(playerSlot));
            }

            for (var i = 0; i < _standings.Count; i++)
            {
                if (_standings[i].PlayerSlot == playerSlot)
                {
                    return _standings[i];
                }
            }

            throw new InvalidOperationException("Round result does not contain the requested player.");
        }
    }

    public static class MinefieldRoundScoring
    {
        public static MinefieldRoundResult Score(
            IReadOnlyList<MinefieldRoundOutcome> outcomes)
        {
            if (outcomes == null)
            {
                throw new ArgumentNullException(nameof(outcomes));
            }

            if (outcomes.Count != MinefieldRules.PlayerCount)
            {
                throw new ArgumentException(
                    "A completed round must contain exactly four outcomes.",
                    nameof(outcomes));
            }

            var ordered = new MinefieldRoundOutcome[MinefieldRules.PlayerCount];
            var seenSlots = new bool[MinefieldRules.PlayerCount];
            for (var i = 0; i < outcomes.Count; i++)
            {
                var outcome = outcomes[i];
                if (!MinefieldRules.IsValidPlayerSlot(outcome.PlayerSlot))
                {
                    throw new ArgumentException("Outcome contains an invalid player slot.", nameof(outcomes));
                }

                if (seenSlots[outcome.PlayerSlot])
                {
                    throw new ArgumentException("Outcome contains a duplicate player slot.", nameof(outcomes));
                }

                seenSlots[outcome.PlayerSlot] = true;
                ordered[i] = outcome;
            }

            Array.Sort(ordered, CompareOutcomes);

            var standings = new MinefieldRoundStanding[MinefieldRules.PlayerCount];
            for (var index = 0; index < ordered.Length; index++)
            {
                var rank = index + 1;
                standings[index] = new MinefieldRoundStanding(
                    rank,
                    MinefieldRules.GetPointsForRank(rank),
                    ordered[index]);
            }

            return new MinefieldRoundResult(standings);
        }

        private static int CompareOutcomes(
            MinefieldRoundOutcome left,
            MinefieldRoundOutcome right)
        {
            if (left.TerminalKind != right.TerminalKind)
            {
                return left.TerminalKind == MinefieldTerminalKind.Finished ? -1 : 1;
            }

            var order = left.ResolutionOrder.CompareTo(right.ResolutionOrder);
            return order != 0
                ? order
                : left.PlayerSlot.CompareTo(right.PlayerSlot);
        }
    }

    public readonly struct MinefieldLeaderboardEntry
    {
        internal MinefieldLeaderboardEntry(int playerSlot, int rank, int totalPoints)
        {
            PlayerSlot = playerSlot;
            Rank = rank;
            TotalPoints = totalPoints;
        }

        public int PlayerSlot { get; }
        public int Rank { get; }
        public int TotalPoints { get; }
    }

    public static class MinefieldMatchScoring
    {
        public static IReadOnlyList<MinefieldLeaderboardEntry> BuildLeaderboard(
            IReadOnlyList<MinefieldRoundResult> rounds)
        {
            if (rounds == null)
            {
                throw new ArgumentNullException(nameof(rounds));
            }

            if (rounds.Count != MinefieldRules.RoundCount)
            {
                throw new ArgumentException(
                    "A completed match must contain exactly three rounds.",
                    nameof(rounds));
            }

            var totals = new int[MinefieldRules.PlayerCount];
            for (var roundIndex = 0; roundIndex < rounds.Count; roundIndex++)
            {
                var round = rounds[roundIndex];
                if (round == null)
                {
                    throw new ArgumentException("Round results cannot contain null.", nameof(rounds));
                }

                for (var standingIndex = 0;
                     standingIndex < round.Standings.Count;
                     standingIndex++)
                {
                    var standing = round.Standings[standingIndex];
                    totals[standing.PlayerSlot] += standing.Points;
                }
            }

            var playerSlots = new int[MinefieldRules.PlayerCount];
            for (var playerSlot = 0; playerSlot < playerSlots.Length; playerSlot++)
            {
                playerSlots[playerSlot] = playerSlot;
            }

            Array.Sort(
                playerSlots,
                (left, right) =>
                {
                    var points = totals[right].CompareTo(totals[left]);
                    return points != 0 ? points : left.CompareTo(right);
                });

            var leaderboard = new MinefieldLeaderboardEntry[MinefieldRules.PlayerCount];
            for (var index = 0; index < playerSlots.Length; index++)
            {
                var playerSlot = playerSlots[index];
                leaderboard[index] = new MinefieldLeaderboardEntry(
                    playerSlot,
                    index + 1,
                    totals[playerSlot]);
            }

            return Array.AsReadOnly(leaderboard);
        }
    }

    public readonly struct MinefieldCell :
        IEquatable<MinefieldCell>,
        IComparable<MinefieldCell>
    {
        public MinefieldCell(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        public bool Equals(MinefieldCell other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is MinefieldCell other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Y;
            }
        }

        public int CompareTo(MinefieldCell other)
        {
            var y = Y.CompareTo(other.Y);
            return y != 0 ? y : X.CompareTo(other.X);
        }

        public override string ToString()
        {
            return $"({X}, {Y})";
        }

        public static bool operator ==(MinefieldCell left, MinefieldCell right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(MinefieldCell left, MinefieldCell right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Creates canonical mine-cell sets from a server seed. The custom PRNG keeps
    /// layouts stable across runtimes instead of depending on System.Random.
    /// </summary>
    public static class MinefieldLayoutGenerator
    {
        public static ulong DeriveRoundSeed(ulong serverSeed, int roundNumber)
        {
            ValidateRoundNumber(roundNumber);
            var mixer = new SplitMix64(
                serverSeed ^
                unchecked(0xD1B54A32D192ED03UL * (ulong)roundNumber));
            return mixer.NextUInt64();
        }

        public static MinefieldCell[] Generate(
            int width,
            int height,
            int mineCount,
            ulong serverSeed,
            int roundNumber,
            IReadOnlyCollection<MinefieldCell> forbiddenCells = null)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            if (mineCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(mineCount));
            }

            ValidateRoundNumber(roundNumber);

            var forbidden = new HashSet<MinefieldCell>();
            if (forbiddenCells != null)
            {
                foreach (var cell in forbiddenCells)
                {
                    if (cell.X < 0 || cell.X >= width ||
                        cell.Y < 0 || cell.Y >= height)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(forbiddenCells),
                            cell,
                            "Forbidden cells must be inside the layout bounds.");
                    }

                    forbidden.Add(cell);
                }
            }

            var availableCount = checked(width * height) - forbidden.Count;
            if (mineCount > availableCount)
            {
                throw new ArgumentException(
                    "Mine count exceeds the number of available cells.",
                    nameof(mineCount));
            }

            var candidates = new List<MinefieldCell>(availableCount);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var cell = new MinefieldCell(x, y);
                    if (!forbidden.Contains(cell))
                    {
                        candidates.Add(cell);
                    }
                }
            }

            var random = new SplitMix64(DeriveRoundSeed(serverSeed, roundNumber));
            for (var i = 0; i < mineCount; i++)
            {
                var swapIndex = i + random.NextInt(candidates.Count - i);
                var temporary = candidates[i];
                candidates[i] = candidates[swapIndex];
                candidates[swapIndex] = temporary;
            }

            var mines = new MinefieldCell[mineCount];
            candidates.CopyTo(0, mines, 0, mineCount);
            Array.Sort(mines);
            return mines;
        }

        private static void ValidateRoundNumber(int roundNumber)
        {
            if (roundNumber < 1 || roundNumber > MinefieldRules.RoundCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(roundNumber),
                    roundNumber,
                    "Round number must be between 1 and 3.");
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
                    throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
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
}
