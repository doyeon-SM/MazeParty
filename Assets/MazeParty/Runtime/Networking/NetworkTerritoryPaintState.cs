using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.TerritoryPaint;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum NetworkTerritoryPaintPhase : byte
    {
        Inactive,
        Countdown,
        Running,
        RoundResult,
        Complete
    }

    public struct TerritoryPaintPlayerSnapshot :
        INetworkSerializable,
        IEquatable<TerritoryPaintPlayerSnapshot>
    {
        public Vector2 Position;
        public Vector2 Facing;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Facing);
        }

        public bool Equals(TerritoryPaintPlayerSnapshot other)
        {
            return Position == other.Position && Facing == other.Facing;
        }

        public override bool Equals(object obj)
        {
            return obj is TerritoryPaintPlayerSnapshot other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Position, Facing);
        }
    }

    /// <summary>
    /// Server-authoritative single-round territory simulation. The paint map is
    /// replicated as a painted bit plane plus two owner bit planes, allowing a
    /// reconnecting client to rebuild the full arena without replaying history.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkTerritoryPaintState : NetworkBehaviour
    {
        public const double CountdownSeconds = 3d;
        public const double ResultSeconds = 4d;
        public const float ArenaCenterX = 940f;
        public const float ArenaHalfExtent = 9f;
        public const float PlayerCollisionRadius = 0.58f;
        public const float MoveSpeed = 5.4f;
        public const float BrushRadius = 1.15f;

        private const float MaximumSimulationStepSeconds = 0.05f;
        private const int BitsPerWord = 64;
        private const int PaintWordCount =
            (TerritoryPaintRules.SurfaceResolution *
             TerritoryPaintRules.SurfaceResolution +
             BitsPerWord - 1) / BitsPerWord;

        private readonly NetworkVariable<bool> _matchActive =
            CreateBoolVariable();
        private readonly NetworkVariable<bool> _paused =
            CreateBoolVariable();
        private readonly NetworkVariable<byte> _phase =
            CreateByteVariable(
                (byte)NetworkTerritoryPaintPhase.Inactive);
        private readonly NetworkVariable<double> _phaseEndsAt =
            CreateDoubleVariable();
        private readonly NetworkVariable<double> _pausedPhaseRemaining =
            CreateDoubleVariable();
        private readonly NetworkVariable<uint> _inputEpoch =
            CreateUIntVariable();
        private readonly NetworkVariable<ulong> _scores =
            CreateULongVariable();
        private readonly NetworkVariable<uint> _finalRanks =
            CreateUIntVariable();
        private readonly NetworkVariable<uint> _paintRevision =
            CreateUIntVariable();

        private readonly NetworkList<TerritoryPaintPlayerSnapshot>
            _playerSnapshots =
                new NetworkList<TerritoryPaintPlayerSnapshot>(
                    default,
                    NetworkVariableReadPermission.Everyone,
                    NetworkVariableWritePermission.Server);
        private readonly NetworkList<ulong> _paintedWords =
            CreateWordList();
        private readonly NetworkList<ulong> _ownerLowWords =
            CreateWordList();
        private readonly NetworkList<ulong> _ownerHighWords =
            CreateWordList();

        private readonly Vector2[] _serverInputs =
            new Vector2[TerritoryPaintRules.PlayerCount];
        private readonly Vector2[] _playerPositions =
            new Vector2[TerritoryPaintRules.PlayerCount];
        private readonly Vector2[] _playerFacings =
            new Vector2[TerritoryPaintRules.PlayerCount];
        private readonly NetworkPlayerAvatar[] _avatars =
            new NetworkPlayerAvatar[TerritoryPaintRules.PlayerCount];
        private readonly ulong[] _paintedScratch =
            new ulong[PaintWordCount];
        private readonly ulong[] _ownerLowScratch =
            new ulong[PaintWordCount];
        private readonly ulong[] _ownerHighScratch =
            new ulong[PaintWordCount];

        private TerritoryPaintSurface _surface;
        private IReadOnlyList<TerritoryPaintLeaderboardEntry>
            _finalLeaderboard;
        private double _runningStartedAt;
        private double _simulatedElapsed;
        private double _pausedRunningElapsed;
        private bool _completionReported;

        public static NetworkTerritoryPaintState Instance
        {
            get;
            private set;
        }

        public NetworkTerritoryPaintPhase Phase =>
            (NetworkTerritoryPaintPhase)_phase.Value;
        public int RoundNumber => _matchActive.Value ? 1 : 0;
        public int PlayerCount => TerritoryPaintRules.PlayerCount;
        public int SurfaceResolution =>
            TerritoryPaintRules.SurfaceResolution;
        public uint InputEpoch => _inputEpoch.Value;
        public uint PaintRevision => _paintRevision.Value;
        public bool IsPaused => _paused.Value;
        public double Remaining => GetRemaining(
            _phaseEndsAt.Value,
            _pausedPhaseRemaining.Value,
            Phase == NetworkTerritoryPaintPhase.Inactive ||
            Phase == NetworkTerritoryPaintPhase.Complete);

        private double ServerNow =>
            NetworkManager != null && NetworkManager.IsListening
                ? NetworkManager.ServerTime.Time
                : Time.unscaledTimeAsDouble;

        public override void OnNetworkSpawn()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError(
                    "More than one NetworkTerritoryPaintState is spawned.");
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
            if (Phase == NetworkTerritoryPaintPhase.Running)
            {
                SimulateToOnServer(GetRunningElapsed(now));
                SyncPlayerSnapshotsOnServer();
                if (now >= _phaseEndsAt.Value)
                {
                    CompleteRoundOnServer(now);
                }
                return;
            }

            if (_phaseEndsAt.Value <= 0d || now < _phaseEndsAt.Value)
            {
                return;
            }

            switch (Phase)
            {
                case NetworkTerritoryPaintPhase.Countdown:
                    BeginRunOnServer(now);
                    break;
                case NetworkTerritoryPaintPhase.RoundResult:
                    CompleteMatchOnServer();
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
            _surface = new TerritoryPaintSurface(
                TerritoryPaintRules.SurfaceResolution,
                ArenaCenterX,
                ArenaHalfExtent);
            for (var slot = 0;
                 slot < TerritoryPaintRules.PlayerCount;
                 slot++)
            {
                _playerPositions[slot] = GetPlayerStartPosition(slot);
                _playerFacings[slot] = GetStartFacing(slot);
            }

            _matchActive.Value = true;
            _paused.Value = false;
            _completionReported = false;
            CacheAndFreezeBoardAvatarsOnServer();
            AdvanceInputEpochOnServer();
            _phase.Value = (byte)NetworkTerritoryPaintPhase.Countdown;
            _phaseEndsAt.Value = ServerNow + CountdownSeconds;
            SyncPlayerSnapshotsOnServer();
            SyncPaintOnServer();
            Debug.Log(
                "[Territory Paint] Match started. Seed " + matchSeed +
                ", one 60-second round, total area normalized to 1000.");
        }

        public bool ReceiveInputOnServer(
            NetworkPlayerAvatar avatar,
            Vector2 input,
            int roundNumber,
            uint inputEpoch)
        {
            if (!TryResolveAuthoritativeSlot(avatar, out var slot) ||
                !ValidateInputEnvelope(roundNumber, inputEpoch) ||
                !IsFinite(input))
            {
                return false;
            }

            SimulateToOnServer(GetRunningElapsed(ServerNow));
            avatar.StopServerInputOnServer();
            _avatars[slot] = avatar;
            _serverInputs[slot] =
                Vector2.ClampMagnitude(input, 1f);
            return true;
        }

        public bool CanAcceptInputForSlot(int slot)
        {
            return TerritoryPaintRules.IsValidPlayerSlot(slot) &&
                   _matchActive.Value && !_paused.Value &&
                   Phase == NetworkTerritoryPaintPhase.Running &&
                   Remaining > 0d;
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
                : GetStartFacing(slot);
        }

        public int GetScore(int slot)
        {
            if (!TerritoryPaintRules.IsValidPlayerSlot(slot))
            {
                return 0;
            }

            return (int)((_scores.Value >> (slot * 16)) & 0xFFFFUL);
        }

        public int GetFinalRank(int slot)
        {
            if (!TerritoryPaintRules.IsValidPlayerSlot(slot))
            {
                return 0;
            }

            return (int)((_finalRanks.Value >> (slot * 8)) & 0xFFU);
        }

        public byte GetPaintOwner(int x, int y)
        {
            if (x < 0 || x >= TerritoryPaintRules.SurfaceResolution ||
                y < 0 || y >= TerritoryPaintRules.SurfaceResolution)
            {
                return TerritoryPaintRules.UnpaintedOwner;
            }

            var cellIndex =
                y * TerritoryPaintRules.SurfaceResolution + x;
            var wordIndex = cellIndex / BitsPerWord;
            var bitIndex = cellIndex % BitsPerWord;
            if (wordIndex >= _paintedWords.Count ||
                wordIndex >= _ownerLowWords.Count ||
                wordIndex >= _ownerHighWords.Count)
            {
                return TerritoryPaintRules.UnpaintedOwner;
            }

            var mask = 1UL << bitIndex;
            if ((_paintedWords[wordIndex] & mask) == 0UL)
            {
                return TerritoryPaintRules.UnpaintedOwner;
            }

            var owner = 0;
            if ((_ownerLowWords[wordIndex] & mask) != 0UL)
            {
                owner |= 1;
            }
            if ((_ownerHighWords[wordIndex] & mask) != 0UL)
            {
                owner |= 2;
            }
            return (byte)owner;
        }

        public void PauseOnServer(double now)
        {
            if (!IsServer || !_matchActive.Value || _paused.Value ||
                !IsFinite(now))
            {
                return;
            }

            if (Phase == NetworkTerritoryPaintPhase.Running)
            {
                _pausedRunningElapsed = GetRunningElapsed(now);
                SimulateToOnServer(_pausedRunningElapsed);
                SyncPlayerSnapshotsOnServer();
            }

            _pausedPhaseRemaining.Value = _phaseEndsAt.Value > 0d
                ? Math.Max(0d, _phaseEndsAt.Value - now)
                : 0d;
            _phaseEndsAt.Value = 0d;
            ClearInputsOnServer();
            AdvanceInputEpochOnServer();
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
            if (Phase == NetworkTerritoryPaintPhase.Running)
            {
                _runningStartedAt = now - _pausedRunningElapsed;
            }

            _pausedPhaseRemaining.Value = 0d;
            _pausedRunningElapsed = 0d;
            ClearInputsOnServer();
            AdvanceInputEpochOnServer();
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

        public static Vector2 GetPlayerStartPosition(int playerSlot)
        {
            if (!TerritoryPaintRules.IsValidPlayerSlot(playerSlot))
            {
                return new Vector2(ArenaCenterX, 0f);
            }

            var right = (playerSlot & 1) != 0;
            var top = (playerSlot & 2) != 0;
            var inset =
                ArenaHalfExtent - PlayerCollisionRadius - 0.85f;
            return new Vector2(
                ArenaCenterX + (right ? inset : -inset),
                top ? inset : -inset);
        }

        private static Vector2 GetStartFacing(int playerSlot)
        {
            var start = GetPlayerStartPosition(playerSlot);
            return (new Vector2(ArenaCenterX, 0f) - start).normalized;
        }

        private void BeginRunOnServer(double now)
        {
            ClearInputsOnServer();
            AdvanceInputEpochOnServer();
            _phase.Value = (byte)NetworkTerritoryPaintPhase.Running;
            _phaseEndsAt.Value = now + TerritoryPaintRules.RoundSeconds;
            _runningStartedAt = now;
            _simulatedElapsed = 0d;

            for (var slot = 0;
                 slot < TerritoryPaintRules.PlayerCount;
                 slot++)
            {
                _surface.PaintStroke(
                    slot,
                    _playerPositions[slot],
                    _playerPositions[slot],
                    BrushRadius);
            }

            SyncPaintOnServer();
            SyncPlayerSnapshotsOnServer();
        }

        private void CompleteRoundOnServer(double now)
        {
            if (Phase != NetworkTerritoryPaintPhase.Running ||
                _surface == null)
            {
                return;
            }

            SimulateToOnServer(TerritoryPaintRules.RoundSeconds);
            SyncPlayerSnapshotsOnServer();
            SyncPaintOnServer();

            var ownedCounts =
                new int[TerritoryPaintRules.PlayerCount];
            for (var slot = 0; slot < ownedCounts.Length; slot++)
            {
                ownedCounts[slot] =
                    _surface.GetOwnedCellCount(slot);
            }

            _finalLeaderboard =
                TerritoryPaintScoring.BuildLeaderboard(
                    ownedCounts,
                    _surface.CellCount);
            uint ranks = 0U;
            for (var index = 0;
                 index < _finalLeaderboard.Count;
                 index++)
            {
                var entry = _finalLeaderboard[index];
                ranks |= (uint)entry.Rank <<
                         (entry.PlayerSlot * 8);
            }

            _finalRanks.Value = ranks;
            ClearInputsOnServer();
            AdvanceInputEpochOnServer();
            _phase.Value =
                (byte)NetworkTerritoryPaintPhase.RoundResult;
            _phaseEndsAt.Value = now + ResultSeconds;
            Debug.Log(
                "[Territory Paint] Round complete: " +
                BuildScoreLog());
        }

        private void CompleteMatchOnServer()
        {
            if (_completionReported || _finalLeaderboard == null)
            {
                return;
            }

            var match = NetworkMatchState.Instance;
            if (match == null ||
                !match.TryCompleteTerritoryPaintOnServer(
                    _finalLeaderboard))
            {
                return;
            }

            _completionReported = true;
            _matchActive.Value = false;
            _phase.Value =
                (byte)NetworkTerritoryPaintPhase.Complete;
            _phaseEndsAt.Value = 0d;
            ClearInputsOnServer();
            FreezeBoardAvatarsOnServer();
        }

        private void SimulateToOnServer(double targetElapsed)
        {
            if (_surface == null ||
                Phase != NetworkTerritoryPaintPhase.Running)
            {
                return;
            }

            targetElapsed = Math.Max(
                _simulatedElapsed,
                Math.Min(TerritoryPaintRules.RoundSeconds, targetElapsed));
            while (_simulatedElapsed + 0.000000001d < targetElapsed)
            {
                var next = Math.Min(
                    targetElapsed,
                    _simulatedElapsed +
                    MaximumSimulationStepSeconds);
                SimulateStepOnServer((float)(next - _simulatedElapsed));
                _simulatedElapsed = next;
            }

            SyncPaintOnServer();
        }

        private void SimulateStepOnServer(float deltaSeconds)
        {
            for (var slot = 0;
                 slot < TerritoryPaintRules.PlayerCount;
                 slot++)
            {
                var input =
                    Vector2.ClampMagnitude(_serverInputs[slot], 1f);
                if (input.sqrMagnitude <= 0.0001f)
                {
                    continue;
                }

                var origin = _playerPositions[slot];
                var next = ResolvePlayerMovement(
                    slot,
                    origin,
                    input * MoveSpeed * deltaSeconds);
                _playerPositions[slot] = next;
                _playerFacings[slot] = input.normalized;
                _surface.PaintStroke(
                    slot,
                    origin,
                    next,
                    BrushRadius);
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
            var minimumDistanceSquared =
                minimumDistance * minimumDistance;
            for (var other = 0;
                 other < TerritoryPaintRules.PlayerCount;
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

        private static Vector2 ClampPlayerPosition(Vector2 position)
        {
            var limit = ArenaHalfExtent - PlayerCollisionRadius;
            position.x = Mathf.Clamp(
                position.x,
                ArenaCenterX - limit,
                ArenaCenterX + limit);
            position.y = Mathf.Clamp(position.y, -limit, limit);
            return position;
        }

        private void SyncPlayerSnapshotsOnServer()
        {
            EnsurePlayerSnapshotCountOnServer();
            for (var slot = 0;
                 slot < TerritoryPaintRules.PlayerCount;
                 slot++)
            {
                var snapshot = new TerritoryPaintPlayerSnapshot
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

        private void SyncPaintOnServer()
        {
            if (_surface == null)
            {
                return;
            }

            Array.Clear(_paintedScratch, 0, _paintedScratch.Length);
            Array.Clear(_ownerLowScratch, 0, _ownerLowScratch.Length);
            Array.Clear(_ownerHighScratch, 0, _ownerHighScratch.Length);
            for (var cellIndex = 0;
                 cellIndex < _surface.CellCount;
                 cellIndex++)
            {
                var owner = _surface.GetOwnerAtIndex(cellIndex);
                if (owner == TerritoryPaintRules.UnpaintedOwner)
                {
                    continue;
                }

                var word = cellIndex / BitsPerWord;
                var mask = 1UL << (cellIndex % BitsPerWord);
                _paintedScratch[word] |= mask;
                if ((owner & 1) != 0)
                {
                    _ownerLowScratch[word] |= mask;
                }
                if ((owner & 2) != 0)
                {
                    _ownerHighScratch[word] |= mask;
                }
            }

            EnsurePaintWordCountOnServer();
            var changed = false;
            for (var word = 0; word < PaintWordCount; word++)
            {
                changed |= SetWordIfChanged(
                    _paintedWords,
                    word,
                    _paintedScratch[word]);
                changed |= SetWordIfChanged(
                    _ownerLowWords,
                    word,
                    _ownerLowScratch[word]);
                changed |= SetWordIfChanged(
                    _ownerHighWords,
                    word,
                    _ownerHighScratch[word]);
            }

            ulong scores = 0UL;
            for (var slot = 0;
                 slot < TerritoryPaintRules.PlayerCount;
                 slot++)
            {
                scores |=
                    (ulong)_surface.GetNormalizedScore(slot) <<
                    (slot * 16);
            }
            if (_scores.Value != scores)
            {
                _scores.Value = scores;
            }

            if (changed)
            {
                unchecked
                {
                    _paintRevision.Value =
                        _paintRevision.Value == uint.MaxValue
                            ? 1U
                            : _paintRevision.Value + 1U;
                }
            }
        }

        private bool ValidateInputEnvelope(
            int roundNumber,
            uint inputEpoch)
        {
            return _matchActive.Value && !_paused.Value &&
                   Phase == NetworkTerritoryPaintPhase.Running &&
                   roundNumber == 1 &&
                   inputEpoch != 0U &&
                   inputEpoch == _inputEpoch.Value &&
                   Remaining > 0d;
        }

        private bool TryResolveAuthoritativeSlot(
            NetworkPlayerAvatar avatar,
            out int slot)
        {
            slot = avatar != null ? avatar.AssignedSlot : -1;
            return IsSpawned && IsServer && avatar != null &&
                   avatar.IsSpawned &&
                   TerritoryPaintRules.IsValidPlayerSlot(slot);
        }

        private bool TryGetPlayerSnapshot(
            int slot,
            out TerritoryPaintPlayerSnapshot snapshot)
        {
            if (TerritoryPaintRules.IsValidPlayerSlot(slot) &&
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
                 slot < TerritoryPaintRules.PlayerCount;
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
                 slot < TerritoryPaintRules.PlayerCount;
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
                (byte)NetworkTerritoryPaintPhase.Inactive;
            _phaseEndsAt.Value = 0d;
            _pausedPhaseRemaining.Value = 0d;
            _inputEpoch.Value = 0U;
            _scores.Value = 0UL;
            _finalRanks.Value = 0U;
            _paintRevision.Value = 0U;
            ClearInputsOnServer();

            for (var slot = 0;
                 slot < TerritoryPaintRules.PlayerCount;
                 slot++)
            {
                _playerPositions[slot] = GetPlayerStartPosition(slot);
                _playerFacings[slot] = GetStartFacing(slot);
            }
            EnsurePlayerSnapshotCountOnServer();
            SyncPlayerSnapshotsOnServer();

            _paintedWords.Clear();
            _ownerLowWords.Clear();
            _ownerHighWords.Clear();
            EnsurePaintWordCountOnServer();
        }

        private void EnsurePlayerSnapshotCountOnServer()
        {
            while (_playerSnapshots.Count <
                   TerritoryPaintRules.PlayerCount)
            {
                _playerSnapshots.Add(default);
            }
            while (_playerSnapshots.Count >
                   TerritoryPaintRules.PlayerCount)
            {
                _playerSnapshots.RemoveAt(
                    _playerSnapshots.Count - 1);
            }
        }

        private void EnsurePaintWordCountOnServer()
        {
            EnsureWordCount(_paintedWords);
            EnsureWordCount(_ownerLowWords);
            EnsureWordCount(_ownerHighWords);
        }

        private static void EnsureWordCount(NetworkList<ulong> words)
        {
            while (words.Count < PaintWordCount)
            {
                words.Add(0UL);
            }
            while (words.Count > PaintWordCount)
            {
                words.RemoveAt(words.Count - 1);
            }
        }

        private static bool SetWordIfChanged(
            NetworkList<ulong> words,
            int index,
            ulong value)
        {
            if (words[index] == value)
            {
                return false;
            }

            words[index] = value;
            return true;
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
            for (var slot = 0; slot < _serverInputs.Length; slot++)
            {
                _serverInputs[slot] = Vector2.zero;
            }
        }

        private void ClearLocalRuntime()
        {
            _surface = null;
            _finalLeaderboard = null;
            _runningStartedAt = 0d;
            _simulatedElapsed = 0d;
            _pausedRunningElapsed = 0d;
            _completionReported = false;
            ClearInputsOnServer();
            Array.Clear(_avatars, 0, _avatars.Length);
        }

        private double GetRunningElapsed(double now)
        {
            return Math.Max(
                0d,
                Math.Min(
                    TerritoryPaintRules.RoundSeconds,
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

        private string BuildScoreLog()
        {
            return "P1 " + GetScore(0) +
                   ", P2 " + GetScore(1) +
                   ", P3 " + GetScore(2) +
                   ", P4 " + GetScore(3);
        }

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value);
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

        private static NetworkList<ulong> CreateWordList()
        {
            return new NetworkList<ulong>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }
    }
}
