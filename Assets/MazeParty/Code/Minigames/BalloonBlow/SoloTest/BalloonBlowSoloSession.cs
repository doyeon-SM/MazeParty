using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.BalloonBlow;

namespace MazeParty.Dev.MinigameSoloTest
{
    public enum BalloonBlowSoloPhase : byte
    {
        Countdown,
        Running,
        RoundResult,
        Complete
    }

    /// <summary>
    /// Network-free clock and deterministic practice-player adapter around the
    /// production Balloon Blow rules. Input remains edge-safe because the pure
    /// round state owns held state, forced release rearming and cooldowns.
    /// </summary>
    public sealed class BalloonBlowSoloSession
    {
        public const int LocalPlayerSlot = 0;
        public const double CountdownSeconds = 3d;
        public const double ResultSeconds = 4d;

        private readonly BalloonBlowRoundResult[] _roundResults =
            new BalloonBlowRoundResult[BalloonBlowRules.RoundCount];
        private readonly bool[] _practiceHolding =
            new bool[BalloonBlowRules.PlayerCount];
        private readonly double[] _practiceNextTransitionAt =
            new double[BalloonBlowRules.PlayerCount];
        private readonly double[] _practiceHoldSeconds =
            new double[BalloonBlowRules.PlayerCount];
        private readonly double[] _practiceRestSeconds =
            new double[BalloonBlowRules.PlayerCount];

        private IReadOnlyList<BalloonBlowLeaderboardEntry> _leaderboard;

        public int Seed { get; private set; }
        public int RoundNumber { get; private set; }
        public BalloonBlowSoloPhase Phase { get; private set; }
        public double RemainingSeconds { get; private set; }
        public double RunningElapsedSeconds { get; private set; }
        public BalloonBlowRoundState RoundState { get; private set; }
        public IReadOnlyList<BalloonBlowLeaderboardEntry> Leaderboard =>
            _leaderboard;
        public BalloonBlowPlayerRoundState LocalPlayer =>
            RoundState?.GetPlayer(LocalPlayerSlot);

        public void Begin(int seed)
        {
            Seed = seed;
            Array.Clear(_roundResults, 0, _roundResults.Length);
            _leaderboard = null;
            RoundNumber = 1;
            EnterCountdown();
        }

        public void Tick(float unscaledDeltaTime)
        {
            if (float.IsNaN(unscaledDeltaTime) ||
                float.IsInfinity(unscaledDeltaTime))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(unscaledDeltaTime));
            }

