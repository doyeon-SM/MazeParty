using System;
using MazeParty.Gameplay.Minigames.CliffBarrage;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum NetworkCliffBarragePhase : byte
    {
        Inactive,
        Countdown,
        Playing,
        RoundResult,
        Complete
    }

    public struct CliffBarragePlayerNetworkSnapshot :
        INetworkSerializable, IEquatable<CliffBarragePlayerNetworkSnapshot>
    {
        public Vector2 Position;
        public Vector2 Facing;
        public byte HitCount;
        public bool IsEliminated;
        public byte RoundRank;
        public byte Score;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Facing);
            serializer.SerializeValue(ref HitCount);
            serializer.SerializeValue(ref IsEliminated);
            serializer.SerializeValue(ref RoundRank);
            serializer.SerializeValue(ref Score);
        }

        public bool Equals(CliffBarragePlayerNetworkSnapshot other)
        {
            return Position == other.Position && Facing == other.Facing &&
                HitCount == other.HitCount &&
                IsEliminated == other.IsEliminated &&
                RoundRank == other.RoundRank && Score == other.Score;
        }

        public override bool Equals(object obj)
        {
            return obj is CliffBarragePlayerNetworkSnapshot other &&
                Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Position);
            hash.Add(Facing);
            hash.Add(HitCount);
            hash.Add(IsEliminated);
            hash.Add(RoundRank);
            hash.Add(Score);
            return hash.ToHashCode();
        }
    }

    public struct CliffBarrageProjectileNetworkSnapshot :
        INetworkSerializable,
        IEquatable<CliffBarrageProjectileNetworkSnapshot>
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public bool Active;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Velocity);
            serializer.SerializeValue(ref Active);
        }

        public bool Equals(CliffBarrageProjectileNetworkSnapshot other)
        {
            return Position == other.Position &&
                Velocity == other.Velocity && Active == other.Active;
        }

        public override bool Equals(object obj)
        {
            return obj is CliffBarrageProjectileNetworkSnapshot other &&
                Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Position);
            hash.Add(Velocity);
            hash.Add(Active);
            return hash.ToHashCode();
        }
    }

    public struct CliffBarrageLaserNetworkSnapshot :
        INetworkSerializable,
        IEquatable<CliffBarrageLaserNetworkSnapshot>
    {
        public Vector2 Start;
        public Vector2 End;
        public CliffBarrageLaserPhase Phase;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Start);
            serializer.SerializeValue(ref End);
            var phase = (byte)Phase;
            serializer.SerializeValue(ref phase);
            Phase = (CliffBarrageLaserPhase)phase;
        }

        public bool Equals(CliffBarrageLaserNetworkSnapshot other)
        {
            return Start == other.Start && End == other.End &&
                Phase == other.Phase;
        }

        public override bool Equals(object obj)
        {
            return obj is CliffBarrageLaserNetworkSnapshot other &&
                Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Start);
            hash.Add(End);
            hash.Add(Phase);
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// Server-authoritative three-round cliff arena. Clients submit axes and
    /// edge-triggered pushes, tagged with the observed round/input epoch.
    /// Positions, hazard hits, lives, fall order and ranks are server-owned.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkCliffBarrageState : NetworkBehaviour
    {
        public const int ProjectilePoolSize =
            CliffBarrageRules.MaximumProjectiles;
        public const int LaserPoolSize =
            CliffBarrageRules.MaximumLasers;
        public const float ArenaCenterX = 1740f;
        public const float ArenaHalfExtent = 8f;
        private const double SnapshotIntervalSeconds = 0.05d;

        [SerializeField, Range(0, ProjectilePoolSize)]
        private int _projectileLimit = ProjectilePoolSize;
        [SerializeField, Range(0, LaserPoolSize)]
        private int _laserLimit = LaserPoolSize;

        private readonly NetworkVariable<bool> _matchActive =
            CreateBoolVariable();
        private readonly NetworkVariable<bool> _paused =
            CreateBoolVariable();
        private readonly NetworkVariable<byte> _phase =
            CreateByteVariable((byte)NetworkCliffBarragePhase.Inactive);
        private readonly NetworkVariable<byte> _roundNumber =
            CreateByteVariable();
        private readonly NetworkVariable<uint> _inputEpoch =
            CreateUIntVariable();
        private readonly NetworkVariable<double> _phaseEndsAt =
            CreateDoubleVariable();
        private readonly NetworkVariable<double> _pausedPhaseRemaining =
            CreateDoubleVariable();
        private readonly NetworkVariable<uint> _roundTransitionSequence =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _finalRanks =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _damageSequence =
            CreateUIntVariable();
        private readonly NetworkVariable<byte> _lastDamagedSlot =
            CreateByteVariable(byte.MaxValue);
        private readonly NetworkList<CliffBarragePlayerNetworkSnapshot>
            _playerSnapshots =
                new NetworkList<CliffBarragePlayerNetworkSnapshot>(
                    default,
                    NetworkVariableReadPermission.Everyone,
                    NetworkVariableWritePermission.Server);
        private readonly NetworkList<CliffBarrageProjectileNetworkSnapshot>
            _projectileSnapshots =
                new NetworkList<CliffBarrageProjectileNetworkSnapshot>(
                    default,
                    NetworkVariableReadPermission.Everyone,
                    NetworkVariableWritePermission.Server);
        private readonly NetworkList<CliffBarrageLaserNetworkSnapshot>
            _laserSnapshots =
                new NetworkList<CliffBarrageLaserNetworkSnapshot>(
                    default,
                    NetworkVariableReadPermission.Everyone,
                    NetworkVariableWritePermission.Server);
        private readonly NetworkPlayerAvatar[] _avatars =
            new NetworkPlayerAvatar[CliffBarrageRules.PlayerCount];

        private CliffBarrageMatchState _serverMatch;
        private double _phaseStartedAt;
        private double _phaseElapsedAtPause;
        private double _lastSnapshotAt;
        private bool _completionReported;

        public static NetworkCliffBarrageState Instance { get; private set; }
        public NetworkCliffBarragePhase Phase =>
            (NetworkCliffBarragePhase)_phase.Value;
        public int RoundNumber => _roundNumber.Value;
        public uint InputEpoch => _inputEpoch.Value;
        public uint RoundTransitionSequence =>
            _roundTransitionSequence.Value;
        public uint DamageSequence => _damageSequence.Value;
        public int LastDamagedSlot => _lastDamagedSlot.Value ==
            byte.MaxValue ? -1 : _lastDamagedSlot.Value;
        public bool IsMatchActive => _matchActive.Value;
        public bool IsPaused => _paused.Value;
        public double Remaining =>
            Phase == NetworkCliffBarragePhase.Inactive ||
            (!_matchActive.Value &&
             Phase == NetworkCliffBarragePhase.Complete)
                ? 0d
                : _paused.Value
                    ? Math.Max(0d, _pausedPhaseRemaining.Value)
                    : Math.Max(0d, _phaseEndsAt.Value - ServerNow);

        private double ServerNow =>
            NetworkManager != null && NetworkManager.IsListening
                ? NetworkManager.ServerTime.Time
                : Time.unscaledTimeAsDouble;

        public override void OnNetworkSpawn()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError(
                    "More than one NetworkCliffBarrageState is spawned.");
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
            switch (Phase)
            {
                case NetworkCliffBarragePhase.Countdown:
                    if (HasReachedDeadline(now))
                    {
                        BeginPlayingOnServer(now);
                    }
                    break;
                case NetworkCliffBarragePhase.Playing:
                    AdvancePlayingOnServer(now);
                    break;
                case NetworkCliffBarragePhase.RoundResult:
                    if (HasReachedDeadline(now))
                    {
                        BeginNextRoundOnServer(now);
                    }
                    break;
                case NetworkCliffBarragePhase.Complete:
                    if (HasReachedDeadline(now))
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
            var seed = unchecked((int)(matchSeed ^ (matchSeed >> 32)));
            _serverMatch = new CliffBarrageMatchState(seed,
                Mathf.Clamp(_projectileLimit, 0, ProjectilePoolSize),
                Mathf.Clamp(_laserLimit, 0, LaserPoolSize));
            _matchActive.Value = true;
            _roundNumber.Value = (byte)_serverMatch.RoundNumber;
            CacheAndFreezeBoardAvatarsOnServer();
            SyncSnapshotOnServer();
            var now = ServerNow;
            _phaseStartedAt = now;
            _phase.Value = (byte)NetworkCliffBarragePhase.Countdown;
            _phaseEndsAt.Value = now +
                CliffBarrageRules.CountdownSeconds;
            Debug.Log("[CliffBarrage] Three-round match started.");
        }

        public bool CanAcceptInputForSlot(int slot)
        {
            return CliffBarrageRules.IsValidSlot(slot) &&
                _matchActive.Value && !_paused.Value &&
                Phase == NetworkCliffBarragePhase.Playing &&
                Remaining > 0d && !IsEliminated(slot);
        }

        public bool RequestMovementInputOnServer(
            NetworkPlayerAvatar avatar, Vector2 input,
            byte roundNumber, uint inputEpoch)
        {
            var now = ServerNow;
            if (!TryValidateInput(avatar, roundNumber, inputEpoch,
                now, out var slot) || !IsFiniteUnitAxis(input))
            {
                return false;
            }
            avatar.StopServerInputOnServer();
            AdvancePlayingModelOnServer(now);
            if (_serverMatch.IsRoundComplete)
            {
                FinishRoundOnServer(now);
                return false;
            }
            input = Vector2.ClampMagnitude(input, 1f);
            _serverMatch.SetMovementInput(slot, input.x, input.y);
            return true;
        }

        public bool RequestPrimaryActionOnServer(
            NetworkPlayerAvatar avatar, byte roundNumber,
            uint inputEpoch)
        {
            var now = ServerNow;
            if (!TryValidateInput(avatar, roundNumber, inputEpoch,
                now, out var slot))
            {
                return false;
            }
            avatar.StopServerInputOnServer();
            AdvancePlayingModelOnServer(now);
            if (_serverMatch.IsRoundComplete)
            {
                FinishRoundOnServer(now);
                return false;
            }
            var hit = _serverMatch.TryPush(slot);
            SyncSnapshotOnServer();
            if (_serverMatch.IsRoundComplete)
            {
                FinishRoundOnServer(now);
            }
            return hit;
        }

        public Vector2 GetPlayerPosition(int slot)
        {
            return CliffBarrageRules.IsValidSlot(slot) &&
                slot < _playerSnapshots.Count
                    ? _playerSnapshots[slot].Position
                    : Vector2.zero;
        }

        public Vector2 GetPlayerFacing(int slot)
        {
            return CliffBarrageRules.IsValidSlot(slot) &&
                slot < _playerSnapshots.Count
                    ? _playerSnapshots[slot].Facing
                    : Vector2.up;
        }

        public int GetHitCount(int slot)
        {
            return CliffBarrageRules.IsValidSlot(slot) &&
                slot < _playerSnapshots.Count
                    ? _playerSnapshots[slot].HitCount : 0;
        }

        public bool IsEliminated(int slot)
        {
            return CliffBarrageRules.IsValidSlot(slot) &&
                slot < _playerSnapshots.Count &&
                _playerSnapshots[slot].IsEliminated;
        }

        public int GetRoundRank(int slot)
        {
            return CliffBarrageRules.IsValidSlot(slot) &&
                slot < _playerSnapshots.Count
                    ? _playerSnapshots[slot].RoundRank : 0;
        }

        public int GetScore(int slot)
        {
            return CliffBarrageRules.IsValidSlot(slot) &&
                slot < _playerSnapshots.Count
                    ? _playerSnapshots[slot].Score : 0;
        }

        public int GetFinalRank(int slot)
        {
            return CliffBarrageRules.IsValidSlot(slot)
                ? (int)((_finalRanks.Value >> (slot * 8)) & 0xffU)
                : 0;
        }

        public CliffBarrageProjectileNetworkSnapshot GetProjectile(
            int index)
        {
            return index >= 0 && index < _projectileSnapshots.Count
                ? _projectileSnapshots[index]
                : default;
        }

        public CliffBarrageLaserNetworkSnapshot GetLaser(int index)
        {
            return index >= 0 && index < _laserSnapshots.Count
                ? _laserSnapshots[index]
                : default;
        }

        public void PauseOnServer(double now)
        {
            if (!IsSpawned || !IsServer || !_matchActive.Value ||
                _paused.Value)
            {
                return;
            }
            if (Phase == NetworkCliffBarragePhase.Playing)
            {
                AdvancePlayingModelOnServer(now);
                if (_serverMatch != null && _serverMatch.IsRoundComplete)
                {
                    FinishRoundOnServer(now);
                }
                StopAllMovementOnServer();
                SyncSnapshotOnServer();
            }
            _phaseElapsedAtPause =
                Math.Max(0d, now - _phaseStartedAt);
            _pausedPhaseRemaining.Value =
                Math.Max(0d, _phaseEndsAt.Value - now);
            _phaseEndsAt.Value = 0d;
            _paused.Value = true;
            AdvanceInputEpochOnServer();
        }

        public void ResumeOnServer(double now)
        {
            if (!IsSpawned || !IsServer || !_matchActive.Value ||
                !_paused.Value)
            {
                return;
            }
            _phaseStartedAt = now - _phaseElapsedAtPause;
            _phaseEndsAt.Value = now +
                Math.Max(0d, _pausedPhaseRemaining.Value);
            _pausedPhaseRemaining.Value = 0d;
            _paused.Value = false;
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
            _serverMatch?.SetMovementInput(slot, 0d, 0d);
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

        private void BeginPlayingOnServer(double now)
        {
            if (_serverMatch == null)
            {
                return;
            }
            _roundNumber.Value = (byte)_serverMatch.RoundNumber;
            _phaseStartedAt = now;
            _phase.Value = (byte)NetworkCliffBarragePhase.Playing;
            _phaseEndsAt.Value = now +
                CliffBarrageRules.RoundDurationSeconds;
            AdvanceInputEpochOnServer();
            SyncSnapshotOnServer();
            _lastSnapshotAt = now;
        }

        private void AdvancePlayingOnServer(double now)
        {
            if (_serverMatch == null)
            {
                return;
            }
            AdvancePlayingModelOnServer(now);
            if (now - _lastSnapshotAt >=
                SnapshotIntervalSeconds ||
                _serverMatch.IsRoundComplete)
            {
                SyncSnapshotOnServer();
                _lastSnapshotAt = now;
            }
            if (_serverMatch.IsRoundComplete)
            {
                FinishRoundOnServer(now);
            }
        }

        private void AdvancePlayingModelOnServer(double now)
        {
            _serverMatch?.AdvanceTo(Math.Min(
                CliffBarrageRules.RoundDurationSeconds,
                Math.Max(0d, now - _phaseStartedAt)));
        }

        private void FinishRoundOnServer(double now)
        {
            if (_serverMatch == null ||
                !_serverMatch.IsRoundComplete ||
                Phase != NetworkCliffBarragePhase.Playing)
            {
                return;
            }
            StopAllMovementOnServer();
            SyncSnapshotOnServer();
            AdvanceInputEpochOnServer();
            _phaseStartedAt = now;
            if (_serverMatch.IsComplete)
            {
                PackFinalRanksOnServer();
                _phase.Value = (byte)NetworkCliffBarragePhase.Complete;
                _phaseEndsAt.Value = now +
                    CliffBarrageRules.ResultSeconds;
            }
            else
            {
                _phase.Value =
                    (byte)NetworkCliffBarragePhase.RoundResult;
                _phaseEndsAt.Value = now +
                    CliffBarrageRules.ResultSeconds;
            }
        }

        private void BeginNextRoundOnServer(double now)
        {
            if (_serverMatch == null || _serverMatch.IsComplete)
            {
                return;
            }
            _serverMatch.BeginNextRound();
            // The shared three-second countdown belongs to game start only.
            // Later rounds begin as soon as the result display ends.
            BeginPlayingOnServer(now);
        }

        private void CompleteMatchOnServer()
        {
            if (_completionReported || _serverMatch == null ||
                !_serverMatch.IsComplete)
            {
                return;
            }
            var ranks = new int[CliffBarrageRules.PlayerCount];
            for (var slot = 0; slot < ranks.Length; slot++)
            {
                ranks[slot] = _serverMatch.GetFinalRank(slot);
            }
            var match = NetworkMatchState.Instance;
            if (match == null ||
                !match.TryCompleteCliffBarrageOnServer(ranks))
            {
                return;
            }
            _completionReported = true;
            _matchActive.Value = false;
            _phaseEndsAt.Value = 0d;
            FreezeBoardAvatarsOnServer();
        }

        private void PackFinalRanksOnServer()
        {
            uint packed = 0U;
            for (var slot = 0; slot <
                CliffBarrageRules.PlayerCount; slot++)
            {
                packed |= (uint)_serverMatch.GetFinalRank(slot) <<
                    (slot * 8);
            }
            _finalRanks.Value = packed;
        }

        private void SyncSnapshotOnServer()
        {
            if (_serverMatch == null)
            {
                return;
            }
            EnsureListLengthsOnServer();
            for (var slot = 0; slot <
                CliffBarrageRules.PlayerCount; slot++)
            {
                var player = _serverMatch.GetPlayer(slot);
                var snapshot = new CliffBarragePlayerNetworkSnapshot
                {
                    Position = new Vector2((float)player.X,
                        (float)player.Z),
                    Facing = new Vector2((float)player.FacingX,
                        (float)player.FacingZ),
                    HitCount = (byte)player.HitCount,
                    IsEliminated = player.IsEliminated,
                    RoundRank = (byte)player.RoundRank,
                    Score = (byte)player.TotalScore
                };
                if (!_playerSnapshots[slot].Equals(snapshot))
                {
                    _playerSnapshots[slot] = snapshot;
                }
            }
            for (var index = 0; index < ProjectilePoolSize;
                index++)
            {
                var projectile = _serverMatch.GetProjectile(index);
                var snapshot =
                    new CliffBarrageProjectileNetworkSnapshot
                    {
                        Active = projectile.Active,
                        Position = new Vector2((float)projectile.X,
                            (float)projectile.Z),
                        Velocity = new Vector2(
                            (float)projectile.VelocityX,
                            (float)projectile.VelocityZ)
                    };
                if (!_projectileSnapshots[index].Equals(snapshot))
                {
                    _projectileSnapshots[index] = snapshot;
                }
            }
            for (var index = 0; index < LaserPoolSize; index++)
            {
                var laser = _serverMatch.GetLaser(index);
                var snapshot = new CliffBarrageLaserNetworkSnapshot
                {
                    Phase = laser.Phase,
                    Start = new Vector2((float)laser.StartX,
                        (float)laser.StartZ),
                    End = new Vector2((float)laser.EndX,
                        (float)laser.EndZ)
                };
                if (!_laserSnapshots[index].Equals(snapshot))
                {
                    _laserSnapshots[index] = snapshot;
                }
            }
            _roundTransitionSequence.Value = (uint)
                _serverMatch.RoundTransitionSequence;
            _damageSequence.Value =
                (uint)_serverMatch.DamageSequence;
            _lastDamagedSlot.Value =
                _serverMatch.LastDamagedSlot < 0 ? byte.MaxValue :
                (byte)_serverMatch.LastDamagedSlot;
        }

        private bool TryValidateInput(NetworkPlayerAvatar avatar,
            byte roundNumber, uint inputEpoch, double now,
            out int slot)
        {
            return TryResolveAuthoritativeSlot(avatar, out slot) &&
                _matchActive.Value && !_paused.Value &&
                Phase == NetworkCliffBarragePhase.Playing &&
                _phaseEndsAt.Value > 0d &&
                now < _phaseEndsAt.Value &&
                roundNumber == _roundNumber.Value &&
                inputEpoch != 0U &&
                inputEpoch == _inputEpoch.Value &&
                _serverMatch != null && !IsEliminated(slot);
        }

        private bool TryResolveAuthoritativeSlot(
            NetworkPlayerAvatar avatar, out int slot)
        {
            slot = avatar != null ? avatar.AssignedSlot : -1;
            var match = NetworkMatchState.Instance;
            return IsSpawned && IsServer && avatar != null &&
                avatar.IsSpawned &&
                CliffBarrageRules.IsValidSlot(slot) &&
                match != null &&
                match.GetAvatarForSlot(slot) == avatar;
        }

        private void CacheAndFreezeBoardAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0; slot < _avatars.Length; slot++)
            {
                _avatars[slot] = match != null
                    ? match.GetAvatarForSlot(slot) : null;
                if (_avatars[slot] != null)
                {
                    _avatars[slot].StopServerInputOnServer();
                }
            }
        }

        private void FreezeBoardAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0; slot < _avatars.Length; slot++)
            {
                var current = match != null
                    ? match.GetAvatarForSlot(slot) : null;
                if (current != null)
                {
                    _avatars[slot] = current;
                }
                var avatar = _avatars[slot];
                if (avatar != null)
                {
                    avatar.StopServerInputOnServer();
                }
            }
        }

        private void StopAllMovementOnServer()
        {
            if (_serverMatch == null)
            {
                return;
            }
            for (var slot = 0; slot <
                CliffBarrageRules.PlayerCount; slot++)
            {
                _serverMatch.SetMovementInput(slot, 0d, 0d);
            }
        }

        private void EnsureListLengthsOnServer()
        {
            while (_playerSnapshots.Count <
                CliffBarrageRules.PlayerCount)
            {
                _playerSnapshots.Add(default);
            }
            while (_projectileSnapshots.Count < ProjectilePoolSize)
            {
                _projectileSnapshots.Add(default);
            }
            while (_laserSnapshots.Count < LaserPoolSize)
            {
                _laserSnapshots.Add(default);
            }
        }

        private void ResetReplicatedStateOnServer()
        {
            _matchActive.Value = false;
            _paused.Value = false;
            _phase.Value = (byte)NetworkCliffBarragePhase.Inactive;
            _roundNumber.Value = 0;
            _inputEpoch.Value = 0U;
            _phaseEndsAt.Value = 0d;
            _pausedPhaseRemaining.Value = 0d;
            _roundTransitionSequence.Value = 0U;
            _finalRanks.Value = 0U;
            _damageSequence.Value = 0U;
            _lastDamagedSlot.Value = byte.MaxValue;
            _playerSnapshots.Clear();
            _projectileSnapshots.Clear();
            _laserSnapshots.Clear();
            EnsureListLengthsOnServer();
        }

        private void AdvanceInputEpochOnServer()
        {
            unchecked
            {
                _inputEpoch.Value = _inputEpoch.Value ==
                    uint.MaxValue ? 1U : _inputEpoch.Value + 1U;
            }
        }

        private void ClearLocalRuntime()
        {
            _serverMatch = null;
            _phaseStartedAt = 0d;
            _phaseElapsedAtPause = 0d;
            _lastSnapshotAt = 0d;
            _completionReported = false;
            Array.Clear(_avatars, 0, _avatars.Length);
        }

        private bool HasReachedDeadline(double now)
        {
            return _phaseEndsAt.Value > 0d &&
                now >= _phaseEndsAt.Value;
        }

        private static bool IsFiniteUnitAxis(Vector2 input)
        {
            return !float.IsNaN(input.x) &&
                !float.IsInfinity(input.x) &&
                !float.IsNaN(input.y) &&
                !float.IsInfinity(input.y) &&
                Mathf.Abs(input.x) <= 1.001f &&
                Mathf.Abs(input.y) <= 1.001f;
        }

        private static NetworkVariable<bool> CreateBoolVariable()
        {
            return new NetworkVariable<bool>(false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        private static NetworkVariable<byte> CreateByteVariable(
            byte value = 0)
        {
            return new NetworkVariable<byte>(value,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        private static NetworkVariable<uint> CreateUIntVariable()
        {
            return new NetworkVariable<uint>(0U,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        private static NetworkVariable<double> CreateDoubleVariable()
        {
            return new NetworkVariable<double>(0d,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }
    }
}
