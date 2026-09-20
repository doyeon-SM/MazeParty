using System;

namespace MazeParty.Gameplay.Minigames.SnowySpin
{
    public readonly struct SnowySpinBallSnapshot
    {
        internal SnowySpinBallSnapshot(int slot, double x, double z,
            double velocityX, double velocityZ, bool isEliminated,
            int roundRank, int totalScore)
        {
            Slot = slot;
            X = x;
            Z = z;
            VelocityX = velocityX;
            VelocityZ = velocityZ;
            IsEliminated = isEliminated;
            RoundRank = roundRank;
            TotalScore = totalScore;
        }

        public int Slot { get; }
        public double X { get; }
        public double Z { get; }
        public double VelocityX { get; }
        public double VelocityZ { get; }
        public bool IsEliminated { get; }
        public int RoundRank { get; }
        public int TotalScore { get; }
    }

    public readonly struct SnowySpinFallEvent
    {
        internal SnowySpinFallEvent(ulong sequence, int roundNumber,
            int slot, double elapsedSeconds)
        {
            Sequence = sequence;
            RoundNumber = roundNumber;
            Slot = slot;
            ElapsedSeconds = elapsedSeconds;
        }

        public ulong Sequence { get; }
        public int RoundNumber { get; }
        public int Slot { get; }
        public double ElapsedSeconds { get; }
    }

