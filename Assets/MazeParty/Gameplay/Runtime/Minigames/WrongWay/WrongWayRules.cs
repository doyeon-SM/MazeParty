using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MazeParty.Gameplay.Minigames.WrongWay
{
    /// <summary>
    /// Fixed rules shared by the server-authoritative simulation and clients.
    /// </summary>
    public static class WrongWayRules
    {
        public const int PlayerCount = 4;
        public const int StepCount = 50;
        public const int RoundCount = 2;
        public const double CountdownSeconds = 3d;
        public const double RoundSeconds = 60d;
        public const double IncorrectInputLockSeconds = 0.5d;

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
                    "Round number must be between 1 and 2.");
            }
        }
    }

    /// <summary>
    /// One of the four prompts mapped to W, S, A and D respectively.
    /// </summary>
    public enum WrongWayDirection : byte
    {
        Up,
        Down,
        Left,
        Right
    }

    public enum WrongWayInputStatus : byte
    {
        IgnoredRoundComplete,
        IgnoredWhileLocked,
        Incorrect,
        Correct,
        Finished
    }

    public enum WrongWayRoundEndReason : byte
    {
        None,
        FirstFinisher,
        TimeLimit
    }

    public readonly struct WrongWayInputResolution
    {
        internal WrongWayInputResolution(
            int playerSlot,
            WrongWayDirection submittedDirection,
            WrongWayDirection expectedDirection,
            WrongWayInputStatus status,
            int previousStep,
            int currentStep,
            double inputLockedUntil,
            ulong serverEventOrder)
        {
            PlayerSlot = playerSlot;
            SubmittedDirection = submittedDirection;
            ExpectedDirection = expectedDirection;
            Status = status;
            PreviousStep = previousStep;
            CurrentStep = currentStep;
            InputLockedUntil = inputLockedUntil;
            ServerEventOrder = serverEventOrder;
        }

        public int PlayerSlot { get; }
        public WrongWayDirection SubmittedDirection { get; }
        public WrongWayDirection ExpectedDirection { get; }
        public WrongWayInputStatus Status { get; }
        public int PreviousStep { get; }
        public int CurrentStep { get; }
        public double InputLockedUntil { get; }

        /// <summary>
        /// Monotonic order assigned by the authoritative round state. Ignored
        /// inputs have an order of zero.
        /// </summary>
        public ulong ServerEventOrder { get; }

        public bool WasAccepted =>
            Status == WrongWayInputStatus.Incorrect ||
            Status == WrongWayInputStatus.Correct ||
            Status == WrongWayInputStatus.Finished;

        public bool WasCorrect =>
            Status == WrongWayInputStatus.Correct ||
            Status == WrongWayInputStatus.Finished;

        public bool FinishedRound =>
            Status == WrongWayInputStatus.Finished;
    }

    /// <summary>
    /// Pure state for one participant during a running round.
    /// </summary>
    public sealed class WrongWayPlayerRoundState
    {
        internal WrongWayPlayerRoundState(int playerSlot)
        {
            if (!WrongWayRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerSlot),
                    playerSlot,
                    "Player slot must be between 0 and 3.");
            }

            PlayerSlot = playerSlot;
        }

        public int PlayerSlot { get; }
        public int CompletedSteps { get; private set; }
        public bool IsFinished => CompletedSteps >= WrongWayRules.StepCount;
        public double InputLockedUntil { get; private set; }

        /// <summary>
        /// Server event order at which this player reached CompletedSteps.
        /// It remains zero while the player is still at the starting line.
        /// </summary>
        public ulong CurrentStepReachedOrder { get; private set; }

        public bool IsInputLocked(double serverNow)
        {
            ValidateServerTime(serverNow);
            return serverNow < InputLockedUntil;
        }

        internal WrongWayInputResolution ApplyInput(
            WrongWayDirection submittedDirection,
            WrongWayDirection expectedDirection,
            double serverNow,
            ulong serverEventOrder)
        {
            ValidateDirection(submittedDirection, nameof(submittedDirection));
            ValidateDirection(expectedDirection, nameof(expectedDirection));
            ValidateServerTime(serverNow);

            var previousStep = CompletedSteps;
            if (IsInputLocked(serverNow))
            {
                return new WrongWayInputResolution(
                    PlayerSlot,
                    submittedDirection,
                    expectedDirection,
                    WrongWayInputStatus.IgnoredWhileLocked,
                    previousStep,
                    CompletedSteps,
                    InputLockedUntil,
                    0UL);
            }

            if (submittedDirection != expectedDirection)
            {
                InputLockedUntil =
                    serverNow + WrongWayRules.IncorrectInputLockSeconds;
                return new WrongWayInputResolution(
                    PlayerSlot,
                    submittedDirection,
                    expectedDirection,
                    WrongWayInputStatus.Incorrect,
                    previousStep,
                    CompletedSteps,
                    InputLockedUntil,
                    serverEventOrder);
            }

            CompletedSteps++;
            CurrentStepReachedOrder = serverEventOrder;
            InputLockedUntil = serverNow;
            var status = IsFinished
                ? WrongWayInputStatus.Finished
                : WrongWayInputStatus.Correct;
            return new WrongWayInputResolution(
                PlayerSlot,
                submittedDirection,
                expectedDirection,
                status,
                previousStep,
                CompletedSteps,
                InputLockedUntil,
                serverEventOrder);
        }

        internal WrongWayRoundOutcome CaptureOutcome()
        {
            return new WrongWayRoundOutcome(
                PlayerSlot,
                CompletedSteps,
                CurrentStepReachedOrder);
        }

        private static void ValidateDirection(
            WrongWayDirection direction,
            string parameterName)
        {
            if (direction < WrongWayDirection.Up ||
                direction > WrongWayDirection.Right)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    direction,
                    "Direction must be Up, Down, Left or Right.");
            }
        }

        private static void ValidateServerTime(double serverNow)
        {
            if (double.IsNaN(serverNow) ||
                double.IsInfinity(serverNow) ||
                serverNow < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(serverNow),
                    serverNow,
                    "Server time must be a finite non-negative value.");
            }
        }
    }

    /// <summary>
    /// Creates a canonical 50-direction prompt sequence from a server seed.
    /// A custom PRNG avoids System.Random differences between runtimes.
    /// </summary>
    public static class WrongWayPromptGenerator
    {
        public static ulong DeriveRoundSeed(ulong serverSeed, int roundNumber)
        {
            WrongWayRules.ValidateRoundNumber(roundNumber);
            var mixer = new SplitMix64(
                serverSeed ^
                unchecked(0xD1B54A32D192ED03UL * (ulong)roundNumber));
            return mixer.NextUInt64();
        }

        public static WrongWayDirection[] Generate(
            ulong serverSeed,
            int roundNumber)
        {
            WrongWayRules.ValidateRoundNumber(roundNumber);
            var random = new SplitMix64(
                DeriveRoundSeed(serverSeed, roundNumber));
            var prompts =
                new WrongWayDirection[WrongWayRules.StepCount];

            for (var index = 0; index < prompts.Length; index++)
            {
                prompts[index] =
                    (WrongWayDirection)random.NextInt(4);
            }

            return prompts;
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

    public readonly struct WrongWayRoundOutcome
    {
        public WrongWayRoundOutcome(
            int playerSlot,
            int completedSteps,
            ulong currentStepReachedOrder)
        {
            if (!WrongWayRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerSlot),
                    playerSlot,
                    "Player slot must be between 0 and 3.");
            }

            if (completedSteps < 0 ||
                completedSteps > WrongWayRules.StepCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(completedSteps),
                    completedSteps,
                    "Completed steps must be between 0 and 50.");
            }

            if (completedSteps > 0 && currentStepReachedOrder == 0UL)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(currentStepReachedOrder),
                    currentStepReachedOrder,
                    "A player above step zero must have a server event order.");
            }

            PlayerSlot = playerSlot;
            CompletedSteps = completedSteps;
            CurrentStepReachedOrder = currentStepReachedOrder;
        }

        public int PlayerSlot { get; }
        public int CompletedSteps { get; }

        /// <summary>
        /// Server event order at which this player reached CompletedSteps.
        /// </summary>
        public ulong CurrentStepReachedOrder { get; }
        public bool Finished =>
            CompletedSteps == WrongWayRules.StepCount;
    }

    public readonly struct WrongWayRoundStanding
    {
        internal WrongWayRoundStanding(
            int rank,
            int points,
            WrongWayRoundOutcome outcome)
        {
            Rank = rank;
            Points = points;
            Outcome = outcome;
        }

        public int PlayerSlot => Outcome.PlayerSlot;
        public int Rank { get; }
        public int Points { get; }
        public int CompletedSteps => Outcome.CompletedSteps;
        public WrongWayRoundOutcome Outcome { get; }
    }

    public sealed class WrongWayRoundResult
    {
        private readonly ReadOnlyCollection<WrongWayRoundStanding>
            _standings;

        internal WrongWayRoundResult(
            WrongWayRoundStanding[] standings)
        {
            _standings = Array.AsReadOnly(standings);
        }

        /// <summary>
        /// Standings are ordered from rank 1 through rank 4.
        /// </summary>
        public IReadOnlyList<WrongWayRoundStanding> Standings =>
            _standings;

        public WrongWayRoundStanding GetStandingForSlot(
            int playerSlot)
        {
            if (!WrongWayRules.IsValidPlayerSlot(playerSlot))
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

    public static class WrongWayRoundScoring
    {
        public static WrongWayRoundResult Score(
            IReadOnlyList<WrongWayRoundOutcome> outcomes)
        {
            if (outcomes == null)
            {
                throw new ArgumentNullException(nameof(outcomes));
            }

            if (outcomes.Count != WrongWayRules.PlayerCount)
            {
                throw new ArgumentException(
                    "A completed round must contain exactly four outcomes.",
                    nameof(outcomes));
            }

            var ordered =
                new WrongWayRoundOutcome[WrongWayRules.PlayerCount];
            var seenSlots =
                new bool[WrongWayRules.PlayerCount];

            for (var index = 0; index < outcomes.Count; index++)
            {
                var outcome = outcomes[index];
                if (!WrongWayRules.IsValidPlayerSlot(
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
                new WrongWayRoundStanding[WrongWayRules.PlayerCount];
            for (var index = 0; index < ordered.Length; index++)
            {
                var rank = index + 1;
                standings[index] = new WrongWayRoundStanding(
                    rank,
                    WrongWayRules.GetPointsForRank(rank),
                    ordered[index]);
            }

            return new WrongWayRoundResult(standings);
        }

        private static int CompareOutcomes(
            WrongWayRoundOutcome left,
            WrongWayRoundOutcome right)
        {
            var progress =
                right.CompletedSteps.CompareTo(left.CompletedSteps);
            if (progress != 0)
            {
                return progress;
            }

            if (left.CompletedSteps > 0)
            {
                var order = left.CurrentStepReachedOrder.CompareTo(
                    right.CurrentStepReachedOrder);
                if (order != 0)
                {
                    return order;
                }
            }

            return left.PlayerSlot.CompareTo(right.PlayerSlot);
        }
    }

    public readonly struct WrongWayLeaderboardEntry
    {
        internal WrongWayLeaderboardEntry(
            int playerSlot,
            int rank,
            int totalPoints,
            int totalCompletedSteps,
            int secondRoundRank)
        {
            PlayerSlot = playerSlot;
            Rank = rank;
            TotalPoints = totalPoints;
            TotalCompletedSteps = totalCompletedSteps;
            SecondRoundRank = secondRoundRank;
        }

        public int PlayerSlot { get; }
        public int Rank { get; }
        public int TotalPoints { get; }
        public int TotalCompletedSteps { get; }
        public int SecondRoundRank { get; }
    }

    public static class WrongWayMatchScoring
    {
        public static IReadOnlyList<WrongWayLeaderboardEntry>
            BuildLeaderboard(
                IReadOnlyList<WrongWayRoundResult> rounds)
        {
            if (rounds == null)
            {
                throw new ArgumentNullException(nameof(rounds));
            }

            if (rounds.Count != WrongWayRules.RoundCount)
            {
                throw new ArgumentException(
                    "A completed match must contain exactly two rounds.",
                    nameof(rounds));
            }

            var totalPoints =
                new int[WrongWayRules.PlayerCount];
            var totalSteps =
                new int[WrongWayRules.PlayerCount];
            var secondRoundRanks =
                new int[WrongWayRules.PlayerCount];

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
                    var standing =
                        round.Standings[standingIndex];
                    totalPoints[standing.PlayerSlot] +=
                        standing.Points;
                    totalSteps[standing.PlayerSlot] +=
                        standing.CompletedSteps;

                    if (roundIndex == WrongWayRules.RoundCount - 1)
                    {
                        secondRoundRanks[standing.PlayerSlot] =
                            standing.Rank;
                    }
                }
            }

            var playerSlots =
                new int[WrongWayRules.PlayerCount];
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

                    var steps = totalSteps[right].CompareTo(
                        totalSteps[left]);
                    if (steps != 0)
                    {
                        return steps;
                    }

                    var secondRound =
                        secondRoundRanks[left].CompareTo(
                            secondRoundRanks[right]);
                    return secondRound != 0
                        ? secondRound
                        : left.CompareTo(right);
                });

            var leaderboard =
                new WrongWayLeaderboardEntry[
                    WrongWayRules.PlayerCount];
            for (var index = 0;
                 index < playerSlots.Length;
                 index++)
            {
                var playerSlot = playerSlots[index];
                leaderboard[index] =
                    new WrongWayLeaderboardEntry(
                        playerSlot,
                        index + 1,
                        totalPoints[playerSlot],
                        totalSteps[playerSlot],
                        secondRoundRanks[playerSlot]);
            }

            return Array.AsReadOnly(leaderboard);
        }
    }
}
