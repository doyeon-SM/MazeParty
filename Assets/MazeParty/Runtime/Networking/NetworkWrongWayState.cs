using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.WrongWay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum NetworkWrongWayPhase : byte
    {
        Inactive,
        Countdown,
        Running,
        RoundResult,
        Complete
    }

    /// <summary>
    /// Server-authoritative two-round WrongWay race. Only compact progress,
    /// prompts and standings are replicated; the canonical direction sequence
    /// and event-order tie breaker remain on the host.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkWrongWayState : NetworkBehaviour
    {
        public const double RoundResultSeconds = 4d;
        public const float ArenaCenterX = 340f;
        public const float StepWidth = 1.45f;
        public const float StepHeight = 0.24f;
        public const float StepDepth = 0.72f;
        public const float LaneSpacing = 2.2f;

        private readonly NetworkVariable<byte> _phase =
            new NetworkVariable<byte>((byte)NetworkWrongWayPhase.Inactive);
        private readonly NetworkVariable<byte> _roundNumber =
            new NetworkVariable<byte>();
        private readonly NetworkVariable<double> _phaseEndsAt =
            new NetworkVariable<double>();
        private readonly NetworkVariable<bool> _paused =
            new NetworkVariable<bool>();
        private readonly NetworkVariable<double> _pausedPhaseRemaining =
            new NetworkVariable<double>();
        private readonly NetworkVariable<uint> _progressPacked =
            new NetworkVariable<uint>();
        private readonly NetworkVariable<byte> _promptPacked =
            new NetworkVariable<byte>();
        private readonly NetworkVariable<uint> _roundPointsPacked =
            new NetworkVariable<uint>();
        private readonly NetworkVariable<uint> _scorePacked =
            new NetworkVariable<uint>();
        private readonly NetworkVariable<uint> _roundRanksPacked =
            new NetworkVariable<uint>();
        private readonly NetworkVariable<uint> _finalRanksPacked =
            new NetworkVariable<uint>();
        private readonly NetworkVariable<uint> _wrongCountsPacked =
            new NetworkVariable<uint>();
        private readonly NetworkVariable<double> _recovery0 =
            new NetworkVariable<double>();
        private readonly NetworkVariable<double> _recovery1 =
            new NetworkVariable<double>();
        private readonly NetworkVariable<double> _recovery2 =
            new NetworkVariable<double>();
        private readonly NetworkVariable<double> _recovery3 =
            new NetworkVariable<double>();
        private readonly NetworkVariable<double> _pausedRecovery0 =
            new NetworkVariable<double>();
        private readonly NetworkVariable<double> _pausedRecovery1 =
            new NetworkVariable<double>();
        private readonly NetworkVariable<double> _pausedRecovery2 =
            new NetworkVariable<double>();
        private readonly NetworkVariable<double> _pausedRecovery3 =
            new NetworkVariable<double>();

        private readonly List<WrongWayRoundResult> _roundResults =
            new List<WrongWayRoundResult>(WrongWayRules.RoundCount);
        private readonly int[] _scores = new int[WrongWayRules.PlayerCount];
        private readonly int[] _wrongCounts = new int[WrongWayRules.PlayerCount];

        private WrongWayRoundState _roundState;
        private IReadOnlyList<WrongWayLeaderboardEntry> _leaderboard;
        private ulong _matchSeed;
        private double _roundRunningStartedAt;

        public static NetworkWrongWayState Instance { get; private set; }

        public NetworkWrongWayPhase Phase =>
            (NetworkWrongWayPhase)_phase.Value;
        public int RoundNumber => _roundNumber.Value;
        public bool IsPaused => _paused.Value;
        public double Remaining
        {
            get
            {
                if (Phase == NetworkWrongWayPhase.Inactive ||
                    Phase == NetworkWrongWayPhase.Complete)
                {
                    return 0d;
                }

                return _paused.Value
                    ? Math.Max(0d, _pausedPhaseRemaining.Value)
                    : Math.Max(0d, _phaseEndsAt.Value - ServerNow);
            }
        }

        private double ServerNow =>
            NetworkManager != null && NetworkManager.IsListening
                ? NetworkManager.ServerTime.Time
                : Time.unscaledTimeAsDouble;

        public override void OnNetworkSpawn()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(
                    "Multiple NetworkWrongWayState instances are active.");
            }

            Instance = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || _paused.Value)
            {
                return;
            }

            var now = ServerNow;
            switch (Phase)
            {
                case NetworkWrongWayPhase.Countdown:
                    if (now >= _phaseEndsAt.Value)
                    {
                        _phase.Value = (byte)NetworkWrongWayPhase.Running;
                        _roundRunningStartedAt = now;
                        _phaseEndsAt.Value = now + WrongWayRules.RoundSeconds;
                    }
                    break;
                case NetworkWrongWayPhase.Running:
                    if (now >= _phaseEndsAt.Value && _roundState != null)
                    {
                        _roundState.TryEndForTimeout(
                            WrongWayRules.RoundSeconds);
                        CompleteCurrentRound(now);
                    }
                    break;
                case NetworkWrongWayPhase.RoundResult:
                    if (now >= _phaseEndsAt.Value)
                    {
                        if (_roundNumber.Value < WrongWayRules.RoundCount)
                        {
                            BeginRoundOnServer(_roundNumber.Value + 1, now);
                        }
                        else
                        {
                            TryCompleteMatchOnServer();
                        }
                    }
                    break;
            }
        }

        public void BeginMatchOnServer(ulong matchSeed)
        {
            if (!IsServer)
            {
                return;
            }

            _matchSeed = matchSeed;
            _roundResults.Clear();
            _leaderboard = null;
            Array.Clear(_scores, 0, _scores.Length);
            Array.Clear(_wrongCounts, 0, _wrongCounts.Length);
            _scorePacked.Value = 0u;
            _roundPointsPacked.Value = 0u;
            _roundRanksPacked.Value = 0u;
            _finalRanksPacked.Value = 0u;
            _wrongCountsPacked.Value = 0u;
            _paused.Value = false;
            _pausedPhaseRemaining.Value = 0d;
            ClearRecovery();
            BeginRoundOnServer(1, ServerNow);
        }

        public void EndMatchOnServer()
        {
            if (!IsServer)
            {
                return;
            }

            _roundState = null;
            _roundResults.Clear();
            _leaderboard = null;
            _phase.Value = (byte)NetworkWrongWayPhase.Inactive;
            _roundNumber.Value = 0;
            _phaseEndsAt.Value = 0d;
            _paused.Value = false;
            _pausedPhaseRemaining.Value = 0d;
            _progressPacked.Value = 0u;
            _promptPacked.Value = 0;
            _roundPointsPacked.Value = 0u;
            _roundRanksPacked.Value = 0u;
            _finalRanksPacked.Value = 0u;
            _wrongCountsPacked.Value = 0u;
            ClearRecovery();
        }

        public bool TrySubmitDirectionOnServer(
            NetworkPlayerAvatar avatar,
            WrongWayDirection direction)
        {
            if (!IsServer || avatar == null || !avatar.IsSpawned ||
                !WrongWayRules.IsValidPlayerSlot(avatar.AssignedSlot) ||
                direction < WrongWayDirection.Up ||
                direction > WrongWayDirection.Right ||
                Phase != NetworkWrongWayPhase.Running ||
                _roundState == null ||
                _paused.Value)
            {
                return false;
            }

            var slot = avatar.AssignedSlot;
            var now = ServerNow;
            if (now >= _phaseEndsAt.Value)
            {
                _roundState.TryEndForTimeout(WrongWayRules.RoundSeconds);
                CompleteCurrentRound(now);
                return false;
            }

            if (!CanAcceptInputAt(slot, now))
            {
                return false;
            }

            var resolution = _roundState.SubmitInput(slot, direction, now);
            if (!resolution.WasAccepted)
            {
                return false;
            }

            if (!resolution.WasCorrect)
            {
                _wrongCounts[slot]++;
                SetRecoveryDeadline(slot, resolution.InputLockedUntil);
            }
            else
            {
                SetRecoveryDeadline(slot, 0d);
            }

            SyncRoundSnapshot();
            if (_roundState.IsComplete)
            {
                CompleteCurrentRound(now);
            }

            return true;
        }

        public bool CanAcceptInputForSlot(int slot)
        {
            return CanAcceptInputAt(slot, ServerNow);
        }

        private bool CanAcceptInputAt(int slot, double now)
        {
            return WrongWayRules.IsValidPlayerSlot(slot) &&
                   Phase == NetworkWrongWayPhase.Running &&
                   !_paused.Value &&
                   now < _phaseEndsAt.Value &&
                   GetProgress(slot) < WrongWayRules.StepCount &&
                   now >= GetRecoveryDeadline(slot);
        }

        public int GetProgress(int slot)
        {
            ValidateSlot(slot);
            return GetPackedByte(_progressPacked.Value, slot);
        }

        public WrongWayDirection GetCurrentDirection(int slot)
        {
            ValidateSlot(slot);
            return (WrongWayDirection)(
                (_promptPacked.Value >> (slot * 2)) & 0x3);
        }

        public bool IsRecovering(int slot)
        {
            ValidateSlot(slot);
            return Phase == NetworkWrongWayPhase.Running &&
                   (_paused.Value
                       ? GetPausedRecovery(slot) > 0d
                       : ServerNow < GetRecoveryDeadline(slot));
        }

        public int GetRoundPoints(int slot)
        {
            ValidateSlot(slot);
            return GetPackedByte(_roundPointsPacked.Value, slot);
        }

        public int GetScore(int slot)
        {
            ValidateSlot(slot);
            return GetPackedByte(_scorePacked.Value, slot);
        }

        public int GetRoundRank(int slot)
        {
            ValidateSlot(slot);
            return GetPackedByte(_roundRanksPacked.Value, slot);
        }

        public int GetFinalRank(int slot)
        {
            ValidateSlot(slot);
            return GetPackedByte(_finalRanksPacked.Value, slot);
        }

        public int GetWrongCount(int slot)
        {
            ValidateSlot(slot);
            return GetPackedByte(_wrongCountsPacked.Value, slot);
        }

        public void PauseOnServer(double now)
        {
            if (!IsServer || _paused.Value ||
                Phase == NetworkWrongWayPhase.Inactive ||
                Phase == NetworkWrongWayPhase.Complete)
            {
                return;
            }

            _pausedPhaseRemaining.Value =
                Math.Max(0d, _phaseEndsAt.Value - now);
            for (var slot = 0; slot < WrongWayRules.PlayerCount; slot++)
            {
                SetPausedRecovery(
                    slot,
                    Math.Max(0d, GetRecoveryDeadline(slot) - now));
                SetRecoveryDeadline(slot, 0d);
            }

            _phaseEndsAt.Value = 0d;
            _paused.Value = true;
        }

        public void ResumeOnServer(double now)
        {
            if (!IsServer || !_paused.Value)
            {
                return;
            }

            _phaseEndsAt.Value =
                now + Math.Max(0d, _pausedPhaseRemaining.Value);
            for (var slot = 0; slot < WrongWayRules.PlayerCount; slot++)
            {
                var remaining = GetPausedRecovery(slot);
                SetRecoveryDeadline(
                    slot,
                    remaining > 0d ? now + remaining : 0d);
                SetPausedRecovery(slot, 0d);
            }

            if (Phase == NetworkWrongWayPhase.Running)
            {
                _roundRunningStartedAt =
                    _phaseEndsAt.Value - WrongWayRules.RoundSeconds;
            }

            _pausedPhaseRemaining.Value = 0d;
            _paused.Value = false;
        }

        private void BeginRoundOnServer(int roundNumber, double now)
        {
            _roundState = new WrongWayRoundState(_matchSeed, roundNumber);
            _roundNumber.Value = (byte)roundNumber;
            _phase.Value = (byte)NetworkWrongWayPhase.Countdown;
            _phaseEndsAt.Value = now + WrongWayRules.CountdownSeconds;
            _roundRunningStartedAt = 0d;
            _progressPacked.Value = 0u;
            _roundPointsPacked.Value = 0u;
            _roundRanksPacked.Value = 0u;
            ClearRecovery();
            SyncRoundSnapshot();
        }

        private void CompleteCurrentRound(double now)
        {
            if (_roundState == null || !_roundState.IsComplete ||
                _roundResults.Count >= _roundNumber.Value)
            {
                return;
            }

            var result = _roundState.Result;
            _roundResults.Add(result);
            uint pointsPacked = 0u;
            uint ranksPacked = 0u;
            for (var index = 0; index < result.Standings.Count; index++)
            {
                var standing = result.Standings[index];
                _scores[standing.PlayerSlot] += standing.Points;
                pointsPacked = SetPackedByte(
                    pointsPacked,
                    standing.PlayerSlot,
                    standing.Points);
                ranksPacked = SetPackedByte(
                    ranksPacked,
                    standing.PlayerSlot,
                    standing.Rank);
            }

            _roundPointsPacked.Value = pointsPacked;
            _roundRanksPacked.Value = ranksPacked;
            _scorePacked.Value = PackBytes(_scores);
            _wrongCountsPacked.Value = PackBytes(_wrongCounts);

            if (_roundResults.Count == WrongWayRules.RoundCount)
            {
                _leaderboard =
                    WrongWayMatchScoring.BuildLeaderboard(_roundResults);
                uint finalRanks = 0u;
                for (var index = 0; index < _leaderboard.Count; index++)
                {
                    var entry = _leaderboard[index];
                    finalRanks = SetPackedByte(
                        finalRanks,
                        entry.PlayerSlot,
                        entry.Rank);
                }

                _finalRanksPacked.Value = finalRanks;
            }

            _phase.Value = (byte)NetworkWrongWayPhase.RoundResult;
            _phaseEndsAt.Value = now + RoundResultSeconds;
            ClearRecovery();
        }

        private void TryCompleteMatchOnServer()
        {
            if (_leaderboard == null)
            {
                return;
            }

            var match = NetworkMatchState.Instance;
            if (match == null ||
                !match.TryCompleteWrongWayOnServer(_leaderboard))
            {
                return;
            }

            _phase.Value = (byte)NetworkWrongWayPhase.Complete;
            _phaseEndsAt.Value = 0d;
            _pausedPhaseRemaining.Value = 0d;
        }

        private void SyncRoundSnapshot()
        {
            if (_roundState == null)
            {
                return;
            }

            uint progress = 0u;
            byte prompts = 0;
            for (var slot = 0; slot < WrongWayRules.PlayerCount; slot++)
            {
                var player = _roundState.GetPlayer(slot);
                progress = SetPackedByte(
                    progress,
                    slot,
                    player.CompletedSteps);
                var prompt = _roundState.GetPromptForSlot(slot);
                var direction = prompt ?? WrongWayDirection.Up;
                prompts = (byte)(
                    prompts |
                    (((byte)direction & 0x3) << (slot * 2)));
            }

            _progressPacked.Value = progress;
            _promptPacked.Value = prompts;
        }

        private void ClearRecovery()
        {
            for (var slot = 0; slot < WrongWayRules.PlayerCount; slot++)
            {
                SetRecoveryDeadline(slot, 0d);
                SetPausedRecovery(slot, 0d);
            }
        }

        private double GetRecoveryDeadline(int slot)
        {
            switch (slot)
            {
                case 0: return _recovery0.Value;
                case 1: return _recovery1.Value;
                case 2: return _recovery2.Value;
                case 3: return _recovery3.Value;
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        private void SetRecoveryDeadline(int slot, double value)
        {
            switch (slot)
            {
                case 0: _recovery0.Value = value; break;
                case 1: _recovery1.Value = value; break;
                case 2: _recovery2.Value = value; break;
                case 3: _recovery3.Value = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        private double GetPausedRecovery(int slot)
        {
            switch (slot)
            {
                case 0: return _pausedRecovery0.Value;
                case 1: return _pausedRecovery1.Value;
                case 2: return _pausedRecovery2.Value;
                case 3: return _pausedRecovery3.Value;
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        private void SetPausedRecovery(int slot, double value)
        {
            switch (slot)
            {
                case 0: _pausedRecovery0.Value = value; break;
                case 1: _pausedRecovery1.Value = value; break;
                case 2: _pausedRecovery2.Value = value; break;
                case 3: _pausedRecovery3.Value = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        private static int GetPackedByte(uint packed, int slot)
        {
            return (int)((packed >> (slot * 8)) & 0xFFu);
        }

        private static uint PackBytes(int[] values)
        {
            uint packed = 0u;
            for (var slot = 0; slot < WrongWayRules.PlayerCount; slot++)
            {
                packed = SetPackedByte(packed, slot, values[slot]);
            }

            return packed;
        }

        private static uint SetPackedByte(
            uint packed,
            int slot,
            int value)
        {
            var shift = slot * 8;
            var mask = 0xFFu << shift;
            return (packed & ~mask) |
                   ((uint)Mathf.Clamp(value, 0, byte.MaxValue) << shift);
        }

        private static void ValidateSlot(int slot)
        {
            if (!WrongWayRules.IsValidPlayerSlot(slot))
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }
    }
}