            var deltaTime = Math.Max(0d, unscaledDeltaTime);
            switch (Phase)
            {
                case BalloonBlowSoloPhase.Countdown:
                    RemainingSeconds = Math.Max(
                        0d,
                        RemainingSeconds - deltaTime);
                    if (RemainingSeconds <= 0d)
                    {
                        Phase = BalloonBlowSoloPhase.Running;
                        RunningElapsedSeconds = 0d;
                        RemainingSeconds = BalloonBlowRules.RoundSeconds;
                    }
                    break;

                case BalloonBlowSoloPhase.Running:
                {
                    var target = Math.Min(
                        BalloonBlowRules.RoundSeconds,
                        RunningElapsedSeconds + deltaTime);
                    AdvancePracticePlayersTo(target);
                    RoundState.AdvanceTo(target);
                    RunningElapsedSeconds = RoundState.ElapsedSeconds;
                    RemainingSeconds = Math.Max(
                        0d,
                        BalloonBlowRules.RoundSeconds -
                        RunningElapsedSeconds);
                    if (RoundState.IsComplete)
                    {
                        EnterRoundResult();
                    }
                    break;
                }

                case BalloonBlowSoloPhase.RoundResult:
                    RemainingSeconds = Math.Max(
                        0d,
                        RemainingSeconds - deltaTime);
                    if (RemainingSeconds <= 0d)
                    {
                        AdvanceAfterRoundResult();
                    }
                    break;
            }
        }

        public BalloonBlowInputResolution SetLocalInflateHeld(bool isHeld)
        {
            EnsureStarted();
            if (Phase != BalloonBlowSoloPhase.Running)
            {
                var local = RoundState.GetPlayer(LocalPlayerSlot);
                return new BalloonBlowInputResolution();
            }

            return RoundState.SetInflateHeld(
                LocalPlayerSlot,
                isHeld,
                RunningElapsedSeconds);
        }

        public BalloonBlowPlayerRoundState GetPlayer(int playerSlot)
        {
            EnsureStarted();
            return RoundState.GetPlayer(playerSlot);
        }

        public void RestartCurrentRound()
        {
            EnsureStarted();
            for (var index = RoundNumber - 1;
                 index < _roundResults.Length;
                 index++)
            {
                _roundResults[index] = null;
            }
            _leaderboard = null;
            EnterCountdown();
        }

        public BalloonBlowRoundResult GetRoundResult(int roundNumber)
        {
            if (roundNumber < 1 ||
                roundNumber > BalloonBlowRules.RoundCount)
            {
                throw new ArgumentOutOfRangeException(nameof(roundNumber));
            }
            return _roundResults[roundNumber - 1];
        }

        private void AdvancePracticePlayersTo(double targetSeconds)
        {
            while (true)
            {
                var nextAt = double.PositiveInfinity;
                for (var slot = 1;
                     slot < BalloonBlowRules.PlayerCount;
                     slot++)
                {
                    if (_practiceNextTransitionAt[slot] < nextAt)
                    {
                        nextAt = _practiceNextTransitionAt[slot];
                    }
                }

                if (nextAt > targetSeconds)
                {
                    return;
                }

                for (var slot = 1;
                     slot < BalloonBlowRules.PlayerCount;
                     slot++)
                {
                    if (Math.Abs(
                            _practiceNextTransitionAt[slot] - nextAt) >
                        0.0000001d)
                    {
                        continue;
                    }

                    var player = RoundState.GetPlayer(slot);
                    if (player.IsPopped)
                    {
                        _practiceNextTransitionAt[slot] =
                            double.PositiveInfinity;
                        continue;
                    }

                    _practiceHolding[slot] = !_practiceHolding[slot];
                    RoundState.SetInflateHeld(
                        slot,
                        _practiceHolding[slot],
                        nextAt);
                    _practiceNextTransitionAt[slot] = nextAt +
                        (_practiceHolding[slot]
                            ? _practiceHoldSeconds[slot]
                            : _practiceRestSeconds[slot]);
                }
            }
        }

        private void EnterCountdown()
        {
            RoundState = new BalloonBlowRoundState(RoundNumber);
            Phase = BalloonBlowSoloPhase.Countdown;
            RemainingSeconds = CountdownSeconds;
            RunningElapsedSeconds = 0d;
            ConfigurePracticePlayers();
        }

        private void ConfigurePracticePlayers()
        {
            Array.Clear(
                _practiceHolding,
                0,
                _practiceHolding.Length);
            var random = new Random(unchecked(
                Seed ^ (RoundNumber * 7919)));

            _practiceNextTransitionAt[0] = double.PositiveInfinity;
            _practiceHoldSeconds[0] = 0d;
            _practiceRestSeconds[0] = 0d;

            _practiceNextTransitionAt[1] =
                0.25d + random.NextDouble() * 0.2d;
            _practiceHoldSeconds[1] = 1.65d;
            _practiceRestSeconds[1] = 1.05d;

            _practiceNextTransitionAt[2] =
                0.55d + random.NextDouble() * 0.2d;
            _practiceHoldSeconds[2] = 1.85d;
            _practiceRestSeconds[2] = 1.05d;

            // Practice player four deliberately crosses the two-second limit,
            // demonstrating forced stop, 1.5-second cooldown and release rearm.
            _practiceNextTransitionAt[3] =
                0.85d + random.NextDouble() * 0.2d;
            _practiceHoldSeconds[3] = 2.05d;
            _practiceRestSeconds[3] = 1.55d;
        }

        private void EnterRoundResult()
        {
            if (RoundState == null || !RoundState.IsComplete)
            {
                throw new InvalidOperationException(
                    "The Balloon Blow round must complete before results.");
            }

            _roundResults[RoundNumber - 1] = RoundState.Result;
            Phase = BalloonBlowSoloPhase.RoundResult;
            RemainingSeconds = ResultSeconds;
        }

        private void AdvanceAfterRoundResult()
        {
            if (RoundNumber >= BalloonBlowRules.RoundCount)
            {
                _leaderboard = BalloonBlowMatchScoring.BuildLeaderboard(
                    _roundResults);
                Phase = BalloonBlowSoloPhase.Complete;
                RemainingSeconds = 0d;
                return;
            }

            RoundNumber++;
            EnterCountdown();
        }

        private void EnsureStarted()
        {
            if (RoundState == null ||
                RoundNumber < 1 ||
                RoundNumber > BalloonBlowRules.RoundCount)
            {
                throw new InvalidOperationException(
                    "Start the Balloon Blow solo session first.");
            }
        }
    }
}
