using System;

namespace MazeParty.Gameplay.Minigames.CliffBarrage
{
    public enum CliffBarrageLaserPhase : byte
    {
        Inactive,
        Warning,
        Firing
    }

    public readonly struct CliffBarragePlayerSnapshot
    {
        internal CliffBarragePlayerSnapshot(
            CliffBarrageMatchState.PlayerState value, int score)
        {
            X = value.X;
            Z = value.Z;
            FacingX = value.FacingX;
            FacingZ = value.FacingZ;
            HitCount = value.HitCount;
            IsEliminated = value.IsEliminated;
            RoundRank = value.RoundRank;
            TotalScore = score;
        }

        public double X { get; }
        public double Z { get; }
        public double FacingX { get; }
        public double FacingZ { get; }
        public int HitCount { get; }
        public int RemainingLives => IsEliminated ? 0 : 2 - HitCount;
        public bool IsEliminated { get; }
        public int RoundRank { get; }
        public int TotalScore { get; }
    }

    public readonly struct CliffBarrageProjectileSnapshot
    {
        internal CliffBarrageProjectileSnapshot(
            CliffBarrageMatchState.ProjectileState value)
        {
            Active = value.Active;
            X = value.X;
            Z = value.Z;
            VelocityX = value.VelocityX;
            VelocityZ = value.VelocityZ;
        }

        public bool Active { get; }
        public double X { get; }
        public double Z { get; }
        public double VelocityX { get; }
        public double VelocityZ { get; }
    }

    public readonly struct CliffBarrageLaserSnapshot
    {
        internal CliffBarrageLaserSnapshot(
            CliffBarrageMatchState.LaserState value)
        {
            Phase = value.Phase;
            StartX = value.StartX;
            StartZ = value.StartZ;
            EndX = value.EndX;
            EndZ = value.EndZ;
        }

        public CliffBarrageLaserPhase Phase { get; }
        public double StartX { get; }
        public double StartZ { get; }
        public double EndX { get; }
        public double EndZ { get; }
    }

    /// <summary>
    /// Pure, fixed-step, server-time XZ simulation. All arrays are allocated
    /// once and their slots are recycled; no projectile or laser is destroyed.
    /// </summary>
    public sealed class CliffBarrageMatchState
    {
        private const double Epsilon = 0.000000001d;
        private const double ProjectileSpawnMin = 1.5d;
        private const double ProjectileSpawnMax = 2.5d;
        private const double LaserSpawnMin = 7d;
        private const double LaserSpawnMax = 10d;
        private readonly PlayerState[] _players =
            new PlayerState[CliffBarrageRules.PlayerCount];
        private readonly ProjectileState[] _projectiles =
            new ProjectileState[CliffBarrageRules.MaximumProjectiles];
        private readonly LaserState[] _lasers =
            new LaserState[CliffBarrageRules.MaximumLasers];
        private readonly int[] _scores =
            new int[CliffBarrageRules.PlayerCount];
        private readonly int[,] _roundRanks =
            new int[CliffBarrageRules.RoundCount,
                CliffBarrageRules.PlayerCount];
        private readonly int _projectileLimit;
        private readonly int _laserLimit;
        private uint _randomState;
        private int _simulatedSteps;
        private double _nextProjectileAt;
        private double _nextLaserAt;

        public CliffBarrageMatchState(int seed,
            int projectileLimit = CliffBarrageRules.MaximumProjectiles,
            int laserLimit = CliffBarrageRules.MaximumLasers)
        {
            if (projectileLimit < 0 || projectileLimit >
                CliffBarrageRules.MaximumProjectiles)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(projectileLimit));
            }
            if (laserLimit < 0 || laserLimit >
                CliffBarrageRules.MaximumLasers)
            {
                throw new ArgumentOutOfRangeException(nameof(laserLimit));
            }

