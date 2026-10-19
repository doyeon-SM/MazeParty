using System;

namespace MazeParty.Gameplay.Minigames.BouncingBalls
{
    public readonly struct BouncingBallsBallSnapshot
    {
        public BouncingBallsBallSnapshot(
            int id,
            double x,
            double y,
            double velocityX,
            double velocityY,
            int ownerSlot)
        {
            Id = id;
            X = x;
            Y = y;
            VelocityX = velocityX;
            VelocityY = velocityY;
            OwnerSlot = ownerSlot;
        }

        public int Id { get; }
        public double X { get; }
        public double Y { get; }
        public double VelocityX { get; }
        public double VelocityY { get; }
        public int OwnerSlot { get; }
    }

    public readonly struct BouncingBallsShieldSnapshot
    {
        internal BouncingBallsShieldSnapshot(
            int slot,
            double center,
            int input)
        {
            Slot = slot;
            Center = center;
            Input = input;
        }

        public int Slot { get; }
        public double Center { get; }
        public int Input { get; }
    }

    public readonly struct BouncingBallsGoalEvent
    {
        internal BouncingBallsGoalEvent(
            ulong sequence,
            int roundNumber,
            double elapsedSeconds,
            int ballId,
            int defenderSlot,
            int scorerSlot,
            int scoreDelta)
        {
            Sequence = sequence;
            RoundNumber = roundNumber;
            ElapsedSeconds = elapsedSeconds;
            BallId = ballId;
            DefenderSlot = defenderSlot;
            ScorerSlot = scorerSlot;
            ScoreDelta = scoreDelta;
        }

        public ulong Sequence { get; }
        public int RoundNumber { get; }
        public double ElapsedSeconds { get; }
        public int BallId { get; }
        public int DefenderSlot { get; }
        public int ScorerSlot { get; }
        public int ScoreDelta { get; }
        public bool AwardedPoint => ScoreDelta > 0;
        public bool WasOwnGoal =>
            BouncingBallsRules.IsValidPlayerSlot(ScorerSlot) &&
            ScorerSlot == DefenderSlot;
    }

    /// <summary>
    /// Pure server-side four-goal arena. Inputs are held A/D directions;
    /// fixed 60 Hz steps keep launches, bounces and goals seed-deterministic.
    /// Call AdvanceTo with active (unpaused) time for the current round.
    /// </summary>
    public sealed class BouncingBallsMatchState
    {
        private const double TimeEpsilon = 0.000000001d;
        private const double Tau = Math.PI * 2d;
        private const int BallCollisionSolverIterations = 12;

        private readonly BallState[] _balls =
            new BallState[BouncingBallsRules.BallCount];
        private readonly double[] _shieldCenters =
            new double[BouncingBallsRules.PlayerCount];
        private readonly int[] _shieldInputs =
            new int[BouncingBallsRules.PlayerCount];
        private readonly int[] _scores =
            new int[BouncingBallsRules.PlayerCount];
        private readonly int[] _conceded =
            new int[BouncingBallsRules.PlayerCount];
        private readonly double[] _previousBallX =
            new double[BouncingBallsRules.BallCount];
        private readonly double[] _previousBallY =
            new double[BouncingBallsRules.BallCount];
        private uint _randomState;
        private int _simulatedSteps;
        private int _activeBallCount;

        public BouncingBallsMatchState(int seed)
        {
            _randomState = unchecked((uint)seed);
            for (var id = 0; id < _balls.Length; id++)
            {
                _balls[id] = new BallState(id);
            }

            BeginRound(1);
        }

        public int RoundNumber { get; private set; }
        public double RoundElapsedSeconds { get; private set; }
        public bool IsRoundComplete { get; private set; }
        public bool IsComplete { get; private set; }
        public int ActiveBallCount => _activeBallCount;
        public ulong GoalSequence { get; private set; }
        public BouncingBallsGoalEvent? LastGoal { get; private set; }
        // Transient simulation events, never reconstructed from a saved snapshot.
        public event Action<int, double, double> BallReflected;
        public event Action<BouncingBallsGoalEvent> GoalResolved;

        public BouncingBallsBallSnapshot GetBall(int ballId)
        {
            if (!BouncingBallsRules.IsValidBallId(ballId))
            {
                throw new ArgumentOutOfRangeException(nameof(ballId));
            }

            var ball = _balls[ballId];
            return new BouncingBallsBallSnapshot(
                ball.Id,
                ball.X,
                ball.Y,
                ball.VelocityX,
                ball.VelocityY,
                ball.OwnerSlot);
        }

