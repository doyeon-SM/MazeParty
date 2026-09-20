using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.Race;
using MazeParty.Multiplayer;
using UnityEngine;

namespace MazeParty.Dev.MinigameSoloTest
{
    public enum RaceSoloPhase
    {
        Countdown,
        Running,
        RoundResult,
        Complete
    }

    /// <summary>
    /// Deterministic local practice simulation for the three-round race.
    /// Production networking remains server-authoritative.
    /// </summary>
    public sealed class RaceSoloSession
    {
        public const int LocalPlayerSlot = 0;

        private readonly int[] _progress = new int[RaceRules.PlayerCount];
        private readonly ulong[] _progressOrder =
            new ulong[RaceRules.PlayerCount];
        private readonly RaceStepInput[] _lastInputs =
            new RaceStepInput[RaceRules.PlayerCount];
        private readonly int[] _totalPoints =
            new int[RaceRules.PlayerCount];
        private readonly double[] _nextAiStepsAt =
            new double[RaceRules.PlayerCount];
        private readonly double[] _aiStepIntervals =
            new double[RaceRules.PlayerCount];

        private System.Random _random;
        private double _elapsed;
        private ulong _eventOrder;
        private IReadOnlyList<RaceRoundEntry> _roundLeaderboard;
        private IReadOnlyList<RaceLeaderboardEntry> _leaderboard;

        public int Seed { get; private set; }
        public int RoundNumber { get; private set; }
        public RaceSoloPhase Phase { get; private set; }
        public IReadOnlyList<RaceRoundEntry> RoundLeaderboard =>
            _roundLeaderboard;
        public IReadOnlyList<RaceLeaderboardEntry> Leaderboard =>
            _leaderboard;

        public double Remaining
        {
            get
            {
                switch (Phase)
                {
                    case RaceSoloPhase.Countdown:
                        return Math.Max(
                            0d,
                            NetworkRaceState.CountdownSeconds - _elapsed);
                    case RaceSoloPhase.Running:
                        return Math.Max(0d, RaceRules.RoundSeconds - _elapsed);
                    case RaceSoloPhase.RoundResult:
                        return Math.Max(
                            0d,
                            NetworkRaceState.RoundResultSeconds - _elapsed);
                    default:
                        return 0d;
                }
            }
        }

        public void Begin(int seed)
        {
            Seed = seed;
            _random = new System.Random(seed);
            Array.Clear(_totalPoints, 0, _totalPoints.Length);
            _leaderboard = null;
            RoundNumber = 1;
            BeginRound();
        }

        public void Tick(double deltaSeconds, RaceStepInput localInput)
        {
            if (deltaSeconds <= 0d || Phase == RaceSoloPhase.Complete)
            {
                return;
            }

            _elapsed += deltaSeconds;
            if (Phase == RaceSoloPhase.Countdown)
            {
                if (_elapsed >= NetworkRaceState.CountdownSeconds)
                {
                    _elapsed = 0d;
                    Phase = RaceSoloPhase.Running;
                }
                return;
            }

            if (Phase == RaceSoloPhase.RoundResult)
            {
                if (_elapsed < NetworkRaceState.RoundResultSeconds)
                {
                    return;
                }
                if (RoundNumber >= RaceRules.RoundCount)
                {
                    _leaderboard =
                        RaceRules.BuildFinalLeaderboard(_totalPoints);
                    Phase = RaceSoloPhase.Complete;
                    _elapsed = 0d;
                    return;
                }
                RoundNumber++;
                BeginRound();
                return;
            }

            if (localInput != RaceStepInput.None)
            {
                TryStep(LocalPlayerSlot, localInput);
            }
            if (Phase != RaceSoloPhase.Running)
            {
                return;
            }

            for (var slot = 1; slot < RaceRules.PlayerCount; slot++)
            {
                while (Phase == RaceSoloPhase.Running &&
                       _elapsed >= _nextAiStepsAt[slot])
                {
                    var input = _lastInputs[slot] == RaceStepInput.Left
                        ? RaceStepInput.Right
                        : RaceStepInput.Left;
                    TryStep(slot, input);
                    _nextAiStepsAt[slot] += _aiStepIntervals[slot];
                }
            }

            if (Phase == RaceSoloPhase.Running &&
                _elapsed >= RaceRules.RoundSeconds)
            {
                CompleteRound();
            }
        }

        public int GetProgress(int slot)
        {
            ValidateSlot(slot);
            return _progress[slot];
        }

        public int GetTotalPoints(int slot)
        {
            ValidateSlot(slot);
            return _totalPoints[slot];
        }

        private bool TryStep(int slot, RaceStepInput input)
        {
            if (Phase != RaceSoloPhase.Running ||
                !RaceRules.IsAlternatingStep(_lastInputs[slot], input))
            {
                return false;
            }

            _lastInputs[slot] = input;
            _progress[slot] = Math.Min(
                RaceRules.RequiredSteps,
                _progress[slot] + 1);
            _eventOrder++;
            _progressOrder[slot] = _eventOrder;
            if (_progress[slot] >= RaceRules.RequiredSteps)
            {
                CompleteRound();
            }
            return true;
        }

        private void BeginRound()
        {
            Array.Clear(_progress, 0, _progress.Length);
            Array.Clear(_progressOrder, 0, _progressOrder.Length);
            Array.Clear(_lastInputs, 0, _lastInputs.Length);
            _roundLeaderboard = null;
            _eventOrder = 0UL;
            _elapsed = 0d;
            Phase = RaceSoloPhase.Countdown;
            for (var slot = 1; slot < RaceRules.PlayerCount; slot++)
            {
                _aiStepIntervals[slot] =
                    0.09d + _random.NextDouble() * 0.025d + slot * 0.002d;
                _nextAiStepsAt[slot] = _aiStepIntervals[slot];
            }
        }

        private void CompleteRound()
        {
            if (Phase != RaceSoloPhase.Running)
            {
                return;
            }
            _roundLeaderboard = RaceRules.BuildRoundLeaderboard(
                _progress,
                _progressOrder);
            var roundPoints = RaceRules.BuildRoundPoints(_roundLeaderboard);
            for (var slot = 0; slot < RaceRules.PlayerCount; slot++)
            {
                _totalPoints[slot] += roundPoints[slot];
            }
            Phase = RaceSoloPhase.RoundResult;
            _elapsed = 0d;
            Debug.Log(
                "[Minigame Solo Test] Race round " + RoundNumber +
                " winner: P" +
                (_roundLeaderboard[0].PlayerSlot + 1) + ".");
        }

        private static void ValidateSlot(int slot)
        {
            if (!RaceRules.IsValidPlayerSlot(slot))
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }
    }
}
