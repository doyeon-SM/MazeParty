using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.StableFooting;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum NetworkStableFootingPhase : byte
    {
        Inactive,
        Countdown,
        Running,
        RoundResult,
        Complete
    }

    /// <summary>
    /// Server-authoritative simulation for Stable Footing. Board avatars stay
    /// frozen while logical runners move, collide, push and fall in the
    /// additive minigame scene.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkStableFootingState : NetworkBehaviour
    {
        public const double CountdownSeconds = 3d;
        public const double RoundResultSeconds = 4d;
        public const float ArenaCenterX = 460f;
        public const float TileSize = 2.4f;
        public const float MovementSpeed = 5f;
        public const float RunnerCollisionRadius = 0.65f;
        public const float PushRangeInTiles = 1.75f;
        public const float PushConeDot = 0.62f;

        private const float MovementInputThreshold = 0.0001f;
        private const int CollisionSolverIterations = 3;
        private const int BitsPerTileSymbol = 2;
        private const ulong FullTileMask =
            (1UL << StableFootingRules.TileCount) - 1UL;
        private const byte NoPlayerSlot = byte.MaxValue;

        private static readonly int[] StartTileIndices =
        {
            20,
            21,
            26,
            27
        };

        private readonly NetworkVariable<bool> _matchActive =
            CreateBoolVariable();
        private readonly NetworkVariable<bool> _paused =
            CreateBoolVariable();
        private readonly NetworkVariable<byte> _phase =
            CreateByteVariable((byte)NetworkStableFootingPhase.Inactive);
        private readonly NetworkVariable<byte> _roundNumber =
            CreateByteVariable();
        private readonly NetworkVariable<double> _phaseEndsAt =
            CreateDoubleVariable();
        private readonly NetworkVariable<double> _pausedPhaseRemaining =
            CreateDoubleVariable();
        private readonly NetworkVariable<byte> _cycleNumber =
            CreateByteVariable();
        private readonly NetworkVariable<byte> _cyclePhase =
            CreateByteVariable((byte)StableFootingCyclePhase.RoundComplete);
        private readonly NetworkVariable<byte> _safeSymbol =
            CreateByteVariable((byte)StableFootingSymbol.Cross);
        private readonly NetworkVariable<double> _cyclePhaseEndsAt =
            CreateDoubleVariable();
        private readonly NetworkVariable<double> _pausedCycleRemaining =
            CreateDoubleVariable();
        private readonly NetworkVariable<byte> _roundEndReason =
            CreateByteVariable();

        private readonly NetworkVariable<Vector3> _runnerPosition0 =
            CreateRunnerPositionVariable();
        private readonly NetworkVariable<Vector3> _runnerPosition1 =
            CreateRunnerPositionVariable();
        private readonly NetworkVariable<Vector3> _runnerPosition2 =
            CreateRunnerPositionVariable();
        private readonly NetworkVariable<Vector3> _runnerPosition3 =
            CreateRunnerPositionVariable();

        private readonly NetworkVariable<ulong> _activeTileMask =
            CreateULongVariable(FullTileMask);
        private readonly NetworkVariable<ulong> _safeTileMask =
            CreateULongVariable();
        private readonly NetworkVariable<ulong> _tileSymbolsLow =
            CreateULongVariable();
        private readonly NetworkVariable<uint> _tileSymbolsHigh =
            CreateUIntVariable();
        private readonly NetworkVariable<byte> _eliminatedMask =
            CreateByteVariable();
        private readonly NetworkVariable<uint> _eliminationOrders =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _scores =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _roundPoints =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _roundRanks =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _finalRanks =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _pushRevision =
            CreateUIntVariable();
        private readonly NetworkVariable<byte> _lastPusherSlot =
            CreateByteVariable(NoPlayerSlot);
        private readonly NetworkVariable<byte> _lastPushTargetSlot =
            CreateByteVariable(NoPlayerSlot);

        private readonly Vector2[] _serverInputs =
            new Vector2[StableFootingRules.PlayerCount];
        private readonly Vector2[] _lastFacing =
            new Vector2[StableFootingRules.PlayerCount];
        private readonly Vector3[] _nextPositions =
            new Vector3[StableFootingRules.PlayerCount];
        private readonly double[] _nextPushAllowedAt =
            new double[StableFootingRules.PlayerCount];
        private readonly double[] _pausedPushCooldownRemaining =
            new double[StableFootingRules.PlayerCount];
        private readonly NetworkPlayerAvatar[] _avatars =
            new NetworkPlayerAvatar[StableFootingRules.PlayerCount];
        private readonly List<StableFootingRoundResult> _roundResults =
            new List<StableFootingRoundResult>(
                StableFootingRules.RoundCount);
        private readonly List<int> _fallingSlots =
            new List<int>(StableFootingRules.PlayerCount);

        private StableFootingRoundState _roundState;
        private StableFootingCycle _currentCycle;
        private ulong _matchSeed;
        private double _runningStartedAt;
        private double _pausedRunningElapsed;
        private int _resolvedDropCycle;
        private bool _completionReported;

        public static NetworkStableFootingState Instance {
            get;
            private set;
        }

        public NetworkStableFootingPhase Phase =>
            (NetworkStableFootingPhase)_phase.Value;
        public int RoundNumber => _roundNumber.Value;
        public int CycleNumber => _cycleNumber.Value;
        public bool IsPaused => _paused.Value;
        public StableFootingCyclePhase CyclePhase =>
            (StableFootingCyclePhase)_cyclePhase.Value;
        public StableFootingSymbol SafeSymbol =>
            (StableFootingSymbol)_safeSymbol.Value;
        public StableFootingRoundEndReason RoundEndReason =>
            (StableFootingRoundEndReason)_roundEndReason.Value;
        public uint PushRevision => _pushRevision.Value;
        public int LastPusherSlot =>
            _lastPusherSlot.Value == NoPlayerSlot
                ? -1
                : _lastPusherSlot.Value;
        public int LastPushTargetSlot =>
            _lastPushTargetSlot.Value == NoPlayerSlot
                ? -1
                : _lastPushTargetSlot.Value;
        public double Remaining => GetRemaining(
            _phaseEndsAt.Value,
            _pausedPhaseRemaining.Value,
            Phase == NetworkStableFootingPhase.Inactive ||
            Phase == NetworkStableFootingPhase.Complete);
        public double CycleRemaining => GetRemaining(
            _cyclePhaseEndsAt.Value,
            _pausedCycleRemaining.Value,
            Phase != NetworkStableFootingPhase.Running);

        private double ServerNow =>
            NetworkManager != null && NetworkManager.IsListening
                ? NetworkManager.ServerTime.Time
                : Time.unscaledTimeAsDouble;

        public override void OnNetworkSpawn()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError(
                    "More than one NetworkStableFootingState is spawned.");
            }

            Instance = this;
            if (IsServer && !_matchActive.Value)
            {
                ResetReplicatedStateOnServer();
            }
        }

        public override void OnNetworkDespawn()
        {
            ClearLocalRuntime();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || !_matchActive.Value ||
                _paused.Value)
            {
                return;
            }

            FreezeBoardAvatarsOnServer();
            var now = ServerNow;
            if (Phase == NetworkStableFootingPhase.Running)
            {
                RefreshCycleOnServer(now);
                if (Phase != NetworkStableFootingPhase.Running)
                {
                    return;
                }
                if (_roundState != null && _roundState.IsComplete)
                {
                    CompleteCurrentRoundOnServer(now);
                    return;
                }
                if (_phaseEndsAt.Value > 0d &&
                    now >= _phaseEndsAt.Value)
                {
                    _roundState?.TryEndForTimeout(
                        StableFootingRules.RoundSeconds);
                    CompleteCurrentRoundOnServer(now);
                    return;
                }
                return;
            }

            if (_phaseEndsAt.Value <= 0d || now < _phaseEndsAt.Value)
            {
                return;
            }

            switch (Phase)
            {
                case NetworkStableFootingPhase.Countdown:
                    BeginRunOnServer(now);
                    break;
                case NetworkStableFootingPhase.RoundResult:
                    if (_roundNumber.Value < StableFootingRules.RoundCount)
                    {
                        BeginRoundOnServer(_roundNumber.Value + 1, now);
                    }
                    else
                    {
                        CompleteMatchOnServer();
                    }
                    break;
            }
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || !_matchActive.Value ||
                _paused.Value ||
                Phase != NetworkStableFootingPhase.Running ||
                Remaining <= 0d || _roundState == null ||
                _roundState.IsComplete)
            {
                return;
            }

            FreezeBoardAvatarsOnServer();
            var now = ServerNow;
            RefreshCycleOnServer(now);
            if (CyclePhase != StableFootingCyclePhase.Move ||
                _roundState.IsComplete)
            {
                return;
            }

            SimulateRunnerMovementOnServer(Time.fixedDeltaTime, now);
            if (_roundState.IsComplete)
            {
                CompleteCurrentRoundOnServer(now);
            }
        }

        public void BeginMatchOnServer(ulong matchSeed)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            ClearLocalRuntime();
            _matchSeed = matchSeed != 0UL
                ? matchSeed
                : 0x9E3779B97F4A7C15UL;
            _scores.Value = 0U;
            _roundPoints.Value = 0U;
            _roundRanks.Value = 0U;
            _finalRanks.Value = 0U;
            _pushRevision.Value = 0U;
            _lastPusherSlot.Value = NoPlayerSlot;
            _lastPushTargetSlot.Value = NoPlayerSlot;
            _matchActive.Value = true;
            _paused.Value = false;
            _pausedPhaseRemaining.Value = 0d;
            _pausedCycleRemaining.Value = 0d;
            _completionReported = false;

            CacheAndFreezeBoardAvatarsOnServer();
            BeginRoundOnServer(1, ServerNow);
        }

        public void ReceiveInputOnServer(
            NetworkPlayerAvatar avatar,
            Vector2 input)
        {
            if (!TryResolveAuthoritativeSlot(avatar, out var slot))
            {
                return;
            }

            avatar.StopServerInputOnServer();
            _avatars[slot] = avatar;
            if (!CanAcceptInputForSlot(slot) || !IsFinite(input))
            {
                _serverInputs[slot] = Vector2.zero;
                return;
            }

            input = Vector2.ClampMagnitude(input, 1f);
            _serverInputs[slot] = input;
            if (input.sqrMagnitude > MovementInputThreshold)
            {
                _lastFacing[slot] = input.normalized;
            }
        }

        public bool TryPushOnServer(NetworkPlayerAvatar avatar)
        {
            if (!TryResolveAuthoritativeSlot(avatar, out var pusherSlot) ||
                !CanAcceptInputForSlot(pusherSlot))
            {
                return false;
            }

            var now = ServerNow;
            if (now < _nextPushAllowedAt[pusherSlot])
            {
                return false;
            }

            var facing2D = _lastFacing[pusherSlot];
            if (facing2D.sqrMagnitude <= MovementInputThreshold)
            {
                facing2D = Vector2.up;
            }
            facing2D.Normalize();
            var facing = new Vector3(facing2D.x, 0f, facing2D.y);
            var origin = GetRunnerPosition(pusherSlot);
            var maximumDistance = TileSize * PushRangeInTiles;
            var targetSlot = -1;
            var nearestDistance = float.PositiveInfinity;

            for (var slot = 0;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                if (slot == pusherSlot || IsEliminated(slot))
                {
                    continue;
                }

                var delta = GetRunnerPosition(slot) - origin;
                delta.y = 0f;
                var distance = delta.magnitude;
                if (distance > maximumDistance ||
                    distance >= nearestDistance ||
                    distance > 0.0001f &&
                    Vector3.Dot(facing, delta / distance) < PushConeDot)
                {
                    continue;
                }

                targetSlot = slot;
                nearestDistance = distance;
            }

            if (targetSlot < 0)
            {
                return false;
            }

            for (var slot = 0;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                _nextPositions[slot] = GetRunnerPosition(slot);
            }

            var pushedFrom = _nextPositions[targetSlot];
            var pushedTo = ClampRunnerPosition(
                pushedFrom + facing *
                (TileSize * StableFootingRules.PushDistanceInTiles));
            if (TryFindFirstUnsupportedPoint(
                    pushedFrom,
                    pushedTo,
                    _activeTileMask.Value,
                    out var unsupportedPoint))
            {
                // A push is continuous movement, not a teleport. Leave the
                // runner over the first gap so the normal fall resolver below
                // eliminates them even when the final destination was active.
                _nextPositions[targetSlot] = unsupportedPoint;
            }
            else
            {
                _nextPositions[targetSlot] = pushedTo;
                ResolveRunnerCollisions();
            }
            CommitNextPositionsOnServer();

            _nextPushAllowedAt[pusherSlot] =
                now + StableFootingRules.PushCooldownSeconds;
            _lastPusherSlot.Value = (byte)pusherSlot;
            _lastPushTargetSlot.Value = (byte)targetSlot;
            _pushRevision.Value++;

            ResolveUnsupportedPlayersOnServer(GetRunningElapsed(now));
            if (_roundState.IsComplete)
            {
                CompleteCurrentRoundOnServer(now);
            }
            return true;
        }

        public bool CanAcceptInputForSlot(int slot)
        {
            return StableFootingRules.IsValidPlayerSlot(slot) &&
                   _matchActive.Value && !_paused.Value &&
                   Phase == NetworkStableFootingPhase.Running &&
                   CyclePhase == StableFootingCyclePhase.Move &&
                   Remaining > 0d && CycleRemaining > 0d &&
                   !IsEliminated(slot);
        }

        public Vector3 GetRunnerPosition(int slot)
        {
            switch (slot)
            {
                case 0: return _runnerPosition0.Value;
                case 1: return _runnerPosition1.Value;
                case 2: return _runnerPosition2.Value;
                case 3: return _runnerPosition3.Value;
                default: return Vector3.zero;
            }
        }

        public bool IsEliminated(int slot)
        {
            return !StableFootingRules.IsValidPlayerSlot(slot) ||
                   IsMaskBitSet(_eliminatedMask.Value, slot);
        }

        public int GetEliminationOrder(int slot) =>
            ReadPackedByte(_eliminationOrders.Value, slot);

        public int GetScore(int slot) =>
            ReadPackedByte(_scores.Value, slot);

        public int GetRoundPoints(int slot) =>
            ReadPackedByte(_roundPoints.Value, slot);

        public int GetRoundRank(int slot) =>
            ReadPackedByte(_roundRanks.Value, slot);

        public int GetFinalRank(int slot) =>
            ReadPackedByte(_finalRanks.Value, slot);

        public bool IsTileActive(int tileIndex)
        {
            return IsValidTileIndex(tileIndex) &&
                   (_activeTileMask.Value & (1UL << tileIndex)) != 0UL;
        }

        public bool IsTileSafe(int tileIndex)
        {
            return IsValidTileIndex(tileIndex) &&
                   (_safeTileMask.Value & (1UL << tileIndex)) != 0UL;
        }

        public StableFootingSymbol GetTileSymbol(int tileIndex)
        {
            if (!IsValidTileIndex(tileIndex))
            {
                return StableFootingSymbol.Cross;
            }

            var value = tileIndex < 32
                ? (_tileSymbolsLow.Value >>
                   (tileIndex * BitsPerTileSymbol)) & 0x3UL
                : (_tileSymbolsHigh.Value >>
                   ((tileIndex - 32) * BitsPerTileSymbol)) & 0x3U;
            return (StableFootingSymbol)value;
        }

        public static Vector3 GetTileCenter(int tileIndex)
        {
            if (!IsValidTileIndex(tileIndex))
            {
                throw new ArgumentOutOfRangeException(nameof(tileIndex));
            }

            var column = tileIndex % StableFootingRules.BoardWidth;
            var row = tileIndex / StableFootingRules.BoardWidth;
            return new Vector3(
                ArenaCenterX +
                (column - (StableFootingRules.BoardWidth - 1) * 0.5f) *
                TileSize,
                0f,
                (row - (StableFootingRules.BoardHeight - 1) * 0.5f) *
                TileSize);
        }

        public static int GetStartTileIndex(int playerSlot)
        {
            if (!StableFootingRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(nameof(playerSlot));
            }

            return StartTileIndices[playerSlot];
        }

        public static bool TryGetTileIndex(
            Vector3 position,
            out int tileIndex)
        {
            var minimumX = ArenaCenterX -
                           StableFootingRules.BoardWidth * TileSize * 0.5f;
            var minimumZ = -StableFootingRules.BoardHeight *
                           TileSize * 0.5f;
            var column = Mathf.FloorToInt(
                (position.x - minimumX) / TileSize);
            var row = Mathf.FloorToInt(
                (position.z - minimumZ) / TileSize);
            if (column < 0 ||
                column >= StableFootingRules.BoardWidth ||
                row < 0 || row >= StableFootingRules.BoardHeight)
            {
                tileIndex = -1;
                return false;
            }

            tileIndex = row * StableFootingRules.BoardWidth + column;
            return true;
        }

        public void PauseOnServer(double now)
        {
            if (!IsServer || !_matchActive.Value || _paused.Value ||
                !IsFinite(now))
            {
                return;
            }

            _pausedPhaseRemaining.Value = _phaseEndsAt.Value > 0d
                ? Math.Max(0d, _phaseEndsAt.Value - now)
                : 0d;
            _pausedCycleRemaining.Value =
                Phase == NetworkStableFootingPhase.Running &&
                _cyclePhaseEndsAt.Value > 0d
                    ? Math.Max(0d, _cyclePhaseEndsAt.Value - now)
                    : 0d;
            _pausedRunningElapsed =
                Phase == NetworkStableFootingPhase.Running
                    ? GetRunningElapsed(now)
                    : 0d;
            for (var slot = 0;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                _pausedPushCooldownRemaining[slot] = Math.Max(
                    0d,
                    _nextPushAllowedAt[slot] - now);
            }

            _phaseEndsAt.Value = 0d;
            _cyclePhaseEndsAt.Value = 0d;
            ClearAllInputsOnServer();
            _paused.Value = true;
            FreezeBoardAvatarsOnServer();
        }

        public void ResumeOnServer(double now)
        {
            if (!IsServer || !_matchActive.Value || !_paused.Value ||
                !IsFinite(now))
            {
                return;
            }

            _phaseEndsAt.Value =
                now + Math.Max(0d, _pausedPhaseRemaining.Value);
            if (Phase == NetworkStableFootingPhase.Running)
            {
                _runningStartedAt = now - _pausedRunningElapsed;
                _cyclePhaseEndsAt.Value =
                    now + Math.Max(0d, _pausedCycleRemaining.Value);
            }
            for (var slot = 0;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                _nextPushAllowedAt[slot] =
                    now + _pausedPushCooldownRemaining[slot];
                _pausedPushCooldownRemaining[slot] = 0d;
            }

            _pausedPhaseRemaining.Value = 0d;
            _pausedCycleRemaining.Value = 0d;
            _pausedRunningElapsed = 0d;
            _paused.Value = false;
            FreezeBoardAvatarsOnServer();
        }

        public void RestoreAvatarForReconnectOnServer(
            NetworkPlayerAvatar avatar)
        {
            if (!TryResolveAuthoritativeSlot(avatar, out var slot))
            {
                return;
            }

            _avatars[slot] = avatar;
            _serverInputs[slot] = Vector2.zero;
            avatar.StopServerInputOnServer();
        }

        public void EndMatchOnServer()
        {
            if (!IsServer)
            {
                return;
            }

            FreezeBoardAvatarsOnServer();
            ResetReplicatedStateOnServer();
            ClearLocalRuntime();
        }

        private void BeginRoundOnServer(int roundNumber, double now)
        {
            _roundState = new StableFootingRoundState(
                _matchSeed,
                roundNumber);
            _currentCycle = _roundState.Schedule.Cycles[0];
            _roundNumber.Value = (byte)roundNumber;
            _phase.Value = (byte)NetworkStableFootingPhase.Countdown;
            _phaseEndsAt.Value = now + CountdownSeconds;
            _pausedPhaseRemaining.Value = 0d;
            _cycleNumber.Value = (byte)_currentCycle.CycleNumber;
            _cyclePhase.Value =
                (byte)StableFootingCyclePhase.ShuffleReveal;
            _cyclePhaseEndsAt.Value = 0d;
            _pausedCycleRemaining.Value = 0d;
            _roundEndReason.Value =
                (byte)StableFootingRoundEndReason.None;
            _eliminatedMask.Value = 0;
            _eliminationOrders.Value = 0U;
            _roundPoints.Value = 0U;
            _roundRanks.Value = 0U;
            _resolvedDropCycle = -1;
            _runningStartedAt = 0d;

            ApplyCyclePresentationOnServer(
                _currentCycle,
                StableFootingCyclePhase.ShuffleReveal);
            for (var slot = 0;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                _serverInputs[slot] = Vector2.zero;
                _lastFacing[slot] = Vector2.up;
                _nextPushAllowedAt[slot] = 0d;
                _pausedPushCooldownRemaining[slot] = 0d;
                SetRunnerPositionOnServer(
                    slot,
                    GetTileCenter(GetStartTileIndex(slot)));
            }
            FreezeBoardAvatarsOnServer();
        }

        private void BeginRunOnServer(double now)
        {
            _phase.Value = (byte)NetworkStableFootingPhase.Running;
            _phaseEndsAt.Value = now + StableFootingRules.RoundSeconds;
            _runningStartedAt = now;
            _resolvedDropCycle = -1;
            RefreshCycleOnServer(now, true);
        }

        private void RefreshCycleOnServer(
            double now,
            bool force = false)
        {
            if (_roundState == null ||
                Phase != NetworkStableFootingPhase.Running)
            {
                return;
            }

            var elapsed = GetRunningElapsed(now);
            ResolveCrossedDropBoundariesOnServer(elapsed);
            if (_roundState.IsComplete)
            {
                return;
            }

            var cycle = _roundState.Schedule.GetCycleAt(elapsed);
            if (cycle == null)
            {
                _roundState.TryEndForTimeout(
                    StableFootingRules.RoundSeconds);
                CompleteCurrentRoundOnServer(now);
                return;
            }
            var cyclePhase = cycle.GetPhaseAt(elapsed);
            var changed = force ||
                          _currentCycle == null ||
                          cycle.CycleNumber != _cycleNumber.Value ||
                          cyclePhase != CyclePhase;
            _currentCycle = cycle;
            if (!changed)
            {
                return;
            }

            _cycleNumber.Value = (byte)cycle.CycleNumber;
            _cyclePhase.Value = (byte)cyclePhase;
            _cyclePhaseEndsAt.Value =
                _runningStartedAt + GetCyclePhaseEnd(cycle, cyclePhase);
            ApplyCyclePresentationOnServer(cycle, cyclePhase);
            ClearAllInputsOnServer();
        }

        private void ResolveCrossedDropBoundariesOnServer(double elapsed)
        {
            var cycles = _roundState.Schedule.Cycles;
            for (var index = 0; index < cycles.Count; index++)
            {
                var cycle = cycles[index];
                if (cycle.CycleNumber <= _resolvedDropCycle)
                {
                    continue;
                }
                if (cycle.MoveEndsAtSeconds > elapsed)
                {
                    break;
                }

                ulong safeMask = 0UL;
                for (var tileIndex = 0;
                     tileIndex < StableFootingRules.TileCount;
                     tileIndex++)
                {
                    if (cycle.IsTileActive(tileIndex) &&
                        cycle.IsTileSafe(tileIndex))
                    {
                        safeMask |= 1UL << tileIndex;
                    }
                }

                ResolvePlayersOutsideMaskOnServer(
                    safeMask,
                    cycle.MoveEndsAtSeconds);
                _resolvedDropCycle = cycle.CycleNumber;
                if (_roundState.IsComplete)
                {
                    // Preserve the decisive collapse throughout the result
                    // phase instead of leaving the arena in its prior Move
                    // presentation when this boundary ends the round.
                    ApplyCyclePresentationOnServer(
                        cycle,
                        StableFootingCyclePhase.Drop);
                    return;
                }
            }
        }

        private void ApplyCyclePresentationOnServer(
            StableFootingCycle cycle,
            StableFootingCyclePhase cyclePhase)
        {
            _safeSymbol.Value = (byte)cycle.SafeSymbol;
            ulong activeMask = 0UL;
            ulong safeMask = 0UL;
            ulong symbolsLow = 0UL;
            uint symbolsHigh = 0U;

            for (var tileIndex = 0;
                 tileIndex < StableFootingRules.TileCount;
                 tileIndex++)
            {
                var isActive = cycle.IsTileActive(tileIndex);
                if (isActive)
                {
                    activeMask |= 1UL << tileIndex;
                    if (cycle.IsTileSafe(tileIndex))
                    {
                        safeMask |= 1UL << tileIndex;
                    }
                }

                var symbol = isActive
                    ? (ulong)cycle.GetSymbolForTile(tileIndex)
                    : (ulong)StableFootingSymbol.Cross;
                if (tileIndex < 32)
                {
                    symbolsLow |= symbol <<
                                  (tileIndex * BitsPerTileSymbol);
                }
                else
                {
                    symbolsHigh |= (uint)symbol <<
                                   ((tileIndex - 32) *
                                    BitsPerTileSymbol);
                }
            }

            _safeTileMask.Value = safeMask;
            var restoredMask = activeMask;
            for (var index = 0;
                 index < cycle.PermanentlyRemovedTileIndices.Count;
                 index++)
            {
                restoredMask &=
                    ~(1UL << cycle.PermanentlyRemovedTileIndices[index]);
            }
            _activeTileMask.Value =
                cyclePhase == StableFootingCyclePhase.Drop
                    ? safeMask
                    : cyclePhase == StableFootingCyclePhase.Restore
                        ? restoredMask
                        : activeMask;
            _tileSymbolsLow.Value = symbolsLow;
            _tileSymbolsHigh.Value = symbolsHigh;
        }

        private void SimulateRunnerMovementOnServer(
            float deltaTime,
            double now)
        {
            for (var slot = 0;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                var position = GetRunnerPosition(slot);
                if (!IsEliminated(slot))
                {
                    var input = Vector2.ClampMagnitude(
                        _serverInputs[slot],
                        1f);
                    position += new Vector3(input.x, 0f, input.y) *
                                (MovementSpeed * deltaTime);
                    position = ClampRunnerPosition(position);
                }
                _nextPositions[slot] = position;
            }

            ResolveRunnerCollisions();
            CommitNextPositionsOnServer();
            ResolveUnsupportedPlayersOnServer(GetRunningElapsed(now));
        }

        private void ResolveRunnerCollisions()
        {
            var minimumDistance = RunnerCollisionRadius * 2f;
            for (var iteration = 0;
                 iteration < CollisionSolverIterations;
                 iteration++)
            {
                for (var left = 0;
                     left < StableFootingRules.PlayerCount;
                     left++)
                {
                    if (IsEliminated(left))
                    {
                        continue;
                    }
                    for (var right = left + 1;
                         right < StableFootingRules.PlayerCount;
                         right++)
                    {
                        if (IsEliminated(right))
                        {
                            continue;
                        }

                        var delta = _nextPositions[right] -
                                    _nextPositions[left];
                        delta.y = 0f;
                        var distance = delta.magnitude;
                        if (distance >= minimumDistance)
                        {
                            continue;
                        }

                        var direction = distance > 0.0001f
                            ? delta / distance
                            : ((left + right) & 1) == 0
                                ? Vector3.right
                                : Vector3.left;
                        var correction = direction *
                            ((minimumDistance - distance) * 0.5f);
                        _nextPositions[left] = ClampRunnerPosition(
                            _nextPositions[left] - correction);
                        _nextPositions[right] = ClampRunnerPosition(
                            _nextPositions[right] + correction);
                    }
                }
            }
        }

        private void CommitNextPositionsOnServer()
        {
            for (var slot = 0;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                if (!IsEliminated(slot))
                {
                    SetRunnerPositionOnServer(slot, _nextPositions[slot]);
                }
            }
        }

        private void ResolveUnsupportedPlayersOnServer(double elapsed)
        {
            ResolvePlayersOutsideMaskOnServer(
                _activeTileMask.Value,
                elapsed);
        }

        private void ResolvePlayersOutsideMaskOnServer(
            ulong supportedMask,
            double elapsed)
        {
            if (_roundState == null || _roundState.IsComplete)
            {
                return;
            }

            _fallingSlots.Clear();
            for (var slot = 0;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                if (IsEliminated(slot))
                {
                    continue;
                }

                if (!TryGetTileIndex(
                        GetRunnerPosition(slot),
                        out var tileIndex) ||
                    (supportedMask & (1UL << tileIndex)) == 0UL)
                {
                    _fallingSlots.Add(slot);
                }
            }

            if (_fallingSlots.Count == 0)
            {
                return;
            }

            _roundState.ResolveFalls(_fallingSlots, elapsed);
            SyncEliminationsFromRules();
        }

        private void SyncEliminationsFromRules()
        {
            byte eliminatedMask = 0;
            uint eliminationOrders = 0U;
            for (var slot = 0;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                var player = _roundState.GetPlayer(slot);
                if (player.IsEliminated)
                {
                    eliminatedMask = SetMaskBit(
                        eliminatedMask,
                        slot,
                        true);
                    eliminationOrders = WritePackedByte(
                        eliminationOrders,
                        slot,
                        (int)Math.Min(
                            player.EliminationOrder,
                            (ulong)byte.MaxValue));
                    _serverInputs[slot] = Vector2.zero;
                }
            }
            _eliminatedMask.Value = eliminatedMask;
            _eliminationOrders.Value = eliminationOrders;
        }

        private void CompleteCurrentRoundOnServer(double now)
        {
            if (_roundState == null || !_roundState.IsComplete ||
                _roundResults.Count >= _roundNumber.Value)
            {
                return;
            }

            var result = _roundState.Result;
            _roundResults.Add(result);
            uint roundPoints = 0U;
            uint roundRanks = 0U;
            for (var index = 0; index < result.Standings.Count; index++)
            {
                var standing = result.Standings[index];
                roundPoints = WritePackedByte(
                    roundPoints,
                    standing.PlayerSlot,
                    standing.Points);
                roundRanks = WritePackedByte(
                    roundRanks,
                    standing.PlayerSlot,
                    standing.Rank);
                _scores.Value = WritePackedByte(
                    _scores.Value,
                    standing.PlayerSlot,
                    GetScore(standing.PlayerSlot) + standing.Points);
            }

            _roundPoints.Value = roundPoints;
            _roundRanks.Value = roundRanks;
            _roundEndReason.Value = (byte)_roundState.EndReason;
            _phase.Value = (byte)NetworkStableFootingPhase.RoundResult;
            _phaseEndsAt.Value = now + RoundResultSeconds;
            _cyclePhase.Value =
                (byte)StableFootingCyclePhase.RoundComplete;
            _cyclePhaseEndsAt.Value = 0d;
            ClearAllInputsOnServer();
        }

        private void CompleteMatchOnServer()
        {
            if (_completionReported ||
                _roundResults.Count != StableFootingRules.RoundCount)
            {
                return;
            }

            var leaderboard =
                StableFootingMatchScoring.BuildLeaderboard(_roundResults);
            uint finalRanks = 0U;
            for (var index = 0; index < leaderboard.Count; index++)
            {
                var entry = leaderboard[index];
                finalRanks = WritePackedByte(
                    finalRanks,
                    entry.PlayerSlot,
                    entry.Rank);
            }
            _finalRanks.Value = finalRanks;

            var match = NetworkMatchState.Instance;
            if (match == null ||
                !match.TryCompleteStableFootingOnServer(leaderboard))
            {
                return;
            }

            _completionReported = true;
            _matchActive.Value = false;
            _phase.Value = (byte)NetworkStableFootingPhase.Complete;
            _phaseEndsAt.Value = 0d;
            _cyclePhaseEndsAt.Value = 0d;
            ClearAllInputsOnServer();
            FreezeBoardAvatarsOnServer();
        }

        private bool TryResolveAuthoritativeSlot(
            NetworkPlayerAvatar avatar,
            out int slot)
        {
            slot = avatar != null ? avatar.AssignedSlot : -1;
            return IsSpawned && IsServer && avatar != null &&
                   avatar.IsSpawned &&
                   StableFootingRules.IsValidPlayerSlot(slot);
        }

        private void CacheAndFreezeBoardAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                _avatars[slot] =
                    match != null ? match.GetAvatarForSlot(slot) : null;
                _avatars[slot]?.StopServerInputOnServer();
            }
        }

        private void FreezeBoardAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                var avatar = _avatars[slot];
                if (avatar == null && match != null)
                {
                    avatar = match.GetAvatarForSlot(slot);
                    _avatars[slot] = avatar;
                }
                avatar?.StopServerInputOnServer();
            }
        }

        private void ClearAllInputsOnServer()
        {
            for (var slot = 0; slot < _serverInputs.Length; slot++)
            {
                _serverInputs[slot] = Vector2.zero;
            }
        }

        private void SetRunnerPositionOnServer(int slot, Vector3 position)
        {
            switch (slot)
            {
                case 0: _runnerPosition0.Value = position; break;
                case 1: _runnerPosition1.Value = position; break;
                case 2: _runnerPosition2.Value = position; break;
                case 3: _runnerPosition3.Value = position; break;
            }
        }

        private static Vector3 ClampRunnerPosition(Vector3 position)
        {
            // Do not clamp X/Z to the board. Crossing the outer edge or being
            // pushed into a missing tile must be resolved as a fall by
            // TryGetTileIndex/ResolveUnsupportedPlayersOnServer.
            position.y = 0f;
            return position;
        }

        /// <summary>
        /// Sweeps a runner path densely enough that a one-tile gap cannot be
        /// skipped by an instantaneous push.
        /// </summary>
        public static bool TryFindFirstUnsupportedPoint(
            Vector3 start,
            Vector3 end,
            ulong supportedMask,
            out Vector3 unsupportedPoint)
        {
            var distance = Vector3.Distance(start, end);
            var stepLength = TileSize * 0.25f;
            var stepCount = Mathf.Max(
                1,
                Mathf.CeilToInt(distance / stepLength));
            for (var step = 1; step <= stepCount; step++)
            {
                var point = Vector3.Lerp(
                    start,
                    end,
                    (float)step / stepCount);
                if (!TryGetTileIndex(point, out var tileIndex) ||
                    (supportedMask & (1UL << tileIndex)) == 0UL)
                {
                    unsupportedPoint = point;
                    return true;
                }
            }

            unsupportedPoint = end;
            return false;
        }

        private double GetRunningElapsed(double now)
        {
            return Math.Max(
                0d,
                Math.Min(
                    StableFootingRules.RoundSeconds,
                    now - _runningStartedAt));
        }

        private double GetRemaining(
            double deadline,
            double pausedRemaining,
            bool inactive)
        {
            if (inactive)
            {
                return 0d;
            }
            return _paused.Value
                ? Math.Max(0d, pausedRemaining)
                : Math.Max(0d, deadline - ServerNow);
        }

        private static double GetCyclePhaseEnd(
            StableFootingCycle cycle,
            StableFootingCyclePhase phase)
        {
            switch (phase)
            {
                case StableFootingCyclePhase.ShuffleReveal:
                    return cycle.ShuffleRevealEndsAtSeconds;
                case StableFootingCyclePhase.Move:
                    return cycle.MoveEndsAtSeconds;
                case StableFootingCyclePhase.Drop:
                    return cycle.DropEndsAtSeconds;
                default:
                    return cycle.EndsAtSeconds;
            }
        }

        private void ResetReplicatedStateOnServer()
        {
            _matchActive.Value = false;
            _paused.Value = false;
            _phase.Value = (byte)NetworkStableFootingPhase.Inactive;
            _roundNumber.Value = 0;
            _phaseEndsAt.Value = 0d;
            _pausedPhaseRemaining.Value = 0d;
            _cycleNumber.Value = 0;
            _cyclePhase.Value =
                (byte)StableFootingCyclePhase.RoundComplete;
            _safeSymbol.Value = (byte)StableFootingSymbol.Cross;
            _cyclePhaseEndsAt.Value = 0d;
            _pausedCycleRemaining.Value = 0d;
            _roundEndReason.Value = 0;
            _activeTileMask.Value = FullTileMask;
            _safeTileMask.Value = 0UL;
            _tileSymbolsLow.Value = 0UL;
            _tileSymbolsHigh.Value = 0U;
            _eliminatedMask.Value = 0;
            _eliminationOrders.Value = 0U;
            _scores.Value = 0U;
            _roundPoints.Value = 0U;
            _roundRanks.Value = 0U;
            _finalRanks.Value = 0U;
            _pushRevision.Value = 0U;
            _lastPusherSlot.Value = NoPlayerSlot;
            _lastPushTargetSlot.Value = NoPlayerSlot;
            for (var slot = 0;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                SetRunnerPositionOnServer(
                    slot,
                    GetTileCenter(GetStartTileIndex(slot)));
            }
        }

        private void ClearLocalRuntime()
        {
            _roundState = null;
            _currentCycle = null;
            _roundResults.Clear();
            _fallingSlots.Clear();
            _matchSeed = 0UL;
            _runningStartedAt = 0d;
            _pausedRunningElapsed = 0d;
            _resolvedDropCycle = -1;
            _completionReported = false;
            for (var slot = 0;
                 slot < StableFootingRules.PlayerCount;
                 slot++)
            {
                _serverInputs[slot] = Vector2.zero;
                _lastFacing[slot] = Vector2.up;
                _nextPositions[slot] = Vector3.zero;
                _nextPushAllowedAt[slot] = 0d;
                _pausedPushCooldownRemaining[slot] = 0d;
                _avatars[slot] = null;
            }
        }

        private static bool IsValidTileIndex(int tileIndex)
        {
            return tileIndex >= 0 &&
                   tileIndex < StableFootingRules.TileCount;
        }

        private static NetworkVariable<bool> CreateBoolVariable()
        {
            return new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        private static NetworkVariable<byte> CreateByteVariable(
            byte initialValue = 0)
        {
            return new NetworkVariable<byte>(
                initialValue,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        private static NetworkVariable<double> CreateDoubleVariable()
        {
            return new NetworkVariable<double>(
                0d,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        private static NetworkVariable<uint> CreateUIntVariable()
        {
            return new NetworkVariable<uint>(
                0U,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        private static NetworkVariable<ulong> CreateULongVariable(
            ulong initialValue = 0UL)
        {
            return new NetworkVariable<ulong>(
                initialValue,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        private static NetworkVariable<Vector3>
            CreateRunnerPositionVariable()
        {
            return new NetworkVariable<Vector3>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        private static byte SetMaskBit(
            byte mask,
            int slot,
            bool enabled)
        {
            var bit = 1 << slot;
            return enabled
                ? (byte)(mask | bit)
                : (byte)(mask & ~bit);
        }

        private static bool IsMaskBitSet(byte mask, int slot)
        {
            return (mask & (1 << slot)) != 0;
        }

        private static uint WritePackedByte(
            uint packed,
            int slot,
            int value)
        {
            if (!StableFootingRules.IsValidPlayerSlot(slot))
            {
                return packed;
            }
            var shift = slot * 8;
            var clearMask = ~(0xFFU << shift);
            return (packed & clearMask) |
                   ((uint)Mathf.Clamp(value, 0, byte.MaxValue) << shift);
        }

        private static int ReadPackedByte(uint packed, int slot)
        {
            if (!StableFootingRules.IsValidPlayerSlot(slot))
            {
                return 0;
            }
            return (int)((packed >> (slot * 8)) & 0xFFU);
        }

        private static bool IsFinite(Vector2 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsInfinity(value.y);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
