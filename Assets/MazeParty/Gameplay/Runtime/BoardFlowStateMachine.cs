using System;

namespace MazeParty.Gameplay
{
    public enum BoardFlowState
    {
        TurnOverview,
        Descending,
        Action,
        AscendingResolve,
        CombatResolve,
        LandingEffectResolve,
        MinigameIntroReady,
        SkippedResult,
        MinigameLoading,
        MinigamePlaying
    }

    public enum BoardActionEndReason
    {
        None,
        AllPlayersArrived,
        TimeExpired
    }

    public readonly struct BoardFlowTransition
    {
        public BoardFlowTransition(
            BoardFlowState previous,
            BoardFlowState current,
            int turn,
            double occurredAt)
        {
            Previous = previous;
            Current = current;
            Turn = turn;
            OccurredAt = occurredAt;
        }

        public BoardFlowState Previous { get; }
        public BoardFlowState Current { get; }
        public int Turn { get; }
        public double OccurredAt { get; }
    }

    /// <summary>
    /// Network-independent board turn timeline. Callers inject a monotonically
    /// increasing synchronized server timestamp. Pause intervals are removed from
    /// the logical flow timeline so reconnect-wide pauses preserve every countdown.
    /// </summary>
    public sealed class BoardFlowStateMachine
    {
        public const int RequiredPlayerCount = 4;
        public const double TurnOverviewDurationSeconds = 5d;
        public const double DescendingDurationSeconds = 1d;
        public const double AscendingResolveDurationSeconds = 5d;
        public const double LandingEffectResolveDurationSeconds = 4d;
        public const double SkippedResultDurationSeconds = 3d;

        private const int AllPlayersMask = (1 << RequiredPlayerCount) - 1;
        private const int TransitionSafetyLimit = 12;

        private int _arrivedPlayerMask;
        private double _stateStartedAt;
        private double _totalPausedDuration;
        private double _pauseStartedAt;

        public BoardFlowStateMachine(GameplayPhaseClock actionClock = null)
        {
            ActionClock = actionClock ?? new GameplayPhaseClock();
        }

        public event Action<BoardFlowTransition> Transitioned;

        public GameplayPhaseClock ActionClock { get; }
        public BoardFlowState State { get; private set; } = BoardFlowState.TurnOverview;
        public BoardActionEndReason LastActionEndReason { get; private set; }
        public int CurrentTurn { get; private set; }
        public bool IsStarted { get; private set; }
        public bool IsPaused { get; private set; }
        public double StateStartedAt => _stateStartedAt;

        public int ArrivedPlayerCount
        {
            get
            {
                var mask = _arrivedPlayerMask;
                var count = 0;
                while (mask != 0)
                {
                    count += mask & 1;
                    mask >>= 1;
                }

                return count;
            }
        }

        public void Start(double synchronizedNow, int startingTurn = 1)
        {
            ValidateTimestamp(synchronizedNow);
            if (startingTurn < 1)
                throw new ArgumentOutOfRangeException(nameof(startingTurn));

            ActionClock.Stop();
            IsStarted = true;
            IsPaused = false;
            CurrentTurn = startingTurn;
            State = BoardFlowState.TurnOverview;
            LastActionEndReason = BoardActionEndReason.None;
            _arrivedPlayerMask = 0;
            _stateStartedAt = synchronizedNow;
            _totalPausedDuration = 0d;
            _pauseStartedAt = 0d;
        }

