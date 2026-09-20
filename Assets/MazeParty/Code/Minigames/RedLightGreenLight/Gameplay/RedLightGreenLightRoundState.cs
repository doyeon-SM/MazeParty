using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MazeParty.Gameplay.Minigames.RedLightGreenLight
{
    public enum RedLightGreenLightMovementIntentStatus : byte
    {
        IgnoredRoundComplete,
        IgnoredTerminalPlayer,
        NoVoluntaryMovement,
        AllowedBySignal,
        AllowedDuringRedGrace,
        AlreadyPenalizedDuringCurrentRed,
        FirstViolation,
        Eliminated
    }

    public enum RedLightGreenLightRoundEndReason : byte
    {
        None,
        FirstFinisher,
        AllPlayersEliminated,
        TimeLimit
    }

    public readonly struct RedLightGreenLightMovementIntentResolution
    {
        internal RedLightGreenLightMovementIntentResolution(
            int playerSlot,
            RedLightGreenLightSignalPhase signalPhase,
            RedLightGreenLightMovementIntentStatus status,
            int violationCount,
            ulong serverEventOrder)
        {
            PlayerSlot = playerSlot;
            SignalPhase = signalPhase;
            Status = status;
            ViolationCount = violationCount;
            ServerEventOrder = serverEventOrder;
        }

        public int PlayerSlot { get; }
        public RedLightGreenLightSignalPhase SignalPhase { get; }
        public RedLightGreenLightMovementIntentStatus Status { get; }
        public int ViolationCount { get; }
        public ulong ServerEventOrder { get; }
        public bool WasViolation =>
            Status ==
                RedLightGreenLightMovementIntentStatus.FirstViolation ||
            Status == RedLightGreenLightMovementIntentStatus.Eliminated;
        public bool BecameWarned =>
            Status ==
            RedLightGreenLightMovementIntentStatus.FirstViolation;
        public bool BecameEliminated =>
            Status == RedLightGreenLightMovementIntentStatus.Eliminated;
    }

    /// <summary>
    /// Server-ready pure state for one round. Physics displacement is recorded
    /// only as progress; a penalty can be produced solely by explicitly
    /// submitting voluntary movement intent from the authoritative input path.
    /// </summary>
    public sealed class RedLightGreenLightRoundState
    {
        private readonly RedLightGreenLightPlayerRoundState[] _players;
        private readonly ReadOnlyCollection<
            RedLightGreenLightPlayerRoundState> _readOnlyPlayers;
        private readonly double[] _lastPenalizedRedStartsAt;
        private ulong _serverEventSequence;

        public RedLightGreenLightRoundState(
            ulong serverSeed,
            int roundNumber)
        {
            RedLightGreenLightRules.ValidateRoundNumber(roundNumber);
            ServerSeed = serverSeed;
            RoundNumber = roundNumber;
            SignalSchedule =
                RedLightGreenLightSignalScheduleGenerator.Generate(
                    serverSeed,
                    roundNumber);

            _players =
                new RedLightGreenLightPlayerRoundState[
                    RedLightGreenLightRules.PlayerCount];
            for (var playerSlot = 0;
                 playerSlot < _players.Length;
                 playerSlot++)
            {
                _players[playerSlot] =
                    new RedLightGreenLightPlayerRoundState(playerSlot);
            }

            _readOnlyPlayers = Array.AsReadOnly(_players);
            _lastPenalizedRedStartsAt =
                new double[RedLightGreenLightRules.PlayerCount];
            for (var playerSlot = 0;
                 playerSlot < _lastPenalizedRedStartsAt.Length;
                 playerSlot++)
            {
                _lastPenalizedRedStartsAt[playerSlot] =
                    double.NegativeInfinity;
            }
        }

        public ulong ServerSeed { get; }
        public int RoundNumber { get; }
        public RedLightGreenLightSignalSchedule SignalSchedule { get; }
        public IReadOnlyList<RedLightGreenLightPlayerRoundState>
            Players => _readOnlyPlayers;
        public RedLightGreenLightRoundEndReason EndReason {
            get;
            private set;
        }
        public bool IsComplete =>
            EndReason != RedLightGreenLightRoundEndReason.None;
        public RedLightGreenLightRoundResult Result { get; private set; }

        public RedLightGreenLightPlayerRoundState GetPlayer(
            int playerSlot)
        {
            if (!RedLightGreenLightRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerSlot),
                    playerSlot,
                    "Player slot must be between 0 and 3.");
            }

            return _players[playerSlot];
        }

        /// <summary>
        /// Updates current distance from the starting line. Calling this after
        /// an external push never counts as a Red violation.
        /// </summary>
        public bool SetForwardProgress(
            int playerSlot,
            float forwardProgressMeters)
        {
            var player = GetPlayer(playerSlot);
            if (IsComplete)
            {
                ValidateForwardProgress(forwardProgressMeters);
                return false;
            }

            return player.SetForwardProgress(forwardProgressMeters);
        }

        /// <summary>
        /// Evaluates only server-authoritative voluntary input. A false value
        /// can accompany arbitrary physics displacement without a penalty.
        /// </summary>
        public RedLightGreenLightMovementIntentResolution
            SubmitMovementIntent(
                int playerSlot,
                bool hasVoluntaryMovementIntent,
                double runningElapsedSeconds)
        {
            RedLightGreenLightRules.ValidateRunningElapsedSeconds(
                runningElapsedSeconds);
            var player = GetPlayer(playerSlot);
            var signalWindow =
                SignalSchedule.GetWindowAt(runningElapsedSeconds);

            if (IsComplete)
            {
                return CreateResolution(
                    player,
                    signalWindow.Phase,
                    RedLightGreenLightMovementIntentStatus
                        .IgnoredRoundComplete,
                    0UL);
            }

            if (player.IsTerminal)
            {
                return CreateResolution(
                    player,
                    signalWindow.Phase,
                    RedLightGreenLightMovementIntentStatus
                        .IgnoredTerminalPlayer,
                    0UL);
            }

            if (!hasVoluntaryMovementIntent)
            {
                return CreateResolution(
                    player,
                    signalWindow.Phase,
                    RedLightGreenLightMovementIntentStatus
                        .NoVoluntaryMovement,
                    0UL);
            }

            if (signalWindow.Phase !=
                RedLightGreenLightSignalPhase.Red)
            {
                return CreateResolution(
                    player,
                    signalWindow.Phase,
                    RedLightGreenLightMovementIntentStatus
                        .AllowedBySignal,
                    0UL);
            }

            if (!SignalSchedule.IsVoluntaryMovementViolationAt(
                    runningElapsedSeconds))
            {
                return CreateResolution(
                    player,
                    signalWindow.Phase,
                    RedLightGreenLightMovementIntentStatus
                        .AllowedDuringRedGrace,
                    0UL);
            }

            if (_lastPenalizedRedStartsAt[playerSlot].Equals(
                    signalWindow.StartsAtSeconds))
            {
                return CreateResolution(
                    player,
                    signalWindow.Phase,
                    RedLightGreenLightMovementIntentStatus
                        .AlreadyPenalizedDuringCurrentRed,
                    0UL);
            }

            var eventOrder = NextServerEventOrder();
            _lastPenalizedRedStartsAt[playerSlot] =
                signalWindow.StartsAtSeconds;
            var state = player.ApplyViolation();
            var status = state ==
                         RedLightGreenLightPlayerState.Eliminated
                ? RedLightGreenLightMovementIntentStatus.Eliminated
                : RedLightGreenLightMovementIntentStatus
                    .FirstViolation;
            var resolution = CreateResolution(
                player,
                signalWindow.Phase,
                status,
                eventOrder);

            if (AllPlayersEliminated())
            {
                CompleteRound(
                    RedLightGreenLightRoundEndReason
                        .AllPlayersEliminated);
            }

            return resolution;
        }

        /// <summary>
        /// The first authoritative finish call ends the round immediately.
        /// </summary>
        public bool TryFinish(
            int playerSlot,
            float forwardProgressMeters,
            double runningElapsedSeconds)
        {
            RedLightGreenLightRules.ValidateRunningElapsedSeconds(
                runningElapsedSeconds);
            var player = GetPlayer(playerSlot);
            if (IsComplete ||
                !player.TryFinish(forwardProgressMeters))
            {
                return false;
            }

            NextServerEventOrder();
            CompleteRound(
                RedLightGreenLightRoundEndReason.FirstFinisher);
            return true;
        }

        /// <summary>
        /// Ends the round once the running clock reaches 60 seconds.
        /// Countdown time is intentionally excluded by the caller.
        /// </summary>
        public bool TryEndForTimeout(double runningElapsedSeconds)
        {
            RedLightGreenLightRules.ValidateRunningElapsedSeconds(
                runningElapsedSeconds,
                true);
            if (IsComplete ||
                runningElapsedSeconds <
                RedLightGreenLightRules.RoundSeconds)
            {
                return false;
            }

            CompleteRound(
                RedLightGreenLightRoundEndReason.TimeLimit);
            return true;
        }

        private static RedLightGreenLightMovementIntentResolution
            CreateResolution(
                RedLightGreenLightPlayerRoundState player,
                RedLightGreenLightSignalPhase phase,
                RedLightGreenLightMovementIntentStatus status,
                ulong serverEventOrder)
        {
            return new RedLightGreenLightMovementIntentResolution(
                player.PlayerSlot,
                phase,
                status,
                player.ViolationCount,
                serverEventOrder);
        }

        private bool AllPlayersEliminated()
        {
            for (var index = 0; index < _players.Length; index++)
            {
                if (_players[index].State !=
                    RedLightGreenLightPlayerState.Eliminated)
                {
                    return false;
                }
            }

            return true;
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

        private void CompleteRound(
            RedLightGreenLightRoundEndReason endReason)
        {
            var outcomes =
                new RedLightGreenLightRoundOutcome[
                    RedLightGreenLightRules.PlayerCount];
            for (var playerSlot = 0;
                 playerSlot < _players.Length;
                 playerSlot++)
            {
                outcomes[playerSlot] =
                    _players[playerSlot].CaptureOutcome();
            }

            Result = RedLightGreenLightRoundScoring.Score(outcomes);
            EndReason = endReason;
        }

        private static void ValidateForwardProgress(
            float forwardProgressMeters)
        {
            if (float.IsNaN(forwardProgressMeters) ||
                float.IsInfinity(forwardProgressMeters) ||
                forwardProgressMeters < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(forwardProgressMeters));
            }
        }
    }
}
