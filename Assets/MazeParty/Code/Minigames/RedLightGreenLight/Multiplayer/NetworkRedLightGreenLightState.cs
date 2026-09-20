using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.RedLightGreenLight;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum NetworkRedLightGreenLightPhase : byte
    {
        Inactive,
        Countdown,
        Running,
        RoundResult,
        Complete
    }

    /// <summary>
    /// Server-authoritative Red Light / Green Light simulation. Board avatars
    /// stay frozen while four logical runners race in the additive scene.
    /// Voluntary input and collision displacement are intentionally separate so
    /// another player's push can never count as a Red-light violation.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkRedLightGreenLightState : NetworkBehaviour
    {
        public const float ArenaCenterX = 380f;
        public const float ArenaMinX = 370f;
        public const float ArenaMaxX = 390f;
        public const float ArenaMinZ = -22f;
        public const float ArenaMaxZ = 22f;
        public const float RunnerCollisionRadius = 0.7f;
        public const float RunnerLaneSpacing = 3f;

        private const float RunnerStartZ = ArenaMinZ + 0.75f;
        private const float FinishWorldZ = ArenaMaxZ - 0.5f;
        private const float RunnerBoundsPadding = 0.5f;
        private const float MovementInputThreshold = 0.0001f;
        private const int CollisionSolverIterations = 3;

        private readonly NetworkVariable<bool> _matchActive =
            CreateBoolVariable();
        private readonly NetworkVariable<bool> _paused =
            CreateBoolVariable();
        private readonly NetworkVariable<byte> _phase =
            CreateByteVariable((byte)NetworkRedLightGreenLightPhase.Inactive);
        private readonly NetworkVariable<byte> _roundNumber =
            CreateByteVariable();
        private readonly NetworkVariable<double> _phaseEndsAt =
            CreateDoubleVariable();
        private readonly NetworkVariable<double> _pausedPhaseRemaining =
            CreateDoubleVariable();
        private readonly NetworkVariable<byte> _signalPhase =
            CreateByteVariable((byte)RedLightGreenLightSignalPhase.Green);
        private readonly NetworkVariable<double> _signalPhaseEndsAt =
            CreateDoubleVariable();
        private readonly NetworkVariable<double> _pausedSignalRemaining =
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

        private readonly NetworkVariable<byte> _warnedMask =
            CreateByteVariable();
        private readonly NetworkVariable<byte> _eliminatedMask =
            CreateByteVariable();
        private readonly NetworkVariable<byte> _finishedMask =
            CreateByteVariable();
        private readonly NetworkVariable<uint> _scores =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _roundPoints =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _roundRanks =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _finalRanks =
            CreateUIntVariable();

        private readonly Vector2[] _serverInputs =
            new Vector2[RedLightGreenLightRules.PlayerCount];
        private readonly NetworkPlayerAvatar[] _avatars =
            new NetworkPlayerAvatar[RedLightGreenLightRules.PlayerCount];
        private readonly Vector3[] _nextPositions =
            new Vector3[RedLightGreenLightRules.PlayerCount];
        private readonly List<RedLightGreenLightRoundResult> _roundResults =
            new List<RedLightGreenLightRoundResult>(
                RedLightGreenLightRules.RoundCount);

        private RedLightGreenLightRoundState _roundState;
        private ulong _matchSeed;
        private double _runningStartedAt;
        private double _pausedRunningElapsed;
        private double _currentSignalWindowStart = -1d;
        private byte _penalizedThisRedMask;
        private bool _completionReported;

        public static NetworkRedLightGreenLightState Instance {
            get;
            private set;
        }

        public NetworkRedLightGreenLightPhase Phase =>
            (NetworkRedLightGreenLightPhase)_phase.Value;
        public int RoundNumber => _roundNumber.Value;
        public bool IsPaused => _paused.Value;
        public RedLightGreenLightSignalPhase SignalPhase =>
            (RedLightGreenLightSignalPhase)_signalPhase.Value;
        public RedLightGreenLightRoundEndReason RoundEndReason =>
            (RedLightGreenLightRoundEndReason)_roundEndReason.Value;
        public double Remaining => GetRemaining(
            _phaseEndsAt.Value,
            _pausedPhaseRemaining.Value,
            Phase == NetworkRedLightGreenLightPhase.Inactive ||
            Phase == NetworkRedLightGreenLightPhase.Complete);
        public double SignalRemaining => GetRemaining(
            _signalPhaseEndsAt.Value,
            _pausedSignalRemaining.Value,
            Phase != NetworkRedLightGreenLightPhase.Running);

        private double ServerNow =>
            NetworkManager != null && NetworkManager.IsListening
                ? NetworkManager.ServerTime.Time
                : Time.unscaledTimeAsDouble;

        public override void OnNetworkSpawn()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError(
                    "More than one NetworkRedLightGreenLightState is spawned.");
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
            if (Phase == NetworkRedLightGreenLightPhase.Running)
            {
                RefreshSignalWindowOnServer(now);
            }

            if (_phaseEndsAt.Value <= 0d || now < _phaseEndsAt.Value)
            {
                return;
            }

            switch (Phase)
            {
                case NetworkRedLightGreenLightPhase.Countdown:
                    BeginRunOnServer(now);
                    break;
                case NetworkRedLightGreenLightPhase.Running:
                    if (_roundState != null)
                    {
                        SyncAllProgressToRules();
                        _roundState.TryEndForTimeout(
                            RedLightGreenLightRules.RoundSeconds);
                        CompleteCurrentRoundOnServer(now);
                    }
                    break;
                case NetworkRedLightGreenLightPhase.RoundResult:
                    if (_roundNumber.Value <
                        RedLightGreenLightRules.RoundCount)
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
                Phase != NetworkRedLightGreenLightPhase.Running ||
                Remaining <= 0d || _roundState == null)
            {
                return;
            }

            FreezeBoardAvatarsOnServer();
            var now = ServerNow;
            RefreshSignalWindowOnServer(now);

            if (SignalPhase == RedLightGreenLightSignalPhase.Red)
            {
                EvaluateRedLightInputsOnServer(now);
            }
            else
            {
                SimulateRunnerMovementOnServer(Time.fixedDeltaTime, now);
            }

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
            _matchActive.Value = true;
            _paused.Value = false;
            _pausedPhaseRemaining.Value = 0d;
            _pausedSignalRemaining.Value = 0d;
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

            _serverInputs[slot] = Vector2.ClampMagnitude(input, 1f);
        }

        public bool CanAcceptInputForSlot(int slot)
        {
            if (!RedLightGreenLightRules.IsValidPlayerSlot(slot) ||
                !_matchActive.Value || _paused.Value ||
                Phase != NetworkRedLightGreenLightPhase.Running ||
                Remaining <= 0d)
            {
                return false;
            }

            // This method is also queried by each owning client before it
            // submits input. _roundState exists only on the server, so using
            // it here would make every non-host client permanently send zero.
            // The replicated masks are the client-safe source of truth.
            var playerState = GetPlayerState(slot);
            return playerState == RedLightGreenLightPlayerState.Healthy ||
                   playerState == RedLightGreenLightPlayerState.Warned;
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

        public RedLightGreenLightPlayerState GetPlayerState(int slot)
        {
            if (!RedLightGreenLightRules.IsValidPlayerSlot(slot))
            {
                return RedLightGreenLightPlayerState.Eliminated;
            }
            if (IsMaskBitSet(_finishedMask.Value, slot))
            {
                return RedLightGreenLightPlayerState.Finished;
            }
            if (IsMaskBitSet(_eliminatedMask.Value, slot))
            {
                return RedLightGreenLightPlayerState.Eliminated;
            }
            return IsMaskBitSet(_warnedMask.Value, slot)
                ? RedLightGreenLightPlayerState.Warned
                : RedLightGreenLightPlayerState.Healthy;
        }

        public int GetViolationCount(int slot)
        {
            if (!RedLightGreenLightRules.IsValidPlayerSlot(slot))
            {
                return 0;
            }
            if (IsMaskBitSet(_eliminatedMask.Value, slot))
            {
                return RedLightGreenLightRules.ViolationsToEliminate;
            }
            return IsMaskBitSet(_warnedMask.Value, slot) ? 1 : 0;
        }

        public int GetScore(int slot) =>
            ReadPackedByte(_scores.Value, slot);
        public int GetRoundPoints(int slot) =>
            ReadPackedByte(_roundPoints.Value, slot);
        public int GetRoundRank(int slot) =>
            ReadPackedByte(_roundRanks.Value, slot);
        public int GetFinalRank(int slot) =>
            ReadPackedByte(_finalRanks.Value, slot);

        public float GetForwardProgress(int slot)
        {
            return RedLightGreenLightRules.IsValidPlayerSlot(slot)
                ? Mathf.Max(0f, GetRunnerPosition(slot).z - RunnerStartZ)
                : 0f;
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
            _pausedSignalRemaining.Value =
                Phase == NetworkRedLightGreenLightPhase.Running &&
                _signalPhaseEndsAt.Value > 0d
                    ? Math.Max(0d, _signalPhaseEndsAt.Value - now)
                    : 0d;
            _pausedRunningElapsed =
                Phase == NetworkRedLightGreenLightPhase.Running
                    ? GetRunningElapsed(now)
                    : 0d;
            _phaseEndsAt.Value = 0d;
            _signalPhaseEndsAt.Value = 0d;
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
            if (Phase == NetworkRedLightGreenLightPhase.Running)
            {
                _runningStartedAt = now - _pausedRunningElapsed;
                _signalPhaseEndsAt.Value =
                    now + Math.Max(0d, _pausedSignalRemaining.Value);
            }
            _pausedPhaseRemaining.Value = 0d;
            _pausedSignalRemaining.Value = 0d;
            _pausedRunningElapsed = 0d;
            _paused.Value = false;
            FreezeBoardAvatarsOnServer();
        }

        public bool RestoreAvatarForReconnectOnServer(
            NetworkPlayerAvatar avatar)
        {
            if (!TryResolveAuthoritativeSlot(avatar, out var slot))
            {
                return false;
            }

            _avatars[slot] = avatar;
            _serverInputs[slot] = Vector2.zero;
            avatar.StopServerInputOnServer();
            return true;
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
            _roundState = new RedLightGreenLightRoundState(
                _matchSeed,
                roundNumber);
            _roundNumber.Value = (byte)roundNumber;
            _phase.Value = (byte)NetworkRedLightGreenLightPhase.Countdown;
            _phaseEndsAt.Value =
                now + RedLightGreenLightRules.CountdownSeconds;
            _pausedPhaseRemaining.Value = 0d;
            _signalPhase.Value =
                (byte)RedLightGreenLightSignalPhase.Green;
            _signalPhaseEndsAt.Value = 0d;
            _pausedSignalRemaining.Value = 0d;
            _roundEndReason.Value =
                (byte)RedLightGreenLightRoundEndReason.None;
            _warnedMask.Value = 0;
            _eliminatedMask.Value = 0;
            _finishedMask.Value = 0;
            _roundPoints.Value = 0U;
            _roundRanks.Value = 0U;
            _penalizedThisRedMask = 0;
            _currentSignalWindowStart = -1d;
            _runningStartedAt = 0d;

            for (var slot = 0;
                 slot < RedLightGreenLightRules.PlayerCount;
                 slot++)
            {
                _serverInputs[slot] = Vector2.zero;
                SetRunnerPositionOnServer(slot, GetStartPosition(slot));
            }
            FreezeBoardAvatarsOnServer();
        }

        private void BeginRunOnServer(double now)
        {
            _phase.Value = (byte)NetworkRedLightGreenLightPhase.Running;
            _phaseEndsAt.Value =
                now + RedLightGreenLightRules.RoundSeconds;
            _runningStartedAt = now;
            _currentSignalWindowStart = -1d;
            RefreshSignalWindowOnServer(now);
        }

        private void RefreshSignalWindowOnServer(double now)
        {
            if (_roundState == null ||
                Phase != NetworkRedLightGreenLightPhase.Running)
            {
                return;
            }

            var elapsed = GetRunningElapsed(now);
            var window = _roundState.SignalSchedule.GetWindowAt(elapsed);
            if (_currentSignalWindowStart.Equals(window.StartsAtSeconds) &&
                SignalPhase == window.Phase)
            {
                return;
            }

            _currentSignalWindowStart = window.StartsAtSeconds;
            _signalPhase.Value = (byte)window.Phase;
            _signalPhaseEndsAt.Value =
                _runningStartedAt + window.EndsAtSeconds;
            if (window.Phase == RedLightGreenLightSignalPhase.Red)
            {
                _penalizedThisRedMask = 0;
            }
        }

        private void EvaluateRedLightInputsOnServer(double now)
        {
            var elapsed = GetRunningElapsed(now);
            for (var slot = 0;
                 slot < RedLightGreenLightRules.PlayerCount;
                 slot++)
            {
                if (!CanAcceptInputForSlot(slot) ||
                    IsMaskBitSet(_penalizedThisRedMask, slot))
                {
                    continue;
                }

                var hasMovement =
                    _serverInputs[slot].sqrMagnitude >
                    MovementInputThreshold;
                var resolution = _roundState.SubmitMovementIntent(
                    slot,
                    hasMovement,
                    elapsed);
                if (!resolution.WasViolation)
                {
                    continue;
                }

                _penalizedThisRedMask = SetMaskBit(
                    _penalizedThisRedMask,
                    slot,
                    true);
                _serverInputs[slot] = Vector2.zero;
                SyncPlayerStateFromRules(slot);
                if (_roundState.IsComplete)
                {
                    break;
                }
            }
        }

        private void SimulateRunnerMovementOnServer(
            float deltaTime,
            double now)
        {
            for (var slot = 0;
                 slot < RedLightGreenLightRules.PlayerCount;
                 slot++)
            {
                var position = GetRunnerPosition(slot);
                var player = _roundState.GetPlayer(slot);
                if (player.CanMove)
                {
                    var input = Vector2.ClampMagnitude(
                        _serverInputs[slot],
                        1f);
                    position += new Vector3(input.x, 0f, input.y) *
                                (player.MovementSpeedMetersPerSecond *
                                 deltaTime);
                    position = ClampRunnerPosition(position);
                }
                _nextPositions[slot] = position;
            }

            ResolveRunnerCollisions();

            for (var slot = 0;
                 slot < RedLightGreenLightRules.PlayerCount;
                 slot++)
            {
                if (!_roundState.GetPlayer(slot).CanMove)
                {
                    continue;
                }

                SetRunnerPositionOnServer(slot, _nextPositions[slot]);
                _roundState.SetForwardProgress(
                    slot,
                    GetForwardProgress(slot));
            }

            var elapsed = GetRunningElapsed(now);
            for (var slot = 0;
                 slot < RedLightGreenLightRules.PlayerCount;
                 slot++)
            {
                if (!_roundState.GetPlayer(slot).CanMove ||
                    GetRunnerPosition(slot).z < FinishWorldZ)
                {
                    continue;
                }

                if (_roundState.TryFinish(
                        slot,
                        GetForwardProgress(slot),
                        elapsed))
                {
                    SyncPlayerStateFromRules(slot);
                    ClearAllInputsOnServer();
                    break;
                }
            }
        }

        private void ResolveRunnerCollisions()
        {
            var minimumDistance = RunnerCollisionRadius * 2f;
            for (var iteration = 0;
                 iteration < CollisionSolverIterations;
                 iteration++)
            {
                for (var left = 0;
                     left < RedLightGreenLightRules.PlayerCount;
                     left++)
                {
                    if (!_roundState.GetPlayer(left).CanMove)
                    {
                        continue;
                    }
                    for (var right = left + 1;
                         right < RedLightGreenLightRules.PlayerCount;
                         right++)
                    {
                        if (!_roundState.GetPlayer(right).CanMove)
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

        private void SyncAllProgressToRules()
        {
            if (_roundState == null || _roundState.IsComplete)
            {
                return;
            }

            for (var slot = 0;
                 slot < RedLightGreenLightRules.PlayerCount;
                 slot++)
            {
                if (_roundState.GetPlayer(slot).CanMove)
                {
                    _roundState.SetForwardProgress(
                        slot,
                        GetForwardProgress(slot));
                }
            }
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
            _phase.Value =
                (byte)NetworkRedLightGreenLightPhase.RoundResult;
            _phaseEndsAt.Value =
                now + RedLightGreenLightRules.ResultSeconds;
            _signalPhaseEndsAt.Value = 0d;
            ClearAllInputsOnServer();
        }

        private void CompleteMatchOnServer()
        {
            if (_completionReported ||
                _roundResults.Count != RedLightGreenLightRules.RoundCount)
            {
                return;
            }

            var leaderboard =
                RedLightGreenLightMatchScoring.BuildLeaderboard(
                    _roundResults);
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
                !match.TryCompleteRedLightGreenLightOnServer(leaderboard))
            {
                return;
            }

            _completionReported = true;
            _matchActive.Value = false;
            _phase.Value =
                (byte)NetworkRedLightGreenLightPhase.Complete;
            _phaseEndsAt.Value = 0d;
            _signalPhaseEndsAt.Value = 0d;
            ClearAllInputsOnServer();
            FreezeBoardAvatarsOnServer();
        }

        private void SyncPlayerStateFromRules(int slot)
        {
            var state = _roundState.GetPlayer(slot).State;
            _warnedMask.Value = SetMaskBit(
                _warnedMask.Value,
                slot,
                state == RedLightGreenLightPlayerState.Warned ||
                state == RedLightGreenLightPlayerState.Eliminated ||
                state == RedLightGreenLightPlayerState.Finished &&
                _roundState.GetPlayer(slot).ViolationCount > 0);
            _eliminatedMask.Value = SetMaskBit(
                _eliminatedMask.Value,
                slot,
                state == RedLightGreenLightPlayerState.Eliminated);
            _finishedMask.Value = SetMaskBit(
                _finishedMask.Value,
                slot,
                state == RedLightGreenLightPlayerState.Finished);
        }

        private bool TryResolveAuthoritativeSlot(
            NetworkPlayerAvatar avatar,
            out int slot)
        {
            slot = avatar != null ? avatar.AssignedSlot : -1;
            return IsSpawned && IsServer && avatar != null &&
                   avatar.IsSpawned &&
                   RedLightGreenLightRules.IsValidPlayerSlot(slot);
        }

        private void CacheAndFreezeBoardAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0;
                 slot < RedLightGreenLightRules.PlayerCount;
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
                 slot < RedLightGreenLightRules.PlayerCount;
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

        private static Vector3 GetStartPosition(int slot)
        {
            var centered =
                slot - (RedLightGreenLightRules.PlayerCount - 1) * 0.5f;
            return new Vector3(
                ArenaCenterX + centered * RunnerLaneSpacing,
                0f,
                RunnerStartZ);
        }

        private static Vector3 ClampRunnerPosition(Vector3 position)
        {
            position.x = Mathf.Clamp(
                position.x,
                ArenaMinX + RunnerBoundsPadding,
                ArenaMaxX - RunnerBoundsPadding);
            position.y = 0f;
            position.z = Mathf.Clamp(position.z, RunnerStartZ, ArenaMaxZ);
            return position;
        }

        private double GetRunningElapsed(double now)
        {
            return Math.Max(
                0d,
                Math.Min(
                    RedLightGreenLightRules.RoundSeconds,
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

        private void ResetReplicatedStateOnServer()
        {
            _matchActive.Value = false;
            _paused.Value = false;
            _phase.Value =
                (byte)NetworkRedLightGreenLightPhase.Inactive;
            _roundNumber.Value = 0;
            _phaseEndsAt.Value = 0d;
            _pausedPhaseRemaining.Value = 0d;
            _signalPhase.Value =
                (byte)RedLightGreenLightSignalPhase.Green;
            _signalPhaseEndsAt.Value = 0d;
            _pausedSignalRemaining.Value = 0d;
            _roundEndReason.Value = 0;
            _warnedMask.Value = 0;
            _eliminatedMask.Value = 0;
            _finishedMask.Value = 0;
            _scores.Value = 0U;
            _roundPoints.Value = 0U;
            _roundRanks.Value = 0U;
            _finalRanks.Value = 0U;
            for (var slot = 0;
                 slot < RedLightGreenLightRules.PlayerCount;
                 slot++)
            {
                SetRunnerPositionOnServer(slot, GetStartPosition(slot));
            }
        }

        private void ClearLocalRuntime()
        {
            _roundState = null;
            _roundResults.Clear();
            _matchSeed = 0UL;
            _runningStartedAt = 0d;
            _pausedRunningElapsed = 0d;
            _currentSignalWindowStart = -1d;
            _penalizedThisRedMask = 0;
            _completionReported = false;
            for (var slot = 0;
                 slot < RedLightGreenLightRules.PlayerCount;
                 slot++)
            {
                _serverInputs[slot] = Vector2.zero;
                _avatars[slot] = null;
                _nextPositions[slot] = Vector3.zero;
            }
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
            if (!RedLightGreenLightRules.IsValidPlayerSlot(slot))
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
            if (!RedLightGreenLightRules.IsValidPlayerSlot(slot))
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
