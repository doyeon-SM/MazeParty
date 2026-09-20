using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MazeParty.Gameplay.Minigames.WrongWay
{
    /// <summary>
    /// Server-ready pure state for one WrongWay round. Every participant reads
    /// from the same prompt sequence but advances through it independently.
    /// </summary>
    public sealed class WrongWayRoundState
    {
        private readonly WrongWayDirection[] _prompts;
        private readonly ReadOnlyCollection<WrongWayDirection>
            _readOnlyPrompts;
        private readonly WrongWayPlayerRoundState[] _players;
        private readonly ReadOnlyCollection<WrongWayPlayerRoundState>
            _readOnlyPlayers;

        private ulong _serverEventSequence;

        public WrongWayRoundState(
            ulong serverSeed,
            int roundNumber)
        {
            WrongWayRules.ValidateRoundNumber(roundNumber);

            ServerSeed = serverSeed;
            RoundNumber = roundNumber;
            _prompts = WrongWayPromptGenerator.Generate(
                serverSeed,
                roundNumber);
            _readOnlyPrompts = Array.AsReadOnly(_prompts);

            _players =
                new WrongWayPlayerRoundState[
                    WrongWayRules.PlayerCount];
            for (var playerSlot = 0;
                 playerSlot < _players.Length;
                 playerSlot++)
            {
                _players[playerSlot] =
                    new WrongWayPlayerRoundState(playerSlot);
            }

            _readOnlyPlayers = Array.AsReadOnly(_players);
        }

        public ulong ServerSeed { get; }
        public int RoundNumber { get; }
        public IReadOnlyList<WrongWayDirection> Prompts =>
            _readOnlyPrompts;
        public IReadOnlyList<WrongWayPlayerRoundState> Players =>
            _readOnlyPlayers;
        public WrongWayRoundEndReason EndReason { get; private set; }
        public bool IsComplete =>
            EndReason != WrongWayRoundEndReason.None;
        public WrongWayRoundResult Result { get; private set; }

        /// <summary>
        /// The next prompt for a participant, or null after all 50 steps.
        /// </summary>
        public WrongWayDirection? GetPromptForSlot(int playerSlot)
        {
            var player = GetPlayer(playerSlot);
            return player.IsFinished
                ? (WrongWayDirection?)null
                : _prompts[player.CompletedSteps];
        }

        /// <summary>
        /// Applies an input in authoritative call order. Incorrect inputs leave
        /// both progress and the current prompt unchanged, then lock input for
        /// exactly 0.5 seconds of server time.
        /// </summary>
        public WrongWayInputResolution SubmitInput(
            int playerSlot,
            WrongWayDirection submittedDirection,
            double serverNow)
        {
            var player = GetPlayer(playerSlot);
            var expectedDirection =
                GetExpectedDirectionForResolution(player);

            if (IsComplete)
            {
                return new WrongWayInputResolution(
                    playerSlot,
                    submittedDirection,
                    expectedDirection,
                    WrongWayInputStatus.IgnoredRoundComplete,
                    player.CompletedSteps,
                    player.CompletedSteps,
                    player.InputLockedUntil,
                    0UL);
            }

            var eventOrder = NextServerEventOrder();
            var resolution = player.ApplyInput(
                submittedDirection,
                expectedDirection,
                serverNow,
                eventOrder);

            if (resolution.FinishedRound)
            {
                CompleteRound(
                    WrongWayRoundEndReason.FirstFinisher);
            }

            return resolution;
        }

        /// <summary>
        /// Ends the round once its running elapsed time reaches 60 seconds.
        /// Countdown time is intentionally excluded by the caller.
        /// </summary>
        public bool TryEndForTimeout(double runningElapsedSeconds)
        {
            if (double.IsNaN(runningElapsedSeconds) ||
                double.IsInfinity(runningElapsedSeconds) ||
                runningElapsedSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(runningElapsedSeconds),
                    runningElapsedSeconds,
                    "Elapsed time must be a finite non-negative value.");
            }

            if (IsComplete ||
                runningElapsedSeconds < WrongWayRules.RoundSeconds)
            {
                return false;
            }

            CompleteRound(WrongWayRoundEndReason.TimeLimit);
            return true;
        }

        public WrongWayPlayerRoundState GetPlayer(int playerSlot)
        {
            if (!WrongWayRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerSlot),
                    playerSlot,
                    "Player slot must be between 0 and 3.");
            }

            return _players[playerSlot];
        }

        private WrongWayDirection
            GetExpectedDirectionForResolution(
                WrongWayPlayerRoundState player)
        {
            var promptIndex = player.IsFinished
                ? WrongWayRules.StepCount - 1
                : player.CompletedSteps;
            return _prompts[promptIndex];
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
            WrongWayRoundEndReason endReason)
        {
            var outcomes =
                new WrongWayRoundOutcome[
                    WrongWayRules.PlayerCount];
            for (var playerSlot = 0;
                 playerSlot < _players.Length;
                 playerSlot++)
            {
                outcomes[playerSlot] =
                    _players[playerSlot].CaptureOutcome();
            }

            Result = WrongWayRoundScoring.Score(outcomes);
            EndReason = endReason;
        }
    }
}
