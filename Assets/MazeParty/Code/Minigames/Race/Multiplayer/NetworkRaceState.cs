using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.Race;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum NetworkRacePhase : byte
    {
        Inactive,
        Countdown,
        Running,
        RoundResult,
        Complete
    }

    /// <summary>
    /// Server-authoritative alternating-input race. Clients submit only A/D
    /// press intents; the server owns alternation validation, progress, timing,
    /// round ranks and final rewards.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkRaceState : NetworkBehaviour
    {
        public const double CountdownSeconds = 3d;
        public const double RoundResultSeconds = 4d;
        public const float TrackCenterX = 1090f;
        public const float TrackStartZ = -20f;
        public const float TrackLength = 40f;
        public const float LaneWidth = 2.4f;

        private readonly NetworkVariable<bool> _matchActive =
            CreateBoolVariable();
        private readonly NetworkVariable<bool> _paused =
            CreateBoolVariable();
        private readonly NetworkVariable<byte> _phase =
            CreateByteVariable((byte)NetworkRacePhase.Inactive);
        private readonly NetworkVariable<byte> _roundNumber =
            CreateByteVariable(0);
        private readonly NetworkVariable<double> _phaseEndsAt =
            CreateDoubleVariable();
        private readonly NetworkVariable<double> _pausedPhaseRemaining =
            CreateDoubleVariable();
        private readonly NetworkVariable<uint> _inputEpoch =
            CreateUIntVariable();
        private readonly NetworkVariable<ulong> _progress =
            CreateULongVariable();
        private readonly NetworkVariable<ulong> _totalScores =
            CreateULongVariable();
        private readonly NetworkVariable<uint> _finalRanks =
            CreateUIntVariable();

        private readonly int[] _serverProgress =
            new int[RaceRules.PlayerCount];
        private readonly ulong[] _progressOrder =
            new ulong[RaceRules.PlayerCount];
        private readonly RaceStepInput[] _lastStepInputs =
            new RaceStepInput[RaceRules.PlayerCount];
        private readonly int[] _totalPoints =
            new int[RaceRules.PlayerCount];
        private readonly NetworkPlayerAvatar[] _avatars =
            new NetworkPlayerAvatar[RaceRules.PlayerCount];

        private IReadOnlyList<RaceRoundEntry> _roundLeaderboard;
        private IReadOnlyList<RaceLeaderboardEntry> _finalLeaderboard;
        private ulong _serverEventOrder;
        private bool _completionReported;

        public static NetworkRaceState Instance { get; private set; }

        public NetworkRacePhase Phase => (NetworkRacePhase)_phase.Value;
        public int RoundNumber => _roundNumber.Value;
        public uint InputEpoch => _inputEpoch.Value;
        public bool IsPaused => _paused.Value;
        public bool IsMatchActive => _matchActive.Value;
        public double Remaining => GetRemaining(
            _phaseEndsAt.Value,
            _pausedPhaseRemaining.Value,
            Phase == NetworkRacePhase.Inactive ||
            Phase == NetworkRacePhase.Complete);

        private double ServerNow =>
            NetworkManager != null && NetworkManager.IsListening
                ? NetworkManager.ServerTime.Time
                : Time.unscaledTimeAsDouble;

        public override void OnNetworkSpawn()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("More than one NetworkRaceState is spawned.");
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
            if (_phaseEndsAt.Value <= 0d || now < _phaseEndsAt.Value)
            {
                return;
            }

            switch (Phase)
            {
                case NetworkRacePhase.Countdown:
                    BeginRunOnServer(now);
                    break;
                case NetworkRacePhase.Running:
                    CompleteRoundOnServer(now);
                    break;
                case NetworkRacePhase.RoundResult:
                    if (_roundNumber.Value < RaceRules.RoundCount)
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
            _matchActive.Value = true;
            _paused.Value = false;
            CacheAndFreezeBoardAvatarsOnServer();
            BeginRoundCountdownOnServer(ServerNow, 1);
            Debug.Log(
                "[Race] Match started. Seed " + matchSeed +
                "; 500 alternating A/D steps per round.");
        }

        public bool CanAcceptInputForSlot(int slot)
        {
            return RaceRules.IsValidPlayerSlot(slot) &&
                   _matchActive.Value && !_paused.Value &&
                   Phase == NetworkRacePhase.Running &&
                   GetProgress(slot) < RaceRules.RequiredSteps &&
                   Remaining > 0d;
        }

        public bool RequestStepOnServer(
            NetworkPlayerAvatar avatar,
            RaceStepInput input,
            int roundNumber,
            uint inputEpoch)
        {
            if (!TryResolveAuthoritativeSlot(avatar, out var slot) ||
                !ValidateInputEnvelope(roundNumber, inputEpoch) ||
                !RaceRules.IsAlternatingStep(
                    _lastStepInputs[slot],
                    input))
            {
                return false;
            }

            _avatars[slot] = avatar;
            avatar.StopServerInputOnServer();
            _lastStepInputs[slot] = input;
            _serverProgress[slot] = Math.Min(
                RaceRules.RequiredSteps,
                _serverProgress[slot] + 1);
            _serverEventOrder++;
            _progressOrder[slot] = _serverEventOrder;
            SyncProgressOnServer();

            if (_serverProgress[slot] >= RaceRules.RequiredSteps)
            {
                Debug.Log(
                    "[Race] P" + (slot + 1) +
                    " reached 500 steps first in round " +
                    _roundNumber.Value + ".");
                CompleteRoundOnServer(ServerNow);
            }
            return true;
        }

        public int GetProgress(int slot)
        {
            if (!RaceRules.IsValidPlayerSlot(slot))
            {
                return 0;
            }
            return (int)((_progress.Value >> (slot * 16)) & 0xffffUL);
        }

        public float GetNormalizedProgress(int slot)
        {
            return Mathf.Clamp01(
                GetProgress(slot) / (float)RaceRules.RequiredSteps);
        }

        public int GetTotalScore(int slot)
        {
            if (!RaceRules.IsValidPlayerSlot(slot))
            {
                return 0;
            }
            return (int)(
                (_totalScores.Value >> (slot * 16)) & 0xffffUL);
        }

        public int GetFinalRank(int slot)
        {
            if (!RaceRules.IsValidPlayerSlot(slot))
            {
                return 0;
            }
            return (int)((_finalRanks.Value >> (slot * 8)) & 0xffU);
        }

        public static float GetLaneX(int slot)
        {
            if (!RaceRules.IsValidPlayerSlot(slot))
            {
                return TrackCenterX;
            }
            return TrackCenterX +
                   (slot - (RaceRules.PlayerCount - 1) * 0.5f) *
                   LaneWidth;
        }

        public static float ProgressToWorldZ(int progress)
        {
            return TrackStartZ +
                   Mathf.Clamp01(
                       progress / (float)RaceRules.RequiredSteps) *
                   TrackLength;
        }

        public void PauseOnServer(double now)
        {
            if (!IsSpawned || !IsServer || !_matchActive.Value ||
                _paused.Value)
            {
                return;
            }
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

        private void BeginRoundCountdownOnServer(double now, int roundNumber)
        {
            _roundNumber.Value = (byte)Mathf.Clamp(
                roundNumber,
                1,
                RaceRules.RoundCount);
            Array.Clear(_serverProgress, 0, _serverProgress.Length);
            Array.Clear(_progressOrder, 0, _progressOrder.Length);
            Array.Clear(_lastStepInputs, 0, _lastStepInputs.Length);
            _serverEventOrder = 0UL;
            _roundLeaderboard = null;
            SyncProgressOnServer();
            AdvanceInputEpochOnServer();
            _phase.Value = (byte)NetworkRacePhase.Countdown;
            _phaseEndsAt.Value = now + CountdownSeconds;
            Debug.Log(
                "[Race] Round " + _roundNumber.Value +
                " countdown started.");
        }

        private void BeginRunOnServer(double now)
        {
            AdvanceInputEpochOnServer();
            _phase.Value = (byte)NetworkRacePhase.Running;
            _phaseEndsAt.Value = now + RaceRules.RoundSeconds;
            Debug.Log(
                "[Race] Round " + _roundNumber.Value +
                " started. Alternate A and D; first to 500 wins.");
        }

        private void CompleteRoundOnServer(double now)
        {
            if (Phase != NetworkRacePhase.Running)
            {
                return;
            }

            _roundLeaderboard = RaceRules.BuildRoundLeaderboard(
                _serverProgress,
                _progressOrder);
            var points = RaceRules.BuildRoundPoints(_roundLeaderboard);
            for (var slot = 0; slot < RaceRules.PlayerCount; slot++)
            {
                _totalPoints[slot] += points[slot];
            }
            SyncTotalScoresOnServer();
            AdvanceInputEpochOnServer();
            _phase.Value = (byte)NetworkRacePhase.RoundResult;
            _phaseEndsAt.Value = now + RoundResultSeconds;
            Debug.Log(
                "[Race] Round " + _roundNumber.Value +
                " complete. " + BuildRoundResultLog(_roundLeaderboard));
        }

        private void CompleteMatchOnServer()
        {
            if (_completionReported)
            {
                return;
            }

            _finalLeaderboard =
                RaceRules.BuildFinalLeaderboard(_totalPoints);
            uint packedRanks = 0U;
            for (var index = 0; index < _finalLeaderboard.Count; index++)
            {
                var entry = _finalLeaderboard[index];
                packedRanks |=
                    (uint)entry.Rank << (entry.PlayerSlot * 8);
            }
            _finalRanks.Value = packedRanks;

            var match = NetworkMatchState.Instance;
            if (match == null ||
                !match.TryCompleteRaceOnServer(_finalLeaderboard))
            {
                return;
            }

            _completionReported = true;
            _matchActive.Value = false;
            _phase.Value = (byte)NetworkRacePhase.Complete;
            _phaseEndsAt.Value = 0d;
            FreezeBoardAvatarsOnServer();
            Debug.Log("[Race] Match complete. " + BuildTotalScoreLog());
        }

        private bool ValidateInputEnvelope(int roundNumber, uint inputEpoch)
        {
            return _matchActive.Value && !_paused.Value &&
                   Phase == NetworkRacePhase.Running &&
                   roundNumber == _roundNumber.Value &&
                   inputEpoch != 0U && inputEpoch == _inputEpoch.Value &&
                   Remaining > 0d;
        }

        private bool TryResolveAuthoritativeSlot(
            NetworkPlayerAvatar avatar,
            out int slot)
        {
            slot = avatar != null ? avatar.AssignedSlot : -1;
            return IsSpawned && IsServer && avatar != null &&
                   avatar.IsSpawned && RaceRules.IsValidPlayerSlot(slot);
        }

        private void CacheAndFreezeBoardAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0; slot < RaceRules.PlayerCount; slot++)
            {
                _avatars[slot] =
                    match != null ? match.GetAvatarForSlot(slot) : null;
                _avatars[slot]?.StopServerInputOnServer();
            }
        }

        private void FreezeBoardAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0; slot < RaceRules.PlayerCount; slot++)
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
            _phase.Value = (byte)NetworkRacePhase.Inactive;
            _roundNumber.Value = 0;
            _phaseEndsAt.Value = 0d;
            _pausedPhaseRemaining.Value = 0d;
            _inputEpoch.Value = 0U;
            _progress.Value = 0UL;
            _totalScores.Value = 0UL;
            _finalRanks.Value = 0U;
            Array.Clear(_serverProgress, 0, _serverProgress.Length);
            Array.Clear(_progressOrder, 0, _progressOrder.Length);
            Array.Clear(_lastStepInputs, 0, _lastStepInputs.Length);
            Array.Clear(_totalPoints, 0, _totalPoints.Length);
            _serverEventOrder = 0UL;
        }

        private void SyncProgressOnServer()
        {
            ulong packed = 0UL;
            for (var slot = 0; slot < RaceRules.PlayerCount; slot++)
            {
                packed |=
                    (ulong)Mathf.Clamp(
                        _serverProgress[slot],
                        0,
                        ushort.MaxValue) << (slot * 16);
            }
            _progress.Value = packed;
        }

        private void SyncTotalScoresOnServer()
        {
            ulong packed = 0UL;
            for (var slot = 0; slot < RaceRules.PlayerCount; slot++)
            {
                packed |=
                    (ulong)Mathf.Clamp(
                        _totalPoints[slot],
                        0,
                        ushort.MaxValue) << (slot * 16);
            }
            _totalScores.Value = packed;
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
            _roundLeaderboard = null;
            _finalLeaderboard = null;
            _serverEventOrder = 0UL;
            _completionReported = false;
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

        private static string BuildRoundResultLog(
            IReadOnlyList<RaceRoundEntry> leaderboard)
        {
            return "1st P" + (leaderboard[0].PlayerSlot + 1) +
                   " (" + leaderboard[0].Progress + "), 2nd P" +
                   (leaderboard[1].PlayerSlot + 1) + " (" +
                   leaderboard[1].Progress + "), 3rd P" +
                   (leaderboard[2].PlayerSlot + 1) + " (" +
                   leaderboard[2].Progress + "), 4th P" +
                   (leaderboard[3].PlayerSlot + 1) + " (" +
                   leaderboard[3].Progress + ").";
        }

        private string BuildTotalScoreLog()
        {
            return "P1 " + _totalPoints[0] + ", P2 " +
                   _totalPoints[1] + ", P3 " + _totalPoints[2] +
                   ", P4 " + _totalPoints[3] + ".";
        }

        private static NetworkVariable<bool> CreateBoolVariable()
        {
            return new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        private static NetworkVariable<byte> CreateByteVariable(byte value)
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
    }
}
