using System;
using MazeParty.Gameplay.Minigames.SnowySpin;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum NetworkSnowySpinPhase : byte
    {
        Inactive,
        Countdown,
        Playing,
        RoundResult,
        Complete
    }

    /// <summary>
    /// Server-owned three-round rolling-ball match. Only compact snapshots
    /// are replicated; clients never resolve collisions, falls or rankings.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkSnowySpinState : NetworkBehaviour
    {
        public const double CountdownSeconds = 3d;
        public const double ResultSeconds = 4d;
        private const double SnapshotIntervalSeconds = 0.05d;

        private readonly NetworkVariable<bool> _matchActive =
            CreateBoolVariable();
        private readonly NetworkVariable<bool> _paused =
            CreateBoolVariable();
        private readonly NetworkVariable<byte> _phase =
            CreateByteVariable((byte)NetworkSnowySpinPhase.Inactive);
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
        private readonly NetworkVariable<byte> _eliminatedMask =
            CreateByteVariable();
        private readonly NetworkVariable<uint> _roundRanks =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _scores =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _finalRanks =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _roundTransitionSequence =
            CreateUIntVariable();

        private readonly NetworkPlayerAvatar[] _avatars =
            new NetworkPlayerAvatar[SnowySpinRules.PlayerCount];
        private SnowySpinMatchState _serverMatch;
        private double _phaseStartedAt;
        private double _phaseElapsedAtPause;
        private double _lastSnapshotAt;
        private bool _completionReported;

        public static NetworkSnowySpinState Instance { get; private set; }

        public NetworkSnowySpinPhase Phase =>
            (NetworkSnowySpinPhase)_phase.Value;
        public int RoundNumber => _roundNumber.Value;
        public uint InputEpoch => _inputEpoch.Value;
        public uint RoundTransitionSequence =>
            _roundTransitionSequence.Value;
        public bool IsPaused => _paused.Value;
        public bool IsMatchActive => _matchActive.Value;
        public double Remaining =>
            Phase == NetworkSnowySpinPhase.Inactive ||
            (!_matchActive.Value &&
             Phase == NetworkSnowySpinPhase.Complete)
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
                    "More than one NetworkSnowySpinState is spawned.");
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
                case NetworkSnowySpinPhase.Countdown:
                    if (HasReachedDeadline(now))
                    {
                        BeginPlayingOnServer(now);
                    }
                    break;
                case NetworkSnowySpinPhase.Playing:
                    AdvancePlayingOnServer(now);
                    break;
                case NetworkSnowySpinPhase.RoundResult:
                    if (HasReachedDeadline(now))
                    {
                        BeginNextRoundOnServer(now);
                    }
                    break;
                case NetworkSnowySpinPhase.Complete:
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
            _serverMatch = new SnowySpinMatchState(seed);
            _matchActive.Value = true;
            _roundNumber.Value = (byte)_serverMatch.RoundNumber;
            CacheAndFreezeBoardAvatarsOnServer();
            SyncSnapshotOnServer();

            var now = ServerNow;
            _phaseStartedAt = now;
            _phase.Value = (byte)NetworkSnowySpinPhase.Countdown;
            _phaseEndsAt.Value = now + CountdownSeconds;
            Debug.Log("[SnowySpin] Three-round match started.");
        }

        public bool CanAcceptInputForSlot(int slot)
        {
            return SnowySpinRules.IsValidPlayerSlot(slot) &&
                _matchActive.Value && !_paused.Value &&
                Phase == NetworkSnowySpinPhase.Playing &&
                Remaining > 0d && !IsEliminated(slot);
        }

        public bool RequestMovementInputOnServer(
            NetworkPlayerAvatar avatar,
            Vector2 input,
            byte roundNumber,
            uint inputEpoch)
        {
            var now = ServerNow;
            if (!TryResolveAuthoritativeSlot(avatar, out var slot) ||
                !_matchActive.Value || _paused.Value ||
                Phase != NetworkSnowySpinPhase.Playing ||
                _phaseEndsAt.Value <= 0d ||
                now >= _phaseEndsAt.Value ||
                roundNumber != _roundNumber.Value ||
                inputEpoch == 0U || inputEpoch != _inputEpoch.Value ||
                _serverMatch == null || IsEliminated(slot) ||
                !IsFiniteUnitAxis(input))
            {
                return false;
            }

            _avatars[slot] = avatar;
            avatar.StopServerInputOnServer();
            // Integrate the preceding held input before replacing it at the
            // server receive time. The client cannot provide simulation time.
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

        public bool IsEliminated(int slot)
        {
            return SnowySpinRules.IsValidPlayerSlot(slot) &&
                (_eliminatedMask.Value & (1 << slot)) != 0;
        }

        public int GetRoundRank(int slot)
        {
            return ReadPackedByte(_roundRanks.Value, slot);
        }

        public int GetScore(int slot)
        {
            return ReadPackedByte(_scores.Value, slot);
        }

        public int GetFinalRank(int slot)
        {
            return ReadPackedByte(_finalRanks.Value, slot);
        }

        public void PauseOnServer(double now)
        {
            if (!IsSpawned || !IsServer || !_matchActive.Value ||
                _paused.Value)
            {
                return;
            }

            if (Phase == NetworkSnowySpinPhase.Playing)
            {
                AdvancePlayingModelOnServer(now);
                if (_serverMatch != null && _serverMatch.IsRoundComplete)
                {
                    FinishRoundOnServer(now);
                }
                StopAllMovementOnServer();
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
            _phase.Value = (byte)NetworkSnowySpinPhase.Playing;
            _phaseEndsAt.Value =
                now + SnowySpinRules.RoundDurationSeconds;
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
                SnowySpinRules.RoundDurationSeconds,
                Math.Max(0d, now - _phaseStartedAt)));
        }

        private void FinishRoundOnServer(double now)
        {
            if (_serverMatch == null ||
                !_serverMatch.IsRoundComplete ||
                Phase != NetworkSnowySpinPhase.Playing)
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
                _phase.Value = (byte)NetworkSnowySpinPhase.Complete;
                _phaseEndsAt.Value = now + ResultSeconds;
                Debug.Log("[SnowySpin] Final placements ready.");
            }
            else
            {
                _phase.Value = (byte)NetworkSnowySpinPhase.RoundResult;
                _phaseEndsAt.Value = now + ResultSeconds;
                Debug.Log("[SnowySpin] Round " +
                    _serverMatch.RoundNumber + " complete.");
            }
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
            _phase.Value = (byte)NetworkSnowySpinPhase.Countdown;
            _phaseEndsAt.Value = now + CountdownSeconds;
            SyncSnapshotOnServer();
            Debug.Log("[SnowySpin] Round " +
                _serverMatch.RoundNumber + " countdown started.");
        }

        private void CompleteMatchOnServer()
        {
            if (_completionReported || _serverMatch == null ||
                !_serverMatch.IsComplete)
            {
                return;
            }

            var ranks = new int[SnowySpinRules.PlayerCount];
            for (var slot = 0; slot < ranks.Length; slot++)
            {
                ranks[slot] = _serverMatch.GetFinalRank(slot);
            }

            var match = NetworkMatchState.Instance;
            if (match == null ||
                !match.TryCompleteSnowySpinOnServer(ranks))
            {
                return;
            }

            _completionReported = true;
            _matchActive.Value = false;
            _phaseEndsAt.Value = 0d;
            FreezeBoardAvatarsOnServer();
            Debug.Log("[SnowySpin] Match complete.");
        }

        private void PackFinalRanksOnServer()
        {
            uint packed = 0U;
            for (var slot = 0; slot < SnowySpinRules.PlayerCount;
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

            byte eliminated = 0;
            uint scores = 0U;
            uint roundRanks = 0U;
            for (var slot = 0; slot < SnowySpinRules.PlayerCount;
                 slot++)
            {
                var player = _serverMatch.GetPlayer(slot);
                var position = new Vector2(
                    (float)player.X,
                    (float)player.Z);
                switch (slot)
                {
                    case 0: _player0.Value = position; break;
                    case 1: _player1.Value = position; break;
                    case 2: _player2.Value = position; break;
                    case 3: _player3.Value = position; break;
                }
                if (player.IsEliminated)
                {
                    eliminated |= (byte)(1 << slot);
                }
                scores |= (uint)Mathf.Clamp(
                    player.TotalScore, 0, byte.MaxValue) <<
                    (slot * 8);
                roundRanks |= (uint)Mathf.Clamp(
                    player.RoundRank, 0, byte.MaxValue) <<
                    (slot * 8);
            }
            _eliminatedMask.Value = eliminated;
            _scores.Value = scores;
            _roundRanks.Value = roundRanks;
            _roundTransitionSequence.Value =
                (uint)_serverMatch.RoundTransitionSequence;
        }

        private void StopAllMovementOnServer()
        {
            if (_serverMatch == null)
            {
                return;
            }
            for (var slot = 0; slot < SnowySpinRules.PlayerCount;
                 slot++)
            {
                _serverMatch.SetMovementInput(slot, 0d, 0d);
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
                SnowySpinRules.IsValidPlayerSlot(slot) &&
                match != null && match.GetAvatarForSlot(slot) == avatar;
        }

        private void CacheAndFreezeBoardAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0; slot < SnowySpinRules.PlayerCount;
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
            for (var slot = 0; slot < SnowySpinRules.PlayerCount;
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
            _phase.Value = (byte)NetworkSnowySpinPhase.Inactive;
            _roundNumber.Value = 0;
            _inputEpoch.Value = 0U;
            _phaseEndsAt.Value = 0d;
            _pausedPhaseRemaining.Value = 0d;
            _player0.Value = Vector2.zero;
            _player1.Value = Vector2.zero;
            _player2.Value = Vector2.zero;
            _player3.Value = Vector2.zero;
            _eliminatedMask.Value = 0;
            _roundRanks.Value = 0U;
            _scores.Value = 0U;
            _finalRanks.Value = 0U;
            _roundTransitionSequence.Value = 0U;
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
            return _phaseEndsAt.Value > 0d &&
                now >= _phaseEndsAt.Value;
        }

        private static int ReadPackedByte(uint packed, int slot)
        {
            return SnowySpinRules.IsValidPlayerSlot(slot)
                ? (int)((packed >> (slot * 8)) & 0xffU)
                : 0;
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

        private static NetworkVariable<Vector2> CreateVectorVariable()
        {
            return new NetworkVariable<Vector2>(
                Vector2.zero,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }
    }
}
