using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MazeParty.Gameplay.Minigames.SequenceMemory
{
    public enum SequenceMemoryPlayerLifeState : byte
    {
        Intact,
        TorsoLost,
        Eliminated
    }

    public enum SequenceMemoryPlayerTurnStatus : byte
    {
        NotStarted,
        ObservingProblem,
        Entering,
        Correct,
        Failed,
        LockedForMatchEnd,
        Eliminated
    }

    public enum SequenceMemoryMistakeKind : byte
    {
        None,
        PrefixMismatch,
        Timeout
    }

    public enum SequenceMemoryMistakeOutcome : byte
    {
        None,
        TorsoLost,
        Eliminated
    }

    public enum SequenceMemoryInputStatus : byte
    {
        IgnoredMatchComplete,
        IgnoredInputNotOpen,
        IgnoredStaleInputWindow,
        IgnoredPlayerEliminated,
        IgnoredPlayerLocked,
        IgnoredAtOrAfterDeadline,
        AcceptedPrefix,
        CompletedProblem,
        FailedPrefixMismatch
    }

    public enum SequenceMemoryInputCloseReason : byte
    {
        None,
        AllPlayersResolved,
        TimeLimit,
        LastSurvivor,
        AllPlayersEliminated
    }

    public readonly struct SequenceMemoryMistakeResolution
    {
        internal SequenceMemoryMistakeResolution(
            int playerSlot,
            int roundNumber,
            SequenceMemoryMistakeKind kind,
            SequenceMemoryMistakeOutcome outcome,
            int mistakeCount,
            int correctPrefixLength,
            double resolvedAtSeconds,
            ulong serverEventOrder)
        {
            PlayerSlot = playerSlot;
            RoundNumber = roundNumber;
            Kind = kind;
            Outcome = outcome;
            MistakeCount = mistakeCount;
            CorrectPrefixLength = correctPrefixLength;
            ResolvedAtSeconds = resolvedAtSeconds;
            ServerEventOrder = serverEventOrder;
        }

        public int PlayerSlot { get; }
        public int RoundNumber { get; }
        public SequenceMemoryMistakeKind Kind { get; }
        public SequenceMemoryMistakeOutcome Outcome { get; }
        public int MistakeCount { get; }
        public int CorrectPrefixLength { get; }
        public double ResolvedAtSeconds { get; }
        public ulong ServerEventOrder { get; }
        public bool BecameTorsoLost =>
            Outcome == SequenceMemoryMistakeOutcome.TorsoLost;
        public bool BecameEliminated =>
            Outcome == SequenceMemoryMistakeOutcome.Eliminated;
    }

    public readonly struct SequenceMemoryInputResolution
    {
        internal SequenceMemoryInputResolution(
            int playerSlot,
            SequenceMemoryInput submittedInput,
            SequenceMemoryInput? expectedInput,
            SequenceMemoryInputStatus status,
            int previousInputLength,
            int currentInputLength,
            int correctPrefixLength,
            SequenceMemoryMistakeOutcome mistakeOutcome,
            ulong serverEventOrder,
            bool inputPhaseClosed,
            SequenceMemoryInputCloseReason inputCloseReason)
        {
            PlayerSlot = playerSlot;
            SubmittedInput = submittedInput;
            ExpectedInput = expectedInput;
            Status = status;
            PreviousInputLength = previousInputLength;
            CurrentInputLength = currentInputLength;
            CorrectPrefixLength = correctPrefixLength;
            MistakeOutcome = mistakeOutcome;
            ServerEventOrder = serverEventOrder;
            InputPhaseClosed = inputPhaseClosed;
            InputCloseReason = inputCloseReason;
        }

        public int PlayerSlot { get; }
        public SequenceMemoryInput SubmittedInput { get; }
        public SequenceMemoryInput? ExpectedInput { get; }
        public SequenceMemoryInputStatus Status { get; }
        public int PreviousInputLength { get; }
        public int CurrentInputLength { get; }
        public int CorrectPrefixLength { get; }
        public SequenceMemoryMistakeOutcome MistakeOutcome { get; }
        public ulong ServerEventOrder { get; }
        public bool InputPhaseClosed { get; }
        public SequenceMemoryInputCloseReason InputCloseReason { get; }

        public bool WasAccepted =>
            Status == SequenceMemoryInputStatus.AcceptedPrefix ||
            Status == SequenceMemoryInputStatus.CompletedProblem ||
            Status ==
                SequenceMemoryInputStatus.FailedPrefixMismatch;

        public bool ShouldPlayInputTone => WasAccepted;
        public bool CompletedProblem =>
            Status == SequenceMemoryInputStatus.CompletedProblem;
        public bool FailedProblem =>
            Status == SequenceMemoryInputStatus.FailedPrefixMismatch;
        public bool BecameTorsoLost =>
            MistakeOutcome == SequenceMemoryMistakeOutcome.TorsoLost;
        public bool BecameEliminated =>
            MistakeOutcome == SequenceMemoryMistakeOutcome.Eliminated;
    }

    public sealed class SequenceMemoryInputCloseResolution
    {
        private readonly ReadOnlyCollection<
            SequenceMemoryMistakeResolution> _timeoutMistakes;

        internal SequenceMemoryInputCloseResolution(
            SequenceMemoryInputCloseReason reason,
            double closedAtSeconds,
            SequenceMemoryMistakeResolution[] timeoutMistakes)
        {
            Reason = reason;
            ClosedAtSeconds = closedAtSeconds;
            _timeoutMistakes = Array.AsReadOnly(
                timeoutMistakes ??
                new SequenceMemoryMistakeResolution[0]);
        }

        public SequenceMemoryInputCloseReason Reason { get; }
        public double ClosedAtSeconds { get; }
        public IReadOnlyList<SequenceMemoryMistakeResolution>
            TimeoutMistakes => _timeoutMistakes;
        public bool WasTimeLimit =>
            Reason == SequenceMemoryInputCloseReason.TimeLimit;
    }

    /// <summary>
    /// Pure persistent state for one player across all ten problem turns.
    /// CurrentInput intentionally remains readable so every client can show it.
    /// </summary>
    public sealed class SequenceMemoryPlayerState
    {
        private readonly List<SequenceMemoryInput> _currentInput =
            new List<SequenceMemoryInput>(8);
        private readonly ReadOnlyCollection<SequenceMemoryInput>
            _readOnlyCurrentInput;

        internal SequenceMemoryPlayerState(int playerSlot)
        {
            if (!SequenceMemoryRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerSlot),
                    playerSlot,
                    "Player slot must be between 0 and 3.");
            }

            PlayerSlot = playerSlot;
            LifeState = SequenceMemoryPlayerLifeState.Intact;
            _readOnlyCurrentInput = _currentInput.AsReadOnly();
        }

        public int PlayerSlot { get; }
        public SequenceMemoryPlayerLifeState LifeState {
            get;
            private set;
        }
        public SequenceMemoryPlayerTurnStatus TurnStatus {
            get;
            private set;
        }
        public int MistakeCount { get; private set; }
        public bool ShouldHideTorso =>
            LifeState != SequenceMemoryPlayerLifeState.Intact;
        public bool IsEliminated =>
            LifeState == SequenceMemoryPlayerLifeState.Eliminated;
        public bool CanAcceptInput =>
            TurnStatus == SequenceMemoryPlayerTurnStatus.Entering;
        public IReadOnlyList<SequenceMemoryInput> CurrentInput =>
            _readOnlyCurrentInput;
        public int CurrentCorrectPrefixLength { get; private set; }
        public double CurrentResolvedAtSeconds { get; private set; }
        public ulong CurrentResolutionEventOrder { get; private set; }
        public SequenceMemoryMistakeKind CurrentMistakeKind {
            get;
            private set;
        }
        public double SuccessfulCompletionSecondsTotal {
            get;
            private set;
        }
        public int EliminatedOnRound { get; private set; }
        public int EliminationCorrectPrefixLength { get; private set; }
        public double SuccessfulCompletionSecondsBeforeElimination {
            get;
            private set;
        }
        public ulong EliminationEventOrder { get; private set; }

        internal void BeginRound()
        {
            _currentInput.Clear();
            CurrentCorrectPrefixLength = 0;
            CurrentResolvedAtSeconds = 0d;
            CurrentResolutionEventOrder = 0UL;
            CurrentMistakeKind = SequenceMemoryMistakeKind.None;
            TurnStatus = IsEliminated
                ? SequenceMemoryPlayerTurnStatus.Eliminated
                : SequenceMemoryPlayerTurnStatus.ObservingProblem;
        }

        internal void OpenInput()
        {
            if (!IsEliminated)
            {
                TurnStatus = SequenceMemoryPlayerTurnStatus.Entering;
            }
        }

        internal void RecordCorrectPrefixInput(
            SequenceMemoryInput input,
            bool completedProblem,
            double inputElapsedSeconds,
            ulong serverEventOrder)
        {
            _currentInput.Add(input);
            CurrentCorrectPrefixLength++;
            if (!completedProblem)
            {
                return;
            }

            TurnStatus = SequenceMemoryPlayerTurnStatus.Correct;
            CurrentResolvedAtSeconds = inputElapsedSeconds;
            CurrentResolutionEventOrder = serverEventOrder;
            SuccessfulCompletionSecondsTotal += inputElapsedSeconds;
        }

        internal SequenceMemoryMistakeResolution RecordMismatch(
            SequenceMemoryInput submittedInput,
            int roundNumber,
            double inputElapsedSeconds,
            ulong serverEventOrder)
        {
            _currentInput.Add(submittedInput);
            return RecordMistake(
                roundNumber,
                SequenceMemoryMistakeKind.PrefixMismatch,
                inputElapsedSeconds,
                serverEventOrder);
        }

        internal SequenceMemoryMistakeResolution RecordTimeout(
            int roundNumber,
            double inputElapsedSeconds,
            ulong serverEventOrder)
        {
            return RecordMistake(
                roundNumber,
                SequenceMemoryMistakeKind.Timeout,
                inputElapsedSeconds,
                serverEventOrder);
        }

        internal void LockForMatchEnd()
        {
            if (TurnStatus == SequenceMemoryPlayerTurnStatus.Entering)
            {
                TurnStatus =
                    SequenceMemoryPlayerTurnStatus.LockedForMatchEnd;
            }
        }

        internal SequenceMemoryPlayerOutcome CaptureOutcome()
        {
            return new SequenceMemoryPlayerOutcome(this);
        }

        private SequenceMemoryMistakeResolution RecordMistake(
            int roundNumber,
            SequenceMemoryMistakeKind mistakeKind,
            double inputElapsedSeconds,
            ulong serverEventOrder)
        {
            MistakeCount++;
            CurrentMistakeKind = mistakeKind;
            CurrentResolvedAtSeconds = inputElapsedSeconds;
            CurrentResolutionEventOrder = serverEventOrder;
            TurnStatus = SequenceMemoryPlayerTurnStatus.Failed;

            SequenceMemoryMistakeOutcome outcome;
            if (MistakeCount >= SequenceMemoryRules.MistakesToEliminate)
            {
                LifeState = SequenceMemoryPlayerLifeState.Eliminated;
                EliminatedOnRound = roundNumber;
                EliminationCorrectPrefixLength =
                    CurrentCorrectPrefixLength;
                SuccessfulCompletionSecondsBeforeElimination =
                    SuccessfulCompletionSecondsTotal;
                EliminationEventOrder = serverEventOrder;
                outcome = SequenceMemoryMistakeOutcome.Eliminated;
            }
            else
            {
                LifeState = SequenceMemoryPlayerLifeState.TorsoLost;
                outcome = SequenceMemoryMistakeOutcome.TorsoLost;
            }

            return new SequenceMemoryMistakeResolution(
                PlayerSlot,
                roundNumber,
                mistakeKind,
                outcome,
                MistakeCount,
                CurrentCorrectPrefixLength,
                inputElapsedSeconds,
                serverEventOrder);
        }
    }

    /// <summary>
    /// Immutable ranking snapshot captured when the match completes.
    /// </summary>
    public sealed class SequenceMemoryPlayerOutcome
    {
        internal SequenceMemoryPlayerOutcome(
            SequenceMemoryPlayerState player)
        {
            PlayerSlot = player.PlayerSlot;
            LifeState = player.LifeState;
            MistakeCount = player.MistakeCount;
            LastTurnStatus = player.TurnStatus;
            LastTurnCorrectPrefixLength =
                player.CurrentCorrectPrefixLength;
            LastTurnResolvedAtSeconds =
                player.CurrentResolvedAtSeconds;
            LastTurnResolutionEventOrder =
                player.CurrentResolutionEventOrder;
            SuccessfulCompletionSecondsTotal =
                player.SuccessfulCompletionSecondsTotal;
            EliminatedOnRound = player.EliminatedOnRound;
            EliminationCorrectPrefixLength =
                player.EliminationCorrectPrefixLength;
            SuccessfulCompletionSecondsBeforeElimination =
                player.SuccessfulCompletionSecondsBeforeElimination;
            EliminationEventOrder = player.EliminationEventOrder;
        }

        public int PlayerSlot { get; }
        public SequenceMemoryPlayerLifeState LifeState { get; }
        public int MistakeCount { get; }
        public SequenceMemoryPlayerTurnStatus LastTurnStatus { get; }
        public int LastTurnCorrectPrefixLength { get; }
        public double LastTurnResolvedAtSeconds { get; }
        public ulong LastTurnResolutionEventOrder { get; }
        public double SuccessfulCompletionSecondsTotal { get; }
        public int EliminatedOnRound { get; }
        public int EliminationCorrectPrefixLength { get; }
        public double SuccessfulCompletionSecondsBeforeElimination {
            get;
        }
        public ulong EliminationEventOrder { get; }
        public bool IsEliminated =>
            LifeState == SequenceMemoryPlayerLifeState.Eliminated;
    }
}
