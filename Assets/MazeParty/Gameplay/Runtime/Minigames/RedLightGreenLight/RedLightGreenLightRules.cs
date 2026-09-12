using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MazeParty.Gameplay.Minigames.RedLightGreenLight
{
    /// <summary>
    /// Fixed rules shared by the server-authoritative simulation and clients.
    /// Countdown and result presentation are not part of the 60 second running
    /// clock.
    /// </summary>
    public static class RedLightGreenLightRules
    {
        public const int PlayerCount = 4;
        public const int RoundCount = 3;
        public const int ViolationsToEliminate = 2;

        public const double CountdownSeconds = 3d;
        public const double RoundSeconds = 60d;
        public const double ResultSeconds = 4d;
        public const double MinimumGreenSeconds = 1.5d;
        public const double MaximumGreenSeconds = 4d;
        public const double TurnWarningSeconds = 0.35d;
        public const double MinimumRedSeconds = 1d;
        public const double MaximumRedSeconds = 2.5d;
        public const double RedMovementGraceSeconds = 0.15d;

        public const float NormalMovementSpeedMetersPerSecond = 5f;
        public const float WarnedMovementSpeedMetersPerSecond = 2.75f;

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

        internal static void ValidateRunningElapsedSeconds(
            double runningElapsedSeconds,
            bool allowPastTimeLimit = false)
        {
            if (double.IsNaN(runningElapsedSeconds) ||
                double.IsInfinity(runningElapsedSeconds) ||
                runningElapsedSeconds < 0d ||
                (!allowPastTimeLimit &&
                 runningElapsedSeconds > RoundSeconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(runningElapsedSeconds),
                    runningElapsedSeconds,
                    allowPastTimeLimit
                        ? "Elapsed time must be finite and non-negative."
                        : "Elapsed time must be between 0 and 60 seconds.");
            }
        }
    }

    public enum RedLightGreenLightSignalPhase : byte
    {
        Green,
        TurnWarning,
        Red
    }

    /// <summary>
    /// One contiguous server-authored signal window. TurnWarning is still a
    /// legal movement window; only Red can produce a movement violation.
    /// </summary>
    public readonly struct RedLightGreenLightSignalWindow :
        IEquatable<RedLightGreenLightSignalWindow>
    {
        internal RedLightGreenLightSignalWindow(
            RedLightGreenLightSignalPhase phase,
            double startsAtSeconds,
            double durationSeconds)
        {
            Phase = phase;
            StartsAtSeconds = startsAtSeconds;
            DurationSeconds = durationSeconds;
        }

        public RedLightGreenLightSignalPhase Phase { get; }
        public double StartsAtSeconds { get; }
        public double DurationSeconds { get; }
        public double EndsAtSeconds =>
            StartsAtSeconds + DurationSeconds;
        public double RedDetectionStartsAtSeconds =>
            Phase == RedLightGreenLightSignalPhase.Red
                ? StartsAtSeconds +
                  RedLightGreenLightRules.RedMovementGraceSeconds
                : double.PositiveInfinity;

        public bool Equals(RedLightGreenLightSignalWindow other)
        {
            return Phase == other.Phase &&
                   StartsAtSeconds.Equals(other.StartsAtSeconds) &&
                   DurationSeconds.Equals(other.DurationSeconds);
        }

        public override bool Equals(object obj)
        {
            return obj is RedLightGreenLightSignalWindow other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (int)Phase;
                hashCode = (hashCode * 397) ^
                           StartsAtSeconds.GetHashCode();
                hashCode = (hashCode * 397) ^
                           DurationSeconds.GetHashCode();
                return hashCode;
            }
        }
    }

    public sealed class RedLightGreenLightSignalSchedule
    {
        private readonly ReadOnlyCollection<RedLightGreenLightSignalWindow>
            _windows;

        internal RedLightGreenLightSignalSchedule(
            RedLightGreenLightSignalWindow[] windows)
        {
            if (windows == null || windows.Length == 0)
            {
                throw new ArgumentException(
                    "A signal schedule must contain at least one window.",
                    nameof(windows));
            }

            _windows = Array.AsReadOnly(windows);
        }

        public IReadOnlyList<RedLightGreenLightSignalWindow> Windows =>
            _windows;

        public RedLightGreenLightSignalWindow GetWindowAt(
            double runningElapsedSeconds)
        {
            RedLightGreenLightRules.ValidateRunningElapsedSeconds(
                runningElapsedSeconds);

            for (var index = 0; index < _windows.Count; index++)
            {
                var window = _windows[index];
                if (runningElapsedSeconds < window.EndsAtSeconds ||
                    (runningElapsedSeconds ==
                     RedLightGreenLightRules.RoundSeconds &&
                     index == _windows.Count - 1))
                {
                    return window;
                }
            }

            throw new InvalidOperationException(
                "Signal schedule does not cover the requested round time.");
        }

        /// <summary>
        /// True only after the authoritative Red transition grace has expired.
        /// The caller must separately confirm that movement came from player
        /// input rather than external physics displacement.
        /// </summary>
        public bool IsVoluntaryMovementViolationAt(
            double runningElapsedSeconds)
        {
            var window = GetWindowAt(runningElapsedSeconds);
            return window.Phase ==
                   RedLightGreenLightSignalPhase.Red &&
                   runningElapsedSeconds >=
                   window.RedDetectionStartsAtSeconds;
        }
    }

    /// <summary>
    /// Creates one canonical signal schedule per server seed and round. Full
    /// windows are kept even when their tail lies beyond the 60 second limit,
    /// so generated Green and Red durations always remain inside their bounds.
    /// </summary>
    public static class RedLightGreenLightSignalScheduleGenerator
    {
        public static ulong DeriveRoundSeed(
            ulong serverSeed,
            int roundNumber)
        {
            RedLightGreenLightRules.ValidateRoundNumber(roundNumber);
            var mixer = new SplitMix64(
                serverSeed ^
                unchecked(0xA0761D6478BD642FUL *
                          (ulong)roundNumber));
            return mixer.NextUInt64();
        }

        public static RedLightGreenLightSignalSchedule Generate(
            ulong serverSeed,
            int roundNumber)
        {
            RedLightGreenLightRules.ValidateRoundNumber(roundNumber);
            var random = new SplitMix64(
                DeriveRoundSeed(serverSeed, roundNumber));
            var windows =
                new List<RedLightGreenLightSignalWindow>();
            var cursor = 0d;

            while (cursor < RedLightGreenLightRules.RoundSeconds)
            {
                var greenDuration = random.NextDouble(
                    RedLightGreenLightRules.MinimumGreenSeconds,
                    RedLightGreenLightRules.MaximumGreenSeconds);
                windows.Add(
                    new RedLightGreenLightSignalWindow(
                        RedLightGreenLightSignalPhase.Green,
                        cursor,
                        greenDuration));
                cursor += greenDuration;

                windows.Add(
                    new RedLightGreenLightSignalWindow(
                        RedLightGreenLightSignalPhase.TurnWarning,
                        cursor,
                        RedLightGreenLightRules.TurnWarningSeconds));
                cursor += RedLightGreenLightRules.TurnWarningSeconds;

                var redDuration = random.NextDouble(
                    RedLightGreenLightRules.MinimumRedSeconds,
                    RedLightGreenLightRules.MaximumRedSeconds);
                windows.Add(
                    new RedLightGreenLightSignalWindow(
                        RedLightGreenLightSignalPhase.Red,
                        cursor,
                        redDuration));
                cursor += redDuration;
            }

            return new RedLightGreenLightSignalSchedule(
                windows.ToArray());
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
                _state = unchecked(
                    _state + 0x9E3779B97F4A7C15UL);
                var value = _state;
                value = unchecked(
                    (value ^ (value >> 30)) *
                    0xBF58476D1CE4E5B9UL);
                value = unchecked(
                    (value ^ (value >> 27)) *
                    0x94D049BB133111EBUL);
                return value ^ (value >> 31);
            }

            public double NextDouble(
                double inclusiveMinimum,
                double exclusiveMaximum)
            {
                var unit =
                    (NextUInt64() >> 11) *
                    (1d / 9007199254740992d);
                return inclusiveMinimum +
                       ((exclusiveMaximum - inclusiveMinimum) * unit);
            }
        }
    }

    public enum RedLightGreenLightPlayerState : byte
    {
        Healthy,
        Warned,
        Eliminated,
        Finished
    }

    public sealed class RedLightGreenLightPlayerRoundState
    {
        internal RedLightGreenLightPlayerRoundState(int playerSlot)
        {
            if (!RedLightGreenLightRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerSlot),
                    playerSlot,
                    "Player slot must be between 0 and 3.");
            }

            PlayerSlot = playerSlot;
        }

        public int PlayerSlot { get; }
        public RedLightGreenLightPlayerState State { get; private set; }
        public int ViolationCount { get; private set; }
        public float ForwardProgressMeters { get; private set; }
        public bool IsTerminal =>
            State == RedLightGreenLightPlayerState.Eliminated ||
            State == RedLightGreenLightPlayerState.Finished;
        public bool CanMove => !IsTerminal;
        public bool ShouldHideTorso => ViolationCount > 0;
        public float MovementSpeedMetersPerSecond =>
            State == RedLightGreenLightPlayerState.Healthy
                ? RedLightGreenLightRules
                    .NormalMovementSpeedMetersPerSecond
                : State == RedLightGreenLightPlayerState.Warned
                    ? RedLightGreenLightRules
                        .WarnedMovementSpeedMetersPerSecond
                    : 0f;

        internal RedLightGreenLightPlayerState ApplyViolation()
        {
            if (IsTerminal)
            {
                return State;
            }

            ViolationCount++;
            State = ViolationCount >=
                    RedLightGreenLightRules.ViolationsToEliminate
                ? RedLightGreenLightPlayerState.Eliminated
                : RedLightGreenLightPlayerState.Warned;
            return State;
        }

        internal bool SetForwardProgress(float forwardProgressMeters)
        {
            ValidateForwardProgress(forwardProgressMeters);
            if (IsTerminal)
            {
                return false;
            }

            ForwardProgressMeters = forwardProgressMeters;
            return true;
        }

        internal bool TryFinish(float forwardProgressMeters)
        {
            ValidateForwardProgress(forwardProgressMeters);
            if (IsTerminal)
            {
                return false;
            }

            ForwardProgressMeters = forwardProgressMeters;
            State = RedLightGreenLightPlayerState.Finished;
            return true;
        }

        internal RedLightGreenLightRoundOutcome CaptureOutcome()
        {
            return new RedLightGreenLightRoundOutcome(
                PlayerSlot,
                State,
                ForwardProgressMeters);
        }

        private static void ValidateForwardProgress(
            float forwardProgressMeters)
        {
            if (float.IsNaN(forwardProgressMeters) ||
                float.IsInfinity(forwardProgressMeters) ||
                forwardProgressMeters < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(forwardProgressMeters),
                    forwardProgressMeters,
                    "Forward progress must be finite and non-negative.");
            }
        }
    }

    public readonly struct RedLightGreenLightRoundOutcome
    {
        public RedLightGreenLightRoundOutcome(
            int playerSlot,
            RedLightGreenLightPlayerState state,
            float forwardProgressMeters)
        {
            if (!RedLightGreenLightRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerSlot),
                    playerSlot,
                    "Player slot must be between 0 and 3.");
            }

            if (state < RedLightGreenLightPlayerState.Healthy ||
                state > RedLightGreenLightPlayerState.Finished)
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }

            if (float.IsNaN(forwardProgressMeters) ||
                float.IsInfinity(forwardProgressMeters) ||
                forwardProgressMeters < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(forwardProgressMeters));
            }

            PlayerSlot = playerSlot;
            State = state;
            ForwardProgressMeters = forwardProgressMeters;
        }

        public int PlayerSlot { get; }
        public RedLightGreenLightPlayerState State { get; }
        public float ForwardProgressMeters { get; }
        public bool Finished =>
            State == RedLightGreenLightPlayerState.Finished;
        public bool Eliminated =>
            State == RedLightGreenLightPlayerState.Eliminated;
        public bool Survived => !Eliminated;
    }

    public readonly struct RedLightGreenLightRoundStanding
    {
        internal RedLightGreenLightRoundStanding(
            int rank,
            int points,
            RedLightGreenLightRoundOutcome outcome)
        {
            Rank = rank;
            Points = points;
            Outcome = outcome;
        }

        public int PlayerSlot => Outcome.PlayerSlot;
        public int Rank { get; }
        public int Points { get; }
        public float ForwardProgressMeters =>
            Outcome.ForwardProgressMeters;
        public RedLightGreenLightRoundOutcome Outcome { get; }
    }

    public sealed class RedLightGreenLightRoundResult
    {
        private readonly ReadOnlyCollection<
            RedLightGreenLightRoundStanding> _standings;

        internal RedLightGreenLightRoundResult(
            RedLightGreenLightRoundStanding[] standings)
        {
            _standings = Array.AsReadOnly(standings);
        }

        public IReadOnlyList<RedLightGreenLightRoundStanding>
            Standings => _standings;

        public RedLightGreenLightRoundStanding GetStandingForSlot(
            int playerSlot)
        {
            if (!RedLightGreenLightRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerSlot));
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

    public static class RedLightGreenLightRoundScoring
    {
        public static RedLightGreenLightRoundResult Score(
            IReadOnlyList<RedLightGreenLightRoundOutcome> outcomes)
        {
            if (outcomes == null)
            {
                throw new ArgumentNullException(nameof(outcomes));
            }

            if (outcomes.Count !=
                RedLightGreenLightRules.PlayerCount)
            {
                throw new ArgumentException(
                    "A completed round must contain exactly four outcomes.",
                    nameof(outcomes));
            }

            var ordered =
                new RedLightGreenLightRoundOutcome[
                    RedLightGreenLightRules.PlayerCount];
            var seenSlots =
                new bool[RedLightGreenLightRules.PlayerCount];

            for (var index = 0; index < outcomes.Count; index++)
            {
                var outcome = outcomes[index];
                if (!RedLightGreenLightRules.IsValidPlayerSlot(
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
                new RedLightGreenLightRoundStanding[
                    RedLightGreenLightRules.PlayerCount];
            for (var index = 0; index < ordered.Length; index++)
            {
                var rank = index + 1;
                standings[index] =
                    new RedLightGreenLightRoundStanding(
                        rank,
                        RedLightGreenLightRules
                            .GetPointsForRank(rank),
                        ordered[index]);
            }

            return new RedLightGreenLightRoundResult(standings);
        }

        private static int CompareOutcomes(
            RedLightGreenLightRoundOutcome left,
            RedLightGreenLightRoundOutcome right)
        {
            var category = GetCategory(left).CompareTo(
                GetCategory(right));
            if (category != 0)
            {
                return category;
            }

            var progress = right.ForwardProgressMeters.CompareTo(
                left.ForwardProgressMeters);
            return progress != 0
                ? progress
                : left.PlayerSlot.CompareTo(right.PlayerSlot);
        }

        private static int GetCategory(
            RedLightGreenLightRoundOutcome outcome)
        {
            if (outcome.Finished)
            {
                return 0;
            }

            return outcome.Eliminated ? 2 : 1;
        }
    }

    public readonly struct RedLightGreenLightLeaderboardEntry
    {
        internal RedLightGreenLightLeaderboardEntry(
            int playerSlot,
            int rank,
            int totalPoints,
            float totalForwardProgressMeters,
            int finalRoundRank)
        {
            PlayerSlot = playerSlot;
            Rank = rank;
            TotalPoints = totalPoints;
            TotalForwardProgressMeters =
                totalForwardProgressMeters;
            FinalRoundRank = finalRoundRank;
        }

        public int PlayerSlot { get; }
        public int Rank { get; }
        public int TotalPoints { get; }
        public float TotalForwardProgressMeters { get; }
        public int FinalRoundRank { get; }
    }

    public static class RedLightGreenLightMatchScoring
    {
        public static IReadOnlyList<
            RedLightGreenLightLeaderboardEntry> BuildLeaderboard(
                IReadOnlyList<RedLightGreenLightRoundResult> rounds)
        {
            if (rounds == null)
            {
                throw new ArgumentNullException(nameof(rounds));
            }

            if (rounds.Count != RedLightGreenLightRules.RoundCount)
            {
                throw new ArgumentException(
                    "A completed match must contain exactly three rounds.",
                    nameof(rounds));
            }

            var totalPoints =
                new int[RedLightGreenLightRules.PlayerCount];
            var totalProgress =
                new float[RedLightGreenLightRules.PlayerCount];
            var finalRoundRanks =
                new int[RedLightGreenLightRules.PlayerCount];

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
                    totalPoints[standing.PlayerSlot] +=
                        standing.Points;
                    totalProgress[standing.PlayerSlot] +=
                        standing.ForwardProgressMeters;

                    if (roundIndex ==
                        RedLightGreenLightRules.RoundCount - 1)
                    {
                        finalRoundRanks[standing.PlayerSlot] =
                            standing.Rank;
                    }
                }
            }

            var playerSlots =
                new int[RedLightGreenLightRules.PlayerCount];
            for (var playerSlot = 0;
                 playerSlot < playerSlots.Length;
                 playerSlot++)
            {
                playerSlots[playerSlot] = playerSlot;
            }

            Array.Sort(
                playerSlots,
                (left, right) =>
                {
                    var points = totalPoints[right].CompareTo(
                        totalPoints[left]);
                    if (points != 0)
                    {
                        return points;
                    }

                    var progress = totalProgress[right].CompareTo(
                        totalProgress[left]);
                    if (progress != 0)
                    {
                        return progress;
                    }

                    var finalRound = finalRoundRanks[left].CompareTo(
                        finalRoundRanks[right]);
                    return finalRound != 0
                        ? finalRound
                        : left.CompareTo(right);
                });

            var leaderboard =
                new RedLightGreenLightLeaderboardEntry[
                    RedLightGreenLightRules.PlayerCount];
            for (var index = 0;
                 index < playerSlots.Length;
                 index++)
            {
                var playerSlot = playerSlots[index];
                leaderboard[index] =
                    new RedLightGreenLightLeaderboardEntry(
                        playerSlot,
                        index + 1,
                        totalPoints[playerSlot],
                        totalProgress[playerSlot],
                        finalRoundRanks[playerSlot]);
            }

            return Array.AsReadOnly(leaderboard);
        }
    }
}
