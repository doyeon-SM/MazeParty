using System;
using MazeParty.Gameplay.Minigames.BouncingBalls;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum NetworkBouncingBallsPhase : byte
    {
        Inactive,
        Countdown,
        Playing,
        RoundBreak,
        Complete
    }

    /// <summary>
    /// Server-authoritative clock, input validation and replicated presentation
    /// for the two-round shield game. Ball physics and placement are owned by
    /// the deterministic BouncingBallsMatchState on the server only.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkBouncingBallsState : NetworkBehaviour
    {
        public const double RoundBreakSeconds =
            BouncingBallsRules.ResultSeconds;
        private const double SnapshotIntervalSeconds = 0.05d;

        private readonly NetworkVariable<bool> _matchActive =
            CreateBoolVariable();
        private readonly NetworkVariable<bool> _paused =
            CreateBoolVariable();
        private readonly NetworkVariable<byte> _phase =
            CreateByteVariable((byte)NetworkBouncingBallsPhase.Inactive);
        private readonly NetworkVariable<byte> _roundNumber =
            CreateByteVariable();
        private readonly NetworkVariable<double> _phaseEndsAt =
            CreateDoubleVariable();
        private readonly NetworkVariable<double> _pausedPhaseRemaining =
            CreateDoubleVariable();
        private readonly NetworkVariable<uint> _inputEpoch =
            CreateUIntVariable();
        private readonly NetworkVariable<ulong> _scores =
            CreateULongVariable();
        private readonly NetworkVariable<ulong> _conceded =
            CreateULongVariable();
        private readonly NetworkVariable<uint> _ballOwners =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _finalRanks =
            CreateUIntVariable();
        private readonly NetworkVariable<Vector2> _ball0 =
            CreateVectorVariable();
        private readonly NetworkVariable<Vector2> _ball1 =
            CreateVectorVariable();
        private readonly NetworkVariable<Vector2> _ball2 =
            CreateVectorVariable();
        private readonly NetworkVariable<float> _shield0 =
            CreateFloatVariable();
        private readonly NetworkVariable<float> _shield1 =
            CreateFloatVariable();
        private readonly NetworkVariable<float> _shield2 =
            CreateFloatVariable();
        private readonly NetworkVariable<float> _shield3 =
            CreateFloatVariable();

        private readonly NetworkPlayerAvatar[] _avatars =
            new NetworkPlayerAvatar[BouncingBallsRules.PlayerCount];
        private BouncingBallsMatchState _serverMatch;
        private double _phaseStartedAt;
        private double _phaseElapsedAtPause;
        private double _lastSnapshotAt;
        private bool _completionReported;

        public static NetworkBouncingBallsState Instance { get; private set; }

        public NetworkBouncingBallsPhase Phase =>
            (NetworkBouncingBallsPhase)_phase.Value;
        public int RoundNumber => _roundNumber.Value;
        public uint InputEpoch => _inputEpoch.Value;
        public bool IsPaused => _paused.Value;
        public bool IsMatchActive => _matchActive.Value;
        public double Remaining => Phase == NetworkBouncingBallsPhase.Inactive
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
                    "More than one NetworkBouncingBallsState is spawned.");
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
                case NetworkBouncingBallsPhase.Countdown:
                    if (HasReachedDeadline(now))
                    {
                        BeginPlayingOnServer(now);
                    }
                    break;
                case NetworkBouncingBallsPhase.Playing:
                    AdvancePlayingOnServer(now);
                    break;
                case NetworkBouncingBallsPhase.RoundBreak:
                    if (HasReachedDeadline(now))
                    {
                        BeginNextRoundOnServer(now);
                    }
                    break;
                case NetworkBouncingBallsPhase.Complete:
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
            _serverMatch = new BouncingBallsMatchState(seed);
            _matchActive.Value = true;
            _roundNumber.Value = (byte)_serverMatch.RoundNumber;
            CacheAndFreezeBoardAvatarsOnServer();
            SyncSnapshotOnServer();

            var now = ServerNow;
            _phaseStartedAt = now;
            _phase.Value = (byte)NetworkBouncingBallsPhase.Countdown;
            _phaseEndsAt.Value = now + BouncingBallsRules.CountdownSeconds;
            Debug.Log(
                "[BouncingBalls] Two-round match started, seed " +
                matchSeed + ".");
        }

        public bool CanAcceptInputForSlot(int slot)
        {
            return BouncingBallsRules.IsValidPlayerSlot(slot) &&
                _matchActive.Value && !_paused.Value &&
                Phase == NetworkBouncingBallsPhase.Playing &&
                Remaining > 0d;
        }

        public bool RequestShieldAxisOnServer(
            NetworkPlayerAvatar avatar,
            float axis,
            byte roundNumber,
            uint inputEpoch)
        {
            var now = ServerNow;
            if (!TryResolveAuthoritativeSlot(avatar, out var slot) ||
                (axis != -1f && axis != 0f && axis != 1f) ||
                !_matchActive.Value || _paused.Value ||
                Phase != NetworkBouncingBallsPhase.Playing ||
                _phaseEndsAt.Value <= 0d ||
                now >= _phaseEndsAt.Value ||
                roundNumber != _roundNumber.Value ||
                inputEpoch == 0U || inputEpoch != _inputEpoch.Value ||
                _serverMatch == null)
            {
                return false;
            }

            _avatars[slot] = avatar;
            avatar.StopServerInputOnServer();
            // Integrate the previous held input up to this authoritative
            // receive time before applying the new direction.
            AdvancePlayingModelOnServer(now);
            _serverMatch.SetShieldInput(slot, (int)axis);
            return true;
        }

        public int GetScore(int slot)
        {
            return UnpackSlotValue(_scores.Value, slot);
        }

        public int GetConceded(int slot)
        {
            return UnpackSlotValue(_conceded.Value, slot);
        }

        public Vector2 GetBallPosition(int ballId)
        {
            switch (ballId)
            {
                case 0: return _ball0.Value;
                case 1: return _ball1.Value;
                case 2: return _ball2.Value;
                default: return Vector2.zero;
            }
        }

        public int GetBallOwner(int ballId)
        {
            return ballId >= 0 && ballId < BouncingBallsRules.BallCount
                ? (int)((_ballOwners.Value >> (ballId * 8)) & 0xffU) - 1
                : -1;
        }

        public float GetShieldCenter(int slot)
        {
            switch (slot)
            {
                case 0: return _shield0.Value;
                case 1: return _shield1.Value;
                case 2: return _shield2.Value;
                case 3: return _shield3.Value;
                default: return 0f;
            }
        }

        public int GetFinalRank(int slot)
        {
            return BouncingBallsRules.IsValidPlayerSlot(slot)
                ? (int)((_finalRanks.Value >> (slot * 8)) & 0xffU)
                : 0;
        }

        public void PauseOnServer(double now)
        {
            if (!IsSpawned || !IsServer || !_matchActive.Value ||
                _paused.Value)
            {
                return;
            }

            if (Phase == NetworkBouncingBallsPhase.Playing)
            {
                AdvancePlayingModelOnServer(now);
                StopAllShieldsOnServer();
                SyncSnapshotOnServer();
            }
            _phaseElapsedAtPause = Math.Max(0d, now - _phaseStartedAt);
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
            _phaseEndsAt.Value =
                now + Math.Max(0d, _pausedPhaseRemaining.Value);
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
            _serverMatch?.SetShieldInput(slot, 0);
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

        private void AdvancePlayingOnServer(double now)
        {
            if (_serverMatch == null)
            {
                return;
            }

            AdvancePlayingModelOnServer(now);
            if (now - _lastSnapshotAt >= SnapshotIntervalSeconds ||
                _serverMatch.IsRoundComplete)
            {
                SyncSnapshotOnServer();
                _lastSnapshotAt = now;
            }

            if (!_serverMatch.IsRoundComplete)
            {
                return;
            }

            StopAllShieldsOnServer();
            SyncSnapshotOnServer();
            AdvanceInputEpochOnServer();
            _phaseStartedAt = now;
            if (_serverMatch.IsComplete)
            {
                PackFinalRanksOnServer();
                _phase.Value = (byte)NetworkBouncingBallsPhase.Complete;
                _phaseEndsAt.Value = now +
                    BouncingBallsRules.ResultSeconds;
                Debug.Log("[BouncingBalls] Final placements ready.");
            }
            else
            {
                _phase.Value = (byte)NetworkBouncingBallsPhase.RoundBreak;
                _phaseEndsAt.Value = now + RoundBreakSeconds;
                Debug.Log("[BouncingBalls] Round 1 complete.");
            }
        }

        private void AdvancePlayingModelOnServer(double now)
        {
            var elapsed = Math.Max(0d, now - _phaseStartedAt);
            _serverMatch.AdvanceTo(
                Math.Min(BouncingBallsRules.RoundSeconds, elapsed));
        }

        private void BeginPlayingOnServer(double now)
        {
            if (_serverMatch == null)
            {
                return;
            }

            _roundNumber.Value = (byte)_serverMatch.RoundNumber;
            _phaseStartedAt = now;
            _phase.Value = (byte)NetworkBouncingBallsPhase.Playing;
            _phaseEndsAt.Value = now + BouncingBallsRules.RoundSeconds;
            AdvanceInputEpochOnServer();
            SyncSnapshotOnServer();
            _lastSnapshotAt = now;
        }

        private void BeginNextRoundOnServer(double now)
        {
            if (_serverMatch == null || _serverMatch.IsComplete)
            {
                return;
            }

            _serverMatch.BeginNextRound();
            _roundNumber.Value = (byte)_serverMatch.RoundNumber;
            _phaseStartedAt = now;
            _phase.Value = (byte)NetworkBouncingBallsPhase.Countdown;
            _phaseEndsAt.Value = now +
                BouncingBallsRules.CountdownSeconds;
            SyncSnapshotOnServer();
            Debug.Log("[BouncingBalls] Round 2 countdown started.");
        }

        private void CompleteMatchOnServer()
        {
            if (_completionReported || _serverMatch == null ||
                !_serverMatch.IsComplete)
            {
                return;
            }

            var ranks = new int[BouncingBallsRules.PlayerCount];
            for (var slot = 0; slot < ranks.Length; slot++)
            {
                ranks[slot] = _serverMatch.GetFinalRank(slot);
            }

            var match = NetworkMatchState.Instance;
            if (match == null ||
                !match.TryCompleteBouncingBallsOnServer(ranks))
            {
                return;
            }

            _completionReported = true;
            _matchActive.Value = false;
            _phaseEndsAt.Value = 0d;
            FreezeBoardAvatarsOnServer();
            Debug.Log("[BouncingBalls] Match complete.");
        }

        private void PackFinalRanksOnServer()
        {
            if (_serverMatch == null || !_serverMatch.IsComplete)
            {
                return;
            }

            uint packed = 0U;
            for (var slot = 0; slot < BouncingBallsRules.PlayerCount;
                 slot++)
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

            var ball0 = _serverMatch.GetBall(0);
            var ball1 = _serverMatch.GetBall(1);
            var ball2 = _serverMatch.GetBall(2);
            _ball0.Value = new Vector2((float)ball0.X, (float)ball0.Y);
            _ball1.Value = new Vector2((float)ball1.X, (float)ball1.Y);
            _ball2.Value = new Vector2((float)ball2.X, (float)ball2.Y);
            _ballOwners.Value =
                PackOwner(ball0.OwnerSlot, 0) |
                PackOwner(ball1.OwnerSlot, 1) |
                PackOwner(ball2.OwnerSlot, 2);
            _shield0.Value = (float)_serverMatch.GetShield(0).Center;
            _shield1.Value = (float)_serverMatch.GetShield(1).Center;
            _shield2.Value = (float)_serverMatch.GetShield(2).Center;
            _shield3.Value = (float)_serverMatch.GetShield(3).Center;

            ulong packedScores = 0UL;
            ulong packedConceded = 0UL;
            for (var slot = 0; slot < BouncingBallsRules.PlayerCount;
                 slot++)
            {
                packedScores |= PackSlotValue(
                    _serverMatch.GetScore(slot), slot);
                packedConceded |= PackSlotValue(
                    _serverMatch.GetConceded(slot), slot);
            }
            _scores.Value = packedScores;
            _conceded.Value = packedConceded;
        }

        private void StopAllShieldsOnServer()
        {
            if (_serverMatch == null)
            {
                return;
            }

            for (var slot = 0; slot < BouncingBallsRules.PlayerCount;
                 slot++)
            {
                _serverMatch.SetShieldInput(slot, 0);
            }
        }

        private bool TryResolveAuthoritativeSlot(
            NetworkPlayerAvatar avatar,
            out int slot)
        {
            slot = avatar != null ? avatar.AssignedSlot : -1;
            var match = NetworkMatchState.Instance;
            return IsSpawned && IsServer && avatar != null &&
                avatar.IsSpawned &&
                BouncingBallsRules.IsValidPlayerSlot(slot) &&
                match != null && match.GetAvatarForSlot(slot) == avatar;
        }

        private void CacheAndFreezeBoardAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0; slot < BouncingBallsRules.PlayerCount;
                 slot++)
            {
                _avatars[slot] = match != null
                    ? match.GetAvatarForSlot(slot)
                    : null;
                _avatars[slot]?.StopServerInputOnServer();
            }
        }

        private void FreezeBoardAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0; slot < BouncingBallsRules.PlayerCount;
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
            _phase.Value = (byte)NetworkBouncingBallsPhase.Inactive;
            _roundNumber.Value = 0;
            _phaseEndsAt.Value = 0d;
            _pausedPhaseRemaining.Value = 0d;
            _inputEpoch.Value = 0U;
            _scores.Value = 0UL;
            _conceded.Value = 0UL;
            _ballOwners.Value = 0U;
            _finalRanks.Value = 0U;
            _ball0.Value = Vector2.zero;
            _ball1.Value = Vector2.zero;
            _ball2.Value = Vector2.zero;
            _shield0.Value = 0f;
            _shield1.Value = 0f;
            _shield2.Value = 0f;
            _shield3.Value = 0f;
        }

        private void AdvanceInputEpochOnServer()
        {
            unchecked
            {
                _inputEpoch.Value = _inputEpoch.Value == uint.MaxValue
                    ? 1U
                    : _inputEpoch.Value + 1U;
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
            return _phaseEndsAt.Value > 0d && now >= _phaseEndsAt.Value;
        }

        private static uint PackOwner(int slot, int ballId)
        {
            return (uint)(Mathf.Clamp(slot + 1, 0, 255) & 0xff) <<
                (ballId * 8);
        }

        private static ulong PackSlotValue(int value, int slot)
        {
            return (ulong)Mathf.Clamp(value, 0, ushort.MaxValue) <<
                (slot * 16);
        }

        private static int UnpackSlotValue(ulong packed, int slot)
        {
            return BouncingBallsRules.IsValidPlayerSlot(slot)
                ? (int)((packed >> (slot * 16)) & 0xffffUL)
                : 0;
        }

        private static NetworkVariable<bool> CreateBoolVariable()
        {
            return new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        private static NetworkVariable<byte> CreateByteVariable(
            byte value = 0)
        {
            return new NetworkVariable<byte>(
                value,
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

        private static NetworkVariable<float> CreateFloatVariable()
        {
            return new NetworkVariable<float>(
                0f,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        private static NetworkVariable<Vector2> CreateVectorVariable()
        {
            return new NetworkVariable<Vector2>(
                Vector2.zero,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }
    }
}