        public void Tick(double synchronizedNow)
        {
            ValidateTimestamp(synchronizedNow);
            if (!IsStarted || IsPaused)
                return;

            var logicalNow = ToFlowTime(synchronizedNow);
            var keepAdvancing = true;
            var transitions = 0;

            while (keepAdvancing && transitions++ < TransitionSafetyLimit)
            {
                keepAdvancing = false;

                switch (State)
                {
                    case BoardFlowState.TurnOverview:
                    {
                        var boundary = _stateStartedAt + TurnOverviewDurationSeconds;
                        if (logicalNow >= boundary)
                        {
                            TransitionTo(BoardFlowState.Descending, boundary);
                            keepAdvancing = true;
                        }

                        break;
                    }
                    case BoardFlowState.Descending:
                    {
                        var boundary = _stateStartedAt + DescendingDurationSeconds;
                        if (logicalNow >= boundary)
                        {
                            BeginAction(boundary);
                            keepAdvancing = true;
                        }

                        break;
                    }
                    case BoardFlowState.Action:
                    {
                        ActionClock.Tick(logicalNow);
                        if (ActionClock.IsActionExpired(logicalNow))
                        {
                            var boundary =
                                ActionClock.StartedAt + GameplayPhaseClock.DefaultActionDurationSeconds;
                            EndAction(BoardActionEndReason.TimeExpired, boundary);
                            keepAdvancing = true;
                        }

                        break;
                    }
                    case BoardFlowState.AscendingResolve:
                    {
                        var boundary = _stateStartedAt + AscendingResolveDurationSeconds;
                        if (logicalNow >= boundary)
                        {
                            TransitionTo(BoardFlowState.CombatResolve, boundary);
                            keepAdvancing = true;
                        }

                        break;
                    }
                    case BoardFlowState.LandingEffectResolve:
                    {
                        var boundary = _stateStartedAt + LandingEffectResolveDurationSeconds;
                        if (logicalNow >= boundary)
                        {
                            TransitionTo(BoardFlowState.MinigameIntroReady, boundary);
                            keepAdvancing = true;
                        }

                        break;
                    }
                    case BoardFlowState.SkippedResult:
                    {
                        var boundary = _stateStartedAt + SkippedResultDurationSeconds;
                        if (logicalNow >= boundary)
                        {
                            BeginNextTurn(boundary);
                            keepAdvancing = true;
                        }

                        break;
                    }
                    case BoardFlowState.MinigameIntroReady:
                    case BoardFlowState.MinigameLoading:
                    case BoardFlowState.MinigamePlaying:
                    case BoardFlowState.CombatResolve:
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported board flow state.");
                }
            }

            if (transitions > TransitionSafetyLimit)
                throw new InvalidOperationException("Board flow exceeded its transition safety limit.");
        }

        public bool TryReportPlayerArrived(int playerSlot, double synchronizedNow)
        {
            ValidateTimestamp(synchronizedNow);
            if (playerSlot < 0 || playerSlot >= RequiredPlayerCount)
                throw new ArgumentOutOfRangeException(nameof(playerSlot));
            if (!IsStarted || IsPaused)
                return false;

            Tick(synchronizedNow);
            if (State != BoardFlowState.Action)
                return false;

            var bit = 1 << playerSlot;
            if ((_arrivedPlayerMask & bit) != 0)
                return false;

            _arrivedPlayerMask |= bit;
            if (_arrivedPlayerMask == AllPlayersMask)
                EndAction(BoardActionEndReason.AllPlayersArrived, ToFlowTime(synchronizedNow));

            return true;
        }

        public bool TrySkipMinigame(double synchronizedNow)
        {
            ValidateTimestamp(synchronizedNow);
            if (!IsStarted || IsPaused)
                return false;

            Tick(synchronizedNow);
            if (State != BoardFlowState.MinigameIntroReady)
                return false;

            // Development skip deliberately produces no minigame reward mutation.
            // TODO(BOARD-FLOW): replace this extension point with authoritative
            // minigame selection and result settlement.
            TransitionTo(BoardFlowState.SkippedResult, ToFlowTime(synchronizedNow));
            return true;
        }

        public bool TryBeginMinigameLoading(double synchronizedNow)
        {
            ValidateTimestamp(synchronizedNow);
            if (!IsStarted || IsPaused)
                return false;

            Tick(synchronizedNow);
            if (State != BoardFlowState.MinigameIntroReady)
                return false;

            TransitionTo(BoardFlowState.MinigameLoading, ToFlowTime(synchronizedNow));
            return true;
        }

        public bool TryBeginMinigame(double synchronizedNow)
        {
            ValidateTimestamp(synchronizedNow);
            if (!IsStarted || IsPaused)
                return false;

            Tick(synchronizedNow);
            if (State != BoardFlowState.MinigameLoading)
                return false;

            TransitionTo(BoardFlowState.MinigamePlaying, ToFlowTime(synchronizedNow));
            return true;
        }

        public bool TryCompleteMinigame(double synchronizedNow)
        {
            ValidateTimestamp(synchronizedNow);
            if (!IsStarted || IsPaused)
                return false;

            Tick(synchronizedNow);
            if (State != BoardFlowState.MinigamePlaying)
                return false;

            TransitionTo(BoardFlowState.SkippedResult, ToFlowTime(synchronizedNow));
            return true;
        }


        public bool TryCompleteCombat(double synchronizedNow)
        {
            ValidateTimestamp(synchronizedNow);
            if (!IsStarted || IsPaused)
            {
                return false;
            }

            Tick(synchronizedNow);
            if (State != BoardFlowState.CombatResolve)
            {
                return false;
            }

            TransitionTo(BoardFlowState.LandingEffectResolve, ToFlowTime(synchronizedNow));
            return true;
        }

