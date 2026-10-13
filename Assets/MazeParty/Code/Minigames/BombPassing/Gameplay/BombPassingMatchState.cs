using System;
using System.Collections.Generic;

namespace MazeParty.Gameplay.Minigames.BombPassing
{
    public readonly struct BombPassingPosition
    {
        public BombPassingPosition(double x, double z)
        {
            BombPassingRules.ValidateFinite(x, nameof(x));
            BombPassingRules.ValidateFinite(z, nameof(z));
            X = x;
            Z = z;
        }

        public double X { get; }
        public double Z { get; }
    }

    public readonly struct BombPassingPlayerSnapshot
    {
        internal BombPassingPlayerSnapshot(
            int slot,
            double x,
            double z,
            double facingX,
            double facingZ,
            bool isEliminated,
            double stunRemainingSeconds,
            double attackCastRemainingSeconds,
            double attackCooldownRemainingSeconds,
            int rank)
        {
            Slot = slot;
            X = x;
            Z = z;
            FacingX = facingX;
            FacingZ = facingZ;
            IsEliminated = isEliminated;
            StunRemainingSeconds = stunRemainingSeconds;
            AttackCastRemainingSeconds = attackCastRemainingSeconds;
            AttackCooldownRemainingSeconds = attackCooldownRemainingSeconds;
            Rank = rank;
        }

        public int Slot { get; }
        public double X { get; }
        public double Z { get; }
        public double FacingX { get; }
        public double FacingZ { get; }
        public bool IsEliminated { get; }
        public double StunRemainingSeconds { get; }
        public bool IsStunned => StunRemainingSeconds > 0d;
        public double AttackCastRemainingSeconds { get; }
        public bool IsAttackCasting => AttackCastRemainingSeconds > 0d;
        public double AttackCooldownRemainingSeconds { get; }
        public bool CanMove => !IsEliminated && !IsStunned &&
            !IsAttackCasting;
        public int Rank { get; }
    }

    public readonly struct BombPassingBombSnapshot
    {
        internal BombPassingBombSnapshot(
            double x,
            double z,
            int holderSlot,
            double remainingSeconds,
            double fuseSeconds,
            int bombNumber,
            bool isChasing)
        {
            X = x;
            Z = z;
            HolderSlot = holderSlot;
            RemainingSeconds = remainingSeconds;
            FuseSeconds = fuseSeconds;
            BombNumber = bombNumber;
            IsChasing = isChasing;
        }

        public double X { get; }
        public double Z { get; }
        public int HolderSlot { get; }
        public double RemainingSeconds { get; }
        public double FuseSeconds { get; }
        public int BombNumber { get; }
        public bool IsChasing { get; }
        public double RemainingRatio => FuseSeconds > 0d
            ? RemainingSeconds / FuseSeconds
            : 0d;
    }

    public enum BombPassingInteractionKind : byte
    {
        None,
        Stun,
        Transfer
    }

    public readonly struct BombPassingInteraction
    {
        internal BombPassingInteraction(
            BombPassingInteractionKind kind,
            ulong sequence,
            int actorSlot,
            int targetSlot)
        {
            Kind = kind;
            Sequence = sequence;
            ActorSlot = actorSlot;
            TargetSlot = targetSlot;
        }

        public BombPassingInteractionKind Kind { get; }
        public ulong Sequence { get; }
        public int ActorSlot { get; }
        public int TargetSlot { get; }
        public bool WasApplied => Kind != BombPassingInteractionKind.None;
    }

    public readonly struct BombPassingExplosion
    {
        internal BombPassingExplosion(
            ulong sequence,
            int bombNumber,
            double elapsedSeconds,
            int eliminatedSlot)
        {
            Sequence = sequence;
            BombNumber = bombNumber;
            ElapsedSeconds = elapsedSeconds;
            EliminatedSlot = eliminatedSlot;
        }

        public ulong Sequence { get; }
        public int BombNumber { get; }
        public double ElapsedSeconds { get; }
        public int EliminatedSlot { get; }
    }