        public BouncingBallsShieldSnapshot GetShield(int playerSlot)
        {
            ValidateSlot(playerSlot);
            return new BouncingBallsShieldSnapshot(
                playerSlot,
                _shieldCenters[playerSlot],
                _shieldInputs[playerSlot]);
        }

        public int GetScore(int playerSlot)
        {
            ValidateSlot(playerSlot);
            return _scores[playerSlot];
        }

        public int GetConceded(int playerSlot)
        {
            ValidateSlot(playerSlot);
            return _conceded[playerSlot];
        }

        public int GetFinalRank(int playerSlot)
        {
            ValidateSlot(playerSlot);
            if (!IsComplete)
            {
                throw new InvalidOperationException(
                    "Final ranks are available only after round two.");
            }

            return BouncingBallsRanking.BuildRanksBySlot(
                _scores,
                _conceded)[playerSlot];
        }

        public void SetShieldInput(int playerSlot, int direction)
        {
            ValidateSlot(playerSlot);
            if (direction < -1 || direction > 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(direction),
                    direction,
                    "Direction must be -1, 0 or 1.");
            }

            _shieldInputs[playerSlot] = IsRoundComplete
                ? 0
                : direction;
        }

        public void AdvanceTo(double activeRoundElapsedSeconds)
        {
            BouncingBallsRules.ValidateRoundElapsed(
                activeRoundElapsedSeconds);
            if (activeRoundElapsedSeconds + TimeEpsilon <
                RoundElapsedSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(activeRoundElapsedSeconds),
                    "Active round time cannot move backwards.");
            }

            if (IsRoundComplete)
            {
                return;
            }

            var target = Math.Min(
                activeRoundElapsedSeconds,
                BouncingBallsRules.RoundSeconds);
            var targetSteps = (int)Math.Floor(
                target * BouncingBallsRules.SimulationHz +
                TimeEpsilon);
            while (_simulatedSteps < targetSteps)
            {
                SimulateStep();
                _simulatedSteps++;
            }

