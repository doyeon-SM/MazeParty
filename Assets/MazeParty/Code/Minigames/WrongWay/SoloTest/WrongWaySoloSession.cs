using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.WrongWay;

namespace MazeParty.Dev.MinigameSoloTest
{
    public enum WrongWaySoloPhase : byte
    {
        Countdown,
        Running,
        RoundResult,
        Complete
    }

    /// <summary>
    /// Local one-player clock and match wrapper around the production
    /// WrongWayRoundState. The three unused slots intentionally remain idle so
    /// the same four-player scoring and prompt rules are exercised offline.
    /// </summary>
    public sealed class WrongWaySoloSession
    {
        public const float RoundResultSeconds = 3f;
        public const int LocalPlayerSlot = 0;

        private readonly WrongWayRoundResult[] _roundResults =
            new WrongWayRoundResult[WrongWayRules.RoundCount];

        private IReadOnlyList<WrongWayLeaderboardEntry> _leaderboard;

        public int Seed { get; private set; }
        public int RoundNumber { get; private set; }
        public WrongWaySoloPhase Phase { get; private set; }
        public float RemainingSeconds { get; private set; }
        public double RunningElapsedSeconds { get; private set; }
        public WrongWayRoundState RoundState { get; private set; }
        public WrongWayInputResolution? LastInputResolution { get; private set; }
        public IReadOnlyList<WrongWayLeaderboardEntry> Leaderboard =>
            _leaderboard;

        public int CompletedSteps =>
            RoundState != null
                ? RoundState.GetPlayer(LocalPlayerSlot).CompletedSteps
                : 0;

        public WrongWayDirection? CurrentPrompt =>
            RoundState != null
                ? RoundState.GetPromptForSlot(LocalPlayerSlot)
                : null;

        public bool IsInputLocked =>
            Phase == WrongWaySoloPhase.Running &&
            RoundState != null &&
            RoundState.GetPlayer(LocalPlayerSlot).IsInputLocked(
                RunningElapsedSeconds);

        public float InputLockSecondsRemaining
        {
            get
            {
                if (!IsInputLocked)
                {
                    return 0f;
                }

                return (float)Math.Max(
                    0d,
                    RoundState.GetPlayer(LocalPlayerSlot)
                        .InputLockedUntil - RunningElapsedSeconds);
            }
        }

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

            var deltaTime = Math.Max(0f, unscaledDeltaTime);
            switch (Phase)
            {
                case WrongWaySoloPhase.Countdown:
                    RemainingSeconds = Math.Max(
                        0f,
                        RemainingSeconds - deltaTime);
                    if (RemainingSeconds <= 0f)
                    {
                        Phase = WrongWaySoloPhase.Running;
                        RunningElapsedSeconds = 0d;
                        RemainingSeconds =
                            (float)WrongWayRules.RoundSeconds;
                    }
                    break;
                case WrongWaySoloPhase.Running:
                    RunningElapsedSeconds = Math.Min(
                        WrongWayRules.RoundSeconds,
                        RunningElapsedSeconds + deltaTime);
                    RemainingSeconds = (float)Math.Max(
                        0d,
                        WrongWayRules.RoundSeconds -
                        RunningElapsedSeconds);
                    if (RoundState.TryEndForTimeout(
                            RunningElapsedSeconds))
                    {
                        EnterRoundResult();
                    }
                    break;
                case WrongWaySoloPhase.RoundResult:
                    RemainingSeconds = Math.Max(
                        0f,
                        RemainingSeconds - deltaTime);
                    if (RemainingSeconds <= 0f)
                    {
                        AdvanceAfterRoundResult();
                    }
                    break;
            }
        }

        public bool TrySubmitDirection(
            WrongWayDirection direction,
            out WrongWayInputResolution resolution)
        {
            resolution = default;
            if (Phase != WrongWaySoloPhase.Running ||
                RoundState == null)
            {
                return false;
            }

            resolution = RoundState.SubmitInput(
                LocalPlayerSlot,
                direction,
                RunningElapsedSeconds);
            LastInputResolution = resolution;
            if (RoundState.IsComplete)
            {
                EnterRoundResult();
            }

            return true;
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

        public WrongWayRoundResult GetRoundResult(int roundNumber)
        {
            if (roundNumber < 1 ||
                roundNumber > WrongWayRules.RoundCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(roundNumber));
            }

            return _roundResults[roundNumber - 1];
        }

        private void EnterCountdown()
        {
            RoundState = new WrongWayRoundState(
                unchecked((ulong)(uint)Seed),
                RoundNumber);
            Phase = WrongWaySoloPhase.Countdown;
            RemainingSeconds =
                (float)WrongWayRules.CountdownSeconds;
            RunningElapsedSeconds = 0d;
            LastInputResolution = null;
        }

        private void EnterRoundResult()
        {
            if (RoundState == null || !RoundState.IsComplete)
            {
                throw new InvalidOperationException(
                    "A WrongWay round must be complete before showing results.");
            }

            _roundResults[RoundNumber - 1] = RoundState.Result;
            Phase = WrongWaySoloPhase.RoundResult;
            RemainingSeconds = RoundResultSeconds;
        }

        private void AdvanceAfterRoundResult()
        {
            if (RoundNumber >= WrongWayRules.RoundCount)
            {
                _leaderboard =
                    WrongWayMatchScoring.BuildLeaderboard(_roundResults);
                Phase = WrongWaySoloPhase.Complete;
                RemainingSeconds = 0f;
                return;
            }

            RoundNumber++;
            EnterCountdown();
        }

        private void EnsureStarted()
        {
            if (RoundState == null ||
                RoundNumber < 1 ||
                RoundNumber > WrongWayRules.RoundCount)
            {
                throw new InvalidOperationException(
                    "Start the solo session before using it.");
            }
        }
    }
}
