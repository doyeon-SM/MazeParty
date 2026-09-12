using System;

namespace MazeParty.Gameplay.Minigames.BalloonBlow
{
    public enum BalloonBlowPlayerPhase : byte
    {
        Ready,
        Inflating,
        Cooldown,
        AwaitingRelease,
        Popped
    }

    public enum BalloonBlowInputStatus : byte
    {
        Pressed,
        Released,
        Unchanged,
        IgnoredPopped,
        IgnoredRoundComplete
    }

    public enum BalloonBlowRoundEndReason : byte
    {
        None,
        AllPopped,
        TimeLimit
    }

    public readonly struct BalloonBlowInputResolution
    {
        internal BalloonBlowInputResolution(
            int playerSlot,
            BalloonBlowInputStatus status,
            BalloonBlowPlayerPhase phase,
            float progressPercent,
            double cooldownEndsAtSeconds)
        {
            PlayerSlot = playerSlot;
            Status = status;
            Phase = phase;
            ProgressPercent = progressPercent;
            CooldownEndsAtSeconds = cooldownEndsAtSeconds;
        }

        public int PlayerSlot { get; }
        public BalloonBlowInputStatus Status { get; }
        public BalloonBlowPlayerPhase Phase { get; }
        public float ProgressPercent { get; }
        public double CooldownEndsAtSeconds { get; }
        public bool WasChanged =>
            Status == BalloonBlowInputStatus.Pressed ||
            Status == BalloonBlowInputStatus.Released;
    }

    public sealed class BalloonBlowPlayerRoundState
    {
        private const double TimeEpsilon = 0.000000001d;
        private const double ProgressEpsilon = 0.000001d;

        private double _observedAtSeconds;
        private double _progressPercent;
        private double _continuousInflateSeconds;
        private double _cooldownEndsAtSeconds;
        private bool _isInflateHeld;
        private bool _requiresReleaseToRearm;

        internal BalloonBlowPlayerRoundState(int playerSlot)
        {
            PlayerSlot = playerSlot;
            Phase = BalloonBlowPlayerPhase.Ready;
        }

        public int PlayerSlot { get; }
        public BalloonBlowPlayerPhase Phase { get; private set; }
        public float ProgressPercent => (float)_progressPercent;
        public bool IsInflateHeld => _isInflateHeld;
        public bool IsPopped => Phase == BalloonBlowPlayerPhase.Popped;
        public bool RequiresReleaseToRearm =>
            _requiresReleaseToRearm;
        public double ContinuousInflateSeconds =>
            _continuousInflateSeconds;
        public double CooldownEndsAtSeconds => _cooldownEndsAtSeconds;
        public double CooldownRemainingSeconds => Math.Max(
            0d,
            _cooldownEndsAtSeconds - _observedAtSeconds);
        public double PoppedAtSeconds { get; private set; }
        public ulong PopOrder { get; private set; }

        internal void AdvanceTo(
            double activeElapsedSeconds,
            ref ulong nextServerEventOrder)
        {
            if (IsPopped)
            {
                return;
            }

            while (_observedAtSeconds + TimeEpsilon <
                   activeElapsedSeconds)
            {
                if (_cooldownEndsAtSeconds >
                    _observedAtSeconds + TimeEpsilon)
                {
                    Phase = BalloonBlowPlayerPhase.Cooldown;
                    var segmentEnd = Math.Min(
                        activeElapsedSeconds,
                        _cooldownEndsAtSeconds);
                    Deflate(segmentEnd - _observedAtSeconds);
                    _observedAtSeconds = segmentEnd;
                    continue;
                }

                if (_requiresReleaseToRearm)
                {
                    Phase = BalloonBlowPlayerPhase.AwaitingRelease;
                    Deflate(
                        activeElapsedSeconds - _observedAtSeconds);
                    _observedAtSeconds = activeElapsedSeconds;
                    break;
                }

                if (!_isInflateHeld)
                {
                    Phase = BalloonBlowPlayerPhase.Ready;
                    Deflate(
                        activeElapsedSeconds - _observedAtSeconds);
                    _observedAtSeconds = activeElapsedSeconds;
                    break;
                }

                Phase = BalloonBlowPlayerPhase.Inflating;
                var timeUntilForcedStop =
                    BalloonBlowRules.MaxContinuousInflateSeconds -
                    _continuousInflateSeconds;
                var timeUntilPop =
                    (BalloonBlowRules.MaxProgressPercent -
                     _progressPercent) /
                    BalloonBlowRules.InflatePercentPerSecond;
                var duration = Math.Min(
                    activeElapsedSeconds - _observedAtSeconds,
                    Math.Min(timeUntilForcedStop, timeUntilPop));

                if (duration > TimeEpsilon)
                {
                    _progressPercent +=
                        duration *
                        BalloonBlowRules.InflatePercentPerSecond;
                    _continuousInflateSeconds += duration;
                    _observedAtSeconds += duration;
                }

                if (_progressPercent >=
                    BalloonBlowRules.MaxProgressPercent -
                    ProgressEpsilon)
                {
                    Pop(ref nextServerEventOrder);
                    return;
                }

                if (_continuousInflateSeconds >=
                    BalloonBlowRules.MaxContinuousInflateSeconds -
                    TimeEpsilon)
                {
                    ForceStopForOverhold();
                    continue;
                }

                // The remaining segment is below the simulation tolerance.
                _observedAtSeconds = activeElapsedSeconds;
            }

            RefreshPhase();
        }

