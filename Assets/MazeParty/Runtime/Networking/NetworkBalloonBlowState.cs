using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.BalloonBlow;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum NetworkBalloonBlowPhase : byte
    {
        Inactive,
        Countdown,
        Running,
        RoundResult,
        Complete
    }

    /// <summary>
    /// Server-authoritative three-round Balloon Blow simulation. Clients send
    /// only the current left-button state; the server owns all elapsed time,
    /// progress, cooldown, pop-order and scoring decisions.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkBalloonBlowState : NetworkBehaviour
    {
        public const double CountdownSeconds = 3d;
        public const double RoundResultSeconds = 4d;

        private const byte NoPlayerSlot = byte.MaxValue;

        private readonly NetworkVariable<bool> _matchActive =
            CreateBoolVariable();
        private readonly NetworkVariable<bool> _paused =
            CreateBoolVariable();
        private readonly NetworkVariable<byte> _phase =
            CreateByteVariable((byte)NetworkBalloonBlowPhase.Inactive);
        private readonly NetworkVariable<byte> _roundNumber =
            CreateByteVariable();
        private readonly NetworkVariable<double> _phaseEndsAt =
            CreateDoubleVariable();
        private readonly NetworkVariable<double> _pausedPhaseRemaining =
            CreateDoubleVariable();
        private readonly NetworkVariable<byte> _roundEndReason =
            CreateByteVariable();

        private readonly NetworkVariable<float> _progress0 =
            CreateFloatVariable();
        private readonly NetworkVariable<float> _progress1 =
            CreateFloatVariable();
        private readonly NetworkVariable<float> _progress2 =
            CreateFloatVariable();
        private readonly NetworkVariable<float> _progress3 =
            CreateFloatVariable();
        private readonly NetworkVariable<float> _cooldownRemaining0 =
            CreateFloatVariable();
        private readonly NetworkVariable<float> _cooldownRemaining1 =
            CreateFloatVariable();
        private readonly NetworkVariable<float> _cooldownRemaining2 =
            CreateFloatVariable();
        private readonly NetworkVariable<float> _cooldownRemaining3 =
            CreateFloatVariable();
        private readonly NetworkVariable<uint> _playerPhases =
            CreateUIntVariable();
        private readonly NetworkVariable<byte> _poppedMask =
            CreateByteVariable();
        private readonly NetworkVariable<byte> _heldMask =
            CreateByteVariable();
        private readonly NetworkVariable<uint> _popOrders =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _scores =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _roundPoints =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _roundRanks =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _finalRanks =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _popRevision =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _inputEpoch =
            CreateUIntVariable();
        private readonly NetworkVariable<byte> _lastPoppedSlot =
            CreateByteVariable(NoPlayerSlot);

        private readonly bool[] _requiresFreshRelease =
            new bool[BalloonBlowRules.PlayerCount];
        private readonly NetworkPlayerAvatar[] _avatars =
            new NetworkPlayerAvatar[BalloonBlowRules.PlayerCount];
        private readonly List<BalloonBlowRoundResult> _roundResults =
            new List<BalloonBlowRoundResult>(
                BalloonBlowRules.RoundCount);

        private BalloonBlowRoundState _roundState;
        private double _runningStartedAt;
        private double _pausedRunningElapsed;
        private bool _completionReported;

        public static NetworkBalloonBlowState Instance {
            get;
            private set;
        }

        public NetworkBalloonBlowPhase Phase =>
            (NetworkBalloonBlowPhase)_phase.Value;
        public int RoundNumber => _roundNumber.Value;
        public int PlayerCount => BalloonBlowRules.PlayerCount;
        public bool IsPaused => _paused.Value;
        public BalloonBlowRoundEndReason RoundEndReason =>
            (BalloonBlowRoundEndReason)_roundEndReason.Value;
        public uint PopRevision => _popRevision.Value;
        public uint InputEpoch => _inputEpoch.Value;
        public int LastPoppedSlot =>
            _lastPoppedSlot.Value == NoPlayerSlot
                ? -1
                : _lastPoppedSlot.Value;
        public double RemainingSeconds => GetRemaining(
            _phaseEndsAt.Value,
            _pausedPhaseRemaining.Value,
            Phase == NetworkBalloonBlowPhase.Inactive ||
            Phase == NetworkBalloonBlowPhase.Complete);
        public double Remaining => RemainingSeconds;

        private double ServerNow =>
            NetworkManager != null && NetworkManager.IsListening
                ? NetworkManager.ServerTime.Time
                : Time.unscaledTimeAsDouble;

        public override void OnNetworkSpawn()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError(
                    "More than one NetworkBalloonBlowState is spawned.");
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
            if (Phase == NetworkBalloonBlowPhase.Running)
            {
                if (_roundState == null)
                {
                    return;
                }

                _roundState.AdvanceTo(GetRunningElapsed(now));
                SyncRoundSnapshotOnServer();
                if (_roundState.IsComplete)
                {
                    CompleteCurrentRoundOnServer(now);
                }
                return;
            }

            if (_phaseEndsAt.Value <= 0d || now < _phaseEndsAt.Value)
            {
                return;
            }

            switch (Phase)
            {
                case NetworkBalloonBlowPhase.Countdown:
                    BeginRunOnServer(now);
                    break;
                case NetworkBalloonBlowPhase.RoundResult:
                    if (_roundNumber.Value < BalloonBlowRules.RoundCount)
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

        public void BeginMatchOnServer(ulong matchSeed)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            _ = matchSeed;
            ClearLocalRuntime();
            ResetReplicatedStateOnServer();
            _matchActive.Value = true;
            _paused.Value = false;
            _completionReported = false;
            CacheAndFreezeBoardAvatarsOnServer();
            BeginRoundOnServer(1, ServerNow);
        }

        public bool SetInflateHeldOnServer(
            NetworkPlayerAvatar avatar,
            bool isHeld,
            int roundNumber,
            uint inputEpoch)
        {
            if (!TryResolveAuthoritativeSlot(avatar, out var slot))
            {
                return false;
            }

            if (!_matchActive.Value || _paused.Value ||
                Phase != NetworkBalloonBlowPhase.Running ||
                roundNumber != _roundNumber.Value ||
                inputEpoch == 0U || inputEpoch != _inputEpoch.Value ||
                RemainingSeconds <= 0d || _roundState == null ||
                _roundState.IsComplete)
            {
                return false;
            }

            avatar.StopServerInputOnServer();
            _avatars[slot] = avatar;
            var now = ServerNow;
            var elapsed = GetRunningElapsed(now);
            _roundState.AdvanceTo(elapsed);
            if (_roundState.IsComplete)
            {
                SyncRoundSnapshotOnServer();
                CompleteCurrentRoundOnServer(now);
                return false;
            }

            if (_requiresFreshRelease[slot])
            {
                if (!isHeld)
                {
                    _requiresFreshRelease[slot] = false;
                    _roundState.SetInflateHeld(slot, false, elapsed);
                    SyncRoundSnapshotOnServer();
                }
                return false;
            }

            var resolution = _roundState.SetInflateHeld(
                slot,
                isHeld,
                elapsed);
            SyncRoundSnapshotOnServer();
            if (_roundState.IsComplete)
            {
                CompleteCurrentRoundOnServer(now);
            }
            return resolution.WasChanged;
        }

        public bool CanAcceptInputForSlot(int slot)
        {
            return BalloonBlowRules.IsValidPlayerSlot(slot) &&
                   _matchActive.Value && !_paused.Value &&
                   Phase == NetworkBalloonBlowPhase.Running &&
                   RemainingSeconds > 0d &&
                   !IsPlayerPopped(slot);
        }

        public float GetPlayerProgress(int slot)
        {
            switch (slot)
            {
                case 0: return _progress0.Value;
                case 1: return _progress1.Value;
                case 2: return _progress2.Value;
                case 3: return _progress3.Value;
                default: return 0f;
            }
        }

        public BalloonBlowPlayerPhase GetPlayerPhase(int slot)
        {
            return BalloonBlowRules.IsValidPlayerSlot(slot)
                ? (BalloonBlowPlayerPhase)ReadPackedByte(
                    _playerPhases.Value,
                    slot)
                : BalloonBlowPlayerPhase.Ready;
        }

        public bool IsPlayerInflating(int slot) =>
            BalloonBlowRules.IsValidPlayerSlot(slot) &&
            GetPlayerPhase(slot) == BalloonBlowPlayerPhase.Inflating;

        public bool IsPlayerPopped(int slot) =>
            !BalloonBlowRules.IsValidPlayerSlot(slot) ||
            IsMaskBitSet(_poppedMask.Value, slot);

        public bool IsPlayerInputHeld(int slot) =>
            BalloonBlowRules.IsValidPlayerSlot(slot) &&
            IsMaskBitSet(_heldMask.Value, slot);

        public double GetPlayerCooldownRemainingSeconds(int slot)
        {
            switch (slot)
            {
                case 0: return _cooldownRemaining0.Value;
                case 1: return _cooldownRemaining1.Value;
                case 2: return _cooldownRemaining2.Value;
                case 3: return _cooldownRemaining3.Value;
                default: return 0d;
            }
        }

        public int GetPopOrder(int slot) =>
            ReadPackedByte(_popOrders.Value, slot);

        public int GetScore(int slot) =>
            ReadPackedByte(_scores.Value, slot);

        public int GetRoundPoints(int slot) =>
            ReadPackedByte(_roundPoints.Value, slot);

        public int GetRoundRank(int slot) =>
            ReadPackedByte(_roundRanks.Value, slot);

        public int GetPlayerRank(int slot) => GetRoundRank(slot);

        public int GetFinalRank(int slot) =>
            ReadPackedByte(_finalRanks.Value, slot);

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
            _pausedRunningElapsed =
                Phase == NetworkBalloonBlowPhase.Running
                    ? GetRunningElapsed(now)
                    : 0d;
            if (Phase == NetworkBalloonBlowPhase.Running &&
                _roundState != null && !_roundState.IsComplete)
            {
                _roundState.AdvanceTo(_pausedRunningElapsed);
                ClearHeldInputsOnServer(
                    _pausedRunningElapsed,
                    true);
                SyncRoundSnapshotOnServer();
            }
            else
            {
                ClearReplicatedHeldStateOnServer();
            }

            AdvanceInputEpochOnServer();
            _phaseEndsAt.Value = 0d;
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
            if (Phase == NetworkBalloonBlowPhase.Running)
            {
                _runningStartedAt = now - _pausedRunningElapsed;
            }

            AdvanceInputEpochOnServer();
            _pausedPhaseRemaining.Value = 0d;
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
            _requiresFreshRelease[slot] = true;
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
            AdvanceInputEpochOnServer();
            _roundState = new BalloonBlowRoundState(roundNumber);
            _roundNumber.Value = (byte)roundNumber;
            _phase.Value = (byte)NetworkBalloonBlowPhase.Countdown;
            _phaseEndsAt.Value = now + CountdownSeconds;
            _pausedPhaseRemaining.Value = 0d;
            _roundEndReason.Value =
                (byte)BalloonBlowRoundEndReason.None;
            _roundPoints.Value = 0U;
            _roundRanks.Value = 0U;
            _poppedMask.Value = 0;
            _heldMask.Value = 0;
            _popOrders.Value = 0U;
            _lastPoppedSlot.Value = NoPlayerSlot;
            _runningStartedAt = 0d;
            for (var slot = 0;
                 slot < BalloonBlowRules.PlayerCount;
                 slot++)
            {
                _requiresFreshRelease[slot] = true;
            }
            SyncRoundSnapshotOnServer();
            FreezeBoardAvatarsOnServer();
        }

        private void BeginRunOnServer(double now)
        {
            AdvanceInputEpochOnServer();
            for (var slot = 0;
                 slot < BalloonBlowRules.PlayerCount;
                 slot++)
            {
                _requiresFreshRelease[slot] = true;
            }
            _phase.Value = (byte)NetworkBalloonBlowPhase.Running;
            _phaseEndsAt.Value = now + BalloonBlowRules.RoundSeconds;
            _runningStartedAt = now;
            SyncRoundSnapshotOnServer();
        }

        private void CompleteCurrentRoundOnServer(double now)
        {
            if (_roundState == null || !_roundState.IsComplete ||
                _roundResults.Count >= _roundNumber.Value)
            {
                return;
            }

            SyncRoundSnapshotOnServer();
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
            AdvanceInputEpochOnServer();
            _phase.Value = (byte)NetworkBalloonBlowPhase.RoundResult;
            _phaseEndsAt.Value = now + RoundResultSeconds;
            ClearReplicatedHeldStateOnServer();
        }

        private void CompleteMatchOnServer()
        {
            if (_completionReported ||
                _roundResults.Count != BalloonBlowRules.RoundCount)
            {
                return;
            }

            var leaderboard =
                BalloonBlowMatchScoring.BuildLeaderboard(_roundResults);
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
                !match.TryCompleteBalloonBlowOnServer(leaderboard))
            {
                return;
            }

            _completionReported = true;
            _matchActive.Value = false;
            _phase.Value = (byte)NetworkBalloonBlowPhase.Complete;
            _phaseEndsAt.Value = 0d;
            ClearReplicatedHeldStateOnServer();
            FreezeBoardAvatarsOnServer();
        }

        private void SyncRoundSnapshotOnServer()
        {
            if (_roundState == null)
            {
                return;
            }

            var previousPoppedMask = _poppedMask.Value;
            byte poppedMask = 0;
            byte heldMask = 0;
            uint playerPhases = 0U;
            uint popOrders = 0U;
            var newestPoppedSlot = -1;
            ulong newestPopOrder = 0UL;
            for (var slot = 0;
                 slot < BalloonBlowRules.PlayerCount;
                 slot++)
            {
                var player = _roundState.GetPlayer(slot);
                SetPlayerProgressOnServer(slot, player.ProgressPercent);
                SetCooldownRemainingOnServer(
                    slot,
                    (float)player.CooldownRemainingSeconds);
                var replicatedPhase =
                    _requiresFreshRelease[slot] &&
                    !player.IsPopped &&
                    player.CooldownRemainingSeconds <= 0d
                        ? BalloonBlowPlayerPhase.AwaitingRelease
                        : player.Phase;
                playerPhases = WritePackedByte(
                    playerPhases,
                    slot,
                    (int)replicatedPhase);
                if (player.IsInflateHeld)
                {
                    heldMask = SetMaskBit(heldMask, slot, true);
                }
                if (!player.IsPopped)
                {
                    continue;
                }

                poppedMask = SetMaskBit(poppedMask, slot, true);
                popOrders = WritePackedByte(
                    popOrders,
                    slot,
                    (int)Math.Min(player.PopOrder, (ulong)byte.MaxValue));
                if (!IsMaskBitSet(previousPoppedMask, slot) &&
                    player.PopOrder > newestPopOrder)
                {
                    newestPopOrder = player.PopOrder;
                    newestPoppedSlot = slot;
                }
            }

            _playerPhases.Value = playerPhases;
            _heldMask.Value = heldMask;
            _poppedMask.Value = poppedMask;
            _popOrders.Value = popOrders;
            if (newestPoppedSlot >= 0)
            {
                _lastPoppedSlot.Value = (byte)newestPoppedSlot;
                _popRevision.Value++;
            }
        }

        private void ClearHeldInputsOnServer(
            double elapsed,
            bool requireFreshRelease)
        {
            if (_roundState == null || _roundState.IsComplete)
            {
                ClearReplicatedHeldStateOnServer();
                return;
            }

            _roundState.InterruptHeldInputs(elapsed);
            for (var slot = 0;
                 slot < BalloonBlowRules.PlayerCount;
                 slot++)
            {
                _requiresFreshRelease[slot] = requireFreshRelease;
            }
            _heldMask.Value = 0;
        }

        private void ClearReplicatedHeldStateOnServer()
        {
            _heldMask.Value = 0;
            for (var slot = 0;
                 slot < BalloonBlowRules.PlayerCount;
                 slot++)
            {
                _requiresFreshRelease[slot] = true;
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
                   BalloonBlowRules.IsValidPlayerSlot(slot) &&
                   match != null &&
                   ReferenceEquals(match.GetAvatarForSlot(slot), avatar);
        }

        private void CacheAndFreezeBoardAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0;
                 slot < BalloonBlowRules.PlayerCount;
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
                 slot < BalloonBlowRules.PlayerCount;
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

        private double GetRunningElapsed(double now)
        {
            return Math.Max(
                0d,
                Math.Min(
                    BalloonBlowRules.RoundSeconds,
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

        private void SetPlayerProgressOnServer(int slot, float value)
        {
            switch (slot)
            {
                case 0: _progress0.Value = value; break;
                case 1: _progress1.Value = value; break;
                case 2: _progress2.Value = value; break;
                case 3: _progress3.Value = value; break;
            }
        }

        private void SetCooldownRemainingOnServer(int slot, float value)
        {
            switch (slot)
            {
                case 0: _cooldownRemaining0.Value = value; break;
                case 1: _cooldownRemaining1.Value = value; break;
                case 2: _cooldownRemaining2.Value = value; break;
                case 3: _cooldownRemaining3.Value = value; break;
            }
        }

        private void ResetReplicatedStateOnServer()
        {
            AdvanceInputEpochOnServer();
            _matchActive.Value = false;
            _paused.Value = false;
            _phase.Value = (byte)NetworkBalloonBlowPhase.Inactive;
            _roundNumber.Value = 0;
            _phaseEndsAt.Value = 0d;
            _pausedPhaseRemaining.Value = 0d;
            _roundEndReason.Value = 0;
            _progress0.Value = 0f;
            _progress1.Value = 0f;
            _progress2.Value = 0f;
            _progress3.Value = 0f;
            _cooldownRemaining0.Value = 0f;
            _cooldownRemaining1.Value = 0f;
            _cooldownRemaining2.Value = 0f;
            _cooldownRemaining3.Value = 0f;
            _playerPhases.Value = 0U;
            _poppedMask.Value = 0;
            _heldMask.Value = 0;
            _popOrders.Value = 0U;
            _scores.Value = 0U;
            _roundPoints.Value = 0U;
            _roundRanks.Value = 0U;
            _finalRanks.Value = 0U;
            _popRevision.Value = 0U;
            _lastPoppedSlot.Value = NoPlayerSlot;
        }

        private void AdvanceInputEpochOnServer()
        {
            // Zero is reserved for an input that has never observed a server
            // epoch. Wrap to one so overflow cannot accidentally authorize it.
            _inputEpoch.Value = _inputEpoch.Value == uint.MaxValue
                ? 1U
                : Math.Max(1U, _inputEpoch.Value + 1U);
        }

        private void ClearLocalRuntime()
        {
            _roundState = null;
            _roundResults.Clear();
            _runningStartedAt = 0d;
            _pausedRunningElapsed = 0d;
            _completionReported = false;
            for (var slot = 0;
                 slot < BalloonBlowRules.PlayerCount;
                 slot++)
            {
                _requiresFreshRelease[slot] = false;
                _avatars[slot] = null;
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

        private static NetworkVariable<float> CreateFloatVariable()
        {
            return new NetworkVariable<float>(
                0f,
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
            if (!BalloonBlowRules.IsValidPlayerSlot(slot))
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
            if (!BalloonBlowRules.IsValidPlayerSlot(slot))
            {
                return 0;
            }
            return (int)((packed >> (slot * 8)) & 0xFFU);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