    /// <summary>
    /// Pure 60 Hz, server-driven XZ arena. Inputs may change between fixed
    /// steps; AdvanceTo uses active time so server pauses do not burn the fuse.
    /// </summary>
    public sealed class BombPassingMatchState
    {
        private const double TimeEpsilon = 0.000000001d;
        private const double BombNavigationGridStep = 0.25d;
        private const int BombNavigationGridSize = 65;
        private const int BombNavigationGridCellCount =
            BombNavigationGridSize * BombNavigationGridSize;
        private static readonly int[] BombNavigationNeighborX =
        {
            0, 1, 0, -1, 1, 1, -1, -1
        };
        private static readonly int[] BombNavigationNeighborZ =
        {
            1, 0, -1, 0, 1, -1, -1, 1
        };
        private static readonly BombPassingPosition[] DefaultSpawns =
        {
            new BombPassingPosition(-6.5d, -6.5d),
            new BombPassingPosition(6.5d, -6.5d),
            new BombPassingPosition(6.5d, 6.5d),
            new BombPassingPosition(-6.5d, 6.5d)
        };

        private readonly PlayerState[] _players =
            new PlayerState[BombPassingRules.PlayerCount];
        private readonly bool[] _bombNavigationWalkable =
            new bool[BombNavigationGridCellCount];
        private readonly bool[] _bombNavigationEdgeClear =
            new bool[
                BombNavigationGridCellCount *
                BombNavigationNeighborX.Length];
        private readonly int[] _bombNavigationParents =
            new int[BombNavigationGridCellCount];
        private readonly int[] _bombNavigationQueue =
            new int[BombNavigationGridCellCount];
        private uint _randomState;
        private int _simulatedSteps;
        private double _bombSpawnElapsedSeconds;
        private double _bombFuseSeconds;
        private double _bombX;
        private double _bombZ;
        private int _bombHolderSlot = BombPassingRules.NoHolderSlot;
        private int _bombNumber;

        public BombPassingMatchState(int seed)
            : this(seed, DefaultSpawns)
        {
        }

        public BombPassingMatchState(
            int seed,
            IReadOnlyList<BombPassingPosition> initialPositions)
        {
            if (initialPositions == null ||
                initialPositions.Count != BombPassingRules.PlayerCount)
            {
                throw new ArgumentException(
                    "Exactly four initial positions are required.",
                    nameof(initialPositions));
            }

            _randomState = unchecked((uint)seed);
            for (var slot = 0; slot < _players.Length; slot++)
            {
                var position = initialPositions[slot];
                if (Math.Abs(position.X) >
                        BombPassingRules.ArenaHalfExtent ||
                    Math.Abs(position.Z) >
                        BombPassingRules.ArenaHalfExtent)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(initialPositions),
                        "Player positions must be inside the arena.");
                }

                _players[slot] = new PlayerState(
                    position.X,
                    position.Z);
            }

