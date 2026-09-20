using System;
using MazeParty.Gameplay.Minigames.BombPassing;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum NetworkBombPassingPhase : byte
    {
        Inactive,
        Countdown,
        Playing,
        Result,
        Complete
    }

    /// <summary>
    /// Authoritative clock, input gate and compact snapshot for the one-life
    /// bomb-passing match. The deterministic model exists on the server only;
    /// clients receive positions and status for shared-camera presentation.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkBombPassingState : NetworkBehaviour
    {
        private const double SnapshotIntervalSeconds = 0.05d;

        private readonly NetworkVariable<bool> _matchActive =
            CreateBoolVariable();
        private readonly NetworkVariable<bool> _paused =
            CreateBoolVariable();
        private readonly NetworkVariable<byte> _phase =
            CreateByteVariable((byte)NetworkBombPassingPhase.Inactive);
        private readonly NetworkVariable<byte> _roundNumber =
            CreateByteVariable();
        private readonly NetworkVariable<uint> _inputEpoch =
            CreateUIntVariable();
        private readonly NetworkVariable<double> _phaseEndsAt =
            CreateDoubleVariable();
        private readonly NetworkVariable<double> _pausedPhaseRemaining =
            CreateDoubleVariable();
        private readonly NetworkVariable<Vector2> _player0 =
            CreateVectorVariable();
        private readonly NetworkVariable<Vector2> _player1 =
            CreateVectorVariable();
        private readonly NetworkVariable<Vector2> _player2 =
            CreateVectorVariable();
        private readonly NetworkVariable<Vector2> _player3 =
            CreateVectorVariable();
        private readonly NetworkVariable<Vector2> _facing0 =
            CreateVectorVariable();
        private readonly NetworkVariable<Vector2> _facing1 =
            CreateVectorVariable();
        private readonly NetworkVariable<Vector2> _facing2 =
            CreateVectorVariable();
        private readonly NetworkVariable<Vector2> _facing3 =
            CreateVectorVariable();
        private readonly NetworkVariable<Vector2> _bombPosition =
            CreateVectorVariable();
        private readonly NetworkVariable<byte> _bombHolder =
            CreateByteVariable();
        private readonly NetworkVariable<byte> _bombNumber =
            CreateByteVariable();
        private readonly NetworkVariable<float> _bombRemaining =
            CreateFloatVariable();
        private readonly NetworkVariable<float> _bombFuse =
            CreateFloatVariable();
        private readonly NetworkVariable<bool> _bombChasing =
            CreateBoolVariable();
        private readonly NetworkVariable<byte> _eliminatedMask =
            CreateByteVariable();
        private readonly NetworkVariable<byte> _stunnedMask =
            CreateByteVariable();
        private readonly NetworkVariable<uint> _finalRanks =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _explosionSequence =
            CreateUIntVariable();
        private readonly NetworkVariable<byte> _lastExplodedSlot =
            CreateByteVariable();

        private readonly NetworkPlayerAvatar[] _avatars =
            new NetworkPlayerAvatar[BombPassingRules.PlayerCount];
        private BombPassingMatchState _serverMatch;
        private double _phaseStartedAt;
        private double _phaseElapsedAtPause;
        private double _lastSnapshotAt;
        private bool _completionReported;

        public static NetworkBombPassingState Instance { get; private set; }

        public NetworkBombPassingPhase Phase =>
            (NetworkBombPassingPhase)_phase.Value;
        public int RoundNumber => _roundNumber.Value;
        public uint InputEpoch => _inputEpoch.Value;
        public bool IsPaused => _paused.Value;
        public bool IsMatchActive => _matchActive.Value;
        public double Remaining => Phase == NetworkBombPassingPhase.Inactive ||
                                   Phase == NetworkBombPassingPhase.Complete
            ? 0d
            : _paused.Value
                ? Math.Max(0d, _pausedPhaseRemaining.Value)
                : Math.Max(0d, _phaseEndsAt.Value - ServerNow);
        public Vector2 BombPosition => _bombPosition.Value;
        public int BombHolderSlot => _bombHolder.Value - 1;
        public int BombNumber => _bombNumber.Value;
        public float BombRemainingSeconds => _bombRemaining.Value;
        public float BombFuseSeconds => _bombFuse.Value;
        public bool IsBombChasing => _bombChasing.Value;
        public uint ExplosionSequence => _explosionSequence.Value;
        public int LastExplodedSlot => _lastExplodedSlot.Value - 1;

        private double ServerNow =>
            NetworkManager != null && NetworkManager.IsListening
                ? NetworkManager.ServerTime.Time
                : Time.unscaledTimeAsDouble;

        public override void OnNetworkSpawn()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError(
                    "More than one NetworkBombPassingState is spawned.");
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
                case NetworkBombPassingPhase.Countdown:
                    if (HasReachedDeadline(now))
                    {
                        BeginPlayingOnServer(now);
                    }
                    break;
                case NetworkBombPassingPhase.Playing:
                    AdvancePlayingOnServer(now);
                    break;
                case NetworkBombPassingPhase.Result:
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
            _serverMatch = new BombPassingMatchState(seed);
            _matchActive.Value = true;
            _roundNumber.Value = 1;
            CacheAndFreezeBoardAvatarsOnServer();
            SyncSnapshotOnServer();

            var now = ServerNow;
            _phaseStartedAt = now;
            _phase.Value = (byte)NetworkBombPassingPhase.Countdown;
            _phaseEndsAt.Value = now +
                BombPassingRules.CountdownSeconds;
            Debug.Log(
                "[BombPassing] Match started, seed " + matchSeed + ".");
        }

        public bool CanAcceptInputForSlot(int slot)
        {
            return BombPassingRules.IsValidPlayerSlot(slot) &&
                _matchActive.Value && !_paused.Value &&
                Phase == NetworkBombPassingPhase.Playing &&
                !IsEliminated(slot);
        }

        public bool RequestMovementInputOnServer(
            NetworkPlayerAvatar avatar,
            Vector2 input,
            byte roundNumber,
            uint inputEpoch)
        {
            var now = ServerNow;
            if (!TryValidateInputEnvelope(
                    avatar,
                    roundNumber,
                    inputEpoch,
                    out var slot) ||
                !IsFiniteUnitAxis(input))
            {
                return false;
            }

            _avatars[slot] = avatar;
            avatar.StopServerInputOnServer();
            AdvancePlayingModelOnServer(now);
            if (_serverMatch.IsComplete)
            {
                FinishPlayingOnServer(now);
                return false;
            }
            if (_serverMatch.GetPlayer(slot).IsEliminated)
            {
                return false;
            }

            input = Vector2.ClampMagnitude(input, 1f);
            _serverMatch.SetMovementInput(slot, input.x, input.y);
            return true;
        }

        public bool RequestPrimaryActionOnServer(
            NetworkPlayerAvatar avatar,
            byte roundNumber,
            uint inputEpoch)
        {
            var now = ServerNow;
            if (!TryValidateInputEnvelope(
                    avatar,
                    roundNumber,
                    inputEpoch,
                    out var slot))
            {
                return false;
            }

            _avatars[slot] = avatar;
            avatar.StopServerInputOnServer();
            AdvancePlayingModelOnServer(now);
            if (_serverMatch.IsComplete)
            {
                FinishPlayingOnServer(now);
                return false;
            }
            if (_serverMatch.GetPlayer(slot).IsEliminated)
            {
                return false;
            }

            _serverMatch.TryAttack(slot);
            SyncSnapshotOnServer();
            return true;
        }

        public Vector2 GetPlayerPosition(int slot)
        {
            switch (slot)
            {
                case 0: return _player0.Value;
                case 1: return _player1.Value;
                case 2: return _player2.Value;
                case 3: return _player3.Value;
                default: return Vector2.zero;
            }
        }

        public Vector2 GetPlayerFacing(int slot)
        {
            switch (slot)
            {
                case 0: return _facing0.Value;
                case 1: return _facing1.Value;
                case 2: return _facing2.Value;
                case 3: return _facing3.Value;
                default: return Vector2.up;
            }
        }

        public bool IsEliminated(int slot)
        {
            return BombPassingRules.IsValidPlayerSlot(slot) &&
                (_eliminatedMask.Value & (1 << slot)) != 0;
        }

        public bool IsStunned(int slot)
        {
            return BombPassingRules.IsValidPlayerSlot(slot) &&
                (_stunnedMask.Value & (1 << slot)) != 0;
        }

        public int GetFinalRank(int slot)
        {
            return BombPassingRules.IsValidPlayerSlot(slot)
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

            if (Phase == NetworkBombPassingPhase.Playing)
            {
                AdvancePlayingModelOnServer(now);
                StopAllMovementOnServer();
                SyncSnapshotOnServer();
            }
            _phaseElapsedAtPause = Math.Max(0d, now - _phaseStartedAt);
            _pausedPhaseRemaining.Value = _phaseEndsAt.Value > 0d
                ? Math.Max(0d, _phaseEndsAt.Value - now)
                : 0d;
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
            if (Phase != NetworkBombPassingPhase.Playing)
            {
                _phaseEndsAt.Value = now +
                    Math.Max(0d, _pausedPhaseRemaining.Value);
            }
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

            _phaseStartedAt = now;
            _phaseEndsAt.Value = 0d;
            _phase.Value = (byte)NetworkBombPassingPhase.Playing;
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
            if (now - _lastSnapshotAt >= SnapshotIntervalSeconds ||
                _serverMatch.IsComplete)
            {
                SyncSnapshotOnServer();
                _lastSnapshotAt = now;
            }

            if (_serverMatch.IsComplete)
            {
                FinishPlayingOnServer(now);
            }
        }

        private void AdvancePlayingModelOnServer(double now)
        {
            _serverMatch?.AdvanceTo(Math.Max(0d, now - _phaseStartedAt));
        }

        private void FinishPlayingOnServer(double now)
        {
            if (_serverMatch == null || !_serverMatch.IsComplete ||
                Phase != NetworkBombPassingPhase.Playing)
            {
                return;
            }

            StopAllMovementOnServer();
            uint packedRanks = 0U;
            for (var slot = 0; slot < BombPassingRules.PlayerCount;
                 slot++)
            {
                packedRanks |= (uint)_serverMatch.GetFinalRank(slot) <<
                    (slot * 8);
            }
            _finalRanks.Value = packedRanks;
            SyncSnapshotOnServer();
            AdvanceInputEpochOnServer();
            _phaseStartedAt = now;
            _phase.Value = (byte)NetworkBombPassingPhase.Result;
            _phaseEndsAt.Value = now + BombPassingRules.ResultSeconds;
            Debug.Log("[BombPassing] Final placements ready.");
        }

        private void CompleteMatchOnServer()
        {
            if (_completionReported || _serverMatch == null ||
                !_serverMatch.IsComplete)
            {
                return;
            }

            var ranks = new int[BombPassingRules.PlayerCount];
            for (var slot = 0; slot < ranks.Length; slot++)
            {
                ranks[slot] = _serverMatch.GetFinalRank(slot);
            }
            var match = NetworkMatchState.Instance;
            if (match == null ||
                !match.TryCompleteBombPassingOnServer(ranks))
            {
                return;
            }

            _completionReported = true;
            _matchActive.Value = false;
            _phase.Value = (byte)NetworkBombPassingPhase.Complete;
            _phaseEndsAt.Value = 0d;
            FreezeBoardAvatarsOnServer();
            Debug.Log("[BombPassing] Match complete.");
        }

        private void SyncSnapshotOnServer()
        {
            if (_serverMatch == null)
            {
                return;
            }

            byte eliminated = 0;
            byte stunned = 0;
            for (var slot = 0; slot < BombPassingRules.PlayerCount;
                 slot++)
            {
                var player = _serverMatch.GetPlayer(slot);
                var position = new Vector2(
                    (float)player.X,
                    (float)player.Z);
                var facing = new Vector2(
                    (float)player.FacingX,
                    (float)player.FacingZ);
                switch (slot)
                {
                    case 0:
                        _player0.Value = position;
                        _facing0.Value = facing;
                        break;
                    case 1:
                        _player1.Value = position;
                        _facing1.Value = facing;
                        break;
                    case 2:
                        _player2.Value = position;
                        _facing2.Value = facing;
                        break;
                    case 3:
                        _player3.Value = position;
                        _facing3.Value = facing;
                        break;
                }
                if (player.IsEliminated)
                {
                    eliminated |= (byte)(1 << slot);
                }
                if (player.StunRemainingSeconds > 0d)
                {
                    stunned |= (byte)(1 << slot);
                }
            }
            _eliminatedMask.Value = eliminated;
            _stunnedMask.Value = stunned;

            var bomb = _serverMatch.GetBomb();
            _bombPosition.Value = new Vector2(
                (float)bomb.X,
                (float)bomb.Z);
            _bombHolder.Value = (byte)(bomb.HolderSlot + 1);
            _bombNumber.Value =
                (byte)Mathf.Clamp(bomb.BombNumber, 0, byte.MaxValue);
            _bombRemaining.Value = (float)bomb.RemainingSeconds;
            _bombFuse.Value = (float)bomb.FuseSeconds;
            _bombChasing.Value = bomb.IsChasing;
            _explosionSequence.Value =
                (uint)_serverMatch.ExplosionSequence;
            var explosion = _serverMatch.LastExplosion;
            _lastExplodedSlot.Value = explosion.HasValue
                ? (byte)(explosion.Value.EliminatedSlot + 1)
                : (byte)0;
        }

        private bool TryValidateInputEnvelope(
            NetworkPlayerAvatar avatar,
            byte roundNumber,
            uint inputEpoch,
            out int slot)
        {
            return TryResolveAuthoritativeSlot(avatar, out slot) &&
                _matchActive.Value && !_paused.Value &&
                Phase == NetworkBombPassingPhase.Playing &&
                roundNumber == _roundNumber.Value &&
                inputEpoch != 0U && inputEpoch == _inputEpoch.Value &&
                _serverMatch != null &&
                !_serverMatch.GetPlayer(slot).IsEliminated;
        }

        private bool TryResolveAuthoritativeSlot(
            NetworkPlayerAvatar avatar,
            out int slot)
        {
            slot = avatar != null ? avatar.AssignedSlot : -1;
            var match = NetworkMatchState.Instance;
            return IsSpawned && IsServer && avatar != null &&
                avatar.IsSpawned &&
                BombPassingRules.IsValidPlayerSlot(slot) &&
                match != null && match.GetAvatarForSlot(slot) == avatar;
        }

        private void CacheAndFreezeBoardAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0; slot < BombPassingRules.PlayerCount;
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
            for (var slot = 0; slot < BombPassingRules.PlayerCount;
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

        private void StopAllMovementOnServer()
        {
            if (_serverMatch == null)
            {
                return;
            }
            for (var slot = 0; slot < BombPassingRules.PlayerCount;
                 slot++)
            {
                _serverMatch.SetMovementInput(slot, 0d, 0d);
            }
        }

        private void ResetReplicatedStateOnServer()
        {
            _matchActive.Value = false;
            _paused.Value = false;
            _phase.Value = (byte)NetworkBombPassingPhase.Inactive;
            _roundNumber.Value = 0;
            _inputEpoch.Value = 0U;
            _phaseEndsAt.Value = 0d;
            _pausedPhaseRemaining.Value = 0d;
            _player0.Value = Vector2.zero;
            _player1.Value = Vector2.zero;
            _player2.Value = Vector2.zero;
            _player3.Value = Vector2.zero;
            _facing0.Value = Vector2.up;
            _facing1.Value = Vector2.up;
            _facing2.Value = Vector2.up;
            _facing3.Value = Vector2.up;
            _bombPosition.Value = Vector2.zero;
            _bombHolder.Value = 0;
            _bombNumber.Value = 0;
            _bombRemaining.Value = 0f;
            _bombFuse.Value = 0f;
            _bombChasing.Value = false;
            _eliminatedMask.Value = 0;
            _stunnedMask.Value = 0;
            _finalRanks.Value = 0U;
            _explosionSequence.Value = 0U;
            _lastExplodedSlot.Value = 0;
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

        private static NetworkVariable<uint> CreateUIntVariable()
        {
            return new NetworkVariable<uint>(
                0U,
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
