using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.ArenaCombat;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum NetworkArenaCombatPhase : byte
    {
        Inactive,
        Countdown,
        Playing,
        Complete
    }

    /// <summary>
    /// Server-owned free-for-all combat match. The regular network avatars
    /// remain the authoritative moving actors, including their hit colliders,
    /// first-person look, punches, temporary HP and knockback.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkArenaCombatState : NetworkBehaviour
    {
        private readonly NetworkVariable<bool> _matchActive =
            CreateBoolVariable();
        private readonly NetworkVariable<bool> _paused =
            CreateBoolVariable();
        private readonly NetworkVariable<byte> _phase =
            CreateByteVariable();
        private readonly NetworkVariable<double> _phaseEndsAt =
            CreateDoubleVariable();
        private readonly NetworkVariable<double> _pausedPhaseRemaining =
            CreateDoubleVariable();
        private readonly NetworkVariable<uint> _inputEpoch =
            CreateUIntVariable();
        private readonly NetworkVariable<byte> _eliminatedMask =
            CreateByteVariable();
        private readonly NetworkVariable<uint> _finalRanks =
            CreateUIntVariable();
        private readonly NetworkVariable<ulong> _hitSequences =
            CreateULongVariable();

        private readonly NetworkPlayerAvatar[] _avatars =
            new NetworkPlayerAvatar[ArenaCombatRules.PlayerCount];
        private readonly Vector3[] _savedPositions =
            new Vector3[ArenaCombatRules.PlayerCount];
        private readonly Quaternion[] _savedRotations =
            new Quaternion[ArenaCombatRules.PlayerCount];
        private readonly double[] _eliminatedAt =
            new double[ArenaCombatRules.PlayerCount];
        private readonly double[] _nextPunchAllowedAt =
            new double[ArenaCombatRules.PlayerCount];
        private readonly double[] _pausedPunchCooldown =
            new double[ArenaCombatRules.PlayerCount];

        private bool _completionReported;
        private bool _hasSavedPoses;

        public static NetworkArenaCombatState Instance { get; private set; }

        public NetworkArenaCombatPhase Phase =>
            (NetworkArenaCombatPhase)_phase.Value;
        public bool IsMatchActive => _matchActive.Value;
        public bool IsPaused => _paused.Value;
        public uint InputEpoch => _inputEpoch.Value;
        public double Remaining =>
            Phase == NetworkArenaCombatPhase.Inactive ||
            (!_matchActive.Value &&
             Phase == NetworkArenaCombatPhase.Complete)
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
                    "More than one NetworkArenaCombatState is spawned.");
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

            var now = ServerNow;
            switch (Phase)
            {
                case NetworkArenaCombatPhase.Countdown:
                    if (HasReachedDeadline(now))
                    {
                        BeginPlayingOnServer(now);
                    }
                    break;
                case NetworkArenaCombatPhase.Playing:
                    if (ArenaCombatRules.ShouldEndMatch(
                            Math.Max(0d, now -
                                (_phaseEndsAt.Value -
                                 ArenaCombatRules.MatchDurationSeconds)),
                            CountSurvivors()))
                    {
                        CompletePlayingOnServer(now);
                    }
                    break;
                case NetworkArenaCombatPhase.Complete:
                    if (HasReachedDeadline(now))
                    {
                        CompleteMatchOnServer();
                    }
                    break;
            }
        }

        public void BeginMatchOnServer(ulong matchSeed)
        {
            if (!IsSpawned || !IsServer || _matchActive.Value ||
                !TryCacheAllAvatarsOnServer())
            {
                return;
            }

            // The arena has fixed spawn slots; the seed is intentionally not
            // used to affect combat mechanics or spawn placement.
            _ = matchSeed;
            ClearTimingRuntime();
            ResetReplicatedStateOnServer();
            var now = ServerNow;
            for (var slot = 0; slot < ArenaCombatRules.PlayerCount;
                 slot++)
            {
                var avatar = _avatars[slot];
                _savedPositions[slot] = avatar.transform.position;
                _savedRotations[slot] = avatar.transform.rotation;
                _eliminatedAt[slot] = double.NaN;
                _nextPunchAllowedAt[slot] = now;
                var spawn = ArenaCombatRules.GetSpawnPosition(slot);
                var facing = new Vector3(
                    ArenaCombatRules.ArenaCenterX - spawn.x,
                    0f,
                    -spawn.z);
                avatar.BeginArenaCombatOnServer(
                    spawn,
                    Quaternion.LookRotation(facing.normalized, Vector3.up));
            }

            _hasSavedPoses = true;
            _completionReported = false;
            _matchActive.Value = true;
            _phase.Value = (byte)NetworkArenaCombatPhase.Countdown;
            _phaseEndsAt.Value = now + ArenaCombatRules.CountdownSeconds;
            Debug.Log("[Arena Combat] Four-player match started.");
        }

        public bool CanAcceptInputForSlot(int slot)
        {
            var avatar = GetAvatar(slot);
            return ArenaCombatRules.IsValidSlot(slot) &&
                _matchActive.Value && !_paused.Value &&
                Phase == NetworkArenaCombatPhase.Playing &&
                Remaining > 0d && !IsEliminated(slot) &&
                avatar != null && avatar.IsSpawned &&
                avatar.IsCombatAlive;
        }

        public bool TryPunchOnServer(
            NetworkPlayerAvatar attacker,
            Vector3 claimedOrigin,
            Vector3 claimedDirection)
        {
            if (!TryResolveAuthoritativeSlot(attacker, out var slot) ||
                !CanAcceptInputForSlot(slot) ||
                !IsFinite(claimedOrigin) ||
                !IsFinite(claimedDirection) ||
                claimedDirection.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            var now = ServerNow;
            if (now >= _phaseEndsAt.Value)
            {
                CompletePlayingOnServer(now);
                return false;
            }

            if (now < _nextPunchAllowedAt[slot])
            {
                return false;
            }

            var authoritativeOrigin = attacker.EyePivot != null
                ? attacker.EyePivot.position
                : attacker.transform.position + Vector3.up * 0.75f;
            if (Vector3.Distance(
                    authoritativeOrigin, claimedOrigin) > 1.5f)
            {
                return false;
            }

            var direction = attacker.EyePivot != null
                ? attacker.EyePivot.forward.normalized
                : attacker.transform.forward.normalized;
            if (Vector3.Dot(
                    direction, claimedDirection.normalized) < 0.94f)
            {
                return false;
            }

            _nextPunchAllowedAt[slot] =
                now + BoardCombatRules.PunchCooldownSeconds;
            attacker.PresentPunchOnServer();
            var hits = Physics.SphereCastAll(
                authoritativeOrigin,
                BoardCombatRules.PunchRadius,
                direction,
                BoardCombatRules.PunchRange,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            NetworkPlayerAvatar target = null;
            var closestDistance = float.MaxValue;
            for (var index = 0; index < hits.Length; index++)
            {
                var candidate = hits[index].collider != null
                    ? hits[index].collider.GetComponentInParent<NetworkPlayerAvatar>()
                    : null;
                if (candidate == null || candidate == attacker ||
                    !TryResolveAuthoritativeSlot(candidate, out var targetSlot) ||
                    !CanAcceptInputForSlot(targetSlot) ||
                    hits[index].distance >= closestDistance)
                {
                    continue;
                }

                target = candidate;
                closestDistance = hits[index].distance;
            }

            if (target == null)
            {
                return true;
            }

            var knockbackDirection = direction;
            knockbackDirection.y = 0f;
            if (knockbackDirection.sqrMagnitude < 0.0001f)
            {
                knockbackDirection = attacker.transform.forward;
                knockbackDirection.y = 0f;
            }
            knockbackDirection.Normalize();

            var eliminated = target.ApplyCombatPunchOnServer(
                attacker,
                knockbackDirection *
                BoardCombatRules.PunchKnockbackSpeed);
            IncrementHitSequenceOnServer(target.AssignedSlot);
            if (!eliminated)
            {
                return true;
            }

            var eliminatedSlot = target.AssignedSlot;
            _eliminatedAt[eliminatedSlot] = now;
            _eliminatedMask.Value = (byte)(
                _eliminatedMask.Value | (1 << eliminatedSlot));
            if (CountSurvivors() <= 1)
            {
                CompletePlayingOnServer(now);
            }
            return true;
        }

        public bool IsEliminated(int slot)
        {
            return ArenaCombatRules.IsValidSlot(slot) &&
                (_eliminatedMask.Value & (1 << slot)) != 0;
        }

        public int GetFinalRank(int slot)
        {
            return ArenaCombatRules.IsValidSlot(slot)
                ? (int)((_finalRanks.Value >> (slot * 8)) & 0xffU)
                : 0;
        }

        public int GetHitSequence(int slot)
        {
            return ArenaCombatRules.IsValidSlot(slot)
                ? (int)((_hitSequences.Value >> (slot * 16)) & 0xffffUL)
                : 0;
        }

        public Vector2 GetPlayerPosition(int slot)
        {
            if (!ArenaCombatRules.IsValidSlot(slot))
            {
                return Vector2.zero;
            }

            var avatar = GetAvatar(slot);
            return avatar != null
                ? new Vector2(
                    avatar.transform.position.x,
                    avatar.transform.position.z)
                : new Vector2(
                    ArenaCombatRules.GetSpawnPosition(slot).x,
                    ArenaCombatRules.GetSpawnPosition(slot).z);
        }

        public Vector2 GetPlayerFacing(int slot)
        {
            var avatar = GetAvatar(slot);
            if (avatar == null)
            {
                return Vector2.up;
            }

            var facing = avatar.transform.forward;
            return new Vector2(facing.x, facing.z).normalized;
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
            for (var slot = 0; slot < ArenaCombatRules.PlayerCount;
                 slot++)
            {
                _pausedPunchCooldown[slot] =
                    Math.Max(0d, _nextPunchAllowedAt[slot] - now);
                var avatar = _avatars[slot];
                if (avatar != null)
                {
                    avatar.StopServerInputOnServer();
                }
            }
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
            for (var slot = 0; slot < ArenaCombatRules.PlayerCount;
                 slot++)
            {
                _nextPunchAllowedAt[slot] =
                    now + _pausedPunchCooldown[slot];
                _pausedPunchCooldown[slot] = 0d;
            }
            _paused.Value = false;
            AdvanceInputEpochOnServer();
        }

        public void RestoreAvatarForReconnectOnServer(
            NetworkPlayerAvatar avatar)
        {
            if (!IsSpawned || !IsServer || !_matchActive.Value ||
                avatar == null || !avatar.IsSpawned ||
                !ArenaCombatRules.IsValidSlot(avatar.AssignedSlot))
            {
                return;
            }

            var slot = avatar.AssignedSlot;
            var match = NetworkMatchState.Instance;
            if (match == null || match.GetAvatarForSlot(slot) != avatar)
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

            if (_hasSavedPoses)
            {
                for (var slot = 0;
                     slot < ArenaCombatRules.PlayerCount;
                     slot++)
                {
                    var avatar = GetAvatar(slot);
                    if (avatar != null)
                    {
                        avatar.EndArenaCombatOnServer(
                            _savedPositions[slot],
                            _savedRotations[slot]);
                    }
                }
            }

            ResetReplicatedStateOnServer();
            ClearLocalRuntime();
        }

        private void BeginPlayingOnServer(double now)
        {
            _phase.Value = (byte)NetworkArenaCombatPhase.Playing;
            _phaseEndsAt.Value =
                now + ArenaCombatRules.MatchDurationSeconds;
            AdvanceInputEpochOnServer();
            for (var slot = 0; slot < ArenaCombatRules.PlayerCount;
                 slot++)
            {
                _nextPunchAllowedAt[slot] = now;
            }
        }

        private void CompletePlayingOnServer(double now)
        {
            if (Phase != NetworkArenaCombatPhase.Playing)
            {
                return;
            }

            var entries =
                new ArenaCombatRankingEntry[ArenaCombatRules.PlayerCount];
            for (var slot = 0; slot < entries.Length; slot++)
            {
                var avatar = GetAvatar(slot);
                avatar?.StopServerInputOnServer();
                entries[slot] = new ArenaCombatRankingEntry(
                    slot,
                    avatar != null ? avatar.CombatHealth : 0,
                    _eliminatedAt[slot]);
            }

            var ranks = ArenaCombatRules.ResolveRanks(entries);
            uint packed = 0U;
            for (var slot = 0; slot < ranks.Length; slot++)
            {
                packed |= (uint)ranks[slot] << (slot * 8);
            }
            _finalRanks.Value = packed;
            _phase.Value = (byte)NetworkArenaCombatPhase.Complete;
            _phaseEndsAt.Value = now + ArenaCombatRules.ResultSeconds;
            AdvanceInputEpochOnServer();
            Debug.Log("[Arena Combat] Final placements ready.");
        }

        private void CompleteMatchOnServer()
        {
            if (_completionReported ||
                Phase != NetworkArenaCombatPhase.Complete)
            {
                return;
            }

            var ranks = new int[ArenaCombatRules.PlayerCount];
            for (var slot = 0; slot < ranks.Length; slot++)
            {
                ranks[slot] = GetFinalRank(slot);
            }

            var match = NetworkMatchState.Instance;
            if (match == null ||
                !match.TryCompleteArenaCombatOnServer(ranks))
            {
                return;
            }

            _completionReported = true;
            _matchActive.Value = false;
            _phaseEndsAt.Value = 0d;
            for (var slot = 0; slot < ArenaCombatRules.PlayerCount;
                 slot++)
            {
                var avatar = _avatars[slot];
                if (avatar != null)
                {
                    avatar.StopServerInputOnServer();
                }
            }
            Debug.Log("[Arena Combat] Match complete.");
        }

        private bool TryCacheAllAvatarsOnServer()
        {
            var match = NetworkMatchState.Instance;
            if (match == null)
            {
                return false;
            }

            for (var slot = 0; slot < ArenaCombatRules.PlayerCount;
                 slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                if (avatar == null || !avatar.IsSpawned)
                {
                    return false;
                }
                _avatars[slot] = avatar;
            }
            return true;
        }

        private bool TryResolveAuthoritativeSlot(
            NetworkPlayerAvatar avatar,
            out int slot)
        {
            slot = avatar != null ? avatar.AssignedSlot : -1;
            var match = NetworkMatchState.Instance;
            return IsSpawned && IsServer && avatar != null &&
                avatar.IsSpawned &&
                ArenaCombatRules.IsValidSlot(slot) &&
                match != null && match.GetAvatarForSlot(slot) == avatar &&
                _avatars[slot] == avatar;
        }

        private NetworkPlayerAvatar GetAvatar(int slot)
        {
            if (!ArenaCombatRules.IsValidSlot(slot))
            {
                return null;
            }

            var match = NetworkMatchState.Instance;
            var current = match != null
                ? match.GetAvatarForSlot(slot)
                : null;
            var cached = _avatars[slot];
            return current != null
                ? current
                : cached != null ? cached : null;
        }

        private int CountSurvivors()
        {
            var survivors = 0;
            for (var slot = 0; slot < ArenaCombatRules.PlayerCount;
                 slot++)
            {
                if (!IsEliminated(slot))
                {
                    survivors++;
                }
            }
            return survivors;
        }

        private void IncrementHitSequenceOnServer(int slot)
        {
            if (!ArenaCombatRules.IsValidSlot(slot))
            {
                return;
            }

            var shift = slot * 16;
            var mask = 0xffffUL << shift;
            var next = ((
                _hitSequences.Value >> shift) + 1UL) & 0xffffUL;
            _hitSequences.Value =
                (_hitSequences.Value & ~mask) | (next << shift);
        }

        private bool HasReachedDeadline(double now)
        {
            return _phaseEndsAt.Value > 0d &&
                now >= _phaseEndsAt.Value;
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

        private void ResetReplicatedStateOnServer()
        {
            _matchActive.Value = false;
            _paused.Value = false;
            _phase.Value = (byte)NetworkArenaCombatPhase.Inactive;
            _phaseEndsAt.Value = 0d;
            _pausedPhaseRemaining.Value = 0d;
            _inputEpoch.Value = 0U;
            _eliminatedMask.Value = 0;
            _finalRanks.Value = 0U;
            _hitSequences.Value = 0UL;
        }

        private void ClearTimingRuntime()
        {
            _completionReported = false;
            _hasSavedPoses = false;
            Array.Clear(_savedPositions, 0, _savedPositions.Length);
            Array.Clear(_savedRotations, 0, _savedRotations.Length);
            Array.Clear(_eliminatedAt, 0, _eliminatedAt.Length);
            Array.Clear(
                _nextPunchAllowedAt,
                0,
                _nextPunchAllowedAt.Length);
            Array.Clear(
                _pausedPunchCooldown,
                0,
                _pausedPunchCooldown.Length);
        }

        private void ClearLocalRuntime()
        {
            ClearTimingRuntime();
            Array.Clear(_avatars, 0, _avatars.Length);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) &&
                !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) &&
                !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) &&
                !float.IsInfinity(value.z);
        }

        private static NetworkVariable<bool> CreateBoolVariable()
        {
            return new NetworkVariable<bool>(
                false,
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

        private static NetworkVariable<double> CreateDoubleVariable()
        {
            return new NetworkVariable<double>(
                0d,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }
    }
}
