using MazeParty.Gameplay.Minigames;
using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.GiftGrab;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum NetworkGiftGrabPhase : byte
    {
        Inactive,
        Countdown,
        Running,
        RoundResult,
        Complete
    }

    public enum GiftGrabNetworkActionType : byte
    {
        None,
        Pickup,
        Deposit,
        Throw,
        ThrownHit,
        Push,
        Drop,
        GiftSpawned,
        BoundsReturn,
        Destroyed,
        PushCastStarted
    }

    public struct GiftGrabPlayerNetworkSnapshot :
        INetworkSerializable,
        IEquatable<GiftGrabPlayerNetworkSnapshot>
    {
        public Vector2 Position;
        public Vector2 Facing;
        public float StunRemainingSeconds;
        public float ThrowRecoveryRemainingSeconds;
        public float PushCastRemainingSeconds;
        public float ActionCooldownRemainingSeconds;
        public byte CarriedGiftId;
        public byte StoredGiftCount;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Facing);
            serializer.SerializeValue(ref StunRemainingSeconds);
            serializer.SerializeValue(ref ThrowRecoveryRemainingSeconds);
            serializer.SerializeValue(ref PushCastRemainingSeconds);
            serializer.SerializeValue(ref ActionCooldownRemainingSeconds);
            serializer.SerializeValue(ref CarriedGiftId);
            serializer.SerializeValue(ref StoredGiftCount);
        }

        public bool Equals(GiftGrabPlayerNetworkSnapshot other)
        {
            return Position == other.Position &&
                   Facing == other.Facing &&
                   StunRemainingSeconds.Equals(
                       other.StunRemainingSeconds) &&
                   ThrowRecoveryRemainingSeconds.Equals(
                       other.ThrowRecoveryRemainingSeconds) &&
                   PushCastRemainingSeconds.Equals(
                       other.PushCastRemainingSeconds) &&
                   ActionCooldownRemainingSeconds.Equals(
                       other.ActionCooldownRemainingSeconds) &&
                   CarriedGiftId == other.CarriedGiftId &&
                   StoredGiftCount == other.StoredGiftCount;
        }

        public override bool Equals(object obj)
        {
            return obj is GiftGrabPlayerNetworkSnapshot other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Position);
            hash.Add(Facing);
            hash.Add(StunRemainingSeconds);
            hash.Add(ThrowRecoveryRemainingSeconds);
            hash.Add(PushCastRemainingSeconds);
            hash.Add(ActionCooldownRemainingSeconds);
            hash.Add(CarriedGiftId);
            hash.Add(StoredGiftCount);
            return hash.ToHashCode();
        }
    }

    public struct GiftGrabGiftNetworkSnapshot :
        INetworkSerializable,
        IEquatable<GiftGrabGiftNetworkSnapshot>
    {
        public Vector2 Position;
        public byte State;
        public byte CarrierSlot;
        public byte StoredOwnerSlot;
        public float PickupLockRemainingSeconds;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref State);
            serializer.SerializeValue(ref CarrierSlot);
            serializer.SerializeValue(ref StoredOwnerSlot);
            serializer.SerializeValue(ref PickupLockRemainingSeconds);
        }

        public bool Equals(GiftGrabGiftNetworkSnapshot other)
        {
            return Position == other.Position &&
                   State == other.State &&
                   CarrierSlot == other.CarrierSlot &&
                   StoredOwnerSlot == other.StoredOwnerSlot &&
                   PickupLockRemainingSeconds.Equals(
                       other.PickupLockRemainingSeconds);
        }

        public override bool Equals(object obj)
        {
            return obj is GiftGrabGiftNetworkSnapshot other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Position);
            hash.Add(State);
            hash.Add(CarrierSlot);
            hash.Add(StoredOwnerSlot);
            hash.Add(PickupLockRemainingSeconds);
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// Server-authoritative two-round Gift Grab simulation. Clients submit only
    /// movement and an edge-triggered primary action tagged with the observed
    /// round and input epoch. The server owns positions, collisions, gift
    /// ownership, stuns, cooldowns, spawn timing and standings.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkGiftGrabState : NetworkBehaviour
    {
        public MinigameRulesConfig GameRules => MinigameRulesConfig.FromMatch(
            ScheduledMinigameId.GiftGrab, NetworkMatchState.Instance?.GameSettings);
        public const double CountdownSeconds = 3d;
        public const double RoundResultSeconds = 4d;
        public const float ArenaCenterX = 700f;
        public const float ArenaHalfExtent = 8f;
        public const float BaseCenterOffset = 6f;
        public const float BaseRadius = 1.75f;
        public const float PlayerCollisionRadius = 0.6f;
        public const float GiftCollisionRadius = 0.45f;
        public const double ThrownFlightSeconds = 0.9d;

        private const byte NoNetworkIndex = byte.MaxValue;
        private const float MaximumSimulationStepSeconds = 0.05f;
        private const double FinalInteractionEpsilonSeconds = 0.000001d;

        private static readonly Vector2[] CentralSpawnOffsets =
        {
            new Vector2(-3f, -2.25f),
            new Vector2(-1.5f, -2.25f),
            new Vector2(0f, -2.25f),
            new Vector2(1.5f, -2.25f),
            new Vector2(3f, -2.25f),
            new Vector2(-3f, -0.75f),
            new Vector2(-1.5f, -0.75f),
            new Vector2(0f, -0.75f),
            new Vector2(1.5f, -0.75f),
            new Vector2(3f, -0.75f),
            new Vector2(-3f, 0.75f),
            new Vector2(-1.5f, 0.75f),
            new Vector2(0f, 0.75f),
            new Vector2(1.5f, 0.75f),
            new Vector2(3f, 0.75f),
            new Vector2(-3f, 2.25f),
            new Vector2(-1.5f, 2.25f),
            new Vector2(0f, 2.25f),
            new Vector2(1.5f, 2.25f)
        };

        private readonly NetworkVariable<bool> _matchActive =
            CreateBoolVariable();
        private readonly NetworkVariable<bool> _paused =
            CreateBoolVariable();
        private readonly NetworkVariable<byte> _phase =
            CreateByteVariable((byte)NetworkGiftGrabPhase.Inactive);
        private readonly NetworkVariable<byte> _roundNumber =
            CreateByteVariable();
        private readonly NetworkVariable<double> _phaseEndsAt =
            CreateDoubleVariable();
        private readonly NetworkVariable<double> _pausedPhaseRemaining =
            CreateDoubleVariable();
        private readonly NetworkVariable<byte> _roundEndReason =
            CreateByteVariable();
        private readonly NetworkVariable<byte> _giftSpawnedCount =
            CreateByteVariable();
        private readonly NetworkVariable<uint> _scores =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _roundPoints =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _roundRanks =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _roundStoredGiftCounts =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _totalStoredGiftCounts =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _finalRanks =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _inputEpoch =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _giftRevision =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _actionRevision =
            CreateUIntVariable();
        private readonly NetworkVariable<byte> _lastActionType =
            CreateByteVariable();
        private readonly NetworkVariable<byte> _lastActionActorSlot =
            CreateByteVariable(NoNetworkIndex);
        private readonly NetworkVariable<byte> _lastActionTargetSlot =
            CreateByteVariable(NoNetworkIndex);
        private readonly NetworkVariable<byte> _lastActionGiftId =
            CreateByteVariable(NoNetworkIndex);

        private readonly NetworkList<GiftGrabPlayerNetworkSnapshot>
            _playerSnapshots =
                new NetworkList<GiftGrabPlayerNetworkSnapshot>(
                    default,
                    NetworkVariableReadPermission.Everyone,
                    NetworkVariableWritePermission.Server);
        private readonly NetworkList<GiftGrabGiftNetworkSnapshot>
            _giftSnapshots =
                new NetworkList<GiftGrabGiftNetworkSnapshot>(
                    default,
                    NetworkVariableReadPermission.Everyone,
                    NetworkVariableWritePermission.Server);

        private readonly Vector2[] _serverInputs =
            new Vector2[GiftGrabRules.PlayerCount];
        private readonly Vector2[] _playerPositions =
            new Vector2[GiftGrabRules.PlayerCount];
        private readonly Vector2[] _playerFacings =
            new Vector2[GiftGrabRules.PlayerCount];
        private readonly Vector2[] _pendingPushFacings =
            new Vector2[GiftGrabRules.PlayerCount];
        private readonly NetworkPlayerAvatar[] _avatars =
            new NetworkPlayerAvatar[GiftGrabRules.PlayerCount];
        private readonly Vector2[] _giftPositions =
            new Vector2[GiftGrabRules.TotalGiftCount];
        private readonly Vector2[] _thrownVelocities =
            new Vector2[GiftGrabRules.TotalGiftCount];
        private readonly double[] _thrownEndsAt =
            new double[GiftGrabRules.TotalGiftCount];
        private readonly bool[] _giftPositionInitialized =
            new bool[GiftGrabRules.TotalGiftCount];
        private readonly int[] _spawnPointOrder =
            new int[GiftGrabRules.TotalGiftCount];
        private readonly List<GiftGrabRoundResult> _roundResults =
            new List<GiftGrabRoundResult>();

        private GiftGrabRoundState _roundState;
        private ulong _matchSeed;
        private double _runningStartedAt;
        private double _simulatedElapsed;
        private double _pausedRunningElapsed;
        private bool _completionReported;

        public static NetworkGiftGrabState Instance { get; private set; }

        public event Action<GiftGrabNetworkActionType, int, int, Vector2>
            ActionPresentationRequested;

        public NetworkGiftGrabPhase Phase =>
            (NetworkGiftGrabPhase)_phase.Value;
        public int RoundNumber => _roundNumber.Value;
        public int PlayerCount => GiftGrabRules.PlayerCount;
        public int GiftCount => GiftGrabRules.TotalGiftCount;
        public int GiftCapacity => GiftGrabRules.TotalGiftCount;
        public int GiftSpawnedCount => _giftSpawnedCount.Value;
        public bool IsPaused => _paused.Value;
        public uint InputEpoch => _inputEpoch.Value;
        public uint GiftRevision => _giftRevision.Value;
        public uint ActionRevision => _actionRevision.Value;
        public GiftGrabRoundEndReason RoundEndReason =>
            (GiftGrabRoundEndReason)_roundEndReason.Value;
        public GiftGrabNetworkActionType LastActionType =>
            (GiftGrabNetworkActionType)_lastActionType.Value;
        public int LastActionActorSlot => DecodeIndex(
            _lastActionActorSlot.Value);
        public int LastActionTargetSlot => DecodeIndex(
            _lastActionTargetSlot.Value);
        public int LastActionGiftId => DecodeIndex(
            _lastActionGiftId.Value);
        public double RemainingSeconds => GetRemaining(
            _phaseEndsAt.Value,
            _pausedPhaseRemaining.Value,
            Phase == NetworkGiftGrabPhase.Inactive ||
            Phase == NetworkGiftGrabPhase.Complete);
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
                    "More than one NetworkGiftGrabState is spawned.");
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
            if (Phase == NetworkGiftGrabPhase.Running)
            {
                if (_roundState == null)
                {
                    return;
                }

                SimulateToOnServer(GetRunningElapsed(now));
                SyncSnapshotsOnServer();
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
                case NetworkGiftGrabPhase.Countdown:
                    BeginRunOnServer(now);
                    break;
                case NetworkGiftGrabPhase.RoundResult:
                    if (_roundNumber.Value < GameRules.Rounds)
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

            ClearLocalRuntime();
            ResetReplicatedStateOnServer();
            _matchSeed = matchSeed;
            _matchActive.Value = true;
            _paused.Value = false;
            _completionReported = false;
            CacheAndFreezeBoardAvatarsOnServer();
            BeginRoundOnServer(1, ServerNow);
        }

        public bool ReceiveInputOnServer(
            NetworkPlayerAvatar avatar,
            Vector2 input,
            int roundNumber,
            uint inputEpoch)
        {
            if (!TryResolveAuthoritativeSlot(avatar, out var slot) ||
                !ValidateInputEnvelope(roundNumber, inputEpoch))
            {
                return false;
            }

            SimulateToOnServer(GetRunningElapsed(ServerNow));
            if (_roundState == null || _roundState.IsComplete)
            {
                _serverInputs[slot] = Vector2.zero;
                return false;
            }

            avatar.StopServerInputOnServer();
            _avatars[slot] = avatar;
            _serverInputs[slot] =
                _roundState.GetPlayer(slot).CanMove
                    ? Vector2.ClampMagnitude(input, 1f)
                    : Vector2.zero;
            return true;
        }

        public bool TryPrimaryActionOnServer(
            NetworkPlayerAvatar avatar,
            int roundNumber,
            uint inputEpoch)
        {
            if (!TryResolveAuthoritativeSlot(avatar, out var slot) ||
                !ValidateInputEnvelope(roundNumber, inputEpoch))
            {
                return false;
            }

            var now = ServerNow;
            var elapsed = GetRunningElapsed(now);
            SimulateToOnServer(elapsed);
            if (_roundState == null || _roundState.IsComplete)
            {
                return false;
            }

            avatar.StopServerInputOnServer();
            _avatars[slot] = avatar;
            var player = _roundState.GetPlayer(slot);
            var changed = player.IsCarryingGift
                ? TryThrowOnServer(slot, elapsed)
                : TryStartPushOnServer(slot, elapsed);
            SyncSnapshotsOnServer();
            return changed;
        }

        public bool CanAcceptInputForSlot(int slot)
        {
            return GiftGrabRules.IsValidPlayerSlot(slot) &&
                   _matchActive.Value && !_paused.Value &&
                   Phase == NetworkGiftGrabPhase.Running &&
                   RemainingSeconds > 0d &&
                   !IsPlayerStunned(slot) &&
                   GetPlayerThrowRecoveryRemaining(slot) <= 0d &&
                   !IsPlayerPushCasting(slot);
        }

        public Vector2 GetPlayerPosition(int slot)
        {
            return TryGetPlayerSnapshot(slot, out var snapshot)
                ? snapshot.Position
                : GetPlayerStartPosition(slot);
        }

        public Vector2 GetPlayerFacing(int slot)
        {
            return TryGetPlayerSnapshot(slot, out var snapshot)
                ? snapshot.Facing
                : Vector2.up;
        }

        public bool IsPlayerStunned(int slot)
        {
            return GetPlayerStunRemaining(slot) > 0d;
        }

        public double GetPlayerStunRemaining(int slot)
        {
            return TryGetPlayerSnapshot(slot, out var snapshot)
                ? snapshot.StunRemainingSeconds
                : 0d;
        }

        public double GetPlayerStunRemainingSeconds(int slot)
        {
            return GetPlayerStunRemaining(slot);
        }

        public double GetPlayerThrowRecoveryRemaining(int slot)
        {
            return TryGetPlayerSnapshot(slot, out var snapshot)
                ? snapshot.ThrowRecoveryRemainingSeconds
                : 0d;
        }

        public bool IsPlayerPushCasting(int slot)
        {
            return GetPlayerPushCastRemainingSeconds(slot) > 0d;
        }

        public double GetPlayerPushCastRemainingSeconds(int slot)
        {
            return TryGetPlayerSnapshot(slot, out var snapshot)
                ? snapshot.PushCastRemainingSeconds
                : 0d;
        }

        public double GetActionCooldownRemainingSeconds(int slot)
        {
            return TryGetPlayerSnapshot(slot, out var snapshot)
                ? snapshot.ActionCooldownRemainingSeconds
                : 0d;
        }

        public int GetCarriedGiftId(int slot)
        {
            return TryGetPlayerSnapshot(slot, out var snapshot)
                ? DecodeIndex(snapshot.CarriedGiftId)
                : GiftGrabRules.NoGiftId;
        }

        public int GetDepositedGiftCount(int slot)
        {
            return TryGetPlayerSnapshot(slot, out var snapshot)
                ? snapshot.StoredGiftCount
                : 0;
        }

        public int GetStoredGiftCount(int slot)
        {
            return GetDepositedGiftCount(slot);
        }

        public int GetScore(int slot)
        {
            return ReadPackedByte(_scores.Value, slot);
        }

        public int GetRoundPoints(int slot)
        {
            return ReadPackedByte(_roundPoints.Value, slot);
        }

        public int GetRoundRank(int slot)
        {
            return ReadPackedByte(_roundRanks.Value, slot);
        }

        public int GetPlayerRank(int slot)
        {
            return GetRoundRank(slot);
        }

        public int GetRoundStoredGiftCount(int slot)
        {
            return ReadPackedByte(_roundStoredGiftCounts.Value, slot);
        }

        public int GetTotalStoredGiftCount(int slot)
        {
            return ReadPackedByte(_totalStoredGiftCounts.Value, slot);
        }

        public int GetTotalStoredGifts(int slot)
        {
            return GetTotalStoredGiftCount(slot);
        }

        public int GetFinalRank(int slot)
        {
            return ReadPackedByte(_finalRanks.Value, slot);
        }

        public bool IsGiftActive(int giftId)
        {
            if (!TryGetGiftSnapshot(giftId, out var snapshot))
            {
                return false;
            }

            var giftState = (GiftGrabGiftState)snapshot.State;
            return giftState != GiftGrabGiftState.Unspawned &&
                   giftState != GiftGrabGiftState.Consumed;
        }

        public Vector2 GetGiftPosition(int giftId)
        {
            return TryGetGiftSnapshot(giftId, out var snapshot)
                ? snapshot.Position
                : new Vector2(ArenaCenterX, 0f);
        }

        public GiftGrabGiftState GetGiftState(int giftId)
        {
            return TryGetGiftSnapshot(giftId, out var snapshot)
                ? (GiftGrabGiftState)snapshot.State
                : GiftGrabGiftState.Unspawned;
        }

        public int GetGiftCarrierSlot(int giftId)
        {
            return TryGetGiftSnapshot(giftId, out var snapshot)
                ? DecodeIndex(snapshot.CarrierSlot)
                : GiftGrabRules.NoPlayerSlot;
        }

        public int GetGiftOwnerSlot(int giftId)
        {
            return TryGetGiftSnapshot(giftId, out var snapshot)
                ? DecodeIndex(snapshot.StoredOwnerSlot)
                : GiftGrabRules.NoPlayerSlot;
        }

        public int GetGiftStoredOwnerSlot(int giftId)
        {
            return GetGiftOwnerSlot(giftId);
        }

        public float GetGiftPickupLockRemaining(int giftId)
        {
            return TryGetGiftSnapshot(giftId, out var snapshot)
                ? snapshot.PickupLockRemainingSeconds : 0f;
        }

        public void PauseOnServer(double now)
        {
            if (!IsServer || !_matchActive.Value || _paused.Value ||
                !IsFinite(now))
            {
                return;
            }

            if (Phase == NetworkGiftGrabPhase.Running &&
                _roundState != null && !_roundState.IsComplete)
            {
                _pausedRunningElapsed = GetRunningElapsed(now);
                SimulateToOnServer(_pausedRunningElapsed);
                _roundState.InterruptForPause(_pausedRunningElapsed);
                SyncSnapshotsOnServer();
                if (_roundState.IsComplete)
                {
                    CompleteCurrentRoundOnServer(now);
                    _pausedRunningElapsed = 0d;
                }
            }

            _pausedPhaseRemaining.Value = _phaseEndsAt.Value > 0d
                ? Math.Max(0d, _phaseEndsAt.Value - now)
                : 0d;
            ClearServerInputs();
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
            if (Phase == NetworkGiftGrabPhase.Running)
            {
                _runningStartedAt = now - _pausedRunningElapsed;
            }

            ClearServerInputs();
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

        public static Vector2 GetBaseCenter(int playerSlot)
        {
            if (!GiftGrabRules.IsValidPlayerSlot(playerSlot))
            {
                return new Vector2(ArenaCenterX, 0f);
            }

            var right = (playerSlot & 1) != 0;
            var top = (playerSlot & 2) != 0;
            return new Vector2(
                ArenaCenterX + (right ? BaseCenterOffset : -BaseCenterOffset),
                top ? BaseCenterOffset : -BaseCenterOffset);
        }

        public static Vector2 GetPlayerStartPosition(int playerSlot)
        {
            var baseCenter = GetBaseCenter(playerSlot);
            var center = new Vector2(ArenaCenterX, 0f);
            return Vector2.MoveTowards(baseCenter, center, 0.65f);
        }

        private void BeginRoundOnServer(int roundNumber, double now)
        {
            AdvanceInputEpochOnServer();
            ClearServerInputs();
            _roundState = new GiftGrabRoundState(roundNumber, rules: GameRules);
            _roundNumber.Value = (byte)roundNumber;
            _phase.Value = (byte)NetworkGiftGrabPhase.Countdown;
            _phaseEndsAt.Value = now + CountdownSeconds;
            _pausedPhaseRemaining.Value = 0d;
            _roundEndReason.Value =
                (byte)GiftGrabRoundEndReason.None;
            _roundPoints.Value = 0U;
            _roundRanks.Value = 0U;
            _roundStoredGiftCounts.Value = 0U;
            _runningStartedAt = 0d;
            _simulatedElapsed = 0d;
            _pausedRunningElapsed = 0d;
            InitializeRoundRuntime(roundNumber);
            SyncSnapshotsOnServer();
            FreezeBoardAvatarsOnServer();
        }

        private void BeginRunOnServer(double now)
        {
            AdvanceInputEpochOnServer();
            ClearServerInputs();
            _phase.Value = (byte)NetworkGiftGrabPhase.Running;
            _phaseEndsAt.Value = now + GameRules.Seconds;
            _runningStartedAt = now;
            _simulatedElapsed = 0d;
            SyncSnapshotsOnServer();
        }

        private void CompleteCurrentRoundOnServer(double now)
        {
            if (_roundState == null || !_roundState.IsComplete ||
                _roundResults.Count >= _roundNumber.Value)
            {
                return;
            }

            SyncSnapshotsOnServer();
            var result = _roundState.Result;
            _roundResults.Add(result);
            for (var slot = 0; slot < 4; slot++)
                NetworkMatchState.Instance?.RecordStolenGiftsOnServer(slot, _roundState.GetPlayer(slot).StolenDeliveries);
            uint roundPoints = 0U;
            uint roundRanks = 0U;
            uint roundStored = 0U;
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
                roundStored = WritePackedByte(
                    roundStored,
                    standing.PlayerSlot,
                    standing.StoredGiftCount);
                _scores.Value = WritePackedByte(
                    _scores.Value,
                    standing.PlayerSlot,
                    GetScore(standing.PlayerSlot) + standing.Points);
                _totalStoredGiftCounts.Value = WritePackedByte(
                    _totalStoredGiftCounts.Value,
                    standing.PlayerSlot,
                    GetTotalStoredGiftCount(standing.PlayerSlot) +
                    standing.StoredGiftCount);
            }

            _roundPoints.Value = roundPoints;
            _roundRanks.Value = roundRanks;
            _roundStoredGiftCounts.Value = roundStored;
            _roundEndReason.Value = (byte)_roundState.EndReason;
            ClearServerInputs();
            AdvanceInputEpochOnServer();
            _phase.Value = (byte)NetworkGiftGrabPhase.RoundResult;
            _phaseEndsAt.Value = now + RoundResultSeconds;
        }

        private void CompleteMatchOnServer()
        {
            if (_completionReported ||
                _roundResults.Count != GameRules.Rounds)
            {
                return;
            }

            var leaderboard =
                GiftGrabMatchScoring.BuildLeaderboard(_roundResults, rules: GameRules);
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
                !match.TryCompleteGiftGrabOnServer(leaderboard))
            {
                return;
            }

            _completionReported = true;
            _matchActive.Value = false;
            _phase.Value = (byte)NetworkGiftGrabPhase.Complete;
            _phaseEndsAt.Value = 0d;
            ClearServerInputs();
            FreezeBoardAvatarsOnServer();
        }

        private bool ValidateInputEnvelope(
            int roundNumber,
            uint inputEpoch)
        {
            return _matchActive.Value && !_paused.Value &&
                   Phase == NetworkGiftGrabPhase.Running &&
                   roundNumber == _roundNumber.Value &&
                   MinigameServerRules.IsCurrentInputEpoch(inputEpoch, _inputEpoch.Value) &&
                   RemainingSeconds > 0d && _roundState != null &&
                   !_roundState.IsComplete;
        }

        private void SimulateToOnServer(double targetElapsed)
        {
            if (_roundState == null || _roundState.IsComplete)
            {
                return;
            }

            targetElapsed = Math.Max(
                _simulatedElapsed,
                Math.Min(GameRules.Seconds, targetElapsed));
            var interactionLimit = Math.Min(
                targetElapsed,
                GameRules.Seconds -
                FinalInteractionEpsilonSeconds);
            while (_simulatedElapsed + 0.000000001d < interactionLimit)
            {
                var stepLimit = Math.Min(
                    interactionLimit,
                    _simulatedElapsed + MaximumSimulationStepSeconds);
                var next = GetNextActionLockBoundary(
                    _simulatedElapsed,
                    stepLimit);
                var delta = (float)(next - _simulatedElapsed);
                // Movement is integrated from the state at the start of the
                // segment. Advancing first would unlock a stun on its exact
                // end boundary and incorrectly grant this whole segment's
                // movement.
                SimulatePlayerMovementOnServer(delta);
                _roundState.AdvanceTo(next);
                InitializeNewGiftPositionsOnServer();
                SimulateThrownGiftsOnServer(
                    delta,
                    _simulatedElapsed,
                    next);
                ResolveDepositsAndPickupsOnServer(next);
                ResolveCompletedPushCastsOnServer(next);
                UpdateCarriedAndStoredGiftPositions();
                _simulatedElapsed = next;
            }

            if (targetElapsed >= GameRules.Seconds)
            {
                _roundState.AdvanceTo(GameRules.Seconds);
                _simulatedElapsed = GameRules.Seconds;
            }
            else if (_simulatedElapsed + 0.000000001d < targetElapsed)
            {
                _roundState.AdvanceTo(targetElapsed);
                InitializeNewGiftPositionsOnServer();
                _simulatedElapsed = targetElapsed;
            }
        }

        private void SimulatePlayerMovementOnServer(float deltaSeconds)
        {
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                var player = _roundState.GetPlayer(slot);
                if (!player.CanMove)
                {
                    continue;
                }

                var input = Vector2.ClampMagnitude(
                    _serverInputs[slot],
                    1f);
                if (input.sqrMagnitude <= 0.0001f)
                {
                    continue;
                }

                var direction = input.normalized;
                _playerFacings[slot] = direction;
                var displacement =
                    input * player.CurrentMoveSpeed * deltaSeconds;
                _playerPositions[slot] = ResolvePlayerMovement(
                    slot,
                    _playerPositions[slot],
                    displacement);
            }
        }

        private Vector2 ResolvePlayerMovement(
            int slot,
            Vector2 origin,
            Vector2 displacement)
        {
            var proposed = ClampPlayerPosition(origin + displacement);
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
            return CanPlayerOccupy(slot, yOnly) ? yOnly : origin;
        }

        private bool CanPlayerOccupy(int slot, Vector2 position)
        {
            var minimumDistance = PlayerCollisionRadius * 2f;
            var minimumDistanceSquared = minimumDistance * minimumDistance;
            for (var other = 0;
                 other < GiftGrabRules.PlayerCount;
                 other++)
            {
                if (other == slot)
                {
                    continue;
                }

                if ((_playerPositions[other] - position).sqrMagnitude <
                    minimumDistanceSquared)
                {
                    return false;
                }
            }

            return true;
        }

        private void ResolveDepositsAndPickupsOnServer(double elapsed)
        {
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                var player = _roundState.GetPlayer(slot);
                if (!player.CanAct)
                {
                    continue;
                }

                if (player.IsCarryingGift &&
                    Vector2.Distance(
                        _playerPositions[slot],
                        GetBaseCenter(slot)) <= BaseRadius)
                {
                    var resolution = _roundState.ResolveDeposit(
                        slot,
                        elapsed);
                    if (resolution.WasDeposited)
                    {
                        _giftPositions[resolution.GiftId] =
                            GetStoredGiftPosition(slot, resolution.GiftId);
                        RecordAction(
                            GiftGrabNetworkActionType.Deposit,
                            slot,
                            GiftGrabRules.NoPlayerSlot,
                            resolution.GiftId);
                    }
                }

                if (_roundState.GetPlayer(slot).IsCarryingGift)
                {
                    continue;
                }

                var pickupRadius =
                    PlayerCollisionRadius + GiftCollisionRadius;
                var pickupRadiusSquared = pickupRadius * pickupRadius;
                for (var giftId = 0;
                     giftId < GiftGrabRules.TotalGiftCount;
                     giftId++)
                {
                    var gift = _roundState.GetGift(giftId);
                    if (gift.State != GiftGrabGiftState.Loose &&
                        gift.State != GiftGrabGiftState.Stored)
                    {
                        continue;
                    }

                    var canReach = gift.State == GiftGrabGiftState.Stored
                        ? gift.StoredOwnerSlot != slot &&
                          Vector2.Distance(_playerPositions[slot],
                              GetBaseCenter(gift.StoredOwnerSlot)) <=
                          BaseRadius
                        : (_giftPositions[giftId] -
                           _playerPositions[slot]).sqrMagnitude <=
                          pickupRadiusSquared;
                    if (!canReach)
                    {
                        continue;
                    }

                    var pickup = _roundState.ResolvePickup(
                        slot,
                        giftId,
                        elapsed);
                    if (!pickup.WasPickedUp)
                    {
                        continue;
                    }

                    _giftPositions[giftId] =
                        GetCarriedGiftPosition(slot);
                    RecordAction(
                        GiftGrabNetworkActionType.Pickup,
                        slot,
                        pickup.PreviousStoredOwnerSlot,
                        giftId);
                    break;
                }
            }
        }

        private bool TryThrowOnServer(int slot, double elapsed)
        {
            var resolution = _roundState.ResolveThrow(slot, elapsed);
            if (!resolution.WasThrown)
            {
                return false;
            }

            var direction = _playerFacings[slot].sqrMagnitude > 0.0001f
                ? _playerFacings[slot].normalized
                : Vector2.up;
            _giftPositions[resolution.GiftId] =
                _playerPositions[slot] + direction *
                (PlayerCollisionRadius + GiftCollisionRadius + 0.1f);
            _thrownVelocities[resolution.GiftId] =
                direction * GiftGrabRules.ThrowSpeed;
            _thrownEndsAt[resolution.GiftId] =
                elapsed + ThrownFlightSeconds;
            RecordAction(
                GiftGrabNetworkActionType.Throw,
                slot,
                GiftGrabRules.NoPlayerSlot,
                resolution.GiftId);
            return true;
        }

        private bool TryStartPushOnServer(int slot, double elapsed)
        {
            var resolution = _roundState.BeginPushCast(slot, elapsed);
            if (!resolution.WasStarted)
            {
                return false;
            }

            var facing = _playerFacings[slot].sqrMagnitude > 0.0001f
                ? _playerFacings[slot].normalized
                : Vector2.up;
            _pendingPushFacings[slot] = facing;
            RecordAction(
                GiftGrabNetworkActionType.PushCastStarted,
                slot,
                GiftGrabRules.NoPlayerSlot,
                GiftGrabRules.NoGiftId);
            return true;
        }

        private double GetNextActionLockBoundary(
            double currentElapsed,
            double stepLimit)
        {
            var next = stepLimit;
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                var player = _roundState.GetPlayer(slot);
                if (player.IsPushCasting &&
                    player.PushCastEndsAtSeconds >
                    currentElapsed + 0.000000001d &&
                    player.PushCastEndsAtSeconds < next)
                {
                    next = player.PushCastEndsAtSeconds;
                }

                if (player.StunnedUntilSeconds >
                    currentElapsed + 0.000000001d &&
                    player.StunnedUntilSeconds < next)
                {
                    next = player.StunnedUntilSeconds;
                }

                if (player.ThrowRecoveryEndsAtSeconds >
                    currentElapsed + 0.000000001d &&
                    player.ThrowRecoveryEndsAtSeconds < next)
                {
                    next = player.ThrowRecoveryEndsAtSeconds;
                }
            }

            return next;
        }

        private void ResolveCompletedPushCastsOnServer(double elapsed)
        {
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                var player = _roundState.GetPlayer(slot);
                if (!player.IsPushCasting ||
                    elapsed + 0.000000001d <
                    player.PushCastEndsAtSeconds)
                {
                    continue;
                }

                var frozenFacing = _pendingPushFacings[slot].sqrMagnitude >
                                   0.0001f
                    ? _pendingPushFacings[slot].normalized
                    : Vector2.up;
                var target = FindPushTarget(slot, frozenFacing);
                var resolution = _roundState.ResolvePush(
                    slot,
                    target,
                    elapsed);
                _pendingPushFacings[slot] = Vector2.zero;
                if (!resolution.WasHit)
                {
                    continue;
                }

                var direction = _playerPositions[target] -
                                _playerPositions[slot];
                if (direction.sqrMagnitude > 0.0001f)
                {
                    direction.Normalize();
                }
                else
                {
                    direction = frozenFacing;
                }
                var targetPosition = ResolvePlayerMovement(
                    target,
                    _playerPositions[target],
                    direction * GiftGrabRules.PushKnockbackDistance);
                _playerPositions[target] = targetPosition;
                if (GiftGrabRules.IsValidGiftId(
                        resolution.DroppedGiftId))
                {
                    _giftPositions[resolution.DroppedGiftId] =
                        targetPosition;
                }

                RecordAction(
                    GiftGrabNetworkActionType.Push,
                    slot,
                    target,
                    resolution.DroppedGiftId);
            }
        }

        private int FindPushTarget(
            int attackerSlot,
            Vector2 frozenFacing)
        {
            var origin = _playerPositions[attackerSlot];
            var direction = frozenFacing.sqrMagnitude > 0.0001f
                ? frozenFacing.normalized
                : Vector2.up;
            var bestSlot = GiftGrabRules.NoPlayerSlot;
            var bestForward = float.MaxValue;
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                if (slot == attackerSlot)
                {
                    continue;
                }

                var offset = _playerPositions[slot] - origin;
                var forward = Vector2.Dot(offset, direction);
                if (forward < 0f || forward > GiftGrabRules.PushRange)
                {
                    continue;
                }

                var lateral = Mathf.Abs(
                    direction.x * offset.y - direction.y * offset.x);
                if (lateral > GiftGrabRules.PushRadius +
                    PlayerCollisionRadius)
                {
                    continue;
                }

                if (forward < bestForward ||
                    Mathf.Approximately(forward, bestForward) &&
                    slot < bestSlot)
                {
                    bestForward = forward;
                    bestSlot = slot;
                }
            }

            return bestSlot;
        }

        private void SimulateThrownGiftsOnServer(
            float deltaSeconds,
            double segmentStartElapsed,
            double elapsed)
        {
            for (var giftId = 0;
                 giftId < GiftGrabRules.TotalGiftCount;
                 giftId++)
            {
                var gift = _roundState.GetGift(giftId);
                if (gift.State != GiftGrabGiftState.Thrown)
                {
                    continue;
                }

                var start = _giftPositions[giftId];
                var travelDelta = Mathf.Min(
                    deltaSeconds,
                    (float)Math.Max(
                        0d,
                        _thrownEndsAt[giftId] - segmentStartElapsed));
                var end = start +
                          _thrownVelocities[giftId] * travelDelta;
                var victim = FindFirstThrownVictim(
                    gift,
                    start,
                    end,
                    segmentStartElapsed,
                    travelDelta,
                    out var collisionFraction);
                if (victim != GiftGrabRules.NoPlayerSlot)
                {
                    var collision = Vector2.Lerp(
                        start,
                        end,
                        collisionFraction);
                    var throwerSlot = gift.LastThrowerSlot;
                    var impactElapsed = segmentStartElapsed +
                        travelDelta * collisionFraction;
                    var hit = _roundState.ResolveThrownHit(
                        giftId,
                        victim,
                        impactElapsed,
                        elapsed);
                    if (hit.WasHit)
                    {
                        var incomingVelocity =
                            _thrownVelocities[giftId];
                        _giftPositions[giftId] = collision;
                        _thrownVelocities[giftId] = Vector2.zero;
                        _thrownEndsAt[giftId] = 0d;
                        if (GiftGrabRules.IsValidGiftId(hit.DroppedGiftId))
                        {
                            var perpendicular = new Vector2(
                                -incomingVelocity.y,
                                incomingVelocity.x);
                            if (perpendicular.sqrMagnitude <= 0.0001f)
                            {
                                perpendicular = Vector2.right;
                            }
                            _giftPositions[hit.DroppedGiftId] =
                                ClampGiftPosition(
                                    collision +
                                    perpendicular.normalized * 0.55f);
                        }
                        RecordAction(
                            GiftGrabNetworkActionType.ThrownHit,
                            throwerSlot,
                            victim,
                            giftId);
                        continue;
                    }
                }

                if (TryGetGiftArenaBoundaryImpact(
                        start,
                        end,
                        out var boundaryImpact))
                {
                    var throwerSlot = gift.LastThrowerSlot;
                    _giftPositions[giftId] = boundaryImpact;
                    var landed = _roundState.ResolveGiftLanded(
                        giftId, elapsed);
                    if (landed.WasLanded)
                    {
                        _thrownVelocities[giftId] = Vector2.zero;
                        _thrownEndsAt[giftId] = 0d;
                        RecordAction(
                            GiftGrabNetworkActionType.Drop,
                            throwerSlot,
                            GiftGrabRules.NoPlayerSlot,
                            giftId);
                    }
                    continue;
                }

                _giftPositions[giftId] = end;

                if (elapsed + 0.000000001d >= _thrownEndsAt[giftId])
                {
                    var landing = _roundState.ResolveGiftLanded(
                        giftId,
                        elapsed);
                    if (landing.WasLanded)
                    {
                        _giftPositions[giftId] =
                            ClampGiftPosition(_giftPositions[giftId]);
                        _thrownVelocities[giftId] = Vector2.zero;
                        _thrownEndsAt[giftId] = 0d;
                        RecordAction(
                            GiftGrabNetworkActionType.Drop,
                            landing.PickupLockedPlayerSlot,
                            GiftGrabRules.NoPlayerSlot,
                            giftId);
                    }
                }
            }
        }

        private int FindFirstThrownVictim(
            GiftGrabGiftRoundState gift,
            Vector2 start,
            Vector2 end,
            double segmentStartElapsed,
            float travelDelta,
            out float collisionFraction)
        {
            var result = GiftGrabRules.NoPlayerSlot;
            collisionFraction = float.MaxValue;
            var radius = PlayerCollisionRadius + GiftCollisionRadius;
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                if (!TrySegmentCircleIntersection(
                        start,
                        end,
                        _playerPositions[slot],
                        radius,
                        out var fraction))
                {
                    continue;
                }

                var impactElapsed = segmentStartElapsed +
                    travelDelta * fraction;
                if (gift.LastThrowerSlot == slot &&
                    impactElapsed + 0.000000001d <
                    gift.ThrowerImmunityEndsAtSeconds)
                {
                    continue;
                }

                if (fraction < collisionFraction ||
                    Mathf.Approximately(fraction, collisionFraction) &&
                    slot < result)
                {
                    collisionFraction = fraction;
                    result = slot;
                }
            }

            return result;
        }

        private static bool TrySegmentCircleIntersection(
            Vector2 start,
            Vector2 end,
            Vector2 center,
            float radius,
            out float fraction)
        {
            var segment = end - start;
            var fromCenter = start - center;
            var a = Vector2.Dot(segment, segment);
            var c = Vector2.Dot(fromCenter, fromCenter) - radius * radius;
            if (c <= 0f)
            {
                fraction = 0f;
                return true;
            }
            if (a <= 0.0000001f)
            {
                fraction = 0f;
                return false;
            }

            var b = 2f * Vector2.Dot(fromCenter, segment);
            var discriminant = b * b - 4f * a * c;
            if (discriminant < 0f)
            {
                fraction = 0f;
                return false;
            }

            var root = (-b - Mathf.Sqrt(discriminant)) / (2f * a);
            fraction = root;
            return root >= 0f && root <= 1f;
        }

        private void ReturnGiftToCenterOnServer(
            int giftId,
            double elapsed)
        {
            var resolution = _roundState.ResolveOutOfBounds(
                giftId,
                elapsed);
            if (!resolution.WasReturned)
            {
                return;
            }

            _giftPositions[giftId] = GetCentralGiftPosition(giftId);
            _thrownVelocities[giftId] = Vector2.zero;
            _thrownEndsAt[giftId] = 0d;
            RecordAction(
                GiftGrabNetworkActionType.BoundsReturn,
                resolution.PreviousCarrierSlot,
                resolution.PreviousStoredOwnerSlot,
                giftId);
        }

        private void InitializeRoundRuntime(int roundNumber)
        {
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                _playerPositions[slot] = GetPlayerStartPosition(slot);
                _pendingPushFacings[slot] = Vector2.zero;
                var towardCenter =
                    new Vector2(ArenaCenterX, 0f) -
                    _playerPositions[slot];
                _playerFacings[slot] = towardCenter.sqrMagnitude > 0.0001f
                    ? towardCenter.normalized
                    : Vector2.up;
            }

            for (var giftId = 0;
                 giftId < GiftGrabRules.TotalGiftCount;
                 giftId++)
            {
                _giftPositions[giftId] =
                    new Vector2(ArenaCenterX, 0f);
                _thrownVelocities[giftId] = Vector2.zero;
                _thrownEndsAt[giftId] = 0d;
                _giftPositionInitialized[giftId] = false;
                _spawnPointOrder[giftId] = giftId;
            }

            var random = new StableRandom(
                _matchSeed ^ ((ulong)(uint)roundNumber *
                              0x9E3779B97F4A7C15UL));
            for (var index = _spawnPointOrder.Length - 1;
                 index > 0;
                 index--)
            {
                var swapIndex = random.Next(index + 1);
                var swap = _spawnPointOrder[index];
                _spawnPointOrder[index] = _spawnPointOrder[swapIndex];
                _spawnPointOrder[swapIndex] = swap;
            }

            InitializeNewGiftPositionsOnServer();
        }

        private void InitializeNewGiftPositionsOnServer()
        {
            if (_roundState == null)
            {
                return;
            }

            _giftSpawnedCount.Value =
                (byte)_roundState.SpawnedGiftCount;
            for (var giftId = 0;
                 giftId < _roundState.SpawnedGiftCount;
                 giftId++)
            {
                if (_giftPositionInitialized[giftId])
                {
                    continue;
                }

                _giftPositionInitialized[giftId] = true;
                _giftPositions[giftId] = GetCentralGiftPosition(giftId);
                if (giftId >= GiftGrabRules.InitialGiftCount)
                {
                    RecordAction(
                        GiftGrabNetworkActionType.GiftSpawned,
                        GiftGrabRules.NoPlayerSlot,
                        GiftGrabRules.NoPlayerSlot,
                        giftId);
                }
            }
        }

        private Vector2 GetCentralGiftPosition(int giftId)
        {
            var orderedIndex = GiftGrabRules.IsValidGiftId(giftId)
                ? _spawnPointOrder[giftId]
                : 0;
            return new Vector2(ArenaCenterX, 0f) +
                   CentralSpawnOffsets[orderedIndex];
        }

        public static Vector2 GetDepositedGiftDisplayCenter(int ownerSlot)
        {
            var baseCenter = GetBaseCenter(ownerSlot);
            var towardArenaCenter =
                new Vector2(ArenaCenterX, 0f) - baseCenter;
            return baseCenter + towardArenaCenter.normalized * 0.5f;
        }

        private Vector2 GetStoredGiftPosition(
            int ownerSlot,
            int giftId)
        {
            var stackIndex = 0;
            for (var candidateGiftId = 0;
                 candidateGiftId < giftId;
                 candidateGiftId++)
            {
                var candidate = _roundState.GetGift(candidateGiftId);
                if (candidate.State == GiftGrabGiftState.Stored &&
                    candidate.StoredOwnerSlot == ownerSlot)
                {
                    stackIndex++;
                }
            }

            var outer = stackIndex < 8;
            var ringIndex = outer ? stackIndex : stackIndex - 8;
            var ringCount = outer ? 8 : 6;
            var angle = Mathf.PI * 2f * ringIndex / ringCount;
            var radius = outer ? 1.12f : 0.61f;
            var offset = new Vector2(
                Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            return GetDepositedGiftDisplayCenter(ownerSlot) + offset;
        }

        private Vector2 GetCarriedGiftPosition(int carrierSlot)
        {
            var facing = _playerFacings[carrierSlot].sqrMagnitude > 0.0001f
                ? _playerFacings[carrierSlot].normalized
                : Vector2.up;
            return _playerPositions[carrierSlot] + facing * 0.7f;
        }

        private void UpdateCarriedAndStoredGiftPositions()
        {
            for (var giftId = 0;
                 giftId < GiftGrabRules.TotalGiftCount;
                 giftId++)
            {
                var gift = _roundState.GetGift(giftId);
                if (gift.State == GiftGrabGiftState.Carried &&
                    GiftGrabRules.IsValidPlayerSlot(gift.CarrierSlot))
                {
                    _giftPositions[giftId] =
                        GetCarriedGiftPosition(gift.CarrierSlot);
                }
                else if (gift.State == GiftGrabGiftState.Stored &&
                         GiftGrabRules.IsValidPlayerSlot(
                             gift.StoredOwnerSlot))
                {
                    _giftPositions[giftId] = GetStoredGiftPosition(
                        gift.StoredOwnerSlot,
                        giftId);
                }
            }
        }

        private void SyncSnapshotsOnServer()
        {
            EnsureSnapshotCountsOnServer();
            var elapsed = _roundState != null
                ? _roundState.ElapsedSeconds
                : 0d;
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                var player = _roundState != null
                    ? _roundState.GetPlayer(slot)
                    : null;
                var snapshot = new GiftGrabPlayerNetworkSnapshot
                {
                    Position = _playerPositions[slot],
                    Facing = _playerFacings[slot],
                    StunRemainingSeconds = player != null
                        ? (float)Math.Max(
                            0d,
                            player.StunnedUntilSeconds - elapsed)
                        : 0f,
                    ThrowRecoveryRemainingSeconds = player != null
                        ? (float)Math.Max(
                            0d,
                            player.ThrowRecoveryEndsAtSeconds - elapsed)
                        : 0f,
                    PushCastRemainingSeconds = player != null &&
                                               player.IsPushCasting
                        ? (float)Math.Max(
                            0d,
                            player.PushCastEndsAtSeconds - elapsed)
                        : 0f,
                    ActionCooldownRemainingSeconds = player != null
                        ? (float)Math.Max(
                            0d,
                            player.PushCooldownEndsAtSeconds - elapsed)
                        : 0f,
                    CarriedGiftId = EncodeIndex(
                        player != null
                            ? player.HeldGiftId
                            : GiftGrabRules.NoGiftId),
                    StoredGiftCount = (byte)(player != null
                        ? player.StoredGiftCount
                        : 0)
                };
                if (!_playerSnapshots[slot].Equals(snapshot))
                {
                    _playerSnapshots[slot] = snapshot;
                }
            }

            var giftChanged = false;
            for (var giftId = 0;
                 giftId < GiftGrabRules.TotalGiftCount;
                 giftId++)
            {
                var gift = _roundState != null
                    ? _roundState.GetGift(giftId)
                    : null;
                var snapshot = new GiftGrabGiftNetworkSnapshot
                {
                    Position = _giftPositions[giftId],
                    State = (byte)(gift != null
                        ? gift.State
                        : GiftGrabGiftState.Unspawned),
                    CarrierSlot = EncodeIndex(
                        gift != null
                            ? gift.CarrierSlot
                            : GiftGrabRules.NoPlayerSlot),
                    StoredOwnerSlot = EncodeIndex(
                        gift != null
                            ? gift.StoredOwnerSlot
                            : GiftGrabRules.NoPlayerSlot),
                    PickupLockRemainingSeconds = gift != null
                        ? (float)Math.Max(0d,
                            gift.PickupLockEndsAtSeconds -
                            _simulatedElapsed)
                        : 0f
                };
                if (_giftSnapshots[giftId].Equals(snapshot))
                {
                    continue;
                }

                _giftSnapshots[giftId] = snapshot;
                giftChanged = true;
            }

            if (giftChanged)
            {
                AdvanceRevision(_giftRevision);
            }
        }

        private void RecordAction(
            GiftGrabNetworkActionType actionType,
            int actorSlot,
            int targetSlot,
            int giftId)
        {
            _lastActionType.Value = (byte)actionType;
            _lastActionActorSlot.Value = EncodeIndex(actorSlot);
            _lastActionTargetSlot.Value = EncodeIndex(targetSlot);
            _lastActionGiftId.Value = EncodeIndex(giftId);
            AdvanceRevision(_actionRevision);
            PlayActionPresentationRpc(
                (byte)actionType,
                EncodeIndex(actorSlot),
                EncodeIndex(targetSlot),
                ResolveActionPresentationPosition(
                    actionType,
                    actorSlot,
                    targetSlot,
                    giftId));
        }

        private Vector2 ResolveActionPresentationPosition(
            GiftGrabNetworkActionType actionType,
            int actorSlot,
            int targetSlot,
            int giftId)
        {
            var actorPosition = GiftGrabRules.IsValidPlayerSlot(actorSlot)
                ? _playerPositions[actorSlot]
                : new Vector2(ArenaCenterX, 0f);
            var targetPosition = GiftGrabRules.IsValidPlayerSlot(targetSlot)
                ? _playerPositions[targetSlot]
                : actorPosition;

            switch (actionType)
            {
                case GiftGrabNetworkActionType.Push:
                case GiftGrabNetworkActionType.ThrownHit:
                    return targetPosition;
                case GiftGrabNetworkActionType.PushCastStarted:
                case GiftGrabNetworkActionType.Throw:
                    return actorPosition;
                case GiftGrabNetworkActionType.Drop:
                case GiftGrabNetworkActionType.BoundsReturn:
                case GiftGrabNetworkActionType.Destroyed:
                    return GiftGrabRules.IsValidGiftId(giftId)
                        ? _giftPositions[giftId]
                        : targetPosition;
                default:
                    return GiftGrabRules.IsValidGiftId(giftId)
                        ? _giftPositions[giftId]
                        : actorPosition;
            }
        }

        [Rpc(
            SendTo.ClientsAndHost,
            Delivery = RpcDelivery.Reliable)]
        private void PlayActionPresentationRpc(
            byte actionTypeValue,
            byte actorSlotValue,
            byte targetSlotValue,
            Vector2 position)
        {
            var actionType = (GiftGrabNetworkActionType)actionTypeValue;
            if (actionType > GiftGrabNetworkActionType.None &&
                actionType <= GiftGrabNetworkActionType.PushCastStarted)
            {
                ActionPresentationRequested?.Invoke(
                    actionType,
                    DecodeIndex(actorSlotValue),
                    DecodeIndex(targetSlotValue),
                    position);
            }
        }

        private void EnsureSnapshotCountsOnServer()
        {
            while (_playerSnapshots.Count < GiftGrabRules.PlayerCount)
            {
                _playerSnapshots.Add(default);
            }
            while (_playerSnapshots.Count > GiftGrabRules.PlayerCount)
            {
                _playerSnapshots.RemoveAt(_playerSnapshots.Count - 1);
            }
            while (_giftSnapshots.Count < GiftGrabRules.TotalGiftCount)
            {
                _giftSnapshots.Add(default);
            }
            while (_giftSnapshots.Count > GiftGrabRules.TotalGiftCount)
            {
                _giftSnapshots.RemoveAt(_giftSnapshots.Count - 1);
            }
        }

        private bool TryGetPlayerSnapshot(
            int slot,
            out GiftGrabPlayerNetworkSnapshot snapshot)
        {
            if (GiftGrabRules.IsValidPlayerSlot(slot) &&
                slot < _playerSnapshots.Count)
            {
                snapshot = _playerSnapshots[slot];
                return true;
            }

            snapshot = default;
            return false;
        }

        private bool TryGetGiftSnapshot(
            int giftId,
            out GiftGrabGiftNetworkSnapshot snapshot)
        {
            if (GiftGrabRules.IsValidGiftId(giftId) &&
                giftId < _giftSnapshots.Count)
            {
                snapshot = _giftSnapshots[giftId];
                return true;
            }

            snapshot = default;
            return false;
        }

        private bool TryResolveAuthoritativeSlot(
            NetworkPlayerAvatar avatar,
            out int slot)
        {
            var match = NetworkMatchState.Instance;
            return MinigameServerRules.TryResolveSpawnedServerSlot(this, avatar, out slot) &&
                match != null && ReferenceEquals(match.GetAvatarForSlot(slot), avatar);
        }

        private void CacheAndFreezeBoardAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                _avatars[slot] =
                    match != null ? match.GetAvatarForSlot(slot) : null;
                _avatars[slot]?.StopServerInputOnServer();
            }
        }

        private void FreezeBoardAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
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

        private void ClearServerInputs()
        {
            for (var slot = 0; slot < _serverInputs.Length; slot++)
            {
                _serverInputs[slot] = Vector2.zero;
            }
        }

        private double GetRunningElapsed(double now)
        {
            return Math.Max(
                0d,
                Math.Min(
                    GameRules.Seconds,
                    now - _runningStartedAt));
        }

        private double GetRemaining(
            double deadline,
            double pausedRemaining,
            bool inactive)
        {
            return MinigameServerRules.Remaining(deadline, pausedRemaining,
                inactive, _paused.Value, ServerNow);
        }

        private static Vector2 ClampPlayerPosition(Vector2 position)
        {
            var inset = PlayerCollisionRadius;
            return new Vector2(
                Mathf.Clamp(
                    position.x,
                    ArenaCenterX - ArenaHalfExtent + inset,
                    ArenaCenterX + ArenaHalfExtent - inset),
                Mathf.Clamp(
                    position.y,
                    -ArenaHalfExtent + inset,
                    ArenaHalfExtent - inset));
        }

        private static Vector2 ClampGiftPosition(Vector2 position)
        {
            var inset = GiftCollisionRadius;
            return new Vector2(
                Mathf.Clamp(
                    position.x,
                    ArenaCenterX - ArenaHalfExtent + inset,
                    ArenaCenterX + ArenaHalfExtent - inset),
                Mathf.Clamp(
                    position.y,
                    -ArenaHalfExtent + inset,
                    ArenaHalfExtent - inset));
        }

        public static bool TryGetGiftArenaBoundaryImpact(
            Vector2 start,
            Vector2 end,
            out Vector2 impact)
        {
            var minX = ArenaCenterX - ArenaHalfExtent +
                       GiftCollisionRadius;
            var maxX = ArenaCenterX + ArenaHalfExtent -
                       GiftCollisionRadius;
            var minY = -ArenaHalfExtent + GiftCollisionRadius;
            var maxY = ArenaHalfExtent - GiftCollisionRadius;
            var clampedStart = new Vector2(
                Mathf.Clamp(start.x, minX, maxX),
                Mathf.Clamp(start.y, minY, maxY));
            if (start.x < minX || start.x > maxX ||
                start.y < minY || start.y > maxY)
            {
                impact = clampedStart;
                return true;
            }

            var delta = end - start;
            var fraction = 1f;
            var collided = false;

            if (delta.x < 0f && end.x <= minX)
            {
                fraction = Mathf.Min(fraction, (minX - start.x) / delta.x);
                collided = true;
            }
            else if (delta.x > 0f && end.x >= maxX)
            {
                fraction = Mathf.Min(fraction, (maxX - start.x) / delta.x);
                collided = true;
            }

            if (delta.y < 0f && end.y <= minY)
            {
                fraction = Mathf.Min(fraction, (minY - start.y) / delta.y);
                collided = true;
            }
            else if (delta.y > 0f && end.y >= maxY)
            {
                fraction = Mathf.Min(fraction, (maxY - start.y) / delta.y);
                collided = true;
            }

            impact = collided
                ? Vector2.Lerp(start, end, Mathf.Clamp01(fraction))
                : end;
            return collided;
        }

        private void ResetReplicatedStateOnServer()
        {
            AdvanceInputEpochOnServer();
            _matchActive.Value = false;
            _paused.Value = false;
            _phase.Value = (byte)NetworkGiftGrabPhase.Inactive;
            _roundNumber.Value = 0;
            _phaseEndsAt.Value = 0d;
            _pausedPhaseRemaining.Value = 0d;
            _roundEndReason.Value = 0;
            _giftSpawnedCount.Value = 0;
            _scores.Value = 0U;
            _roundPoints.Value = 0U;
            _roundRanks.Value = 0U;
            _roundStoredGiftCounts.Value = 0U;
            _totalStoredGiftCounts.Value = 0U;
            _finalRanks.Value = 0U;
            _giftRevision.Value = 0U;
            _actionRevision.Value = 0U;
            _lastActionType.Value = (byte)GiftGrabNetworkActionType.None;
            _lastActionActorSlot.Value = NoNetworkIndex;
            _lastActionTargetSlot.Value = NoNetworkIndex;
            _lastActionGiftId.Value = NoNetworkIndex;
            EnsureSnapshotCountsOnServer();
            for (var slot = 0; slot < _playerSnapshots.Count; slot++)
            {
                var start = GetPlayerStartPosition(slot);
                var towardCenter =
                    new Vector2(ArenaCenterX, 0f) - start;
                _playerSnapshots[slot] =
                    new GiftGrabPlayerNetworkSnapshot
                    {
                        Position = start,
                        Facing = towardCenter.sqrMagnitude > 0.0001f
                            ? towardCenter.normalized
                            : Vector2.up,
                        CarriedGiftId = NoNetworkIndex
                    };
            }
            for (var giftId = 0; giftId < _giftSnapshots.Count; giftId++)
            {
                _giftSnapshots[giftId] =
                    new GiftGrabGiftNetworkSnapshot
                    {
                        Position = new Vector2(ArenaCenterX, 0f),
                        State = (byte)GiftGrabGiftState.Unspawned,
                        CarrierSlot = NoNetworkIndex,
                        StoredOwnerSlot = NoNetworkIndex
                    };
            }
        }

        private void AdvanceInputEpochOnServer()
        {
            _inputEpoch.Value = _inputEpoch.Value == uint.MaxValue
                ? 1U
                : Math.Max(1U, _inputEpoch.Value + 1U);
        }

        private static void AdvanceRevision(
            NetworkVariable<uint> revision)
        {
            revision.Value = revision.Value == uint.MaxValue
                ? 1U
                : revision.Value + 1U;
        }

        private void ClearLocalRuntime()
        {
            _roundState = null;
            _roundResults.Clear();
            _matchSeed = 0UL;
            _runningStartedAt = 0d;
            _simulatedElapsed = 0d;
            _pausedRunningElapsed = 0d;
            _completionReported = false;
            ClearServerInputs();
            for (var slot = 0; slot < _avatars.Length; slot++)
            {
                _avatars[slot] = null;
                _pendingPushFacings[slot] = Vector2.zero;
            }
        }

        private static byte EncodeIndex(int value)
        {
            return value >= 0 && value < NoNetworkIndex
                ? (byte)value
                : NoNetworkIndex;
        }

        private static int DecodeIndex(byte value)
        {
            return value == NoNetworkIndex ? -1 : value;
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

        private static uint WritePackedByte(
            uint packed,
            int slot,
            int value)
        {
            if (!GiftGrabRules.IsValidPlayerSlot(slot))
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
            if (!GiftGrabRules.IsValidPlayerSlot(slot))
            {
                return 0;
            }

            return (int)((packed >> (slot * 8)) & 0xFFU);
        }

        private static bool IsFinite(double value)
        {
            return MinigameServerRules.IsFinite(value);
        }

        private struct StableRandom
        {
            private ulong _state;

            public StableRandom(ulong seed)
            {
                _state = seed ^ 0xD1B54A32D192ED03UL;
                if (_state == 0UL)
                {
                    _state = 0x9E3779B97F4A7C15UL;
                }
            }

            public int Next(int exclusiveMaximum)
            {
                if (exclusiveMaximum <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(exclusiveMaximum));
                }

                var value = NextUInt64();
                return (int)(value % (ulong)exclusiveMaximum);
            }

            private ulong NextUInt64()
            {
                var value = _state;
                value ^= value >> 12;
                value ^= value << 25;
                value ^= value >> 27;
                _state = value;
                return value * 2685821657736338717UL;
            }
        }
    }
}