    public readonly struct SnowySpinRoundResult
    {
        private readonly int[] _ranks;

        internal SnowySpinRoundResult(int roundNumber,
            double elapsedSeconds, int[] ranks)
        {
            RoundNumber = roundNumber;
            ElapsedSeconds = elapsedSeconds;
            _ranks = (int[])ranks.Clone();
        }

        public int RoundNumber { get; }
        public double ElapsedSeconds { get; }

        public int GetRank(int slot)
        {
            if (!SnowySpinRules.IsValidPlayerSlot(slot))
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }

            if (_ranks == null)
            {
                throw new InvalidOperationException("No round result is available.");
            }

            return _ranks[slot];
        }
    }

    /// <summary>
    /// Server-driven four-ball XZ simulation. Call AdvanceTo with active time
    /// for the current round. Between rounds, leave the model paused until
    /// BeginNextRound is called after the result/countdown phases.
    /// </summary>
    public sealed class SnowySpinMatchState
    {
        private const double Epsilon = 0.000000001d;
        private readonly BallState[] _balls =
            new BallState[SnowySpinRules.PlayerCount];
        private readonly int[] _scores =
            new int[SnowySpinRules.PlayerCount];
        private readonly int[,] _roundRanks =
            new int[SnowySpinRules.RoundCount, SnowySpinRules.PlayerCount];
        private uint _randomState;
        private int _simulatedSteps;

        public SnowySpinMatchState(int seed)
        {
            _randomState = unchecked((uint)seed);
            for (var slot = 0; slot < _balls.Length; slot++)
            {
                _balls[slot] = new BallState();
            }

            BeginRound(1);
        }

        public int RoundNumber { get; private set; }
        public double RoundElapsedSeconds { get; private set; }
        public bool IsRoundComplete { get; private set; }
        public bool IsComplete { get; private set; }
        public int SurvivorCount { get; private set; }
        public ulong FallSequence { get; private set; }
        public SnowySpinFallEvent? LastFall { get; private set; }
        public ulong RoundTransitionSequence { get; private set; }
        public SnowySpinRoundResult? LastCompletedRound { get; private set; }

        public SnowySpinBallSnapshot GetPlayer(int slot)
        {
            ValidateSlot(slot);
            var ball = _balls[slot];
            return new SnowySpinBallSnapshot(slot, ball.X, ball.Z,
                ball.VelocityX, ball.VelocityZ, ball.IsEliminated,
                ball.RoundRank, _scores[slot]);
        }

        public int GetScore(int slot)
        {
            ValidateSlot(slot);
            return _scores[slot];
        }

        public int GetRoundRank(int roundNumber, int slot)
        {
            ValidateSlot(slot);
            if (roundNumber < 1 ||
                roundNumber > SnowySpinRules.RoundCount ||
                roundNumber > RoundNumber ||
                (roundNumber == RoundNumber && !IsRoundComplete))
            {
                throw new InvalidOperationException(
                    "The requested round is not complete.");
            }

            return _roundRanks[roundNumber - 1, slot];
        }

        public int GetFinalRank(int slot)
        {
            ValidateSlot(slot);
            if (!IsComplete)
            {
                throw new InvalidOperationException(
                    "Final ranks are available after round three.");
            }

            var rank = 1;
            for (var other = 0; other < _balls.Length; other++)
            {
                if (other != slot && CompareFinal(other, slot) < 0)
                {
                    rank++;
                }
            }

            return rank;
        }

        public void SetMovementInput(int slot, double x, double z)
        {
            ValidateSlot(slot);
            SnowySpinRules.ValidateFinite(x, nameof(x));
            SnowySpinRules.ValidateFinite(z, nameof(z));
            var ball = _balls[slot];
            if (IsRoundComplete || ball.IsEliminated)
            {
                ball.InputX = 0d;
                ball.InputZ = 0d;
                return;
            }

            x = Math.Max(-1d, Math.Min(1d, x));
            z = Math.Max(-1d, Math.Min(1d, z));
            var magnitude = Math.Sqrt(x * x + z * z);
            if (magnitude > 1d)
            {
                x /= magnitude;
                z /= magnitude;
            }

            ball.InputX = x;
            ball.InputZ = z;
        }

        public void AdvanceTo(double activeRoundElapsedSeconds)
        {
            SnowySpinRules.ValidateFinite(activeRoundElapsedSeconds,
                nameof(activeRoundElapsedSeconds));
            if (activeRoundElapsedSeconds < 0d ||
                activeRoundElapsedSeconds + Epsilon < RoundElapsedSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(activeRoundElapsedSeconds),
                    "Active round time must be non-negative and monotonic.");
            }

            if (IsRoundComplete)
            {
                return;
            }

            var target = Math.Min(activeRoundElapsedSeconds,
                SnowySpinRules.RoundDurationSeconds);
            var targetSteps = (int)Math.Floor(
                target * SnowySpinRules.SimulationHz + Epsilon);
            while (_simulatedSteps < targetSteps && !IsRoundComplete)
            {
                _simulatedSteps++;
                var stepEnd = _simulatedSteps *
                    SnowySpinRules.SimulationStepSeconds;
                SimulateStep(stepEnd);
            }

            if (IsRoundComplete)
            {
                return;
            }

            RoundElapsedSeconds = target;
            if (target + Epsilon >= SnowySpinRules.RoundDurationSeconds)
            {
                FinishRound(SnowySpinRules.RoundDurationSeconds);
            }
        }

        public void BeginNextRound()
        {
            if (!IsRoundComplete || IsComplete ||
                RoundNumber >= SnowySpinRules.RoundCount)
            {
                throw new InvalidOperationException(
                    "The next round requires a completed non-final round.");
            }

            BeginRound(RoundNumber + 1);
        }

        private void BeginRound(int roundNumber)
        {
            RoundNumber = roundNumber;
            RoundElapsedSeconds = 0d;
            IsRoundComplete = false;
            SurvivorCount = SnowySpinRules.PlayerCount;
            LastFall = null;
            _simulatedSteps = 0;
            var rotation = (int)(NextRandom() % SnowySpinRules.PlayerCount);
            for (var slot = 0; slot < _balls.Length; slot++)
            {
                var direction = (slot + rotation) %
                    SnowySpinRules.PlayerCount;
                var x = direction == 1 ? SnowySpinRules.SpawnRadius :
                    direction == 3 ? -SnowySpinRules.SpawnRadius : 0d;
                var z = direction == 0 ? SnowySpinRules.SpawnRadius :
                    direction == 2 ? -SnowySpinRules.SpawnRadius : 0d;
                _balls[slot].Reset(x, z);
            }
        }

        private void SimulateStep(double stepEnd)
        {
            var dt = SnowySpinRules.SimulationStepSeconds;
            foreach (var ball in _balls)
            {
                if (ball.IsEliminated)
                {
                    continue;
                }

                var hasInput = Math.Abs(ball.InputX) > Epsilon ||
                    Math.Abs(ball.InputZ) > Epsilon;
                var drag = hasInput ? SnowySpinRules.SteeringDrag :
                    SnowySpinRules.CoastingDrag;
                var dragFactor = Math.Max(0d, 1d - drag * dt);
                ball.VelocityX = (ball.VelocityX +
                    ball.InputX * SnowySpinRules.Acceleration * dt) *
                    dragFactor;
                ball.VelocityZ = (ball.VelocityZ +
                    ball.InputZ * SnowySpinRules.Acceleration * dt) *
                    dragFactor;
                var speed = Math.Sqrt(ball.VelocityX * ball.VelocityX +
                    ball.VelocityZ * ball.VelocityZ);
                if (speed > SnowySpinRules.MaximumSpeed)
                {
                    var scale = SnowySpinRules.MaximumSpeed / speed;
                    ball.VelocityX *= scale;
                    ball.VelocityZ *= scale;
                }

                ball.X += ball.VelocityX * dt;
                ball.Z += ball.VelocityZ * dt;
            }

            for (var first = 0; first < _balls.Length; first++)
            {
                if (_balls[first].IsEliminated)
                {
                    continue;
                }

                for (var second = first + 1; second < _balls.Length;
                    second++)
                {
                    if (!_balls[second].IsEliminated)
                    {
                        ResolveCollision(_balls[first], _balls[second],
                            first, second);
                    }
                }
            }

            for (var slot = 0; slot < _balls.Length; slot++)
            {
                var ball = _balls[slot];
                if (ball.IsEliminated ||
                    ball.X * ball.X + ball.Z * ball.Z <=
                    Math.Pow(SnowySpinRules.ArenaRadius -
                        SnowySpinRules.BallRadius, 2d))
                {
                    continue;
                }

                ball.IsEliminated = true;
                ball.InputX = 0d;
                ball.InputZ = 0d;
                ball.FallElapsedSeconds = stepEnd;
                SurvivorCount--;
                FallSequence++;
                LastFall = new SnowySpinFallEvent(FallSequence,
                    RoundNumber, slot, stepEnd);
            }

            RoundElapsedSeconds = stepEnd;
            if (SurvivorCount <= 1)
            {
                FinishRound(stepEnd);
            }
        }

        private static void ResolveCollision(BallState first,
            BallState second, int firstSlot, int secondSlot)
        {
            var dx = second.X - first.X;
            var dz = second.Z - first.Z;
            var distanceSquared = dx * dx + dz * dz;
            var diameter = SnowySpinRules.BallRadius * 2d;
            if (distanceSquared >= diameter * diameter)
            {
                return;
            }

            var distance = Math.Sqrt(distanceSquared);
            var nx = distance > Epsilon ? dx / distance :
                firstSlot < secondSlot ? 1d : -1d;
            var nz = distance > Epsilon ? dz / distance : 0d;
            var overlap = diameter - distance;
            first.X -= nx * overlap * 0.5d;
            first.Z -= nz * overlap * 0.5d;
            second.X += nx * overlap * 0.5d;
            second.Z += nz * overlap * 0.5d;

            var relativeNormalVelocity =
                (second.VelocityX - first.VelocityX) * nx +
                (second.VelocityZ - first.VelocityZ) * nz;
            if (relativeNormalVelocity >= 0d)
            {
                return;
            }

            var impulse = -(1d + SnowySpinRules.CollisionRestitution) *
                relativeNormalVelocity * 0.5d;
            first.VelocityX -= impulse * nx;
            first.VelocityZ -= impulse * nz;
            second.VelocityX += impulse * nx;
            second.VelocityZ += impulse * nz;
        }

        private void FinishRound(double elapsedSeconds)
        {
            if (IsRoundComplete)
            {
                return;
            }

            RoundElapsedSeconds = elapsedSeconds;
            var ordered = new int[SnowySpinRules.PlayerCount];
            for (var slot = 0; slot < ordered.Length; slot++)
            {
                ordered[slot] = slot;
                _balls[slot].InputX = 0d;
                _balls[slot].InputZ = 0d;
            }

            // Survivors precede fallen balls and are sorted by proximity to
            // center. Fallen balls rank by later fall first; same-step ties
            // use slot to keep the ordering total and deterministic.
            Array.Sort(ordered, CompareRound);
            var ranks = new int[SnowySpinRules.PlayerCount];
            for (var index = 0; index < ordered.Length; index++)
            {
                var slot = ordered[index];
                var rank = index + 1;
                ranks[slot] = rank;
                _balls[slot].RoundRank = rank;
                _roundRanks[RoundNumber - 1, slot] = rank;
                _scores[slot] += SnowySpinRules.PlayerCount - rank;
            }

            IsRoundComplete = true;
            IsComplete = RoundNumber == SnowySpinRules.RoundCount;
            RoundTransitionSequence++;
            LastCompletedRound = new SnowySpinRoundResult(RoundNumber,
                elapsedSeconds, ranks);
        }

        private int CompareRound(int leftSlot, int rightSlot)
        {
            var left = _balls[leftSlot];
            var right = _balls[rightSlot];
            if (left.IsEliminated != right.IsEliminated)
            {
                return left.IsEliminated ? 1 : -1;
            }

            if (left.IsEliminated)
            {
                var fall = right.FallElapsedSeconds.CompareTo(
                    left.FallElapsedSeconds);
                return fall != 0 ? fall : leftSlot.CompareTo(rightSlot);
            }

            var leftRadius = left.X * left.X + left.Z * left.Z;
            var rightRadius = right.X * right.X + right.Z * right.Z;
            var center = leftRadius.CompareTo(rightRadius);
            return center != 0 ? center : leftSlot.CompareTo(rightSlot);
        }

        private int CompareFinal(int leftSlot, int rightSlot)
        {
            var score = _scores[rightSlot].CompareTo(_scores[leftSlot]);
            if (score != 0)
            {
                return score;
            }

            // A tied total favors the better later-round finish, then slot.
            for (var round = SnowySpinRules.RoundCount - 1;
                round >= 0; round--)
            {
                var result = _roundRanks[round, leftSlot].CompareTo(
                    _roundRanks[round, rightSlot]);
                if (result != 0)
                {
                    return result;
                }
            }

            return leftSlot.CompareTo(rightSlot);
        }

        private uint NextRandom()
        {
            _randomState = unchecked(
                _randomState * 1664525u + 1013904223u);
            return _randomState;
        }

        private static void ValidateSlot(int slot)
        {
            if (!SnowySpinRules.IsValidPlayerSlot(slot))
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        private sealed class BallState
        {
            public double X;
            public double Z;
            public double VelocityX;
            public double VelocityZ;
            public double InputX;
            public double InputZ;
            public bool IsEliminated;
            public double FallElapsedSeconds;
            public int RoundRank;

            public void Reset(double x, double z)
            {
                X = x;
                Z = z;
                VelocityX = 0d;
                VelocityZ = 0d;
                InputX = 0d;
                InputZ = 0d;
                IsEliminated = false;
                FallElapsedSeconds = 0d;
                RoundRank = 0;
            }
        }
    }
}