            InitializeBombNavigationGrid();
            SpawnBomb(0d);
        }

        public double MatchElapsedSeconds { get; private set; }
        public bool IsComplete { get; private set; }
        public ulong InteractionSequence { get; private set; }
        public BombPassingInteraction? LastInteraction { get; private set; }
        public event Action<BombPassingInteraction> InteractionResolved;
        public ulong ExplosionSequence { get; private set; }
        public BombPassingExplosion? LastExplosion { get; private set; }
        public int SurvivorCount { get; private set; } =
            BombPassingRules.PlayerCount;

        public BombPassingPlayerSnapshot GetPlayer(int slot)
        {
            ValidateSlot(slot);
            var player = _players[slot];
            return new BombPassingPlayerSnapshot(
                slot,
                player.X,
                player.Z,
                player.FacingX,
                player.FacingZ,
                player.IsEliminated,
                Math.Max(0d, player.StunnedUntil - MatchElapsedSeconds),
                Math.Max(
                    0d,
                    player.AttackCastEndsAt - MatchElapsedSeconds),
                Math.Max(0d, player.NextAttackAt - MatchElapsedSeconds),
                player.Rank);
        }

        public BombPassingBombSnapshot GetBomb()
        {
            var remaining = Math.Max(
                0d,
                _bombFuseSeconds -
                (MatchElapsedSeconds - _bombSpawnElapsedSeconds));
            return new BombPassingBombSnapshot(
                _bombX,
                _bombZ,
                _bombHolderSlot,
                remaining,
                _bombFuseSeconds,
                _bombNumber,
                !IsComplete &&
                _bombHolderSlot == BombPassingRules.NoHolderSlot &&
                remaining <=
                    _bombFuseSeconds *
                    BombPassingRules.ChaseStartRemainingRatio);
        }

        public int GetFinalRank(int slot)
        {
            ValidateSlot(slot);
            if (!IsComplete)
            {
                throw new InvalidOperationException(
                    "Final ranks are available after one survivor remains.");
            }

            return _players[slot].Rank;
        }

        public void SetMovementInput(int slot, double x, double z)
        {
            ValidateSlot(slot);
            BombPassingRules.ValidateFinite(x, nameof(x));
            BombPassingRules.ValidateFinite(z, nameof(z));
            var player = _players[slot];
            if (player.IsEliminated || IsComplete)
            {
                player.InputX = 0d;
                player.InputZ = 0d;
                return;
            }

            x = Math.Max(-1d, Math.Min(1d, x));
            z = Math.Max(-1d, Math.Min(1d, z));
            var magnitude = Math.Sqrt((x * x) + (z * z));
            if (magnitude > 1d)
            {
                x /= magnitude;
                z /= magnitude;
            }

            player.InputX = x;
            player.InputZ = z;
            if (magnitude > TimeEpsilon)
            {
                player.FacingX = x / Math.Min(1d, magnitude);
                player.FacingZ = z / Math.Min(1d, magnitude);
            }
        }

        public void SetFacing(int slot, double x, double z)
        {
            ValidateSlot(slot);
            BombPassingRules.ValidateFinite(x, nameof(x));
            BombPassingRules.ValidateFinite(z, nameof(z));
            if (_players[slot].IsEliminated || IsComplete)
            {
                return;
            }

            var magnitude = Math.Sqrt((x * x) + (z * z));
            if (magnitude <= TimeEpsilon)
            {
                return;
            }

            _players[slot].FacingX = x / magnitude;
            _players[slot].FacingZ = z / magnitude;
        }

        public bool CanStartAttack(int slot)
        {
            ValidateSlot(slot);
            var actor = _players[slot];
            return !IsComplete &&
                !actor.IsEliminated &&
                actor.StunnedUntil <= MatchElapsedSeconds + TimeEpsilon &&
                actor.AttackCastEndsAt <=
                    MatchElapsedSeconds + TimeEpsilon &&
                actor.NextAttackAt <= MatchElapsedSeconds + TimeEpsilon;
        }

        public bool TryStartAttack(int slot)
        {
            if (!CanStartAttack(slot))
            {
                return false;
            }

            var actor = _players[slot];
            actor.AttackCastEndsAt =
                MatchElapsedSeconds + BombPassingRules.AttackCastSeconds;
            actor.NextAttackAt =
                actor.AttackCastEndsAt +
                BombPassingRules.AttackRecoverySeconds;
            actor.AttackFacingX = actor.FacingX;
            actor.AttackFacingZ = actor.FacingZ;
            return true;
        }

        public void AdvanceTo(double activeElapsedSeconds)
        {
            BombPassingRules.ValidateFinite(
                activeElapsedSeconds,
                nameof(activeElapsedSeconds));
            if (activeElapsedSeconds < 0d ||
                activeElapsedSeconds + TimeEpsilon < MatchElapsedSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(activeElapsedSeconds),
                    "Active time must be non-negative and monotonic.");
            }

            if (IsComplete)
            {
                return;
            }

            var targetSteps = (int)Math.Floor(
                activeElapsedSeconds *
                    BombPassingRules.SimulationHz + TimeEpsilon);
            while (_simulatedSteps < targetSteps && !IsComplete)
            {
                _simulatedSteps++;
                SimulateStep(
                    _simulatedSteps *
                    BombPassingRules.SimulationStepSeconds);
            }

            if (!IsComplete)
            {
                ResolveCompletedAttacks(activeElapsedSeconds);
                MatchElapsedSeconds = activeElapsedSeconds;
            }
        }

        private void SimulateStep(double stepEndSeconds)
        {
            var stepStartSeconds =
                stepEndSeconds - BombPassingRules.SimulationStepSeconds;
            ResolveCompletedAttacks(stepStartSeconds);
            for (var slot = 0; slot < _players.Length; slot++)
            {
                var player = _players[slot];
                if (player.IsEliminated ||
                    player.StunnedUntil > stepStartSeconds + TimeEpsilon ||
                    player.AttackCastEndsAt >
                        stepStartSeconds + TimeEpsilon)
                {
                    continue;
                }

                var speed = slot == _bombHolderSlot
                    ? BombPassingRules.CarrierSpeed
                    : BombPassingRules.EmptyHandSpeed;
                ResolvePlayerMovement(
                    player,
                    player.InputX * speed *
                    BombPassingRules.SimulationStepSeconds,
                    player.InputZ * speed *
                    BombPassingRules.SimulationStepSeconds);
            }

            ResolveCompletedAttacks(stepEndSeconds);

            if (_bombHolderSlot != BombPassingRules.NoHolderSlot)
            {
                _bombX = _players[_bombHolderSlot].X;
                _bombZ = _players[_bombHolderSlot].Z;
            }
            else
            {
                var remaining = _bombFuseSeconds -
                    (stepEndSeconds - _bombSpawnElapsedSeconds);
                if (remaining <=
                    _bombFuseSeconds *
                    BombPassingRules.ChaseStartRemainingRatio)
                {
                    ChaseClosestSurvivor();
                }

                TryPickupBomb();
            }

            if (stepEndSeconds + TimeEpsilon >=
                _bombSpawnElapsedSeconds + _bombFuseSeconds)
            {
                Explode(stepEndSeconds);
            }

            MatchElapsedSeconds = stepEndSeconds;
        }

        private void ResolveCompletedAttacks(double elapsedSeconds)
        {
            MatchElapsedSeconds = Math.Max(
                MatchElapsedSeconds,
                elapsedSeconds);
            for (var slot = 0; slot < _players.Length; slot++)
            {
                var actor = _players[slot];
                if (actor.AttackCastEndsAt <= 0d ||
                    actor.AttackCastEndsAt > elapsedSeconds + TimeEpsilon)
                {
                    continue;
                }

                var castCompletedAt = actor.AttackCastEndsAt;
                actor.AttackCastEndsAt = 0d;
                if (actor.IsEliminated)
                {
                    continue;
                }

                var targetSlot = FindClosestAttackTarget(
                    slot,
                    actor.AttackFacingX,
                    actor.AttackFacingZ);
                if (targetSlot == BombPassingRules.NoHolderSlot)
                {
                    continue;
                }

                var target = _players[targetSlot];
                var kind = _bombHolderSlot == slot
                    ? BombPassingInteractionKind.Transfer
                    : BombPassingInteractionKind.Stun;
                if (kind == BombPassingInteractionKind.Transfer)
                {
                    _bombHolderSlot = targetSlot;
                    _bombX = target.X;
                    _bombZ = target.Z;
                }

                target.StunnedUntil = Math.Max(
                    target.StunnedUntil,
                    castCompletedAt + BombPassingRules.StunSeconds);
                InteractionSequence++;
                var interaction = new BombPassingInteraction(
                    kind,
                    InteractionSequence,
                    slot,
                    targetSlot);
                LastInteraction = interaction;
                InteractionResolved?.Invoke(interaction);
            }
        }

        private static void ResolvePlayerMovement(
            PlayerState player,
            double displacementX,
            double displacementZ)
        {
            var originX = player.X;
            var originZ = player.Z;
            ResolveCircleMovement(
                ref originX,
                ref originZ,
                displacementX,
                displacementZ,
                BombPassingRules.PlayerRadius);
            player.X = originX;
            player.Z = originZ;
        }

        private static void ResolveCircleMovement(
            ref double originX,
            ref double originZ,
            double displacementX,
            double displacementZ,
            double radius)
        {
            var proposedX = ClampToArena(originX + displacementX);
            var proposedZ = ClampToArena(originZ + displacementZ);
            if (CanCircleOccupy(proposedX, proposedZ, radius))
            {
                originX = proposedX;
                originZ = proposedZ;
                return;
            }

            var xOnly = ClampToArena(originX + displacementX);
            if (CanCircleOccupy(xOnly, originZ, radius))
            {
                originX = xOnly;
            }

            var zOnly = ClampToArena(originZ + displacementZ);
            if (CanCircleOccupy(originX, zOnly, radius))
            {
                originZ = zOnly;
            }
        }

        private static bool CanCircleOccupy(
            double x,
            double z,
            double radius)
        {
            return !BombPassingArenaCollisionLayout.BlocksCircle(
                x,
                z,
                radius);
        }

        private void TryPickupBomb()
        {
            var nearest = BombPassingRules.NoHolderSlot;
            var nearestDistanceSquared =
                BombPassingRules.PickupRadius *
                BombPassingRules.PickupRadius;
            for (var slot = 0; slot < _players.Length; slot++)
            {
                if (_players[slot].IsEliminated)
                {
                    continue;
                }

                var distanceSquared = DistanceSquared(
                    _bombX,
                    _bombZ,
                    _players[slot].X,
                    _players[slot].Z);
                if (distanceSquared <= nearestDistanceSquared &&
                    !BombPassingArenaCollisionLayout.BlocksSegment(
                        _bombX,
                        _bombZ,
                        _players[slot].X,
                        _players[slot].Z) &&
                    (nearest == BombPassingRules.NoHolderSlot ||
                     distanceSquared < nearestDistanceSquared - TimeEpsilon ||
                     slot < nearest))
                {
                    nearest = slot;
                    nearestDistanceSquared = distanceSquared;
                }
            }

            if (nearest != BombPassingRules.NoHolderSlot)
            {
                _bombHolderSlot = nearest;
                _bombX = _players[nearest].X;
                _bombZ = _players[nearest].Z;
            }
        }

        private void ChaseClosestSurvivor()
        {
            var nearest = FindClosestSurvivor(_bombX, _bombZ);
            if (nearest == BombPassingRules.NoHolderSlot)
            {
                return;
            }

            var target = _players[nearest];
            var destinationX = target.X;
            var destinationZ = target.Z;
            if (BombPassingArenaCollisionLayout.BlocksSegment(
                    _bombX,
                    _bombZ,
                    target.X,
                    target.Z,
                    BombPassingRules.BombCollisionRadius) &&
                TryFindBombNavigationStep(
                    target.X,
                    target.Z,
                    out var routeX,
                    out var routeZ))
            {
                destinationX = routeX;
                destinationZ = routeZ;
            }

            var deltaX = destinationX - _bombX;
            var deltaZ = destinationZ - _bombZ;
            var distance = Math.Sqrt(
                (deltaX * deltaX) + (deltaZ * deltaZ));
            if (distance <= TimeEpsilon)
            {
                return;
            }

            var travel = Math.Min(
                distance,
                BombPassingRules.BombChaseSpeed *
                BombPassingRules.SimulationStepSeconds);
            ResolveCircleMovement(
                ref _bombX,
                ref _bombZ,
                deltaX / distance * travel,
                deltaZ / distance * travel,
                BombPassingRules.BombCollisionRadius);
        }

        private void InitializeBombNavigationGrid()
        {
            for (var z = 0; z < BombNavigationGridSize; z++)
            {
                for (var x = 0; x < BombNavigationGridSize; x++)
                {
                    var index = ToBombNavigationIndex(x, z);
                    _bombNavigationWalkable[index] = CanCircleOccupy(
                        ToBombNavigationCoordinate(x),
                        ToBombNavigationCoordinate(z),
                        BombPassingRules.BombCollisionRadius);
                }
            }

            for (var z = 0; z < BombNavigationGridSize; z++)
            {
                for (var x = 0; x < BombNavigationGridSize; x++)
                {
                    var current = ToBombNavigationIndex(x, z);
                    if (!_bombNavigationWalkable[current])
                    {
                        continue;
                    }

                    for (var direction = 0;
                         direction < BombNavigationNeighborX.Length;
                         direction++)
                    {
                        var nextX = x +
                            BombNavigationNeighborX[direction];
                        var nextZ = z +
                            BombNavigationNeighborZ[direction];
                        if (!IsBombNavigationWalkable(nextX, nextZ))
                        {
                            continue;
                        }

                        var isDiagonal =
                            BombNavigationNeighborX[direction] != 0 &&
                            BombNavigationNeighborZ[direction] != 0;
                        if (isDiagonal &&
                            (!IsBombNavigationWalkable(nextX, z) ||
                             !IsBombNavigationWalkable(x, nextZ)))
                        {
                            continue;
                        }

                        _bombNavigationEdgeClear[
                            ToBombNavigationEdgeIndex(
                                current,
                                direction)] =
                            !BombPassingArenaCollisionLayout.BlocksSegment(
                                ToBombNavigationCoordinate(x),
                                ToBombNavigationCoordinate(z),
                                ToBombNavigationCoordinate(nextX),
                                ToBombNavigationCoordinate(nextZ),
                                BombPassingRules.BombCollisionRadius);
                    }
                }
            }
        }

        private bool TryFindBombNavigationStep(
            double targetX,
            double targetZ,
            out double waypointX,
            out double waypointZ)
        {
            waypointX = _bombX;
            waypointZ = _bombZ;
            var start = FindNearestWalkableBombNavigationCell(
                _bombX,
                _bombZ);
            if (start < 0)
            {
                return false;
            }

            for (var index = 0;
                 index < _bombNavigationParents.Length;
                 index++)
            {
                _bombNavigationParents[index] = -2;
            }

            var read = 0;
            var write = 0;
            _bombNavigationQueue[write++] = start;
            _bombNavigationParents[start] = -1;
            var best = start;
            var bestDistanceSquared = BombNavigationDistanceSquared(
                start,
                targetX,
                targetZ);
            while (read < write)
            {
                var current = _bombNavigationQueue[read++];
                var currentX = current % BombNavigationGridSize;
                var currentZ = current / BombNavigationGridSize;
                for (var direction = 0;
                     direction < BombNavigationNeighborX.Length;
                     direction++)
                {
                    var nextX = currentX +
                        BombNavigationNeighborX[direction];
                    var nextZ = currentZ +
                        BombNavigationNeighborZ[direction];
                    if (nextX < 0 ||
                        nextX >= BombNavigationGridSize ||
                        nextZ < 0 ||
                        nextZ >= BombNavigationGridSize)
                    {
                        continue;
                    }

                    var next = ToBombNavigationIndex(nextX, nextZ);
                    if (!_bombNavigationWalkable[next] ||
                        _bombNavigationParents[next] != -2 ||
                        !_bombNavigationEdgeClear[
                            ToBombNavigationEdgeIndex(
                                current,
                                direction)])
                    {
                        continue;
                    }

                    _bombNavigationParents[next] = current;
                    _bombNavigationQueue[write++] = next;
                    var distanceSquared =
                        BombNavigationDistanceSquared(
                            next,
                            targetX,
                            targetZ);
                    if (distanceSquared <
                            bestDistanceSquared - TimeEpsilon ||
                        (Math.Abs(
                             distanceSquared - bestDistanceSquared) <=
                         TimeEpsilon && next < best))
                    {
                        best = next;
                        bestDistanceSquared = distanceSquared;
                    }
                }
            }

            if (best == start)
            {
                return false;
            }

            var step = best;
            while (_bombNavigationParents[step] != start)
            {
                step = _bombNavigationParents[step];
                if (step < 0)
                {
                    return false;
                }
            }

            waypointX = ToBombNavigationCoordinate(
                step % BombNavigationGridSize);
            waypointZ = ToBombNavigationCoordinate(
                step / BombNavigationGridSize);
            return true;
        }

        private int FindNearestWalkableBombNavigationCell(
            double x,
            double z)
        {
            var nearest = -1;
            var nearestDistanceSquared = double.MaxValue;
            for (var index = 0;
                 index < _bombNavigationWalkable.Length;
                 index++)
            {
                if (!_bombNavigationWalkable[index])
                {
                    continue;
                }

                var gridX = ToBombNavigationCoordinate(
                    index % BombNavigationGridSize);
                var gridZ = ToBombNavigationCoordinate(
                    index / BombNavigationGridSize);
                if (BombPassingArenaCollisionLayout.BlocksSegment(
                        x,
                        z,
                        gridX,
                        gridZ,
                        BombPassingRules.BombCollisionRadius))
                {
                    continue;
                }

                var distanceSquared = BombNavigationDistanceSquared(
                    index,
                    x,
                    z);
                if (distanceSquared <
                        nearestDistanceSquared - TimeEpsilon ||
                    (Math.Abs(
                         distanceSquared - nearestDistanceSquared) <=
                     TimeEpsilon && index < nearest))
                {
                    nearest = index;
                    nearestDistanceSquared = distanceSquared;
                }
            }

            return nearest;
        }

        private bool IsBombNavigationWalkable(int x, int z)
        {
            return x >= 0 && x < BombNavigationGridSize &&
                z >= 0 && z < BombNavigationGridSize &&
                _bombNavigationWalkable[ToBombNavigationIndex(x, z)];
        }

        private static double BombNavigationDistanceSquared(
            int index,
            double x,
            double z)
        {
            var gridX = ToBombNavigationCoordinate(
                index % BombNavigationGridSize);
            var gridZ = ToBombNavigationCoordinate(
                index / BombNavigationGridSize);
            return DistanceSquared(gridX, gridZ, x, z);
        }

        private static int ToBombNavigationIndex(int x, int z)
        {
            return z * BombNavigationGridSize + x;
        }

        private static int ToBombNavigationEdgeIndex(
            int cell,
            int direction)
        {
            return cell * BombNavigationNeighborX.Length + direction;
        }

        private static double ToBombNavigationCoordinate(int value)
        {
            return -BombPassingRules.ArenaHalfExtent +
                value * BombNavigationGridStep;
        }

        private void Explode(double elapsedSeconds)
        {
            // An unheld bomb never arbitrarily eliminates a player. Its
            // explosion still starts the next fuse at the same center.
            var eliminatedSlot = _bombHolderSlot;
            if (eliminatedSlot != BombPassingRules.NoHolderSlot)
            {
                var eliminated = _players[eliminatedSlot];
                eliminated.IsEliminated = true;
                eliminated.InputX = 0d;
                eliminated.InputZ = 0d;
                eliminated.AttackCastEndsAt = 0d;
                eliminated.Rank = SurvivorCount;
                SurvivorCount--;
            }

            ExplosionSequence++;
            LastExplosion = new BombPassingExplosion(
                ExplosionSequence,
                _bombNumber,
                elapsedSeconds,
                eliminatedSlot);
            if (SurvivorCount == 1)
            {
                for (var slot = 0; slot < _players.Length; slot++)
                {
                    if (!_players[slot].IsEliminated)
                    {
                        _players[slot].Rank = 1;
                        break;
                    }
                }

                _bombHolderSlot = BombPassingRules.NoHolderSlot;
                IsComplete = true;
                return;
            }

            SpawnBomb(elapsedSeconds);
        }

        private void SpawnBomb(double elapsedSeconds)
        {
            _bombNumber++;
            _bombX = 0d;
            _bombZ = 0d;
            _bombHolderSlot = BombPassingRules.NoHolderSlot;
            _bombSpawnElapsedSeconds = elapsedSeconds;
            _bombFuseSeconds = BombPassingRules.MinimumFuseSeconds +
                NextRandomUnit() *
                (BombPassingRules.MaximumFuseSeconds -
                 BombPassingRules.MinimumFuseSeconds);
        }

        private int FindClosestAttackTarget(
            int actorSlot,
            double attackFacingX,
            double attackFacingZ)
        {
            var actor = _players[actorSlot];
            var nearest = BombPassingRules.NoHolderSlot;
            var nearestDistanceSquared = double.MaxValue;
            for (var slot = 0; slot < _players.Length; slot++)
            {
                if (slot == actorSlot || _players[slot].IsEliminated)
                {
                    continue;
                }

                var target = _players[slot];
                if (!BombPassingRules.IsInsideAttackHitbox(
                        actor.X,
                        actor.Z,
                        attackFacingX,
                        attackFacingZ,
                        target.X,
                        target.Z) ||
                    BombPassingArenaCollisionLayout.BlocksSegment(
                        actor.X,
                        actor.Z,
                        target.X,
                        target.Z))
                {
                    continue;
                }

                var distanceSquared = DistanceSquared(
                    actor.X,
                    actor.Z,
                    target.X,
                    target.Z);
                if (distanceSquared < nearestDistanceSquared -
                        TimeEpsilon ||
                    (Math.Abs(
                         distanceSquared - nearestDistanceSquared) <=
                     TimeEpsilon && slot < nearest))
                {
                    nearest = slot;
                    nearestDistanceSquared = distanceSquared;
                }
            }

            return nearest;
        }

        private int FindClosestSurvivor(double x, double z)
        {
            var nearest = BombPassingRules.NoHolderSlot;
            var nearestDistanceSquared = double.MaxValue;
            for (var slot = 0; slot < _players.Length; slot++)
            {
                if (_players[slot].IsEliminated)
                {
                    continue;
                }

                var distanceSquared = DistanceSquared(
                    x,
                    z,
                    _players[slot].X,
                    _players[slot].Z);
                if (distanceSquared < nearestDistanceSquared -
                        TimeEpsilon ||
                    (Math.Abs(
                         distanceSquared - nearestDistanceSquared) <=
                     TimeEpsilon && slot < nearest))
                {
                    nearest = slot;
                    nearestDistanceSquared = distanceSquared;
                }
            }

            return nearest;
        }

        private double NextRandomUnit()
        {
            _randomState = unchecked(
                (_randomState * 1664525u) + 1013904223u);
            return _randomState / (double)uint.MaxValue;
        }

        private static double DistanceSquared(
            double leftX,
            double leftZ,
            double rightX,
            double rightZ)
        {
            var x = rightX - leftX;
            var z = rightZ - leftZ;
            return (x * x) + (z * z);
        }

        private static double ClampToArena(double value)
        {
            return Math.Max(
                -BombPassingRules.ArenaHalfExtent,
                Math.Min(BombPassingRules.ArenaHalfExtent, value));
        }

        private static void ValidateSlot(int slot)
        {
            if (!BombPassingRules.IsValidPlayerSlot(slot))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(slot),
                    slot,
                    "Player slot must be between 0 and 3.");
            }
        }

        private sealed class PlayerState
        {
            public PlayerState(double x, double z)
            {
                X = x;
                Z = z;
                var magnitude = Math.Sqrt((x * x) + (z * z));
                FacingX = magnitude > TimeEpsilon ? -x / magnitude : 0d;
                FacingZ = magnitude > TimeEpsilon ? -z / magnitude : 1d;
            }

            public double X { get; set; }
            public double Z { get; set; }
            public double FacingX { get; set; }
            public double FacingZ { get; set; }
            public double InputX { get; set; }
            public double InputZ { get; set; }
            public double StunnedUntil { get; set; }
            public double AttackCastEndsAt { get; set; }
            public double AttackFacingX { get; set; }
            public double AttackFacingZ { get; set; }
            public double NextAttackAt { get; set; }
            public bool IsEliminated { get; set; }
            public int Rank { get; set; }
        }
    }
}
