using System;
using MazeParty.Gameplay.Minigames.Minefield;
using MazeParty.Multiplayer;
using UnityEngine;

namespace MazeParty.Dev.MinigameSoloTest
{
    public enum MinefieldSoloPhase : byte
    {
        Countdown,
        Running,
        RoundResult,
        Complete
    }

    /// <summary>
    /// Clock-driven state for a one-player Minefield session. It deliberately
    /// finishes a round as soon as the solo player resolves, so four network
    /// participants are never required by this developer harness.
    /// </summary>
    public sealed class MinefieldSoloSession
    {
        private readonly bool[] _roundResolved =
            new bool[MinefieldRules.RoundCount];
        private readonly bool[] _roundCleared =
            new bool[MinefieldRules.RoundCount];

        public int Seed { get; private set; }
        public int RoundNumber { get; private set; }
        public MinefieldSoloPhase Phase { get; private set; }
        public float RemainingSeconds { get; private set; }

        public int ClearedRoundCount
        {
            get
            {
                var count = 0;
                for (var index = 0; index < _roundCleared.Length; index++)
                {
                    if (_roundResolved[index] && _roundCleared[index])
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public void Begin(int seed)
        {
            Seed = seed;
            Array.Clear(_roundResolved, 0, _roundResolved.Length);
            Array.Clear(_roundCleared, 0, _roundCleared.Length);
            RoundNumber = 1;
            EnterCountdown();
        }

        public void Tick(float unscaledDeltaTime)
        {
            if (Phase == MinefieldSoloPhase.Complete)
            {
                return;
            }

            RemainingSeconds = Math.Max(
                0f,
                RemainingSeconds - Math.Max(0f, unscaledDeltaTime));
            if (RemainingSeconds > 0f)
            {
                return;
            }

            switch (Phase)
            {
                case MinefieldSoloPhase.Countdown:
                    Phase = MinefieldSoloPhase.Running;
                    RemainingSeconds = (float)NetworkMinefieldState.RunSeconds;
                    break;
                case MinefieldSoloPhase.Running:
                    SetCurrentRoundOutcome(false);
                    EnterRoundResult();
                    break;
                case MinefieldSoloPhase.RoundResult:
                    if (RoundNumber >= MinefieldRules.RoundCount)
                    {
                        Phase = MinefieldSoloPhase.Complete;
                        RemainingSeconds = 0f;
                    }
                    else
                    {
                        RoundNumber++;
                        EnterCountdown();
                    }
                    break;
            }
        }

        public bool ResolveCurrentRound(bool cleared)
        {
            if (Phase != MinefieldSoloPhase.Running)
            {
                return false;
            }

            SetCurrentRoundOutcome(cleared);
            EnterRoundResult();
            return true;
        }

        public void RestartCurrentRound()
        {
            EnsureStarted();
            var index = RoundNumber - 1;
            _roundResolved[index] = false;
            _roundCleared[index] = false;
            EnterCountdown();
        }

        public bool WasRoundCleared(int roundNumber)
        {
            if (roundNumber < 1 || roundNumber > MinefieldRules.RoundCount)
            {
                throw new ArgumentOutOfRangeException(nameof(roundNumber));
            }

            var index = roundNumber - 1;
            return _roundResolved[index] && _roundCleared[index];
        }

        public Vector3[] CreateCurrentMineLayout()
        {
            EnsureStarted();
            return NetworkMinefieldState.GenerateMineWorldPositions(
                unchecked((ulong)(uint)Seed),
                RoundNumber);
        }

        private void EnterCountdown()
        {
            Phase = MinefieldSoloPhase.Countdown;
            RemainingSeconds =
                (float)NetworkMinefieldState.CountdownSeconds;
        }

        private void EnterRoundResult()
        {
            Phase = MinefieldSoloPhase.RoundResult;
            RemainingSeconds =
                (float)NetworkMinefieldState.RoundResultSeconds;
        }

        private void SetCurrentRoundOutcome(bool cleared)
        {
            var index = RoundNumber - 1;
            _roundResolved[index] = true;
            _roundCleared[index] = cleared;
        }

        private void EnsureStarted()
        {
            if (RoundNumber < 1 || RoundNumber > MinefieldRules.RoundCount)
            {
                throw new InvalidOperationException(
                    "Start the solo session before using it.");
            }
        }
    }
}