            RoundElapsedSeconds = target;
            if (RoundElapsedSeconds + TimeEpsilon >=
                BouncingBallsRules.RoundSeconds)
            {
                RoundElapsedSeconds = BouncingBallsRules.RoundSeconds;
                IsRoundComplete = true;
                IsComplete = RoundNumber == BouncingBallsRules.RoundCount;
                Array.Clear(_shieldInputs, 0, _shieldInputs.Length);
            }
        }

        public void BeginNextRound()
        {
            if (!IsRoundComplete || IsComplete || RoundNumber != 1)
            {
                throw new InvalidOperationException(
                    "The next round may begin only after round one ends.");
            }

            BeginRound(2);
        }

        private void BeginRound(int roundNumber)
        {
            RoundNumber = roundNumber;
            RoundElapsedSeconds = 0d;
            IsRoundComplete = false;
            LastGoal = null;
            _simulatedSteps = 0;
            _activeBallCount = BouncingBallsRules.InitialBallCount;
            Array.Clear(_shieldCenters, 0, _shieldCenters.Length);
            Array.Clear(_shieldInputs, 0, _shieldInputs.Length);
            for (var id = 0; id < _balls.Length; id++)
            {
                if (id < _activeBallCount)
                {
                    LaunchBall(id, 0d, 0d);
                }
                else
                {
                    _balls[id].Set(0d, 0d, 0d, 0d,
                        BouncingBallsRules.NoOwnerSlot);
                }
            }
        }

        private void LaunchBall(int id, double x, double y)
        {
            var angle = NextRandomUnit() * Tau;
            _balls[id].Set(
                x, y,
                Math.Cos(angle) * BouncingBallsRules.BallSpeed,
                Math.Sin(angle) * BouncingBallsRules.BallSpeed,
                BouncingBallsRules.NoOwnerSlot);
        }

        private void SimulateStep()
        {
            var dt = BouncingBallsRules.SimulationStepSeconds;
            var elapsed = (_simulatedSteps + 1) * dt;
            var expectedBallCount = Math.Min(
                BouncingBallsRules.BallCount,
                BouncingBallsRules.InitialBallCount +
                (int)Math.Floor((elapsed + TimeEpsilon) /
                    BouncingBallsRules.AdditionalBallIntervalSeconds) *
                BouncingBallsRules.AdditionalBallsPerWave);
            while (_activeBallCount < expectedBallCount)
            {
                var id = _activeBallCount++;
                LaunchBall(id, id % 2 == 0 ? -0.6d : 0.6d, 0d);
            }
            for (var slot = 0; slot < _shieldCenters.Length; slot++)
            {
                _shieldCenters[slot] = Math.Max(
                    -BouncingBallsRules.ShieldMaximumOffset,
                    Math.Min(
                        BouncingBallsRules.ShieldMaximumOffset,
                        _shieldCenters[slot] +
                        (_shieldInputs[slot] *
                         BouncingBallsRules.ShieldSpeed * dt)));
            }

            for (var id = 0; id < _activeBallCount; id++)
            {
                var ball = _balls[id];
                _previousBallX[id] = ball.X;
                _previousBallY[id] = ball.Y;
                ball.X += ball.VelocityX * dt;
                ball.Y += ball.VelocityY * dt;
            }

            ResolveBallCollisions();

            for (var id = 0; id < _activeBallCount; id++)
            {
                var ball = _balls[id];
                for (var slot = 0;
                     slot < BouncingBallsRules.PlayerCount;
                     slot++)
                {
                    if (ResolveSide(
                            ball,
                            slot,
                            _previousBallX[id],
                            _previousBallY[id]))
                    {
                        break;
                    }
                }
            }
        }

        private void ResolveBallCollisions()
        {
            for (var iteration = 0;
                 iteration < BallCollisionSolverIterations;
                 iteration++)
            {
                var foundOverlap = false;
                for (var firstId = 0;
                     firstId < _activeBallCount - 1;
                     firstId++)
                {
                    for (var secondId = firstId + 1;
                         secondId < _activeBallCount;
                         secondId++)
                    {
                        var first = _balls[firstId].ToSnapshot();
                        var second = _balls[secondId].ToSnapshot();
                        if (!BouncingBallsRules.TryResolveBallCollision(
                                first,
                                second,
                                out var resolvedFirst,
                                out var resolvedSecond))
                        {
                            continue;
                        }

                        foundOverlap = true;
                        _balls[firstId].Set(resolvedFirst);
                        _balls[secondId].Set(resolvedSecond);
                    }
                }

                if (!foundOverlap)
                {
                    break;
                }
            }
        }

        // Returns true if a goal respawned the ball this step.
        private bool ResolveSide(
            BallState ball,
            int sideSlot,
            double oldX,
            double oldY)
        {
            var oldRadial = GetRadial(oldX, oldY, sideSlot);
            var radial = GetRadial(ball.X, ball.Y, sideSlot);
            var radialVelocity = GetRadial(
                ball.VelocityX,
                ball.VelocityY,
                sideSlot);
            if (radialVelocity <= 0d)
            {
                return false;
            }

            var shieldFace =
                BouncingBallsRules.ShieldRailDistance -
                BouncingBallsRules.BallRadius;
            if (oldRadial <= shieldFace && radial >= shieldFace)
            {
                var crossingFraction =
                    (shieldFace - oldRadial) /
                    (radial - oldRadial);
                var oldTangent = GetTangent(oldX, oldY, sideSlot);
                var newTangent = GetTangent(
                    ball.X,
                    ball.Y,
                    sideSlot);
                var tangentAtCrossing = oldTangent +
                    ((newTangent - oldTangent) * crossingFraction);
                if (Math.Abs(
                        tangentAtCrossing -
                        _shieldCenters[sideSlot]) <=
                    BouncingBallsRules.ShieldHalfWidth +
                    BouncingBallsRules.BallRadius)
                {
                    SetRadial(ball, sideSlot, (2d * shieldFace) - radial);
                    InvertRadialVelocity(ball, sideSlot);
                    ball.OwnerSlot = sideSlot;
                    var contactX = sideSlot == 1 ? BouncingBallsRules.ShieldRailDistance :
                        sideSlot == 3 ? -BouncingBallsRules.ShieldRailDistance : tangentAtCrossing;
                    var contactY = sideSlot == 0 ? -BouncingBallsRules.ShieldRailDistance :
                        sideSlot == 2 ? BouncingBallsRules.ShieldRailDistance : tangentAtCrossing;
                    BallReflected?.Invoke(sideSlot, contactX, contactY);
                    return false;
                }
            }

            var wallFace = BouncingBallsRules.ArenaHalfExtent -
                BouncingBallsRules.BallRadius;
            var tangent = GetTangent(ball.X, ball.Y, sideSlot);
            if (radial >= wallFace &&
                Math.Abs(tangent) > BouncingBallsRules.GoalHalfWidth)
            {
                SetRadial(ball, sideSlot, (2d * wallFace) - radial);
                InvertRadialVelocity(ball, sideSlot);
                BallReflected?.Invoke(sideSlot, ball.X, ball.Y);
                return false;
            }

            if (radial >= BouncingBallsRules.ArenaHalfExtent +
                BouncingBallsRules.BallRadius &&
                Math.Abs(tangent) <= BouncingBallsRules.GoalHalfWidth)
            {
                ScoreGoal(ball, sideSlot);
                return true;
            }

            return false;
        }

        private void ScoreGoal(BallState ball, int defenderSlot)
        {
            var scorerSlot = ball.OwnerSlot;
            var scoreDelta = 0;
            if (BouncingBallsRules.IsValidPlayerSlot(scorerSlot))
            {
                var previousScore = _scores[scorerSlot];
                _scores[scorerSlot] =
                    BouncingBallsRules.CalculateScoreAfterGoal(
                        previousScore,
                        scorerSlot,
                        defenderSlot);
                scoreDelta = _scores[scorerSlot] - previousScore;
            }

            _conceded[defenderSlot]++;
            GoalSequence++;
            LastGoal = new BouncingBallsGoalEvent(
                GoalSequence,
                RoundNumber,
                (_simulatedSteps + 1) *
                    BouncingBallsRules.SimulationStepSeconds,
                ball.Id,
                defenderSlot,
                scorerSlot,
                scoreDelta);
            GoalResolved?.Invoke(LastGoal.Value);
            RespawnFromShield(ball, defenderSlot);
        }

        private void RespawnFromShield(BallState ball, int defenderSlot)
        {
            var tangent = _shieldCenters[defenderSlot];
            var radial = BouncingBallsRules.ShieldRailDistance;
            var variation = ((NextRandomUnit() * 2d) - 1d) *
                BouncingBallsRules.RespawnVariationRadians;
            var inward = Math.Cos(variation) *
                BouncingBallsRules.BallSpeed;
            var sideways = Math.Sin(variation) *
                BouncingBallsRules.BallSpeed;

            switch (defenderSlot)
            {
                case 0: // Bottom
                    ball.Set(tangent, -radial, sideways, inward,
                        defenderSlot);
                    break;
                case 1: // Right
                    ball.Set(radial, tangent, -inward, sideways,
                        defenderSlot);
                    break;
                case 2: // Top
                    ball.Set(tangent, radial, sideways, -inward,
                        defenderSlot);
                    break;
                default: // Left
                    ball.Set(-radial, tangent, inward, sideways,
                        defenderSlot);
                    break;
            }
        }

        private double NextRandomUnit()
        {
            _randomState = unchecked(
                (_randomState * 1664525u) + 1013904223u);
            return _randomState / (double)uint.MaxValue;
        }

        private static double GetRadial(
            double x,
            double y,
            int sideSlot)
        {
            switch (sideSlot)
            {
                case 0: return -y;
                case 1: return x;
                case 2: return y;
                default: return -x;
            }
        }

        private static double GetTangent(
            double x,
            double y,
            int sideSlot)
        {
            return sideSlot == 0 || sideSlot == 2
                ? x
                : y;
        }

        private static void SetRadial(
            BallState ball,
            int sideSlot,
            double radial)
        {
            switch (sideSlot)
            {
                case 0: ball.Y = -radial; break;
                case 1: ball.X = radial; break;
                case 2: ball.Y = radial; break;
                default: ball.X = -radial; break;
            }
        }

        private static void InvertRadialVelocity(
            BallState ball,
            int sideSlot)
        {
            if (sideSlot == 0 || sideSlot == 2)
            {
                ball.VelocityY = -ball.VelocityY;
            }
            else
            {
                ball.VelocityX = -ball.VelocityX;
            }
        }

        private static void ValidateSlot(int playerSlot)
        {
            if (!BouncingBallsRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerSlot),
                    playerSlot,
                    "Player slot must be between 0 and 3.");
            }
        }

        private sealed class BallState
        {
            public BallState(int id)
            {
                Id = id;
            }

            public int Id { get; }
            public double X { get; set; }
            public double Y { get; set; }
            public double VelocityX { get; set; }
            public double VelocityY { get; set; }
            public int OwnerSlot { get; set; }

            public void Set(
                double x,
                double y,
                double velocityX,
                double velocityY,
                int ownerSlot)
            {
                X = x;
                Y = y;
                VelocityX = velocityX;
                VelocityY = velocityY;
                OwnerSlot = ownerSlot;
            }

            public void Set(BouncingBallsBallSnapshot snapshot)
            {
                Set(
                    snapshot.X,
                    snapshot.Y,
                    snapshot.VelocityX,
                    snapshot.VelocityY,
                    snapshot.OwnerSlot);
            }

            public BouncingBallsBallSnapshot ToSnapshot()
            {
                return new BouncingBallsBallSnapshot(
                    Id,
                    X,
                    Y,
                    VelocityX,
                    VelocityY,
                    OwnerSlot);
            }
        }
    }
}