        internal bool SetInflateHeld(
            bool isHeld,
            double activeElapsedSeconds)
        {
            if (_isInflateHeld == isHeld)
            {
                return false;
            }

            if (isHeld)
            {
                _isInflateHeld = true;
                _continuousInflateSeconds = 0d;
                RefreshPhase();
                return true;
            }

            var wasInflating =
                Phase == BalloonBlowPlayerPhase.Inflating;
            _isInflateHeld = false;
            _requiresReleaseToRearm = false;
            _continuousInflateSeconds = 0d;
            if (wasInflating)
            {
                _cooldownEndsAtSeconds =
                    activeElapsedSeconds +
                    BalloonBlowRules.ReleaseCooldownSeconds;
            }

            RefreshPhase();
            return true;
        }

        internal void InterruptInflationSession()
        {
            _isInflateHeld = false;
            _continuousInflateSeconds = 0d;
            _requiresReleaseToRearm = false;
            RefreshPhase();
        }

        internal BalloonBlowRoundOutcome CaptureOutcome()
        {
            return IsPopped
                ? BalloonBlowRoundOutcome.Popped(
                    PlayerSlot,
                    PoppedAtSeconds,
                    PopOrder)
                : BalloonBlowRoundOutcome.Incomplete(
                    PlayerSlot,
                    ProgressPercent);
        }

        private void ForceStopForOverhold()
        {
            _continuousInflateSeconds = 0d;
            _requiresReleaseToRearm = true;
            _cooldownEndsAtSeconds =
                _observedAtSeconds +
                BalloonBlowRules.OverholdCooldownSeconds;
            Phase = BalloonBlowPlayerPhase.Cooldown;
        }

        private void Pop(ref ulong nextServerEventOrder)
        {
            if (nextServerEventOrder == 0UL)
            {
                throw new InvalidOperationException(
                    "Server pop order was exhausted.");
            }

            _progressPercent = BalloonBlowRules.MaxProgressPercent;
            PoppedAtSeconds = _observedAtSeconds;
            PopOrder = nextServerEventOrder;
            nextServerEventOrder++;
            _continuousInflateSeconds = 0d;
            _isInflateHeld = false;
            _requiresReleaseToRearm = false;
            _cooldownEndsAtSeconds = 0d;
            Phase = BalloonBlowPlayerPhase.Popped;
        }

        private void Deflate(double durationSeconds)
        {
            _progressPercent = Math.Max(
                BalloonBlowRules.MinimumProgressPercent,
                _progressPercent -
                (durationSeconds *
                 BalloonBlowRules.DeflatePercentPerSecond));
        }

        private void RefreshPhase()
        {
            if (IsPopped)
            {
                return;
            }

            if (_cooldownEndsAtSeconds >
                _observedAtSeconds + TimeEpsilon)
            {
                Phase = BalloonBlowPlayerPhase.Cooldown;
            }
            else if (_requiresReleaseToRearm)
            {
                Phase = BalloonBlowPlayerPhase.AwaitingRelease;
            }
            else
            {
                Phase = _isInflateHeld
                    ? BalloonBlowPlayerPhase.Inflating
                    : BalloonBlowPlayerPhase.Ready;
            }
        }
    }

    /// <summary>
    /// Pure authoritative input, timing, popping and scoring state for one round.
    /// The caller supplies monotonically increasing active-round timestamps.
    /// </summary>
    public sealed class BalloonBlowRoundState
    {
        private const double TimeEpsilon = 0.000000001d;

        private readonly BalloonBlowPlayerRoundState[] _players;
        private ulong _nextServerEventOrder = 1UL;

        public BalloonBlowRoundState(int roundNumber)
        {
            BalloonBlowRules.ValidateRoundNumber(roundNumber);
            RoundNumber = roundNumber;
            _players = new BalloonBlowPlayerRoundState[
                BalloonBlowRules.PlayerCount];
            for (var playerSlot = 0;
                 playerSlot < _players.Length;
                 playerSlot++)
            {
                _players[playerSlot] =
                    new BalloonBlowPlayerRoundState(playerSlot);
            }
        }