        public bool Pause(double synchronizedNow)
        {
            ValidateTimestamp(synchronizedNow);
            if (!IsStarted || IsPaused)
                return false;

            Tick(synchronizedNow);
            IsPaused = true;
            _pauseStartedAt = synchronizedNow;
            return true;
        }

        public bool Resume(double synchronizedNow)
        {
            ValidateTimestamp(synchronizedNow);
            if (!IsStarted || !IsPaused)
                return false;
            if (synchronizedNow < _pauseStartedAt)
                throw new ArgumentOutOfRangeException(
                    nameof(synchronizedNow),
                    "Resume time cannot precede pause time.");

            _totalPausedDuration += synchronizedNow - _pauseStartedAt;
            _pauseStartedAt = 0d;
            IsPaused = false;
            return true;
        }

        public double ToFlowTime(double synchronizedNow)
        {
            ValidateTimestamp(synchronizedNow);
            var sampledServerTime = IsPaused ? _pauseStartedAt : synchronizedNow;
            return sampledServerTime - _totalPausedDuration;
        }

        public double GetStateRemaining(double synchronizedNow)
        {
            if (!IsStarted)
                return 0d;

            var logicalNow = ToFlowTime(synchronizedNow);
            switch (State)
            {
                case BoardFlowState.TurnOverview:
                    return Remaining(_stateStartedAt, TurnOverviewDurationSeconds, logicalNow);
                case BoardFlowState.Descending:
                    return Remaining(_stateStartedAt, DescendingDurationSeconds, logicalNow);
                case BoardFlowState.Action:
                    return ActionClock.GetActionRemaining(logicalNow);
                case BoardFlowState.CombatResolve:
                    return 0d;
                case BoardFlowState.AscendingResolve:
                    return Remaining(_stateStartedAt, AscendingResolveDurationSeconds, logicalNow);
                case BoardFlowState.LandingEffectResolve:
                    return Remaining(_stateStartedAt, LandingEffectResolveDurationSeconds, logicalNow);
                case BoardFlowState.SkippedResult:
                    return Remaining(_stateStartedAt, SkippedResultDurationSeconds, logicalNow);
                default:
                    return 0d;
            }
        }

        public double GetActionRemaining(double synchronizedNow)
        {
            return ActionClock.GetActionRemaining(ToFlowTime(synchronizedNow));
        }

        public double GetChoiceRemaining(double synchronizedNow)
        {
            return ActionClock.GetChoiceRemaining(ToFlowTime(synchronizedNow));
        }

        public double GetOpeningProtectionRemaining(double synchronizedNow)
        {
            return ActionClock.GetOpeningProtectionRemaining(ToFlowTime(synchronizedNow));
        }

        public bool IsOpeningProtectionActive(double synchronizedNow)
        {
            return ActionClock.IsOpeningProtectionActive(ToFlowTime(synchronizedNow));
        }

        private void BeginAction(double occurredAt)
        {
            _arrivedPlayerMask = 0;
            LastActionEndReason = BoardActionEndReason.None;

            // Descending completion is the single shared start timestamp for the
            // existing 180-second action, 30-second choice, and five-second shield.
            ActionClock.Start(occurredAt);
            TransitionTo(BoardFlowState.Action, occurredAt);
        }

        private void EndAction(BoardActionEndReason reason, double occurredAt)
        {
            if (ActionClock.IsChoicePending)
                ActionClock.TryChooseNoItem(occurredAt);

            ActionClock.Stop();
            LastActionEndReason = reason;
            TransitionTo(BoardFlowState.AscendingResolve, occurredAt);

            // TODO(BOARD-FLOW): authoritative forced movement and combat resolution
            // are extension points for AscendingResolve and are intentionally absent.
        }

        private void BeginNextTurn(double occurredAt)
        {
            CurrentTurn++;
            _arrivedPlayerMask = 0;
            LastActionEndReason = BoardActionEndReason.None;
            TransitionTo(BoardFlowState.TurnOverview, occurredAt);

            // TODO(BOARD-FLOW): enforce the 15-turn match ending rule here.
            // TODO(BOARD-FLOW): settle rewards/currency before starting the next turn.
        }

        private void TransitionTo(BoardFlowState next, double occurredAt)
        {
            var previous = State;
            State = next;
            _stateStartedAt = occurredAt;
            Transitioned?.Invoke(new BoardFlowTransition(previous, next, CurrentTurn, occurredAt));
        }

        private static double Remaining(double startedAt, double duration, double now)
        {
            return Math.Max(0d, startedAt + duration - now);
        }

        private static void ValidateTimestamp(double timestamp)
        {
            if (double.IsNaN(timestamp) || double.IsInfinity(timestamp))
                throw new ArgumentOutOfRangeException(nameof(timestamp));
        }
    }
}
