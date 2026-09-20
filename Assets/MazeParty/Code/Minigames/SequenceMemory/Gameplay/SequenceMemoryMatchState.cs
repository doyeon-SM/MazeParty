using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MazeParty.Gameplay.Minigames.SequenceMemory
{
    public enum SequenceMemoryMatchPhase : byte
    {
        Ready,
        PresentingProblem,
        AcceptingInput,
        RevealingAnswer,
        Complete
    }

    public enum SequenceMemoryMatchEndReason : byte
    {
        None,
        AllRoundsCompleted,
        LastSurvivor,
        AllPlayersEliminated
    }

    public readonly struct SequenceMemoryStanding
    {
        internal SequenceMemoryStanding(
            int rank,
            SequenceMemoryPlayerOutcome outcome)
        {
            Rank = rank;
            Outcome = outcome;
        }

        public int PlayerSlot => Outcome.PlayerSlot;
        public int Rank { get; }
        public SequenceMemoryPlayerLifeState LifeState =>
            Outcome.LifeState;
        public int MistakeCount => Outcome.MistakeCount;
        public SequenceMemoryPlayerOutcome Outcome { get; }
    }

    public sealed class SequenceMemoryMatchResult
    {
        private readonly ReadOnlyCollection<SequenceMemoryStanding>
            _standings;

        internal SequenceMemoryMatchResult(
            SequenceMemoryMatchEndReason endReason,
            int completedRoundCount,
            SequenceMemoryStanding[] standings)
        {
            EndReason = endReason;
            CompletedRoundCount = completedRoundCount;
            _standings = Array.AsReadOnly(standings);
        }

        public SequenceMemoryMatchEndReason EndReason { get; }
        public int CompletedRoundCount { get; }

        /// <summary>
        /// Unique standings ordered from rank 1 through rank 4. There is no
        /// per-round point value in this minigame.
        /// </summary>
        public IReadOnlyList<SequenceMemoryStanding> Standings =>
            _standings;

        public SequenceMemoryStanding GetStandingForSlot(
            int playerSlot)
        {
            if (!SequenceMemoryRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerSlot),
                    playerSlot,
                    "Player slot must be between 0 and 3.");
            }

            for (var index = 0; index < _standings.Count; index++)
            {
                if (_standings[index].PlayerSlot == playerSlot)
                {
                    return _standings[index];
                }
            }

            throw new InvalidOperationException(
                "Match result does not contain the requested player.");
        }
    }

    /// <summary>
    /// Produces the direct single-match ranking without intermediate round
    /// points. Survivors precede eliminated players. Round-ten solvers use
    /// completion time; elimination ties use round, prefix, previous speed,
    /// then authoritative processing order.
    /// </summary>
    public static class SequenceMemoryMatchRanking
    {
        public static SequenceMemoryMatchResult BuildResult(
            IReadOnlyList<SequenceMemoryPlayerOutcome> outcomes,
            SequenceMemoryMatchEndReason endReason,
            int completedRoundCount)
        {
            if (outcomes == null)
            {
                throw new ArgumentNullException(nameof(outcomes));
            }

            if (endReason == SequenceMemoryMatchEndReason.None)
            {
                throw new ArgumentOutOfRangeException(nameof(endReason));
            }

            SequenceMemoryRules.ValidateRoundNumber(completedRoundCount);
            if (outcomes.Count != SequenceMemoryRules.PlayerCount)
            {
                throw new ArgumentException(
                    "A completed match must contain exactly four outcomes.",
                    nameof(outcomes));
            }

            var ordered =
                new SequenceMemoryPlayerOutcome[
                    SequenceMemoryRules.PlayerCount];
            var seenSlots = new bool[SequenceMemoryRules.PlayerCount];
            for (var index = 0; index < outcomes.Count; index++)
            {
                var outcome = outcomes[index];
                if (outcome == null ||
                    !SequenceMemoryRules.IsValidPlayerSlot(
                        outcome.PlayerSlot))
                {
                    throw new ArgumentException(
                        "Outcome contains a null or invalid player.",
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

            Array.Sort(
                ordered,
                (left, right) => CompareOutcomes(
                    left,
                    right,
                    completedRoundCount));

            var standings =
                new SequenceMemoryStanding[
                    SequenceMemoryRules.PlayerCount];
            for (var index = 0; index < ordered.Length; index++)
            {
                standings[index] = new SequenceMemoryStanding(
                    index + 1,
                    ordered[index]);
            }

            return new SequenceMemoryMatchResult(
                endReason,
                completedRoundCount,
                standings);
        }

        private static int CompareOutcomes(
            SequenceMemoryPlayerOutcome left,
            SequenceMemoryPlayerOutcome right,
            int completedRoundCount)
        {
            if (left.IsEliminated != right.IsEliminated)
            {
                return left.IsEliminated ? 1 : -1;
            }

            if (!left.IsEliminated)
            {
                return CompareSurvivors(
                    left,
                    right,
                    completedRoundCount);
            }

            var eliminationRound = right.EliminatedOnRound.CompareTo(
                left.EliminatedOnRound);
            if (eliminationRound != 0)
            {
                return eliminationRound;
            }

            var prefix =
                right.EliminationCorrectPrefixLength.CompareTo(
                    left.EliminationCorrectPrefixLength);
            if (prefix != 0)
            {
                return prefix;
            }

            var previousSpeed =
                left.SuccessfulCompletionSecondsBeforeElimination
                    .CompareTo(
                        right.SuccessfulCompletionSecondsBeforeElimination);
            if (previousSpeed != 0)
            {
                return previousSpeed;
            }

            var eventOrder = left.EliminationEventOrder.CompareTo(
                right.EliminationEventOrder);
            return eventOrder != 0
                ? eventOrder
                : left.PlayerSlot.CompareTo(right.PlayerSlot);
        }

        private static int CompareSurvivors(
            SequenceMemoryPlayerOutcome left,
            SequenceMemoryPlayerOutcome right,
            int completedRoundCount)
        {
            if (completedRoundCount == SequenceMemoryRules.RoundCount)
            {
                var leftSolved = left.LastTurnStatus ==
                    SequenceMemoryPlayerTurnStatus.Correct;
                var rightSolved = right.LastTurnStatus ==
                    SequenceMemoryPlayerTurnStatus.Correct;
                if (leftSolved != rightSolved)
                {
                    return leftSolved ? -1 : 1;
                }

                if (leftSolved)
                {
                    var completionTime =
                        left.LastTurnResolvedAtSeconds.CompareTo(
                            right.LastTurnResolvedAtSeconds);
                    if (completionTime != 0)
                    {
                        return completionTime;
                    }

                    var completionOrder =
                        left.LastTurnResolutionEventOrder.CompareTo(
                            right.LastTurnResolutionEventOrder);
                    if (completionOrder != 0)
                    {
                        return completionOrder;
                    }
                }
                else
                {
                    var prefix =
                        right.LastTurnCorrectPrefixLength.CompareTo(
                            left.LastTurnCorrectPrefixLength);
                    if (prefix != 0)
                    {
                        return prefix;
                    }

                    var previousSpeed =
                        left.SuccessfulCompletionSecondsTotal.CompareTo(
                            right.SuccessfulCompletionSecondsTotal);
                    if (previousSpeed != 0)
                    {
                        return previousSpeed;
                    }

                    var resolutionOrder =
                        left.LastTurnResolutionEventOrder.CompareTo(
                            right.LastTurnResolutionEventOrder);
                    if (resolutionOrder != 0)
                    {
                        return resolutionOrder;
                    }
                }
            }

            return left.PlayerSlot.CompareTo(right.PlayerSlot);
        }
    }

    /// <summary>
    /// Server-ready pure lifecycle for the complete ten-problem match. The
    /// caller owns countdown, cue playback and reveal clocks, and advances the
    /// explicit phases at their authoritative boundaries.
    /// </summary>
    public sealed class SequenceMemoryMatchState
    {
        private readonly SequenceMemoryProblem[] _problems;
        private readonly ReadOnlyCollection<SequenceMemoryProblem>
            _readOnlyProblems;
        private readonly SequenceMemoryPlayerState[] _players;
        private readonly ReadOnlyCollection<SequenceMemoryPlayerState>
            _readOnlyPlayers;

        private double _latestInputElapsedSeconds;
        private ulong _serverEventSequence;

        public SequenceMemoryMatchState(ulong serverSeed)
        {
            ServerSeed = serverSeed;
            _problems =
                SequenceMemoryProblemGenerator.GenerateMatch(serverSeed);
            _readOnlyProblems = Array.AsReadOnly(_problems);
            _players =
                new SequenceMemoryPlayerState[
                    SequenceMemoryRules.PlayerCount];
            for (var playerSlot = 0;
                 playerSlot < _players.Length;
                 playerSlot++)
            {
                _players[playerSlot] =
                    new SequenceMemoryPlayerState(playerSlot);
            }

            _readOnlyPlayers = Array.AsReadOnly(_players);
            Phase = SequenceMemoryMatchPhase.Ready;
        }

        public ulong ServerSeed { get; }
        public SequenceMemoryMatchPhase Phase { get; private set; }
        public SequenceMemoryMatchEndReason EndReason {
            get;
            private set;
        }
        public int CurrentRoundNumber { get; private set; }
        public uint InputEpoch { get; private set; }
        public bool IsComplete =>
            Phase == SequenceMemoryMatchPhase.Complete;
        public IReadOnlyList<SequenceMemoryProblem> Problems =>
            _readOnlyProblems;
        public IReadOnlyList<SequenceMemoryPlayerState> Players =>
            _readOnlyPlayers;
        public SequenceMemoryProblem CurrentProblem =>
            CurrentRoundNumber == 0
                ? null
                : _problems[CurrentRoundNumber - 1];
        public SequenceMemoryInputCloseResolution
            LastInputCloseResolution { get; private set; }
        public SequenceMemoryMatchResult Result { get; private set; }

        public int AlivePlayerCount
        {
            get
            {
                var count = 0;
                for (var index = 0; index < _players.Length; index++)
                {
                    if (!_players[index].IsEliminated)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public void BeginMatch()
        {
            if (Phase != SequenceMemoryMatchPhase.Ready)
            {
                throw new InvalidOperationException(
                    "The sequence memory match has already begun.");
            }

            BeginRound(1);
        }

        public void OpenInputWindow()
        {
            if (Phase != SequenceMemoryMatchPhase.PresentingProblem)
            {
                throw new InvalidOperationException(
                    "Input can open only after the current problem is shown.");
            }

            if (InputEpoch == uint.MaxValue)
            {
                throw new InvalidOperationException(
                    "Input epoch was exhausted.");
            }

            InputEpoch++;
            _latestInputElapsedSeconds = 0d;
            for (var index = 0; index < _players.Length; index++)
            {
                _players[index].OpenInput();
            }

            Phase = SequenceMemoryMatchPhase.AcceptingInput;
        }

        public bool CanAcceptInputForSlot(
            int playerSlot,
            byte roundNumber,
            uint inputEpoch)
        {
            var player = GetPlayer(playerSlot);
            return Phase == SequenceMemoryMatchPhase.AcceptingInput &&
                roundNumber == CurrentRoundNumber &&
                inputEpoch == InputEpoch &&
                player.CanAcceptInput;
        }

        public SequenceMemoryInputResolution SubmitInput(
            int playerSlot,
            SequenceMemoryInput submittedInput,
            byte roundNumber,
            uint inputEpoch,
            double inputElapsedSeconds)
        {
            var player = GetPlayer(playerSlot);
            SequenceMemoryRules.ValidateInput(
                submittedInput,
                nameof(submittedInput));
            SequenceMemoryRules.ValidateInputElapsedSeconds(
                inputElapsedSeconds);

            if (IsComplete)
            {
                return IgnoredInput(
                    player,
                    submittedInput,
                    SequenceMemoryInputStatus.IgnoredMatchComplete);
            }

            if (Phase != SequenceMemoryMatchPhase.AcceptingInput)
            {
                return IgnoredInput(
                    player,
                    submittedInput,
                    SequenceMemoryInputStatus.IgnoredInputNotOpen);
            }

            if (roundNumber != CurrentRoundNumber ||
                inputEpoch != InputEpoch)
            {
                return IgnoredInput(
                    player,
                    submittedInput,
                    SequenceMemoryInputStatus.IgnoredStaleInputWindow);
            }

            ValidateMonotonicInputTime(inputElapsedSeconds);
            if (inputElapsedSeconds >=
                SequenceMemoryRules.InputWindowSeconds)
            {
                var previousInputLength = player.CurrentInput.Count;
                var expectedInput = GetExpectedInput(player);
                TryEndInputForTimeout(inputElapsedSeconds);
                return new SequenceMemoryInputResolution(
                    playerSlot,
                    submittedInput,
                    expectedInput,
                    SequenceMemoryInputStatus.IgnoredAtOrAfterDeadline,
                    previousInputLength,
                    player.CurrentInput.Count,
                    player.CurrentCorrectPrefixLength,
                    SequenceMemoryMistakeOutcome.None,
                    0UL,
                    true,
                    LastInputCloseResolution.Reason);
            }

            _latestInputElapsedSeconds = inputElapsedSeconds;
            if (player.IsEliminated)
            {
                return IgnoredInput(
                    player,
                    submittedInput,
                    SequenceMemoryInputStatus.IgnoredPlayerEliminated);
            }

            if (!player.CanAcceptInput)
            {
                return IgnoredInput(
                    player,
                    submittedInput,
                    SequenceMemoryInputStatus.IgnoredPlayerLocked);
            }

            var previousLength = player.CurrentInput.Count;
            var expected = CurrentProblem[
                player.CurrentCorrectPrefixLength];
            var serverEventOrder = NextServerEventOrder();
            SequenceMemoryInputStatus status;
            SequenceMemoryMistakeOutcome mistakeOutcome;
            if (submittedInput != expected)
            {
                var mistake = player.RecordMismatch(
                    submittedInput,
                    CurrentRoundNumber,
                    inputElapsedSeconds,
                    serverEventOrder);
                status =
                    SequenceMemoryInputStatus.FailedPrefixMismatch;
                mistakeOutcome = mistake.Outcome;
            }
            else
            {
                var completed = previousLength + 1 ==
                    CurrentProblem.Length;
                player.RecordCorrectPrefixInput(
                    submittedInput,
                    completed,
                    inputElapsedSeconds,
                    serverEventOrder);
                status = completed
                    ? SequenceMemoryInputStatus.CompletedProblem
                    : SequenceMemoryInputStatus.AcceptedPrefix;
                mistakeOutcome = SequenceMemoryMistakeOutcome.None;
            }

            TryCloseInputAfterResolution(inputElapsedSeconds);
            return new SequenceMemoryInputResolution(
                playerSlot,
                submittedInput,
                expected,
                status,
                previousLength,
                player.CurrentInput.Count,
                player.CurrentCorrectPrefixLength,
                mistakeOutcome,
                serverEventOrder,
                Phase == SequenceMemoryMatchPhase.RevealingAnswer,
                LastInputCloseResolution == null
                    ? SequenceMemoryInputCloseReason.None
                    : LastInputCloseResolution.Reason);
        }

        /// <summary>
        /// At the exact ten-second boundary, unresolved empty and partial
        /// inputs all receive one mistake as one authoritative batch.
        /// </summary>
        public bool TryEndInputForTimeout(double inputElapsedSeconds)
        {
            SequenceMemoryRules.ValidateInputElapsedSeconds(
                inputElapsedSeconds);
            if (Phase != SequenceMemoryMatchPhase.AcceptingInput)
            {
                return false;
            }

            ValidateMonotonicInputTime(inputElapsedSeconds);
            if (inputElapsedSeconds <
                SequenceMemoryRules.InputWindowSeconds)
            {
                _latestInputElapsedSeconds = inputElapsedSeconds;
                return false;
            }

            _latestInputElapsedSeconds =
                SequenceMemoryRules.InputWindowSeconds;
            var mistakes =
                new SequenceMemoryMistakeResolution[
                    SequenceMemoryRules.PlayerCount];
            var mistakeCount = 0;
            for (var playerSlot = 0;
                 playerSlot < _players.Length;
                 playerSlot++)
            {
                var player = _players[playerSlot];
                if (!player.CanAcceptInput)
                {
                    continue;
                }

                mistakes[mistakeCount] = player.RecordTimeout(
                    CurrentRoundNumber,
                    SequenceMemoryRules.InputWindowSeconds,
                    NextServerEventOrder());
                mistakeCount++;
            }

            var appliedMistakes =
                new SequenceMemoryMistakeResolution[mistakeCount];
            Array.Copy(
                mistakes,
                appliedMistakes,
                mistakeCount);
            CloseInput(
                SequenceMemoryInputCloseReason.TimeLimit,
                SequenceMemoryRules.InputWindowSeconds,
                appliedMistakes);
            return true;
        }

        /// <summary>
        /// Called after the two-second answer reveal. It either produces the
        /// final direct ranking or immediately enters the next presentation.
        /// </summary>
        public void CompleteRevealAndAdvance()
        {
            if (Phase != SequenceMemoryMatchPhase.RevealingAnswer)
            {
                throw new InvalidOperationException(
                    "The answer can finish only during its reveal phase.");
            }

            if (AlivePlayerCount == 0)
            {
                CompleteMatch(
                    SequenceMemoryMatchEndReason.AllPlayersEliminated);
                return;
            }

            if (AlivePlayerCount == 1)
            {
                CompleteMatch(SequenceMemoryMatchEndReason.LastSurvivor);
                return;
            }

            if (CurrentRoundNumber == SequenceMemoryRules.RoundCount)
            {
                CompleteMatch(
                    SequenceMemoryMatchEndReason.AllRoundsCompleted);
                return;
            }

            BeginRound(CurrentRoundNumber + 1);
        }

        public SequenceMemoryPlayerState GetPlayer(int playerSlot)
        {
            if (!SequenceMemoryRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerSlot),
                    playerSlot,
                    "Player slot must be between 0 and 3.");
            }

            return _players[playerSlot];
        }

        private void BeginRound(int roundNumber)
        {
            CurrentRoundNumber = roundNumber;
            LastInputCloseResolution = null;
            for (var index = 0; index < _players.Length; index++)
            {
                _players[index].BeginRound();
            }

            Phase = SequenceMemoryMatchPhase.PresentingProblem;
        }

        private void TryCloseInputAfterResolution(
            double inputElapsedSeconds)
        {
            SequenceMemoryInputCloseReason reason;
            if (AlivePlayerCount == 0)
            {
                reason =
                    SequenceMemoryInputCloseReason.AllPlayersEliminated;
            }
            else if (AlivePlayerCount == 1)
            {
                reason = SequenceMemoryInputCloseReason.LastSurvivor;
            }
            else if (AllInputEligiblePlayersResolved())
            {
                reason = SequenceMemoryInputCloseReason.AllPlayersResolved;
            }
            else
            {
                return;
            }

            CloseInput(
                reason,
                inputElapsedSeconds,
                new SequenceMemoryMistakeResolution[0]);
        }

        private void CloseInput(
            SequenceMemoryInputCloseReason reason,
            double closedAtSeconds,
            SequenceMemoryMistakeResolution[] timeoutMistakes)
        {
            if (reason == SequenceMemoryInputCloseReason.LastSurvivor ||
                reason ==
                SequenceMemoryInputCloseReason.AllPlayersEliminated)
            {
                for (var index = 0; index < _players.Length; index++)
                {
                    _players[index].LockForMatchEnd();
                }
            }

            LastInputCloseResolution =
                new SequenceMemoryInputCloseResolution(
                    reason,
                    closedAtSeconds,
                    timeoutMistakes);
            Phase = SequenceMemoryMatchPhase.RevealingAnswer;
        }

        private bool AllInputEligiblePlayersResolved()
        {
            for (var index = 0; index < _players.Length; index++)
            {
                if (_players[index].CanAcceptInput)
                {
                    return false;
                }
            }

            return true;
        }

        private SequenceMemoryInput? GetExpectedInput(
            SequenceMemoryPlayerState player)
        {
            if (CurrentProblem == null ||
                player.CurrentCorrectPrefixLength >=
                CurrentProblem.Length)
            {
                return null;
            }

            return CurrentProblem[player.CurrentCorrectPrefixLength];
        }

        private SequenceMemoryInputResolution IgnoredInput(
            SequenceMemoryPlayerState player,
            SequenceMemoryInput submittedInput,
            SequenceMemoryInputStatus status)
        {
            return new SequenceMemoryInputResolution(
                player.PlayerSlot,
                submittedInput,
                GetExpectedInput(player),
                status,
                player.CurrentInput.Count,
                player.CurrentInput.Count,
                player.CurrentCorrectPrefixLength,
                SequenceMemoryMistakeOutcome.None,
                0UL,
                Phase == SequenceMemoryMatchPhase.RevealingAnswer,
                LastInputCloseResolution == null
                    ? SequenceMemoryInputCloseReason.None
                    : LastInputCloseResolution.Reason);
        }

        private void ValidateMonotonicInputTime(
            double inputElapsedSeconds)
        {
            if (inputElapsedSeconds < _latestInputElapsedSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(inputElapsedSeconds),
                    inputElapsedSeconds,
                    "Authoritative input time cannot move backwards.");
            }
        }

        private ulong NextServerEventOrder()
        {
            if (_serverEventSequence == ulong.MaxValue)
            {
                throw new InvalidOperationException(
                    "Server event order was exhausted.");
            }

            _serverEventSequence++;
            return _serverEventSequence;
        }

        private void CompleteMatch(SequenceMemoryMatchEndReason reason)
        {
            var outcomes =
                new SequenceMemoryPlayerOutcome[
                    SequenceMemoryRules.PlayerCount];
            for (var index = 0; index < _players.Length; index++)
            {
                outcomes[index] = _players[index].CaptureOutcome();
            }

            Result = SequenceMemoryMatchRanking.BuildResult(
                outcomes,
                reason,
                CurrentRoundNumber);
            EndReason = reason;
            Phase = SequenceMemoryMatchPhase.Complete;
        }
    }
}
