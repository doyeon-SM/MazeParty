using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.Minefield;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum NetworkMinefieldPhase : byte
    {
        Inactive,
        Countdown,
        Running,
        RoundResult,
        Complete
    }

    /// <summary>
    /// Server-authoritative Minefield simulation. Board avatars remain in place;
    /// this in-scene object replicates four logical runners instead.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkMinefieldState : NetworkBehaviour
    {
        public const int GridWidth = 3;
        public const int GridHeight = 12;
        public const int MineCount = 20;
        public const double CountdownSeconds = 3d;
        public const double RunSeconds = 40d;
        public const double RoundResultSeconds = 4d;
        public const float ArenaCenterX = 300f;
        public const float ArenaMinX = 290f;
        public const float ArenaMaxX = 310f;
        public const float ArenaMinZ = -22f;
        public const float ArenaMaxZ = 22f;
        public const float RunnerSpeed = 5f;
        public const float SonarRadius = 6f;
        public const float MineSafeZoneDepth =
            (ArenaMaxZ - ArenaMinZ) / GridHeight;
        public const float MineSpawnHorizontalPadding = 0.75f;
        public const float MinimumMineSpacing = 0.8f;

        private const float RunnerStartZ = ArenaMinZ + 0.75f;
        private const float FinishWorldZ = ArenaMaxZ - 0.5f;
        private const float RunnerLaneSpacing = 3f;
        private const float RunnerBoundsPadding = 0.5f;
        private const float MineTriggerRadius = 1.05f;
        private const float CrusherStartOffset = 2.5f;
        private const float CrusherCatchPadding = 0.2f;
        private const float CrusherSpeed = 1.1625f;
        private const double SonarDurationSeconds = 0.75d;
        private const float StationaryInputThreshold = 0.0001f;

        private readonly NetworkVariable<bool> _matchActive =
            new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _paused =
            new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<byte> _phase =
            new NetworkVariable<byte>(
                (byte)NetworkMinefieldPhase.Inactive,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _roundNumber =
            new NetworkVariable<int>(
                0,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        // The generated layout is intentionally server-only. Clients receive
        // only sonar-authorized mine positions and the siren strength below.
        private ulong _serverSeed;
        private readonly NetworkVariable<double> _phaseEndsAt =
            new NetworkVariable<double>(
                0d,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<double> _pausedPhaseRemaining =
            new NetworkVariable<double>(
                0d,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _crusherWorldZ =
            new NetworkVariable<float>(
                ArenaMinZ - CrusherStartOffset,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<Vector3> _runnerPosition0 =
            CreateRunnerPositionVariable();
        private readonly NetworkVariable<Vector3> _runnerPosition1 =
            CreateRunnerPositionVariable();
        private readonly NetworkVariable<Vector3> _runnerPosition2 =
            CreateRunnerPositionVariable();
        private readonly NetworkVariable<Vector3> _runnerPosition3 =
            CreateRunnerPositionVariable();

        private readonly NetworkVariable<byte> _crippledMask =
            CreateByteVariable();
        private readonly NetworkVariable<byte> _eliminatedMask =
            CreateByteVariable();
        private readonly NetworkVariable<byte> _finishedMask =
            CreateByteVariable();
        private readonly NetworkVariable<byte> _sonarMask =
            CreateByteVariable();
        private uint _detonatedMineMask;
        private readonly NetworkList<Vector3> _revealedMinePositions =
            new NetworkList<Vector3>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<uint> _sirenIntensities =
            new NetworkVariable<uint>(
                0U,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<uint> _eliminationCauses =
            new NetworkVariable<uint>(
                0U,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<uint> _scores =
            new NetworkVariable<uint>(
                0U,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<uint> _roundPoints =
            new NetworkVariable<uint>(
                0U,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<uint> _finalRanks =
            new NetworkVariable<uint>(
                0U,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private readonly Vector2[] _serverInputs =
            new Vector2[MinefieldRules.PlayerCount];
        private readonly NetworkPlayerAvatar[] _avatars =
            new NetworkPlayerAvatar[MinefieldRules.PlayerCount];
        private readonly double[] _sonarEndsAt =
            new double[MinefieldRules.PlayerCount];
        private readonly double[] _pausedSonarRemaining =
            new double[MinefieldRules.PlayerCount];
        private readonly MinefieldRoundOutcome[] _roundOutcomes =
            new MinefieldRoundOutcome[MinefieldRules.PlayerCount];
        private readonly bool[] _hasRoundOutcome =
            new bool[MinefieldRules.PlayerCount];
        private readonly List<MinefieldRoundResult> _roundResults =
            new List<MinefieldRoundResult>(MinefieldRules.RoundCount);
        private readonly List<Vector3> _revealedMineCache =
            new List<Vector3>(MineCount);
        private readonly List<Vector3> _sensorRevealScratch =
            new List<Vector3>(MineCount);

        private Vector3[] _cachedMineWorldPositions = Array.Empty<Vector3>();
        private Vector3[] _cachedActiveMineWorldPositions = Array.Empty<Vector3>();
        private ulong _cachedLayoutSeed;
        private int _cachedLayoutRound = -1;
        private uint _cachedDetonatedMask = uint.MaxValue;
        private ulong _eventSequence;
        private bool _completionReported;

        public static NetworkMinefieldState Instance { get; private set; }

        public NetworkMinefieldPhase Phase =>
            (NetworkMinefieldPhase)_phase.Value;
        public int RoundNumber => _roundNumber.Value;
        public bool IsPaused => _paused.Value;
        public float CrusherWorldZ => _crusherWorldZ.Value;
        public double Remaining
        {
            get
            {
                if (!_matchActive.Value ||
                    Phase == NetworkMinefieldPhase.Inactive ||
                    Phase == NetworkMinefieldPhase.Complete)
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
                Debug.LogError("More than one NetworkMinefieldState is spawned.");
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
            if (!IsSpawned || !IsServer || !_matchActive.Value || _paused.Value)
            {
                return;
            }

            FreezeBoardAvatarsOnServer();
            var now = ServerNow;
            UpdateSonarOnServer(now);
            RefreshSensorReplicationOnServer();

            if (_phaseEndsAt.Value <= 0d || now < _phaseEndsAt.Value)
            {
                return;
            }

            switch (Phase)
            {
                case NetworkMinefieldPhase.Countdown:
                    BeginRunOnServer(now);
                    break;
                case NetworkMinefieldPhase.Running:
                    EliminateUnresolvedPlayersOnServer();
                    CompleteRoundOnServer(now);
                    break;
                case NetworkMinefieldPhase.RoundResult:
                    if (_roundNumber.Value < MinefieldRules.RoundCount)
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
            if (!IsSpawned || !IsServer || !_matchActive.Value || _paused.Value ||
                Phase != NetworkMinefieldPhase.Running || Remaining <= 0d)
            {
                return;
            }

            FreezeBoardAvatarsOnServer();
            _crusherWorldZ.Value = Mathf.Min(
                FinishWorldZ,
                _crusherWorldZ.Value + CrusherSpeed * Time.fixedDeltaTime);

            for (var slot = 0; slot < MinefieldRules.PlayerCount; slot++)
            {
                if (!CanAcceptInputForSlot(slot))
                {
                    _serverInputs[slot] = Vector2.zero;
                    continue;
                }

                MoveRunnerOnServer(slot, Time.fixedDeltaTime);
            }

            CatchPlayersWithCrusherOnServer();
            RefreshSensorReplicationOnServer();

            if (AllPlayersResolved())
            {
                CompleteRoundOnServer(ServerNow);
            }
        }

        public void BeginMatchOnServer()
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            ClearLocalRuntime();
            _serverSeed = CreateServerSeed();
            _scores.Value = 0U;
            _roundPoints.Value = 0U;
            _finalRanks.Value = 0U;
            _matchActive.Value = true;
            _paused.Value = false;
            _pausedPhaseRemaining.Value = 0d;
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

            if (!CanAcceptInputForSlot(slot))
            {
                _serverInputs[slot] = Vector2.zero;
                return;
            }

            if (!IsFinite(input))
            {
                _serverInputs[slot] = Vector2.zero;
                return;
            }

            _serverInputs[slot] = Vector2.ClampMagnitude(input, 1f);
        }

        public bool TrySonarOnServer(
            NetworkPlayerAvatar avatar,
            Vector2 currentInput)
        {
            if (!IsFinite(currentInput) ||
                !TryResolveAuthoritativeSlot(avatar, out var slot) ||
                !CanAcceptInputForSlot(slot) ||
                currentInput.sqrMagnitude > StationaryInputThreshold)
            {
                return false;
            }

            _serverInputs[slot] = Vector2.ClampMagnitude(currentInput, 1f);
            _avatars[slot] = avatar;
            avatar.StopServerInputOnServer();
            _sonarEndsAt[slot] = ServerNow + SonarDurationSeconds;
            _sonarMask.Value = SetMaskBit(_sonarMask.Value, slot, true);
            RefreshSensorReplicationOnServer();
            return true;
        }

        public void PauseOnServer(double now)
        {
            if (!IsServer || !_matchActive.Value || _paused.Value || !IsFinite(now))
            {
                return;
            }

            _pausedPhaseRemaining.Value = _phaseEndsAt.Value > 0d
                ? Math.Max(0d, _phaseEndsAt.Value - now)
                : 0d;
            _phaseEndsAt.Value = 0d;

            for (var slot = 0; slot < MinefieldRules.PlayerCount; slot++)
            {
                _serverInputs[slot] = Vector2.zero;
                _pausedSonarRemaining[slot] = IsMaskBitSet(_sonarMask.Value, slot)
                    ? Math.Max(0d, _sonarEndsAt[slot] - now)
                    : 0d;
                _sonarEndsAt[slot] = 0d;
            }

            _paused.Value = true;
            FreezeBoardAvatarsOnServer();
        }

        public void ResumeOnServer(double now)
        {
            if (!IsServer || !_matchActive.Value || !_paused.Value || !IsFinite(now))
            {
                return;
            }

            _phaseEndsAt.Value = now + Math.Max(0d, _pausedPhaseRemaining.Value);
            _pausedPhaseRemaining.Value = 0d;

            for (var slot = 0; slot < MinefieldRules.PlayerCount; slot++)
            {
                var remaining = _pausedSonarRemaining[slot];
                _pausedSonarRemaining[slot] = 0d;
                if (remaining > 0d)
                {
                    _sonarEndsAt[slot] = now + remaining;
                }
                else
                {
                    _sonarEndsAt[slot] = 0d;
                    _sonarMask.Value = SetMaskBit(_sonarMask.Value, slot, false);
                }
            }

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

        public bool CanAcceptInputForSlot(int slot)
        {
            if (!MinefieldRules.IsValidPlayerSlot(slot) ||
                !_matchActive.Value ||
                _paused.Value ||
                Phase != NetworkMinefieldPhase.Running ||
                Remaining <= 0d)
            {
                return false;
            }

            var state = GetPlayerState(slot);
            return state == MinefieldPlayerState.Healthy ||
                   state == MinefieldPlayerState.Crippled;
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

        public MinefieldPlayerState GetPlayerState(int slot)
        {
            if (!MinefieldRules.IsValidPlayerSlot(slot))
            {
                return MinefieldPlayerState.Eliminated;
            }

            if (IsMaskBitSet(_finishedMask.Value, slot))
            {
                return MinefieldPlayerState.Finished;
            }

            if (IsMaskBitSet(_eliminatedMask.Value, slot))
            {
                return MinefieldPlayerState.Eliminated;
            }

            return IsMaskBitSet(_crippledMask.Value, slot)
                ? MinefieldPlayerState.Crippled
                : MinefieldPlayerState.Healthy;
        }

        public int GetScore(int slot)
        {
            return ReadPackedByte(_scores.Value, slot);
        }

        public int GetRoundPoints(int slot)
        {
            return ReadPackedByte(_roundPoints.Value, slot);
        }

        public int GetFinalRank(int slot)
        {
            return ReadPackedByte(_finalRanks.Value, slot);
        }

        public int GetMineHitCount(int slot)
        {
            if (!MinefieldRules.IsValidPlayerSlot(slot) ||
                !IsMaskBitSet(_crippledMask.Value, slot))
            {
                return 0;
            }

            return GetEliminationCause(slot) ==
                   MinefieldEliminationCause.SecondMineHit
                ? MinefieldRules.MineHitsToEliminate
                : 1;
        }

        public MinefieldEliminationCause GetEliminationCause(int slot)
        {
            if (!MinefieldRules.IsValidPlayerSlot(slot))
            {
                return MinefieldEliminationCause.None;
            }

            return (MinefieldEliminationCause)ReadPackedByte(
                _eliminationCauses.Value,
                slot);
        }

        public float GetSirenIntensity(int slot)
        {
            return MinefieldRules.IsValidPlayerSlot(slot)
                ? ReadPackedByte(_sirenIntensities.Value, slot) / 255f
                : 0f;
        }

        public bool IsSonarActive(int slot)
        {
            return MinefieldRules.IsValidPlayerSlot(slot) &&
                   IsMaskBitSet(_sonarMask.Value, slot);
        }

        /// <summary>
        /// Returns only the currently sonar-revealed, undetonated mine positions.
        /// The authoritative layout and random seed never leave the server.
        /// </summary>
        public IReadOnlyList<Vector3> GetMineWorldPositions()
        {
            _revealedMineCache.Clear();
            for (var index = 0; index < _revealedMinePositions.Count; index++)
            {
                _revealedMineCache.Add(_revealedMinePositions[index]);
            }

            return _revealedMineCache;
        }

        private void BeginRoundOnServer(int roundNumber, double now)
        {
            _roundNumber.Value = roundNumber;
            _phase.Value = (byte)NetworkMinefieldPhase.Countdown;
            _phaseEndsAt.Value = now + CountdownSeconds;
            _pausedPhaseRemaining.Value = 0d;
            _crusherWorldZ.Value = ArenaMinZ - CrusherStartOffset;
            _crippledMask.Value = 0;
            _eliminatedMask.Value = 0;
            _finishedMask.Value = 0;
            _sonarMask.Value = 0;
            _detonatedMineMask = 0U;
            _eliminationCauses.Value = 0U;
            _sirenIntensities.Value = 0U;
            _revealedMinePositions.Clear();
            _roundPoints.Value = 0U;
            _eventSequence = 0UL;

            for (var slot = 0; slot < MinefieldRules.PlayerCount; slot++)
            {
                _serverInputs[slot] = Vector2.zero;
                _sonarEndsAt[slot] = 0d;
                _pausedSonarRemaining[slot] = 0d;
                _hasRoundOutcome[slot] = false;
                _roundOutcomes[slot] = default;
                SetRunnerPositionOnServer(slot, GetStartPosition(slot));
            }

            InvalidateLayoutCache();
            EnsureLayoutCache();
            RefreshSensorReplicationOnServer();
            FreezeBoardAvatarsOnServer();
        }

        private void BeginRunOnServer(double now)
        {
            _phase.Value = (byte)NetworkMinefieldPhase.Running;
            _phaseEndsAt.Value = now + RunSeconds;
            _crusherWorldZ.Value = ArenaMinZ - CrusherStartOffset;
        }

        private void MoveRunnerOnServer(int slot, float deltaTime)
        {
            var state = GetPlayerState(slot);
            var speedMultiplier = state == MinefieldPlayerState.Crippled
                ? FootstepRules.WalkSpeedMultiplier
                : 1f;
            var movement = new Vector3(
                _serverInputs[slot].x,
                0f,
                _serverInputs[slot].y);
            if (movement.sqrMagnitude > 1f)
            {
                movement.Normalize();
            }

            var position = GetRunnerPosition(slot) +
                           movement * (RunnerSpeed * speedMultiplier * deltaTime);
            position.x = Mathf.Clamp(
                position.x,
                ArenaMinX + RunnerBoundsPadding,
                ArenaMaxX - RunnerBoundsPadding);
            position.y = 0f;
            position.z = Mathf.Clamp(position.z, RunnerStartZ, ArenaMaxZ);
            SetRunnerPositionOnServer(slot, position);

            ResolveMineContactOnServer(slot, position);
            if (CanAcceptInputForSlot(slot) && position.z >= FinishWorldZ)
            {
                RecordFinishOnServer(slot);
            }
        }

        private void ResolveMineContactOnServer(int slot, Vector3 position)
        {
            EnsureLayoutCache();

            for (var mineIndex = 0;
                 mineIndex < _cachedMineWorldPositions.Length;
                 mineIndex++)
            {
                var mineBit = 1U << mineIndex;
                if ((_detonatedMineMask & mineBit) != 0U)
                {
                    continue;
                }

                var minePosition = _cachedMineWorldPositions[mineIndex];
                var deltaX = position.x - minePosition.x;
                var deltaZ = position.z - minePosition.z;
                if (deltaX * deltaX + deltaZ * deltaZ >
                    MineTriggerRadius * MineTriggerRadius)
                {
                    continue;
                }

                _detonatedMineMask |= mineBit;
                InvalidateActiveMineCache();
                ApplyMineHitOnServer(slot);
                RefreshSensorReplicationOnServer();
                return;
            }
        }

        private void ApplyMineHitOnServer(int slot)
        {
            var state = GetPlayerState(slot);
            if (state == MinefieldPlayerState.Healthy)
            {
                SetPlayerStateOnServer(slot, MinefieldPlayerState.Crippled);
                return;
            }

            if (state == MinefieldPlayerState.Crippled)
            {
                RecordEliminationOnServer(
                    slot,
                    MinefieldEliminationCause.SecondMineHit);
            }
        }

        private void CatchPlayersWithCrusherOnServer()
        {
            var crusherFront = _crusherWorldZ.Value + CrusherCatchPadding;
            for (var slot = 0; slot < MinefieldRules.PlayerCount; slot++)
            {
                if (CanAcceptInputForSlot(slot) &&
                    GetRunnerPosition(slot).z <= crusherFront)
                {
                    RecordEliminationOnServer(
                        slot,
                        MinefieldEliminationCause.Crusher);
                }
            }
        }

        private void RecordFinishOnServer(int slot)
        {
            if (_hasRoundOutcome[slot])
            {
                return;
            }

            _serverInputs[slot] = Vector2.zero;
            SetPlayerStateOnServer(slot, MinefieldPlayerState.Finished);
            _roundOutcomes[slot] = MinefieldRoundOutcome.Finish(
                slot,
                ++_eventSequence);
            _hasRoundOutcome[slot] = true;
        }

        private void RecordEliminationOnServer(
            int slot,
            MinefieldEliminationCause cause)
        {
            if (_hasRoundOutcome[slot])
            {
                return;
            }

            if (cause == MinefieldEliminationCause.None)
            {
                cause = MinefieldEliminationCause.RoundTimeout;
            }

            _serverInputs[slot] = Vector2.zero;
            _eliminationCauses.Value = WritePackedByte(
                _eliminationCauses.Value,
                slot,
                (int)cause);
            SetPlayerStateOnServer(slot, MinefieldPlayerState.Eliminated);
            _roundOutcomes[slot] = MinefieldRoundOutcome.Eliminate(
                slot,
                ++_eventSequence);
            _hasRoundOutcome[slot] = true;
        }

        private void EliminateUnresolvedPlayersOnServer()
        {
            for (var slot = 0; slot < MinefieldRules.PlayerCount; slot++)
            {
                if (!_hasRoundOutcome[slot])
                {
                    RecordEliminationOnServer(
                        slot,
                        MinefieldEliminationCause.RoundTimeout);
                }
            }
        }

        private void CompleteRoundOnServer(double now)
        {
            if (Phase != NetworkMinefieldPhase.Running || !AllPlayersResolved())
            {
                return;
            }

            var result = MinefieldRoundScoring.Score(_roundOutcomes);
            if (_roundResults.Count == _roundNumber.Value - 1)
            {
                _roundResults.Add(result);
            }
            else if (_roundResults.Count >= _roundNumber.Value)
            {
                _roundResults[_roundNumber.Value - 1] = result;
            }
            else
            {
                throw new InvalidOperationException(
                    "Minefield round results were completed out of sequence.");
            }

            for (var index = 0; index < result.Standings.Count; index++)
            {
                var standing = result.Standings[index];
                _roundPoints.Value = WritePackedByte(
                    _roundPoints.Value,
                    standing.PlayerSlot,
                    standing.Points);
                _scores.Value = WritePackedByte(
                    _scores.Value,
                    standing.PlayerSlot,
                    GetScore(standing.PlayerSlot) + standing.Points);
            }

            ClearAllInputsOnServer();
            _sonarMask.Value = 0;
            RefreshSensorReplicationOnServer();
            _phase.Value = (byte)NetworkMinefieldPhase.RoundResult;
            _phaseEndsAt.Value = now + RoundResultSeconds;
        }

        private void CompleteMatchOnServer()
        {
            if (_completionReported ||
                _roundResults.Count != MinefieldRules.RoundCount)
            {
                return;
            }

            var leaderboard = MinefieldMatchScoring.BuildLeaderboard(_roundResults);
            for (var index = 0; index < leaderboard.Count; index++)
            {
                var entry = leaderboard[index];
                _finalRanks.Value = WritePackedByte(
                    _finalRanks.Value,
                    entry.PlayerSlot,
                    entry.Rank);
            }

            var match = NetworkMatchState.Instance;
            if (match == null || !match.TryCompleteMinefieldOnServer(leaderboard))
            {
                // Keep RoundResult active so a transient scene/match ordering
                // issue can retry settlement on the following server update.
                return;
            }

            _completionReported = true;
            _matchActive.Value = false;
            _phase.Value = (byte)NetworkMinefieldPhase.Complete;
            _phaseEndsAt.Value = 0d;
            _pausedPhaseRemaining.Value = 0d;
            ClearAllInputsOnServer();
            FreezeBoardAvatarsOnServer();
        }

        private void SetPlayerStateOnServer(
            int slot,
            MinefieldPlayerState state)
        {
            switch (state)
            {
                case MinefieldPlayerState.Healthy:
                    _crippledMask.Value =
                        SetMaskBit(_crippledMask.Value, slot, false);
                    _eliminatedMask.Value =
                        SetMaskBit(_eliminatedMask.Value, slot, false);
                    _finishedMask.Value =
                        SetMaskBit(_finishedMask.Value, slot, false);
                    break;
                case MinefieldPlayerState.Crippled:
                    _crippledMask.Value =
                        SetMaskBit(_crippledMask.Value, slot, true);
                    _eliminatedMask.Value =
                        SetMaskBit(_eliminatedMask.Value, slot, false);
                    _finishedMask.Value =
                        SetMaskBit(_finishedMask.Value, slot, false);
                    break;
                case MinefieldPlayerState.Eliminated:
                    _eliminatedMask.Value =
                        SetMaskBit(_eliminatedMask.Value, slot, true);
                    _finishedMask.Value =
                        SetMaskBit(_finishedMask.Value, slot, false);
                    break;
                case MinefieldPlayerState.Finished:
                    _finishedMask.Value =
                        SetMaskBit(_finishedMask.Value, slot, true);
                    _eliminatedMask.Value =
                        SetMaskBit(_eliminatedMask.Value, slot, false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
        }

        private void UpdateSonarOnServer(double now)
        {
            if (_sonarMask.Value == 0)
            {
                return;
            }

            var sonarMask = _sonarMask.Value;
            for (var slot = 0; slot < MinefieldRules.PlayerCount; slot++)
            {
                if (IsMaskBitSet(sonarMask, slot) &&
                    now >= _sonarEndsAt[slot])
                {
                    sonarMask = SetMaskBit(sonarMask, slot, false);
                    _sonarEndsAt[slot] = 0d;
                }
            }

            if (sonarMask != _sonarMask.Value)
            {
                _sonarMask.Value = sonarMask;
            }
        }

        private void RefreshSensorReplicationOnServer()
        {
            if (!IsServer)
            {
                return;
            }

            EnsureLayoutCache();
            var packedSirenIntensities = 0U;
            var sonarRadiusSqr = SonarRadius * SonarRadius;
            for (var slot = 0; slot < MinefieldRules.PlayerCount; slot++)
            {
                var playerState = GetPlayerState(slot);
                var canSense = _matchActive.Value &&
                               (playerState == MinefieldPlayerState.Healthy ||
                                playerState == MinefieldPlayerState.Crippled);
                var intensity = canSense
                    ? CalculateWarningIntensity(
                        GetRunnerPosition(slot),
                        _cachedActiveMineWorldPositions)
                    : 0f;
                packedSirenIntensities = WritePackedByte(
                    packedSirenIntensities,
                    slot,
                    Mathf.RoundToInt(intensity * byte.MaxValue));
            }

            if (_sirenIntensities.Value != packedSirenIntensities)
            {
                _sirenIntensities.Value = packedSirenIntensities;
            }

            _sensorRevealScratch.Clear();
            for (var mineIndex = 0;
                 mineIndex < _cachedActiveMineWorldPositions.Length;
                 mineIndex++)
            {
                var minePosition = _cachedActiveMineWorldPositions[mineIndex];
                var revealed = false;
                for (var slot = 0;
                     slot < MinefieldRules.PlayerCount && !revealed;
                     slot++)
                {
                    if (!IsMaskBitSet(_sonarMask.Value, slot))
                    {
                        continue;
                    }

                    var playerState = GetPlayerState(slot);
                    if (playerState != MinefieldPlayerState.Healthy &&
                        playerState != MinefieldPlayerState.Crippled)
                    {
                        continue;
                    }

                    var delta = minePosition - GetRunnerPosition(slot);
                    delta.y = 0f;
                    revealed = delta.sqrMagnitude <= sonarRadiusSqr;
                }

                if (revealed)
                {
                    _sensorRevealScratch.Add(minePosition);
                }
            }

            if (ListsMatch(_revealedMinePositions, _sensorRevealScratch))
            {
                return;
            }

            _revealedMinePositions.Clear();
            for (var index = 0; index < _sensorRevealScratch.Count; index++)
            {
                _revealedMinePositions.Add(_sensorRevealScratch[index]);
            }
        }

        private static float CalculateWarningIntensity(
            Vector3 runnerPosition,
            IReadOnlyList<Vector3> minePositions)
        {
            var nearestSqr = float.PositiveInfinity;
            for (var index = 0; index < minePositions.Count; index++)
            {
                var delta = minePositions[index] - runnerPosition;
                delta.y = 0f;
                nearestSqr = Mathf.Min(nearestSqr, delta.sqrMagnitude);
            }

            if (float.IsInfinity(nearestSqr))
            {
                return 0f;
            }

            return 1f - Mathf.Clamp01(
                Mathf.Sqrt(nearestSqr) / SonarRadius);
        }

        private static bool ListsMatch(
            NetworkList<Vector3> networkValues,
            IReadOnlyList<Vector3> expected)
        {
            if (networkValues.Count != expected.Count)
            {
                return false;
            }

            for (var index = 0; index < expected.Count; index++)
            {
                if (networkValues[index] != expected[index])
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryResolveAuthoritativeSlot(
            NetworkPlayerAvatar avatar,
            out int slot)
        {
            slot = avatar != null ? avatar.AssignedSlot : -1;
            return IsSpawned &&
                   IsServer &&
                   avatar != null &&
                   avatar.IsSpawned &&
                   MinefieldRules.IsValidPlayerSlot(slot);
        }

        private void CacheAndFreezeBoardAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0; slot < MinefieldRules.PlayerCount; slot++)
            {
                var avatar = match != null ? match.GetAvatarForSlot(slot) : null;
                _avatars[slot] = avatar;
                avatar?.StopServerInputOnServer();
            }
        }

        private void FreezeBoardAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0; slot < MinefieldRules.PlayerCount; slot++)
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

        private bool AllPlayersResolved()
        {
            for (var slot = 0; slot < _hasRoundOutcome.Length; slot++)
            {
                if (!_hasRoundOutcome[slot])
                {
                    return false;
                }
            }

            return true;
        }

        private void SetRunnerPositionOnServer(int slot, Vector3 position)
        {
            switch (slot)
            {
                case 0:
                    _runnerPosition0.Value = position;
                    break;
                case 1:
                    _runnerPosition1.Value = position;
                    break;
                case 2:
                    _runnerPosition2.Value = position;
                    break;
                case 3:
                    _runnerPosition3.Value = position;
                    break;
            }
        }

        private static Vector3 GetStartPosition(int slot)
        {
            var centeredSlot = slot - (MinefieldRules.PlayerCount - 1) * 0.5f;
            return new Vector3(
                ArenaCenterX + centeredSlot * RunnerLaneSpacing,
                0f,
                RunnerStartZ);
        }

        private void EnsureLayoutCache()
        {
            var round = _roundNumber.Value;
            var seed = _serverSeed;
            var detonatedMask = _detonatedMineMask;
            if (_cachedLayoutRound == round &&
                _cachedLayoutSeed == seed &&
                _cachedDetonatedMask == detonatedMask)
            {
                return;
            }

            if (round < 1 || round > MinefieldRules.RoundCount)
            {
                _cachedMineWorldPositions = Array.Empty<Vector3>();
                _cachedActiveMineWorldPositions = Array.Empty<Vector3>();
                _cachedLayoutRound = round;
                _cachedLayoutSeed = seed;
                _cachedDetonatedMask = detonatedMask;
                return;
            }

            if (_cachedLayoutRound != round || _cachedLayoutSeed != seed)
            {
                _cachedMineWorldPositions = GenerateMineWorldPositions(
                    seed,
                    round);
            }

            var activeMines = new List<Vector3>(_cachedMineWorldPositions.Length);
            for (var index = 0;
                 index < _cachedMineWorldPositions.Length;
                 index++)
            {
                if ((detonatedMask & (1U << index)) == 0U)
                {
                    activeMines.Add(_cachedMineWorldPositions[index]);
                }
            }

            _cachedActiveMineWorldPositions = activeMines.ToArray();
            _cachedLayoutRound = round;
            _cachedLayoutSeed = seed;
            _cachedDetonatedMask = detonatedMask;
        }

        public static Vector3[] GenerateMineWorldPositions(
            ulong serverSeed,
            int roundNumber)
        {
            var random = new MinePositionRandom(
                MinefieldLayoutGenerator.DeriveRoundSeed(
                    serverSeed,
                    roundNumber));
            var positions = new Vector3[MineCount];
            var minimumSpacingSquared = MinimumMineSpacing * MinimumMineSpacing;
            var minX = ArenaMinX + MineSpawnHorizontalPadding;
            var maxX = ArenaMaxX - MineSpawnHorizontalPadding;
            var minZ = ArenaMinZ + MineSafeZoneDepth;
            var maxZ = ArenaMaxZ - MineSafeZoneDepth;

            for (var index = 0; index < positions.Length; index++)
            {
                var accepted = false;
                for (var attempt = 0; attempt < 128; attempt++)
                {
                    var candidate = new Vector3(
                        Mathf.Lerp(minX, maxX, random.NextUnitFloat()),
                        0f,
                        Mathf.Lerp(minZ, maxZ, random.NextUnitFloat()));
                    if (IsFarEnoughFromExisting(
                            positions,
                            index,
                            candidate,
                            minimumSpacingSquared))
                    {
                        positions[index] = candidate;
                        accepted = true;
                        break;
                    }
                }

                if (!accepted)
                {
                    // The arena is far larger than the requested density, so this
                    // is only a deterministic safety fallback for extreme future
                    // rule changes.
                    positions[index] = new Vector3(
                        Mathf.Lerp(minX, maxX, random.NextUnitFloat()),
                        0f,
                        Mathf.Lerp(minZ, maxZ, random.NextUnitFloat()));
                }
            }

            return positions;
        }

        private static bool IsFarEnoughFromExisting(
            IReadOnlyList<Vector3> positions,
            int count,
            Vector3 candidate,
            float minimumSpacingSquared)
        {
            for (var index = 0; index < count; index++)
            {
                if ((positions[index] - candidate).sqrMagnitude <
                    minimumSpacingSquared)
                {
                    return false;
                }
            }
            return true;
        }

        private void InvalidateLayoutCache()
        {
            _cachedLayoutRound = -1;
            _cachedLayoutSeed = 0UL;
            _cachedDetonatedMask = uint.MaxValue;
        }

        private void InvalidateActiveMineCache()
        {
            _cachedDetonatedMask = uint.MaxValue;
        }

        private void ResetReplicatedStateOnServer()
        {
            _matchActive.Value = false;
            _paused.Value = false;
            _phase.Value = (byte)NetworkMinefieldPhase.Inactive;
            _roundNumber.Value = 0;
            _serverSeed = 0UL;
            _phaseEndsAt.Value = 0d;
            _pausedPhaseRemaining.Value = 0d;
            _crusherWorldZ.Value = ArenaMinZ - CrusherStartOffset;
            _crippledMask.Value = 0;
            _eliminatedMask.Value = 0;
            _finishedMask.Value = 0;
            _sonarMask.Value = 0;
            _detonatedMineMask = 0U;
            _sirenIntensities.Value = 0U;
            _eliminationCauses.Value = 0U;
            _revealedMinePositions.Clear();
            _scores.Value = 0U;
            _roundPoints.Value = 0U;
            _finalRanks.Value = 0U;

            for (var slot = 0; slot < MinefieldRules.PlayerCount; slot++)
            {
                SetRunnerPositionOnServer(slot, GetStartPosition(slot));
            }

            InvalidateLayoutCache();
        }

        private void ClearLocalRuntime()
        {
            _roundResults.Clear();
            _eventSequence = 0UL;
            _completionReported = false;

            for (var slot = 0; slot < MinefieldRules.PlayerCount; slot++)
            {
                _serverInputs[slot] = Vector2.zero;
                _avatars[slot] = null;
                _sonarEndsAt[slot] = 0d;
                _pausedSonarRemaining[slot] = 0d;
                _roundOutcomes[slot] = default;
                _hasRoundOutcome[slot] = false;
            }

            _cachedMineWorldPositions = Array.Empty<Vector3>();
            _cachedActiveMineWorldPositions = Array.Empty<Vector3>();
            _revealedMineCache.Clear();
            _sensorRevealScratch.Clear();
            InvalidateLayoutCache();
        }

        private static NetworkVariable<Vector3> CreateRunnerPositionVariable()
        {
            return new NetworkVariable<Vector3>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        private static NetworkVariable<byte> CreateByteVariable()
        {
            return new NetworkVariable<byte>(
                0,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        private struct MinePositionRandom
        {
            private ulong _state;

            public MinePositionRandom(ulong seed)
            {
                _state = seed;
            }

            public float NextUnitFloat()
            {
                _state = unchecked(_state + 0x9E3779B97F4A7C15UL);
                var value = _state;
                value = unchecked(
                    (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
                value = unchecked(
                    (value ^ (value >> 27)) * 0x94D049BB133111EBUL);
                value ^= value >> 31;
                return (value >> 40) * (1f / 16777216f);
            }
        }

        private static byte SetMaskBit(byte mask, int slot, bool enabled)
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
            if (!MinefieldRules.IsValidPlayerSlot(slot))
            {
                return packed;
            }

            var shift = slot * 8;
            var clamped = (uint)Mathf.Clamp(value, 0, byte.MaxValue);
            var clearMask = ~(0xFFU << shift);
            return (packed & clearMask) | (clamped << shift);
        }

        private static int ReadPackedByte(uint packed, int slot)
        {
            if (!MinefieldRules.IsValidPlayerSlot(slot))
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

        private static ulong CreateServerSeed()
        {
            var bytes = Guid.NewGuid().ToByteArray();
            var seed = BitConverter.ToUInt64(bytes, 0);
            return seed != 0UL ? seed : 0x9E3779B97F4A7C15UL;
        }
    }
}
