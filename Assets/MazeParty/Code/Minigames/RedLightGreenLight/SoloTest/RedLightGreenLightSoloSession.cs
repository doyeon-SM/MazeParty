using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.RedLightGreenLight;

namespace MazeParty.Dev.MinigameSoloTest
{
    public enum RedLightGreenLightSoloPhase : byte
    {
        Countdown,
        Running,
        RoundResult,
        Complete
    }

    /// <summary>
    /// Local one-player clock and match wrapper around the production
    /// RedLightGreenLightRoundState. The unused slots remain stationary so the
    /// normal four-player ranking rules are still exercised by the harness.
    /// </summary>
    public sealed class RedLightGreenLightSoloSession
    {
        public const int LocalPlayerSlot = 0;

        private readonly RedLightGreenLightRoundResult[] _roundResults =
            new RedLightGreenLightRoundResult[
                RedLightGreenLightRules.RoundCount];

        private IReadOnlyList<RedLightGreenLightLeaderboardEntry>
            _leaderboard;

        public int Seed { get; private set; }
        public int RoundNumber { get; private set; }
        public RedLightGreenLightSoloPhase Phase { get; private set; }
        public float RemainingSeconds { get; private set; }
        public double RunningElapsedSeconds { get; private set; }
        public RedLightGreenLightRoundState RoundState { get; private set; }
        public RedLightGreenLightMovementIntentResolution?
            LastMovementResolution { get; private set; }
        public IReadOnlyList<RedLightGreenLightLeaderboardEntry>
            Leaderboard => _leaderboard;

        public RedLightGreenLightPlayerRoundState LocalPlayer =>
            RoundState?.GetPlayer(LocalPlayerSlot);

        public RedLightGreenLightSignalPhase SignalPhase
        {
            get
            {
                if (Phase != RedLightGreenLightSoloPhase.Running ||
                    RoundState == null)
                {
                    return RedLightGreenLightSignalPhase.Green;
                }

                return RoundState.SignalSchedule
                    .GetWindowAt(RunningElapsedSeconds).Phase;
            }
        }

        public float SignalRemainingSeconds
        {
            get
            {
                if (Phase != RedLightGreenLightSoloPhase.Running ||
                    RoundState == null)
                {
                    return 0f;
                }

                var window = RoundState.SignalSchedule.GetWindowAt(
                    RunningElapsedSeconds);
                return (float)Math.Max(
                    0d,
                    window.EndsAtSeconds - RunningElapsedSeconds);
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
                case RedLightGreenLightSoloPhase.Countdown:
                    RemainingSeconds = Math.Max(
                        0f,
                        RemainingSeconds - deltaTime);
                    if (RemainingSeconds <= 0f)
                    {
                        Phase = RedLightGreenLightSoloPhase.Running;
                        RunningElapsedSeconds = 0d;
                        RemainingSeconds =
                            (float)RedLightGreenLightRules.RoundSeconds;
                    }
                    break;
                case RedLightGreenLightSoloPhase.Running:
                    RunningElapsedSeconds = Math.Min(
                        RedLightGreenLightRules.RoundSeconds,
                        RunningElapsedSeconds + deltaTime);
                    RemainingSeconds = (float)Math.Max(
                        0d,
                        RedLightGreenLightRules.RoundSeconds -
                        RunningElapsedSeconds);
                    if (RoundState.TryEndForTimeout(
                            RunningElapsedSeconds))
                    {
                        EnterRoundResult();
                    }
                    break;
                case RedLightGreenLightSoloPhase.RoundResult:
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

        public bool TrySubmitMovementIntent(
            bool hasVoluntaryMovementIntent,
            out RedLightGreenLightMovementIntentResolution resolution)
        {
            resolution = default;
            if (Phase != RedLightGreenLightSoloPhase.Running ||
                RoundState == null)
            {
                return false;
            }

            resolution = RoundState.SubmitMovementIntent(
                LocalPlayerSlot,
                hasVoluntaryMovementIntent,
                RunningElapsedSeconds);
            LastMovementResolution = resolution;

            // A local harness has no other human players to watch after the
            // tester is eliminated. Resolve as a timeout so production ranking
            // still places the eliminated local slot behind the idle survivors.
            if (resolution.BecameEliminated && !RoundState.IsComplete)
            {
                RunningElapsedSeconds =
                    RedLightGreenLightRules.RoundSeconds;
                RemainingSeconds = 0f;
                RoundState.TryEndForTimeout(RunningElapsedSeconds);
            }

            if (RoundState.IsComplete)
            {
                EnterRoundResult();
            }

            return true;
        }

        public bool SetLocalProgress(float forwardProgressMeters)
        {
            return Phase == RedLightGreenLightSoloPhase.Running &&
                   RoundState != null &&
                   RoundState.SetForwardProgress(
                       LocalPlayerSlot,
                       forwardProgressMeters);
        }

        public bool TryFinishLocal(float forwardProgressMeters)
        {
            if (Phase != RedLightGreenLightSoloPhase.Running ||
                RoundState == null ||
                !RoundState.TryFinish(
                    LocalPlayerSlot,
                    forwardProgressMeters,
                    RunningElapsedSeconds))
            {
                return false;
            }

            EnterRoundResult();
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

        public RedLightGreenLightRoundResult GetRoundResult(
            int roundNumber)
        {
            if (roundNumber < 1 ||
                roundNumber > RedLightGreenLightRules.RoundCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(roundNumber));
            }

            return _roundResults[roundNumber - 1];
        }

        private void EnterCountdown()
        {
            RoundState = new RedLightGreenLightRoundState(
                unchecked((ulong)(uint)Seed),
                RoundNumber);
            Phase = RedLightGreenLightSoloPhase.Countdown;
            RemainingSeconds =
                (float)RedLightGreenLightRules.CountdownSeconds;
            RunningElapsedSeconds = 0d;
            LastMovementResolution = null;
        }

        private void EnterRoundResult()
        {
            if (RoundState == null || !RoundState.IsComplete)
            {
                throw new InvalidOperationException(
                    "The round must be complete before showing results.");
            }

            _roundResults[RoundNumber - 1] = RoundState.Result;
            Phase = RedLightGreenLightSoloPhase.RoundResult;
            RemainingSeconds =
                (float)RedLightGreenLightRules.ResultSeconds;
        }

        private void AdvanceAfterRoundResult()
        {
            if (RoundNumber >= RedLightGreenLightRules.RoundCount)
            {
                _leaderboard =
                    RedLightGreenLightMatchScoring.BuildLeaderboard(
                        _roundResults);
                Phase = RedLightGreenLightSoloPhase.Complete;
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
                RoundNumber > RedLightGreenLightRules.RoundCount)
            {
                throw new InvalidOperationException(
                    "Start the solo session before using it.");
            }
        }
    }
}