            _projectileLimit = projectileLimit;
            _laserLimit = laserLimit;
            _randomState = unchecked((uint)seed);
            for (var slot = 0; slot < _players.Length; slot++)
            {
                _players[slot] = new PlayerState();
            }
            for (var index = 0; index < _projectiles.Length; index++)
            {
                _projectiles[index] = new ProjectileState();
            }
            for (var index = 0; index < _lasers.Length; index++)
            {
                _lasers[index] = new LaserState();
            }
            BeginRound(1);
        }

        public int RoundNumber { get; private set; }
        public double RoundElapsedSeconds { get; private set; }
        public bool IsRoundComplete { get; private set; }
        public bool IsComplete { get; private set; }
        public int SurvivorCount { get; private set; }
        public ulong RoundTransitionSequence { get; private set; }
        public ulong DamageSequence { get; private set; }
        public int LastDamagedSlot { get; private set; } = -1;
        public int ProjectileLimit => _projectileLimit;
        public int LaserLimit => _laserLimit;

        public CliffBarragePlayerSnapshot GetPlayer(int slot)
        {
            ValidateSlot(slot);
            return new CliffBarragePlayerSnapshot(_players[slot],
                _scores[slot]);
        }

        public CliffBarrageProjectileSnapshot GetProjectile(int index)
        {
            if (index < 0 || index >= _projectiles.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return new CliffBarrageProjectileSnapshot(_projectiles[index]);
        }

        public CliffBarrageLaserSnapshot GetLaser(int index)
        {
            if (index < 0 || index >= _lasers.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return new CliffBarrageLaserSnapshot(_lasers[index]);
        }

        public int GetScore(int slot)
        {
            ValidateSlot(slot);
            return _scores[slot];
        }

        public int GetRoundRank(int roundNumber, int slot)
        {
            ValidateSlot(slot);
            if (roundNumber < 1 || roundNumber > RoundNumber ||
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
                    "Final ranks require all three rounds.");
            }
            var rank = 1;
            for (var other = 0; other < _players.Length; other++)
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
            ValidateFinite(x, nameof(x));
            ValidateFinite(z, nameof(z));
            var player = _players[slot];
            if (IsRoundComplete || player.IsEliminated)
            {
                player.InputX = 0d;
                player.InputZ = 0d;
                return;
            }
            x = Math.Max(-1d, Math.Min(1d, x));
            z = Math.Max(-1d, Math.Min(1d, z));
            var length = Math.Sqrt(x * x + z * z);
            if (length > 1d)
            {
                x /= length;
                z /= length;
            }
            player.InputX = x;
            player.InputZ = z;
            if (length > Epsilon)
            {
                player.FacingX = x / Math.Min(length, 1d);
                player.FacingZ = z / Math.Min(length, 1d);
            }
        }

        public bool TryPush(int slot)
        {
            ValidateSlot(slot);
            var player = _players[slot];
            if (IsRoundComplete || player.IsEliminated ||
                RoundElapsedSeconds + Epsilon < player.NextPushAt)
            {
                return false;
            }
            player.NextPushAt = RoundElapsedSeconds +
                CliffBarrageRules.PushCooldownSeconds;
            var closest = -1;
            var bestDistanceSquared = double.MaxValue;
            for (var otherSlot = 0; otherSlot < _players.Length;
                otherSlot++)
            {
                if (otherSlot == slot ||
                    _players[otherSlot].IsEliminated)
                {
                    continue;
                }
                var other = _players[otherSlot];
                var dx = other.X - player.X;
                var dz = other.Z - player.Z;
                var forward = dx * player.FacingX +
                    dz * player.FacingZ;
                var lateral = Math.Abs(-dx * player.FacingZ +
                    dz * player.FacingX);
                if (forward <= 0d || forward >
                    CliffBarrageRules.PushRange || lateral >
                    CliffBarrageRules.PushHalfWidth +
                    CliffBarrageRules.PlayerRadius)
                {
                    continue;
                }
                var distanceSquared = dx * dx + dz * dz;
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    closest = otherSlot;
                }
            }
            if (closest < 0)
            {
                return false;
            }
            var target = _players[closest];
            target.X += player.FacingX *
                CliffBarrageRules.PushDistance;
            target.Z += player.FacingZ *
                CliffBarrageRules.PushDistance;
            ResolveFall(closest, RoundElapsedSeconds);
            if (SurvivorCount <= 1)
            {
                FinishRound(RoundElapsedSeconds);
            }
            return true;
        }

        /// <summary>
        /// Applies a confirmed hazard impact at the current simulation time.
        /// Projectile/laser collision use the same damage gate internally.
        /// </summary>
        public bool TryApplyHazardHit(int slot)
        {
            ValidateSlot(slot);
            if (IsRoundComplete)
            {
                return false;
            }
            var applied = ApplyHazardHit(slot, RoundElapsedSeconds);
            if (SurvivorCount <= 1)
            {
                FinishRound(RoundElapsedSeconds);
            }
            return applied;
        }

        public void AdvanceTo(double activeElapsedSeconds)
        {
            ValidateFinite(activeElapsedSeconds,
                nameof(activeElapsedSeconds));
            if (activeElapsedSeconds < 0d ||
                activeElapsedSeconds + Epsilon < RoundElapsedSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(activeElapsedSeconds));
            }
            if (IsRoundComplete)
            {
                return;
            }
            var target = Math.Min(activeElapsedSeconds,
                CliffBarrageRules.RoundDurationSeconds);
            var targetSteps = (int)Math.Floor(target *
                CliffBarrageRules.SimulationHz + Epsilon);
            while (_simulatedSteps < targetSteps && !IsRoundComplete)
            {
                _simulatedSteps++;
                SimulateStep(_simulatedSteps *
                    CliffBarrageRules.SimulationStepSeconds);
            }
            if (IsRoundComplete)
            {
                return;
            }
            RoundElapsedSeconds = target;
            if (target + Epsilon >=
                CliffBarrageRules.RoundDurationSeconds)
            {
                FinishRound(CliffBarrageRules.RoundDurationSeconds);
            }
        }

        public void BeginNextRound()
        {
            if (!IsRoundComplete || IsComplete ||
                RoundNumber >= CliffBarrageRules.RoundCount)
            {
                throw new InvalidOperationException(
                    "A completed non-final round is required.");
            }
            BeginRound(RoundNumber + 1);
        }

        private void BeginRound(int roundNumber)
        {
            RoundNumber = roundNumber;
            RoundElapsedSeconds = 0d;
            IsRoundComplete = false;
            SurvivorCount = CliffBarrageRules.PlayerCount;
            LastDamagedSlot = -1;
            _simulatedSteps = 0;
            var rotation = (int)(NextRandom() %
                CliffBarrageRules.PlayerCount);
            for (var slot = 0; slot < _players.Length; slot++)
            {
                var direction = (slot + rotation) %
                    CliffBarrageRules.PlayerCount;
                var x = direction == 1 ?
                    CliffBarrageRules.SpawnRadius : direction == 3 ?
                    -CliffBarrageRules.SpawnRadius : 0d;
                var z = direction == 0 ?
                    CliffBarrageRules.SpawnRadius : direction == 2 ?
                    -CliffBarrageRules.SpawnRadius : 0d;
                _players[slot].Reset(x, z);
            }
            foreach (var projectile in _projectiles)
            {
                projectile.Reset();
            }
            foreach (var laser in _lasers)
            {
                laser.Reset();
            }
            _nextProjectileAt = NextRange(ProjectileSpawnMin,
                ProjectileSpawnMax);
            _nextLaserAt = NextRange(LaserSpawnMin,
                LaserSpawnMax);
        }

        private void SimulateStep(double stepEnd)
        {
            var step = CliffBarrageRules.SimulationStepSeconds;
            for (var slot = 0; slot < _players.Length; slot++)
            {
                var player = _players[slot];
                if (player.IsEliminated)
                {
                    continue;
                }
                player.X += player.InputX *
                    CliffBarrageRules.MovementSpeed * step;
                player.Z += player.InputZ *
                    CliffBarrageRules.MovementSpeed * step;
            }
            ResolvePlayerSeparation();
            for (var slot = 0; slot < _players.Length; slot++)
            {
                ResolveFall(slot, stepEnd);
            }

            // Falls are resolved as one fixed-step batch. Once at most one
            // survivor remains, no hazard later in this step can strike the
            // winner or alter the completed round.
            if (SurvivorCount <= 1)
            {
                FinishRound(stepEnd);
                return;
            }

            if (_projectileLimit > 0 &&
                stepEnd + Epsilon >= _nextProjectileAt)
            {
                SpawnProjectile();
                _nextProjectileAt = stepEnd +
                    NextRange(ProjectileSpawnMin,
                        ProjectileSpawnMax);
            }
            if (_laserLimit > 0 &&
                stepEnd + Epsilon >= _nextLaserAt)
            {
                SpawnLaser(stepEnd);
                _nextLaserAt = stepEnd +
                    NextRange(LaserSpawnMin, LaserSpawnMax);
            }
            SimulateProjectiles(step, stepEnd);
            if (IsRoundComplete)
            {
                return;
            }
            SimulateLasers(stepEnd);
            if (IsRoundComplete)
            {
                return;
            }
            RoundElapsedSeconds = stepEnd;
        }

        private void ResolvePlayerSeparation()
        {
            var diameter = CliffBarrageRules.PlayerRadius * 2d;
            for (var first = 0; first < _players.Length; first++)
            {
                if (_players[first].IsEliminated)
                {
                    continue;
                }
                for (var second = first + 1;
                    second < _players.Length; second++)
                {
                    if (_players[second].IsEliminated)
                    {
                        continue;
                    }
                    var dx = _players[second].X -
                        _players[first].X;
                    var dz = _players[second].Z -
                        _players[first].Z;
                    var distance = Math.Sqrt(dx * dx + dz * dz);
                    if (distance >= diameter)
                    {
                        continue;
                    }
                    var nx = distance > Epsilon ? dx / distance : 1d;
                    var nz = distance > Epsilon ? dz / distance : 0d;
                    var offset = (diameter - distance) * 0.5d;
                    _players[first].X -= nx * offset;
                    _players[first].Z -= nz * offset;
                    _players[second].X += nx * offset;
                    _players[second].Z += nz * offset;
                }
            }
        }

        private void ResolveFall(int slot, double at)
        {
            var player = _players[slot];
            var edge = CliffBarrageRules.ArenaHalfExtent -
                CliffBarrageRules.PlayerRadius;
            if (player.IsEliminated ||
                (Math.Abs(player.X) <= edge &&
                 Math.Abs(player.Z) <= edge))
            {
                return;
            }
            Eliminate(slot, at);
        }

        private void SpawnProjectile()
        {
            ProjectileState free = null;
            for (var index = 0; index < _projectileLimit; index++)
            {
                if (!_projectiles[index].Active)
                {
                    free = _projectiles[index];
                    break;
                }
            }
            if (free == null)
            {
                return;
            }
            var edge = (int)(NextRandom() % 4u);
            var offset = NextRange(-CliffBarrageRules.ArenaHalfExtent,
                CliffBarrageRules.ArenaHalfExtent);
            var outside = CliffBarrageRules.ArenaHalfExtent + 1d;
            free.X = edge == 0 ? -outside :
                edge == 1 ? outside : offset;
            free.Z = edge == 2 ? -outside :
                edge == 3 ? outside : offset;
            var targetX = NextRange(-4d, 4d);
            var targetZ = NextRange(-4d, 4d);
            var dx = targetX - free.X;
            var dz = targetZ - free.Z;
            var length = Math.Sqrt(dx * dx + dz * dz);
            free.VelocityX = dx / length *
                CliffBarrageRules.ProjectileSpeed;
            free.VelocityZ = dz / length *
                CliffBarrageRules.ProjectileSpeed;
            free.Active = true;
        }

        private void SimulateProjectiles(double step, double at)
        {
            for (var index = 0; index < _projectiles.Length; index++)
            {
                var projectile = _projectiles[index];
                if (!projectile.Active)
                {
                    continue;
                }
                var previousX = projectile.X;
                var previousZ = projectile.Z;
                projectile.X += projectile.VelocityX * step;
                projectile.Z += projectile.VelocityZ * step;
                var nearest = -1;
                var nearestDistanceSquared = double.MaxValue;
                for (var slot = 0; slot < _players.Length; slot++)
                {
                    var player = _players[slot];
                    if (player.IsEliminated)
                    {
                        continue;
                    }
                    var radius = CliffBarrageRules.PlayerRadius +
                        CliffBarrageRules.ProjectileRadius;
                    if (!CliffBarrageRules.SegmentIntersectsCircle(
                        player.X, player.Z, previousX, previousZ,
                        projectile.X, projectile.Z, radius))
                    {
                        continue;
                    }
                    var distanceSquared = SegmentDistanceSquared(
                        player.X, player.Z, previousX, previousZ,
                        projectile.X, projectile.Z);
                    if (distanceSquared < nearestDistanceSquared)
                    {
                        nearest = slot;
                        nearestDistanceSquared = distanceSquared;
                    }
                }
                if (nearest >= 0)
                {
                    ApplyHazardHit(nearest, at);
                    projectile.Active = false;
                    if (SurvivorCount <= 1)
                    {
                        FinishRound(at);
                        return;
                    }
                }
                else if (Math.Abs(projectile.X) >
                    CliffBarrageRules.ArenaHalfExtent + 2d ||
                    Math.Abs(projectile.Z) >
                    CliffBarrageRules.ArenaHalfExtent + 2d)
                {
                    projectile.Active = false;
                }
            }
        }

        private void SpawnLaser(double at)
        {
            LaserState free = null;
            for (var index = 0; index < _laserLimit; index++)
            {
                if (_lasers[index].Phase ==
                    CliffBarrageLaserPhase.Inactive)
                {
                    free = _lasers[index];
                    break;
                }
            }
            if (free == null)
            {
                return;
            }
            var angle = NextRange(0d, Math.PI * 2d);
            var dx = Math.Cos(angle);
            var dz = Math.Sin(angle);
            var normalX = -dz;
            var normalZ = dx;
            var offset = NextRange(-4d, 4d);
            var centerX = normalX * offset;
            var centerZ = normalZ * offset;
            free.StartX = centerX - dx * 12d;
            free.StartZ = centerZ - dz * 12d;
            free.EndX = centerX + dx * 12d;
            free.EndZ = centerZ + dz * 12d;
            free.Phase = CliffBarrageLaserPhase.Warning;
            free.FiresAt = at +
                CliffBarrageRules.LaserWarningSeconds;
            free.ExpiresAt = free.FiresAt +
                CliffBarrageRules.LaserFiringSeconds;
        }

        private void SimulateLasers(double at)
        {
            foreach (var laser in _lasers)
            {
                if (laser.Phase == CliffBarrageLaserPhase.Inactive)
                {
                    continue;
                }
                if (at + Epsilon >= laser.ExpiresAt)
                {
                    laser.Reset();
                    continue;
                }
                if (at + Epsilon >= laser.FiresAt)
                {
                    laser.Phase = CliffBarrageLaserPhase.Firing;
                }
                if (laser.Phase != CliffBarrageLaserPhase.Firing)
                {
                    continue;
                }
                for (var slot = 0; slot < _players.Length; slot++)
                {
                    var player = _players[slot];
                    if (player.IsEliminated)
                    {
                        continue;
                    }
                    var radius = CliffBarrageRules.PlayerRadius +
                        CliffBarrageRules.LaserHalfWidth;
                    if (CliffBarrageRules.SegmentIntersectsCircle(
                        player.X, player.Z, laser.StartX,
                        laser.StartZ, laser.EndX, laser.EndZ, radius))
                    {
                        ApplyHazardHit(slot, at);
                        if (SurvivorCount <= 1)
                        {
                            FinishRound(at);
                            return;
                        }
                    }
                }
            }
        }

        private bool ApplyHazardHit(int slot, double at)
        {
            var player = _players[slot];
            if (player.IsEliminated ||
                at + Epsilon < player.InvulnerableUntil)
            {
                return false;
            }
            player.HitCount++;
            player.InvulnerableUntil = at +
                CliffBarrageRules.HitInvulnerabilitySeconds;
            DamageSequence++;
            LastDamagedSlot = slot;
            if (player.HitCount >= 2)
            {
                Eliminate(slot, at);
            }
            return true;
        }

        private void Eliminate(int slot, double at)
        {
            var player = _players[slot];
            if (player.IsEliminated)
            {
                return;
            }
            player.IsEliminated = true;
            player.EliminatedAt = at;
            player.InputX = 0d;
            player.InputZ = 0d;
            SurvivorCount--;
        }

        private void FinishRound(double at)
        {
            if (IsRoundComplete)
            {
                return;
            }
            RoundElapsedSeconds = at;
            var ordered = new int[CliffBarrageRules.PlayerCount];
            for (var slot = 0; slot < ordered.Length; slot++)
            {
                ordered[slot] = slot;
                _players[slot].InputX = 0d;
                _players[slot].InputZ = 0d;
            }
            Array.Sort(ordered, CompareRound);
            for (var index = 0; index < ordered.Length; index++)
            {
                var slot = ordered[index];
                var rank = index + 1;
                _players[slot].RoundRank = rank;
                _roundRanks[RoundNumber - 1, slot] = rank;
                _scores[slot] +=
                    CliffBarrageRules.PlayerCount - rank;
            }
            IsRoundComplete = true;
            IsComplete = RoundNumber ==
                CliffBarrageRules.RoundCount;
            RoundTransitionSequence++;
            foreach (var projectile in _projectiles)
            {
                projectile.Active = false;
            }
            foreach (var laser in _lasers)
            {
                laser.Reset();
            }
        }

        private int CompareRound(int leftSlot, int rightSlot)
        {
            var left = _players[leftSlot];
            var right = _players[rightSlot];
            if (left.IsEliminated != right.IsEliminated)
            {
                return left.IsEliminated ? 1 : -1;
            }
            if (left.IsEliminated)
            {
                var later = right.EliminatedAt.CompareTo(
                    left.EliminatedAt);
                return later != 0 ? later :
                    leftSlot.CompareTo(rightSlot);
            }
            var lives = left.HitCount.CompareTo(right.HitCount);
            if (lives != 0)
            {
                return lives;
            }
            var leftCenter = left.X * left.X + left.Z * left.Z;
            var rightCenter = right.X * right.X +
                right.Z * right.Z;
            var center = leftCenter.CompareTo(rightCenter);
            return center != 0 ? center :
                leftSlot.CompareTo(rightSlot);
        }

        private int CompareFinal(int leftSlot, int rightSlot)
        {
            var score = _scores[rightSlot].CompareTo(
                _scores[leftSlot]);
            if (score != 0)
            {
                return score;
            }
            for (var round = CliffBarrageRules.RoundCount - 1;
                round >= 0; round--)
            {
                var laterRound = _roundRanks[round, leftSlot]
                    .CompareTo(_roundRanks[round, rightSlot]);
                if (laterRound != 0)
                {
                    return laterRound;
                }
            }
            return leftSlot.CompareTo(rightSlot);
        }

        private static double SegmentDistanceSquared(double px,
            double pz, double ax, double az, double bx, double bz)
        {
            var dx = bx - ax;
            var dz = bz - az;
            var denominator = dx * dx + dz * dz;
            var t = denominator > Epsilon ?
                ((px - ax) * dx + (pz - az) * dz) /
                denominator : 0d;
            t = Math.Max(0d, Math.Min(1d, t));
            var nearestX = ax + dx * t;
            var nearestZ = az + dz * t;
            var awayX = px - nearestX;
            var awayZ = pz - nearestZ;
            return awayX * awayX + awayZ * awayZ;
        }

        private double NextRange(double min, double max)
        {
            return min + (max - min) *
                (NextRandom() / ((double)uint.MaxValue + 1d));
        }

        private uint NextRandom()
        {
            _randomState = unchecked(_randomState * 1664525u +
                1013904223u);
            return _randomState;
        }

        private static void ValidateSlot(int slot)
        {
            if (!CliffBarrageRules.IsValidSlot(slot))
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        private static void ValidateFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        internal sealed class PlayerState
        {
            public double X;
            public double Z;
            public double FacingX;
            public double FacingZ;
            public double InputX;
            public double InputZ;
            public int HitCount;
            public bool IsEliminated;
            public double EliminatedAt;
            public double InvulnerableUntil;
            public double NextPushAt;
            public int RoundRank;

            public void Reset(double x, double z)
            {
                X = x;
                Z = z;
                var length = Math.Sqrt(x * x + z * z);
                FacingX = -x / length;
                FacingZ = -z / length;
                InputX = 0d;
                InputZ = 0d;
                HitCount = 0;
                IsEliminated = false;
                EliminatedAt = 0d;
                InvulnerableUntil = 0d;
                NextPushAt = 0d;
                RoundRank = 0;
            }
        }

        internal sealed class ProjectileState
        {
            public bool Active;
            public double X;
            public double Z;
            public double VelocityX;
            public double VelocityZ;

            public void Reset()
            {
                Active = false;
                X = 0d;
                Z = 0d;
                VelocityX = 0d;
                VelocityZ = 0d;
            }
        }

        internal sealed class LaserState
        {
            public CliffBarrageLaserPhase Phase;
            public double StartX;
            public double StartZ;
            public double EndX;
            public double EndZ;
            public double FiresAt;
            public double ExpiresAt;

            public void Reset()
            {
                Phase = CliffBarrageLaserPhase.Inactive;
                StartX = 0d;
                StartZ = 0d;
                EndX = 0d;
                EndZ = 0d;
                FiresAt = 0d;
                ExpiresAt = 0d;
            }
        }
    }
}