        public int RoundNumber { get; }
        public double ElapsedSeconds { get; private set; }
        public bool IsComplete { get; private set; }
        public BalloonBlowRoundEndReason EndReason { get; private set; }
        public BalloonBlowRoundResult Result { get; private set; }

        public int PoppedPlayerCount
        {
            get
            {
                var count = 0;
                for (var index = 0; index < _players.Length; index++)
                {
                    if (_players[index].IsPopped)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public BalloonBlowPlayerRoundState GetPlayer(int playerSlot)
        {
            if (!BalloonBlowRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerSlot),
                    playerSlot,
                    "Player slot must be between 0 and 3.");
            }

            return _players[playerSlot];
        }

        public BalloonBlowInputResolution SetInflateHeld(
            int playerSlot,
            bool isHeld,
            double activeElapsedSeconds)
        {
            var player = GetPlayer(playerSlot);
            AdvanceTo(activeElapsedSeconds);
            if (IsComplete)
            {
                return ResolveInput(
                    player,
                    BalloonBlowInputStatus.IgnoredRoundComplete);
            }

            if (player.IsPopped)
            {
                return ResolveInput(
                    player,
                    BalloonBlowInputStatus.IgnoredPopped);
            }

            if (!player.SetInflateHeld(
                    isHeld,
                    ElapsedSeconds))
            {
                return ResolveInput(
                    player,
                    BalloonBlowInputStatus.Unchanged);
            }

            return ResolveInput(
                player,
                isHeld
                    ? BalloonBlowInputStatus.Pressed
                    : BalloonBlowInputStatus.Released);
        }

        public void AdvanceTo(double activeElapsedSeconds)
        {
            BalloonBlowRules.ValidateActiveElapsedSeconds(
                activeElapsedSeconds);
            if (activeElapsedSeconds + TimeEpsilon < ElapsedSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(activeElapsedSeconds),
                    activeElapsedSeconds,
                    "Active elapsed time cannot move backwards.");
            }

            if (IsComplete)
            {
                return;
            }

            var targetSeconds = Math.Min(
                activeElapsedSeconds,
                BalloonBlowRules.RoundSeconds);
            if (targetSeconds < ElapsedSeconds)
            {
                targetSeconds = ElapsedSeconds;
            }

            for (var index = 0; index < _players.Length; index++)
            {
                _players[index].AdvanceTo(
                    targetSeconds,
                    ref _nextServerEventOrder);
            }

            ElapsedSeconds = targetSeconds;
            if (PoppedPlayerCount == BalloonBlowRules.PlayerCount)
            {
                var completedAtSeconds = 0d;
                for (var index = 0; index < _players.Length; index++)
                {
                    completedAtSeconds = Math.Max(
                        completedAtSeconds,
                        _players[index].PoppedAtSeconds);
                }

                ElapsedSeconds = completedAtSeconds;
                Complete(BalloonBlowRoundEndReason.AllPopped);
            }
            else if (targetSeconds >= BalloonBlowRules.RoundSeconds)
            {
                Complete(BalloonBlowRoundEndReason.TimeLimit);
            }
        }

        /// <summary>
        /// Neutrally ends every held-input session for a server pause or
        /// disconnect. Progress and an already-running cooldown are preserved,
        /// and no release cooldown is created. The transport layer should still
        /// require a fresh physical release before accepting another press.
        /// </summary>
        public void InterruptHeldInputs(double activeElapsedSeconds)
        {
            AdvanceTo(activeElapsedSeconds);
            if (IsComplete)
            {
                return;
            }

            for (var index = 0; index < _players.Length; index++)
            {
                if (!_players[index].IsPopped)
                {
                    _players[index].InterruptInflationSession();
                }
            }
        }

        public bool TryEndForTimeout(double activeElapsedSeconds)
        {
            var wasComplete = IsComplete;
            AdvanceTo(activeElapsedSeconds);
            return !wasComplete &&
                IsComplete &&
                EndReason == BalloonBlowRoundEndReason.TimeLimit;
        }

        private static BalloonBlowInputResolution ResolveInput(
            BalloonBlowPlayerRoundState player,
            BalloonBlowInputStatus status)
        {
            return new BalloonBlowInputResolution(
                player.PlayerSlot,
                status,
                player.Phase,
                player.ProgressPercent,
                player.CooldownEndsAtSeconds);
        }

        private void Complete(BalloonBlowRoundEndReason reason)
        {
            var outcomes = new BalloonBlowRoundOutcome[
                BalloonBlowRules.PlayerCount];
            for (var index = 0; index < _players.Length; index++)
            {
                outcomes[index] = _players[index].CaptureOutcome();
            }

            Result = BalloonBlowRoundScoring.Score(outcomes);
            EndReason = reason;
            IsComplete = true;
        }
    }
}
