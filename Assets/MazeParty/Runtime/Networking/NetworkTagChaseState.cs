using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.TagChase;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum NetworkTagChasePhase : byte
    {
        Inactive,
        Countdown,
        Running,
        RoundResult,
        Complete
    }

    public struct TagChasePlayerSnapshot :
        INetworkSerializable,
        IEquatable<TagChasePlayerSnapshot>
    {
        public Vector2 Position;
        public Vector2 Facing;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Facing);
        }

        public bool Equals(TagChasePlayerSnapshot other)
        {
            return Position == other.Position &&
                   Facing == other.Facing;
        }

        public override bool Equals(object obj)
        {
            return obj is TagChasePlayerSnapshot other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Position, Facing);
        }
    }

    /// <summary>
    /// Server-authoritative four-round asymmetric chase simulation. Every
    /// player becomes the tagger exactly once in a seeded order.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkTagChaseState : NetworkBehaviour
    {
        public const double CountdownSeconds = 3d;
        public const double RoundResultSeconds = 4d;
        public const float ArenaCenterX = 1060f;
        public const float ArenaHalfWidth = 12f;
        public const float ArenaHalfDepth = 10f;
        public const float PlayerCollisionRadius = 0.58f;
        public const float RunnerMoveSpeed = 2.75f;
        public const float TaggerMoveSpeed = 5f;
        public const float CatchRange = 2.15f;
        public const float CatchFacingCosine = 0.45f;
        public const double CatchCooldownSeconds = 0.65d;

        private const float MaximumSimulationStepSeconds = 0.05f;
        private const byte NoTagger = byte.MaxValue;

        private static readonly Rect[] ObstacleRects =
        {
            new Rect(ArenaCenterX - 5.1f, -5.1f, 2.4f, 2.4f),
            new Rect(ArenaCenterX + 2.7f, -5.1f, 2.4f, 2.4f),
            new Rect(ArenaCenterX - 5.1f, 2.7f, 2.4f, 2.4f),
            new Rect(ArenaCenterX + 2.7f, 2.7f, 2.4f, 2.4f)
        };

        private static readonly Vector2[] RunnerStartOffsets =
        {
            new Vector2(-8.2f, -6.8f),
            new Vector2(8.2f, -6.8f),
            new Vector2(0f, 7.2f)
        };

        private readonly NetworkVariable<bool> _matchActive =
            CreateBoolVariable();
        private readonly NetworkVariable<bool> _paused =
            CreateBoolVariable();
        private readonly NetworkVariable<byte> _phase =
            CreateByteVariable((byte)NetworkTagChasePhase.Inactive);
        private readonly NetworkVariable<byte> _roundNumber =
            CreateByteVariable(0);
        private readonly NetworkVariable<byte> _taggerSlot =
            CreateByteVariable(NoTagger);
        private readonly NetworkVariable<byte> _caughtMask =
            CreateByteVariable(0);
        private readonly NetworkVariable<double> _phaseEndsAt =
            CreateDoubleVariable();
        private readonly NetworkVariable<double> _pausedPhaseRemaining =
            CreateDoubleVariable();
        private readonly NetworkVariable<uint> _inputEpoch =
            CreateUIntVariable();
        private readonly NetworkVariable<ulong> _totalScores =
            CreateULongVariable();
        private readonly NetworkVariable<uint> _finalRanks =
            CreateUIntVariable();

        private readonly NetworkList<TagChasePlayerSnapshot>
            _playerSnapshots =
                new NetworkList<TagChasePlayerSnapshot>(
                    default,
                    NetworkVariableReadPermission.Everyone,
                    NetworkVariableWritePermission.Server);

        private readonly Vector2[] _serverInputs =
            new Vector2[TagChaseRules.PlayerCount];
        private readonly float[] _serverViewYaw =
            new float[TagChaseRules.PlayerCount];
        private readonly Vector2[] _playerPositions =
            new Vector2[TagChaseRules.PlayerCount];
        private readonly Vector2[] _playerFacings =
            new Vector2[TagChaseRules.PlayerCount];
        private readonly int[] _totalPoints =
            new int[TagChaseRules.PlayerCount];
        private readonly NetworkPlayerAvatar[] _avatars =
            new NetworkPlayerAvatar[TagChaseRules.PlayerCount];

        private int[] _taggerOrder;
        private IReadOnlyList<TagChaseLeaderboardEntry>
            _finalLeaderboard;
        private double _lastSimulationAt;
        private double _nextCatchAllowedAt;
        private double _pausedCatchCooldownRemaining;
        private bool _completionReported;

        public static NetworkTagChaseState Instance
        {
            get;
            private set;
        }

        public NetworkTagChasePhase Phase =>
            (NetworkTagChasePhase)_phase.Value;
        public int RoundNumber => _roundNumber.Value;
        public int TaggerSlot =>
            _taggerSlot.Value == NoTagger
                ? -1
                : _taggerSlot.Value;
        public byte CaughtMask => _caughtMask.Value;
        public uint InputEpoch => _inputEpoch.Value;
        public bool IsPaused => _paused.Value;
        public bool IsMatchActive => _matchActive.Value;
        public double Remaining => GetRemaining(
            _phaseEndsAt.Value,
            _pausedPhaseRemaining.Value,
            Phase == NetworkTagChasePhase.Inactive ||
            Phase == NetworkTagChasePhase.Complete);
        public static int ObstacleCount => ObstacleRects.Length;

        private double ServerNow =>
            NetworkManager != null && NetworkManager.IsListening
                ? NetworkManager.ServerTime.Time
                : Time.unscaledTimeAsDouble;

        public override void OnNetworkSpawn()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError(
                    "More than one NetworkTagChaseState is spawned.");
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
            if (Phase == NetworkTagChasePhase.Running)
            {
                SimulateToOnServer(now);
                SyncPlayerSnapshotsOnServer();
                if (TagChaseRules.AreAllRunnersCaught(
                        TaggerSlot,
                        _caughtMask.Value) ||
                    now >= _phaseEndsAt.Value)
                {
                    CompleteRoundOnServer(now);
                }
                return;
            }

            if (_phaseEndsAt.Value <= 0d ||
                now < _phaseEndsAt.Value)
            {
                return;
            }

            switch (Phase)
            {
                case NetworkTagChasePhase.Countdown:
                    BeginRunOnServer(now);
                    break;
                case NetworkTagChasePhase.RoundResult:
                    if (_roundNumber.Value < TagChaseRules.RoundCount)
                    {
                        BeginRoundCountdownOnServer(
                            now,
                            _roundNumber.Value + 1);
                    }
                    else
                    {
                        CompleteMatchOnServer();
                    }
                    break;
            }
        }

        public void BeginMatchOnServer(ulong matchSeed)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            ClearLocalRuntime();
            ResetReplicatedStateOnServer();
            _taggerOrder = TagChaseRules.BuildTaggerOrder(matchSeed);
            _matchActive.Value = true;
            _paused.Value = false;
            _completionReported = false;
            CacheAndFreezeBoardAvatarsOnServer();
            BeginRoundCountdownOnServer(ServerNow, 1);
            Debug.Log(
                "[Tag Chase] Match started. Seed " + matchSeed +
                ", tagger order " +
                string.Join("-", Array.ConvertAll(
                    _taggerOrder,
                    slot => "P" + (slot + 1))) + ".");
        }

        public bool CanAcceptInputForSlot(int slot)
        {
            return TagChaseRules.IsValidPlayerSlot(slot) &&
                   _matchActive.Value &&
                   !_paused.Value &&
                   Phase == NetworkTagChasePhase.Running &&
                   !IsCaught(slot) &&
                   Remaining > 0d;
        }

        public bool ReceiveInputOnServer(
            NetworkPlayerAvatar avatar,
            Vector2 input,
            int roundNumber,
            uint inputEpoch)
        {
            if (!TryResolveAuthoritativeSlot(avatar, out var slot) ||
                !ValidateInputEnvelope(roundNumber, inputEpoch) ||
                !IsFinite(input) ||
                IsCaught(slot))
            {
                return false;
            }

            _avatars[slot] = avatar;
            _serverInputs[slot] =
                Vector2.ClampMagnitude(input, 1f);
            return true;
        }

        public bool ReceiveLookOnServer(
            NetworkPlayerAvatar avatar,
            float yaw,
            int roundNumber,
            uint inputEpoch)
        {
            if (!TryResolveAuthoritativeSlot(avatar, out var slot) ||
                slot != TaggerSlot ||
                !ValidateInputEnvelope(roundNumber, inputEpoch) ||
                !IsFinite(yaw))
            {
                return false;
            }

            _serverViewYaw[slot] =
                Mathf.Repeat(yaw, 360f);
            _playerFacings[slot] =
                DirectionFromYaw(_serverViewYaw[slot]);
            return true;
        }

        public bool RequestCatchOnServer(
            NetworkPlayerAvatar avatar,
            int roundNumber,
            uint inputEpoch)
        {
            if (!TryResolveAuthoritativeSlot(avatar, out var slot) ||
                slot != TaggerSlot ||
                !ValidateInputEnvelope(roundNumber, inputEpoch))
            {
                return false;
            }

            var now = ServerNow;
            if (now < _nextCatchAllowedAt)
            {
                return false;
            }

            SimulateToOnServer(now);
            _nextCatchAllowedAt =
                now + CatchCooldownSeconds;
            var target = FindCatchTarget(slot);
            if (target < 0)
            {
                Debug.Log(
                    "[Tag Chase] P" + (slot + 1) +
                    " attacked but caught nobody.");
                return false;
            }

            _caughtMask.Value =
                (byte)(_caughtMask.Value | (1 << target));
            _serverInputs[target] = Vector2.zero;
            Debug.Log(
                "[Tag Chase] P" + (slot + 1) +
                " caught P" + (target + 1) +
                " in round " + _roundNumber.Value + ".");

            if (TagChaseRules.AreAllRunnersCaught(
                    TaggerSlot,
                    _caughtMask.Value))
            {
                CompleteRoundOnServer(now);
            }
            return true;
        }

        public Vector2 GetPlayerPosition(int slot)
        {
            return TryGetPlayerSnapshot(slot, out var snapshot)
                ? snapshot.Position
                : new Vector2(ArenaCenterX, 0f);
        }

        public Vector2 GetPlayerFacing(int slot)
        {
            return TryGetPlayerSnapshot(slot, out var snapshot)
                ? snapshot.Facing
                : Vector2.up;
        }

        public bool IsTagger(int slot)
        {
            return TagChaseRules.IsValidPlayerSlot(slot) &&
                   slot == TaggerSlot;
        }

        public bool IsCaught(int slot)
        {
            return TagChaseRules.IsValidPlayerSlot(slot) &&
                   !IsTagger(slot) &&
                   (_caughtMask.Value & (1 << slot)) != 0;
        }

        public int GetTotalScore(int slot)
        {
            if (!TagChaseRules.IsValidPlayerSlot(slot))
            {
                return 0;
            }

            return (int)(
                (_totalScores.Value >> (slot * 16)) & 0xffffUL);
        }

        public int GetFinalRank(int slot)
        {
            if (!TagChaseRules.IsValidPlayerSlot(slot))
            {
                return 0;
            }

            return (int)(
                (_finalRanks.Value >> (slot * 8)) & 0xffU);
        }

        public static Rect GetObstacleRect(int index)
        {
            if (index < 0 || index >= ObstacleRects.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ObstacleRects[index];
        }

        public void PauseOnServer(double now)
        {
            if (!IsSpawned || !IsServer ||
                !_matchActive.Value || _paused.Value)
            {
                return;
            }

            if (Phase == NetworkTagChasePhase.Running)
            {
                SimulateToOnServer(now);
            }

            _pausedPhaseRemaining.Value =
                Math.Max(0d, _phaseEndsAt.Value - now);
            _pausedCatchCooldownRemaining =
                Math.Max(0d, _nextCatchAllowedAt - now);
            _phaseEndsAt.Value = 0d;
            _paused.Value = true;
            ClearInputsOnServer();
            AdvanceInputEpochOnServer();
            SyncPlayerSnapshotsOnServer();
        }

        public void ResumeOnServer(double now)
        {
            if (!IsSpawned || !IsServer ||
                !_matchActive.Value || !_paused.Value)
            {
                return;
            }

            _phaseEndsAt.Value =
                now + Math.Max(0d, _pausedPhaseRemaining.Value);
            _nextCatchAllowedAt =
                now + _pausedCatchCooldownRemaining;
            _pausedPhaseRemaining.Value = 0d;
            _pausedCatchCooldownRemaining = 0d;
            _lastSimulationAt = now;
            _paused.Value = false;
            ClearInputsOnServer();
            AdvanceInputEpochOnServer();
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

        public static Vector2 GetRoundStartPosition(
            int taggerSlot,
            int playerSlot)
        {
            if (!TagChaseRules.IsValidPlayerSlot(taggerSlot) ||
                !TagChaseRules.IsValidPlayerSlot(playerSlot))
            {
                return new Vector2(ArenaCenterX, 0f);
            }

            if (playerSlot == taggerSlot)
            {
                return new Vector2(ArenaCenterX, 0f);
            }

            var runnerIndex = 0;
            for (var slot = 0; slot < TagChaseRules.PlayerCount; slot++)
            {
                if (slot == taggerSlot)
                {
                    continue;
                }
                if (slot == playerSlot)
                {
                    return new Vector2(ArenaCenterX, 0f) +
                           RunnerStartOffsets[runnerIndex];
                }
                runnerIndex++;
            }

            return new Vector2(ArenaCenterX, 0f);
        }

        private void BeginRoundCountdownOnServer(
            double now,
            int roundNumber)
        {
            _roundNumber.Value = (byte)Mathf.Clamp(
                roundNumber,
                1,
                TagChaseRules.RoundCount);
            _taggerSlot.Value =
                (byte)_taggerOrder[_roundNumber.Value - 1];
            _caughtMask.Value = 0;
            _nextCatchAllowedAt = 0d;
            _pausedCatchCooldownRemaining = 0d;
            ClearInputsOnServer();

            for (var slot = 0;
                 slot < TagChaseRules.PlayerCount;
                 slot++)
            {
                _playerPositions[slot] =
                    GetRoundStartPosition(TaggerSlot, slot);
                var facing =
                    new Vector2(ArenaCenterX, 0f) -
                    _playerPositions[slot];
                if (slot == TaggerSlot)
                {
                    facing = RunnerStartOffsets[0];
                }
                _playerFacings[slot] =
                    facing.sqrMagnitude > 0.0001f
                        ? facing.normalized
                        : Vector2.up;
                _serverViewYaw[slot] =
                    Mathf.Atan2(
                        _playerFacings[slot].x,
                        _playerFacings[slot].y) *
                    Mathf.Rad2Deg;
            }

            AdvanceInputEpochOnServer();
            _phase.Value =
                (byte)NetworkTagChasePhase.Countdown;
            _phaseEndsAt.Value = now + CountdownSeconds;
            SyncPlayerSnapshotsOnServer();
            Debug.Log(
                "[Tag Chase] Round " + _roundNumber.Value +
                " countdown. Tagger: P" +
                (TaggerSlot + 1) + ".");
        }

        private void BeginRunOnServer(double now)
        {
            ClearInputsOnServer();
            AdvanceInputEpochOnServer();
            _phase.Value =
                (byte)NetworkTagChasePhase.Running;
            _phaseEndsAt.Value =
                now + TagChaseRules.RoundSeconds;
            _lastSimulationAt = now;
            _nextCatchAllowedAt = now;
            Debug.Log(
                "[Tag Chase] Round " + _roundNumber.Value +
                " started. Tagger P" + (TaggerSlot + 1) +
                " has 60 seconds.");
        }

        private void CompleteRoundOnServer(double now)
        {
            if (Phase != NetworkTagChasePhase.Running)
            {
                return;
            }

            SimulateToOnServer(
                Math.Min(now, _phaseEndsAt.Value));
            var points = TagChaseRules.BuildRoundPoints(
                TaggerSlot,
                _caughtMask.Value);
            for (var slot = 0;
                 slot < TagChaseRules.PlayerCount;
                 slot++)
            {
                _totalPoints[slot] += points[slot];
            }
            SyncTotalScoresOnServer();

            var taggerWon =
                TagChaseRules.AreAllRunnersCaught(
                    TaggerSlot,
                    _caughtMask.Value);
            ClearInputsOnServer();
            AdvanceInputEpochOnServer();
            _phase.Value =
                (byte)NetworkTagChasePhase.RoundResult;
            _phaseEndsAt.Value =
                now + RoundResultSeconds;
            Debug.Log(
                "[Tag Chase] Round " + _roundNumber.Value +
                " complete. " +
                (taggerWon ? "Tagger victory. " : "Runner victory. ") +
                BuildRoundScoreLog(points));
        }

        private void CompleteMatchOnServer()
        {
            if (_completionReported)
            {
                return;
            }

            _finalLeaderboard =
                TagChaseRules.BuildFinalLeaderboard(_totalPoints);
            uint ranks = 0U;
            for (var index = 0;
                 index < _finalLeaderboard.Count;
                 index++)
            {
                var entry = _finalLeaderboard[index];
                ranks |=
                    (uint)entry.Rank << (entry.PlayerSlot * 8);
            }
            _finalRanks.Value = ranks;

            var match = NetworkMatchState.Instance;
            if (match == null ||
                !match.TryCompleteTagChaseOnServer(
                    _finalLeaderboard))
            {
                return;
            }

            _completionReported = true;
            _matchActive.Value = false;
            _phase.Value =
                (byte)NetworkTagChasePhase.Complete;
            _phaseEndsAt.Value = 0d;
            ClearInputsOnServer();
            FreezeBoardAvatarsOnServer();
            Debug.Log(
                "[Tag Chase] Match complete. " +
                BuildTotalScoreLog());
        }

        private void SimulateToOnServer(double targetTime)
        {
            if (Phase != NetworkTagChasePhase.Running)
            {
                return;
            }

            targetTime = Math.Min(
                targetTime,
                _phaseEndsAt.Value);
            if (_lastSimulationAt <= 0d)
            {
                _lastSimulationAt = targetTime;
            }

            while (_lastSimulationAt + 0.000000001d < targetTime)
            {
                var next = Math.Min(
                    targetTime,
                    _lastSimulationAt +
                    MaximumSimulationStepSeconds);
                SimulateStepOnServer(
                    (float)(next - _lastSimulationAt));
                _lastSimulationAt = next;
            }
        }

        private void SimulateStepOnServer(float deltaSeconds)
        {
            for (var slot = 0;
                 slot < TagChaseRules.PlayerCount;
                 slot++)
            {
                if (IsCaught(slot))
                {
                    continue;
                }

                var input =
                    Vector2.ClampMagnitude(_serverInputs[slot], 1f);
                if (input.sqrMagnitude <= 0.0001f)
                {
                    continue;
                }

                Vector2 direction;
                if (slot == TaggerSlot)
                {
                    var forward =
                        DirectionFromYaw(_serverViewYaw[slot]);
                    var right =
                        new Vector2(forward.y, -forward.x);
                    direction =
                        Vector2.ClampMagnitude(
                            right * input.x +
                            forward * input.y,
                            1f);
                }
                else
                {
                    direction = input;
                }

                var speed = slot == TaggerSlot
                    ? TaggerMoveSpeed
                    : RunnerMoveSpeed;
                _playerPositions[slot] =
                    ResolvePlayerMovement(
                        slot,
                        _playerPositions[slot],
                        direction * speed * deltaSeconds);
                if (direction.sqrMagnitude > 0.0001f)
                {
                    _playerFacings[slot] =
                        direction.normalized;
                }
            }
        }

        private Vector2 ResolvePlayerMovement(
            int slot,
            Vector2 origin,
            Vector2 displacement)
        {
            var proposed =
                ClampPlayerPosition(origin + displacement);
            if (CanPlayerOccupy(slot, proposed))
            {
                return proposed;
            }

            var xOnly = ClampPlayerPosition(
                origin + new Vector2(displacement.x, 0f));
            if (CanPlayerOccupy(slot, xOnly))
            {
                origin = xOnly;
            }

            var yOnly = ClampPlayerPosition(
                origin + new Vector2(0f, displacement.y));
            return CanPlayerOccupy(slot, yOnly)
                ? yOnly
                : origin;
        }

        private bool CanPlayerOccupy(int slot, Vector2 position)
        {
            for (var obstacleIndex = 0;
                 obstacleIndex < ObstacleRects.Length;
                 obstacleIndex++)
            {
                var expanded =
                    ExpandRect(
                        ObstacleRects[obstacleIndex],
                        PlayerCollisionRadius);
                if (expanded.Contains(position))
                {
                    return false;
                }
            }

            var minimumDistance =
                PlayerCollisionRadius * 2f;
            var minimumSquared =
                minimumDistance * minimumDistance;
            for (var other = 0;
                 other < TagChaseRules.PlayerCount;
                 other++)
            {
                if (other == slot || IsCaught(other))
                {
                    continue;
                }

                if ((_playerPositions[other] - position)
                    .sqrMagnitude < minimumSquared)
                {
                    return false;
                }
            }

            return true;
        }

        private int FindCatchTarget(int taggerSlot)
        {
            var origin = _playerPositions[taggerSlot];
            var facing = _playerFacings[taggerSlot];
            var maximumSquared = CatchRange * CatchRange;
            var bestDistance = float.MaxValue;
            var bestSlot = -1;
            for (var slot = 0;
                 slot < TagChaseRules.PlayerCount;
                 slot++)
            {
                if (slot == taggerSlot || IsCaught(slot))
                {
                    continue;
                }

                var offset = _playerPositions[slot] - origin;
                var distanceSquared = offset.sqrMagnitude;
                if (distanceSquared > maximumSquared ||
                    distanceSquared >= bestDistance ||
                    distanceSquared <= 0.000001f)
                {
                    continue;
                }

                var direction =
                    offset / Mathf.Sqrt(distanceSquared);
                if (Vector2.Dot(facing, direction) <
                    CatchFacingCosine)
                {
                    continue;
                }

                bestSlot = slot;
                bestDistance = distanceSquared;
            }

            return bestSlot;
        }

        private static Vector2 ClampPlayerPosition(Vector2 position)
        {
            position.x = Mathf.Clamp(
                position.x,
                ArenaCenterX - ArenaHalfWidth +
                PlayerCollisionRadius,
                ArenaCenterX + ArenaHalfWidth -
                PlayerCollisionRadius);
            position.y = Mathf.Clamp(
                position.y,
                -ArenaHalfDepth + PlayerCollisionRadius,
                ArenaHalfDepth - PlayerCollisionRadius);
            return position;
        }

        private static Rect ExpandRect(Rect rect, float amount)
        {
            return new Rect(
                rect.xMin - amount,
                rect.yMin - amount,
                rect.width + amount * 2f,
                rect.height + amount * 2f);
        }

        private void SyncPlayerSnapshotsOnServer()
        {
            EnsurePlayerSnapshotCountOnServer();
            for (var slot = 0;
                 slot < TagChaseRules.PlayerCount;
                 slot++)
            {
                var snapshot =
                    new TagChasePlayerSnapshot
                    {
                        Position = _playerPositions[slot],
                        Facing = _playerFacings[slot]
                    };
                if (!_playerSnapshots[slot].Equals(snapshot))
                {
                    _playerSnapshots[slot] = snapshot;
                }
            }
        }

        private void SyncTotalScoresOnServer()
        {
            ulong packed = 0UL;
            for (var slot = 0;
                 slot < TagChaseRules.PlayerCount;
                 slot++)
            {
                packed |=
                    (ulong)Mathf.Clamp(
                        _totalPoints[slot],
                        0,
                        ushort.MaxValue) <<
                    (slot * 16);
            }

            _totalScores.Value = packed;
        }

        private bool ValidateInputEnvelope(
            int roundNumber,
            uint inputEpoch)
        {
            return _matchActive.Value &&
                   !_paused.Value &&
                   Phase == NetworkTagChasePhase.Running &&
                   roundNumber == _roundNumber.Value &&
                   inputEpoch != 0U &&
                   inputEpoch == _inputEpoch.Value &&
                   Remaining > 0d;
        }

        private bool TryResolveAuthoritativeSlot(
            NetworkPlayerAvatar avatar,
            out int slot)
        {
            slot = avatar != null ? avatar.AssignedSlot : -1;
            return IsSpawned && IsServer &&
                   avatar != null && avatar.IsSpawned &&
                   TagChaseRules.IsValidPlayerSlot(slot);
        }

        private bool TryGetPlayerSnapshot(
            int slot,
            out TagChasePlayerSnapshot snapshot)
        {
            if (TagChaseRules.IsValidPlayerSlot(slot) &&
                slot < _playerSnapshots.Count)
            {
                snapshot = _playerSnapshots[slot];
                return true;
            }

            snapshot = default;
            return false;
        }

        private void CacheAndFreezeBoardAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0;
                 slot < TagChaseRules.PlayerCount;
                 slot++)
            {
                _avatars[slot] =
                    match != null
                        ? match.GetAvatarForSlot(slot)
                        : null;
                _avatars[slot]?.StopServerInputOnServer();
            }
        }

        private void FreezeBoardAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0;
                 slot < TagChaseRules.PlayerCount;
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

        private void ResetReplicatedStateOnServer()
        {
            _matchActive.Value = false;
            _paused.Value = false;
            _phase.Value =
                (byte)NetworkTagChasePhase.Inactive;
            _roundNumber.Value = 0;
            _taggerSlot.Value = NoTagger;
            _caughtMask.Value = 0;
            _phaseEndsAt.Value = 0d;
            _pausedPhaseRemaining.Value = 0d;
            _inputEpoch.Value = 0U;
            _totalScores.Value = 0UL;
            _finalRanks.Value = 0U;
            ClearInputsOnServer();
            Array.Clear(
                _totalPoints,
                0,
                _totalPoints.Length);
            for (var slot = 0;
                 slot < TagChaseRules.PlayerCount;
                 slot++)
            {
                _playerPositions[slot] =
                    new Vector2(ArenaCenterX, 0f);
                _playerFacings[slot] = Vector2.up;
                _serverViewYaw[slot] = 0f;
            }
            EnsurePlayerSnapshotCountOnServer();
            SyncPlayerSnapshotsOnServer();
        }

        private void EnsurePlayerSnapshotCountOnServer()
        {
            while (_playerSnapshots.Count <
                   TagChaseRules.PlayerCount)
            {
                _playerSnapshots.Add(default);
            }
            while (_playerSnapshots.Count >
                   TagChaseRules.PlayerCount)
            {
                _playerSnapshots.RemoveAt(
                    _playerSnapshots.Count - 1);
            }
        }

        private void AdvanceInputEpochOnServer()
        {
            unchecked
            {
                _inputEpoch.Value =
                    _inputEpoch.Value == uint.MaxValue
                        ? 1U
                        : _inputEpoch.Value + 1U;
            }
        }

        private void ClearInputsOnServer()
        {
            for (var slot = 0;
                 slot < _serverInputs.Length;
                 slot++)
            {
                _serverInputs[slot] = Vector2.zero;
            }
        }

        private void ClearLocalRuntime()
        {
            _taggerOrder = null;
            _finalLeaderboard = null;
            _lastSimulationAt = 0d;
            _nextCatchAllowedAt = 0d;
            _pausedCatchCooldownRemaining = 0d;
            _completionReported = false;
            ClearInputsOnServer();
            Array.Clear(_avatars, 0, _avatars.Length);
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

        private string BuildRoundScoreLog(int[] points)
        {
            return "P1 +" + points[0] +
                   ", P2 +" + points[1] +
                   ", P3 +" + points[2] +
                   ", P4 +" + points[3] + ".";
        }

        private string BuildTotalScoreLog()
        {
            return "P1 " + _totalPoints[0] +
                   ", P2 " + _totalPoints[1] +
                   ", P3 " + _totalPoints[2] +
                   ", P4 " + _totalPoints[3] + ".";
        }

        private static Vector2 DirectionFromYaw(float yaw)
        {
            var radians = yaw * Mathf.Deg2Rad;
            return new Vector2(
                Mathf.Sin(radians),
                Mathf.Cos(radians));
        }

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }

        private static NetworkVariable<bool> CreateBoolVariable()
        {
            return new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        private static NetworkVariable<byte> CreateByteVariable(
            byte initialValue)
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

        private static NetworkVariable<ulong> CreateULongVariable()
        {
            return new NetworkVariable<ulong>(
                0UL,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }
    }
}
