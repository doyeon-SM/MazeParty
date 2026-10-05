#if UNITY_EDITOR || DEBUG
using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Development-player observations used by the opt-in multiplayer item QA.
    /// No method in this file is compiled into a non-development player.
    /// </summary>
    public sealed partial class NetworkPlayerAvatar
    {
        private uint _developmentTrackedItemRequestId;
        private Vector3 _developmentTrackedItemOrigin;
        private Vector3 _developmentTrackedItemDirection;

        public bool DevelopmentGrenadeUsePending =>
            IsOwner && _grenadeRangeUseState.IsPending;

        public int DevelopmentLocalMineViewCount =>
            _mineViews.Count;

        public int DevelopmentActiveItemLineCount
        {
            get
            {
                var lines = GetComponentsInChildren<LineRenderer>(true);
                var active = 0;
                for (var index = 0; index < lines.Length; index++)
                {
                    var line = lines[index];
                    if (line != null && line.gameObject.activeInHierarchy &&
                        line.enabled)
                    {
                        active++;
                    }
                }

                return active;
            }
        }

        /// <summary>
        /// Starts the same local pending state used by real grenade input, but
        /// leaves transmission to a separate call so QA can capture the hidden
        /// pending presentation before the Relay round trip completes.
        /// </summary>
        public bool DevelopmentBeginTrackedItemUse()
        {
            if (!IsOwner || _developmentTrackedItemRequestId != 0u ||
                !TryGetDevelopmentOwnerAim(
                    out _developmentTrackedItemOrigin,
                    out _developmentTrackedItemDirection) ||
                !TryBeginLocalItemUseRequest(
                    LocalEquippedItem,
                    out _developmentTrackedItemRequestId))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Submits a request begun by DevelopmentBeginTrackedItemUse. An
        /// invalid origin is useful for proving that an owner UI recovers after
        /// an ordinary server rejection without bypassing the owner RPC.
        /// </summary>
        public bool DevelopmentSubmitTrackedItemUse(bool invalidOrigin)
        {
            if (!IsOwner || _developmentTrackedItemRequestId == 0u)
            {
                return false;
            }

            var requestId = _developmentTrackedItemRequestId;
            _developmentTrackedItemRequestId = 0u;
            var origin = invalidOrigin
                ? _developmentTrackedItemOrigin + Vector3.up * 100f
                : _developmentTrackedItemOrigin;
            UseSelectedItemRpc(
                origin,
                _developmentTrackedItemDirection,
                requestId);
            return true;
        }

        public bool DevelopmentSetCloakedOnServer(bool cloaked)
        {
            if (!IsServer || !IsSpawned)
            {
                return false;
            }

            _cloaked.Value = cloaked;
            return true;
        }

        public bool DevelopmentClearItemProtectionOnServer()
        {
            if (!IsServer || !IsSpawned)
            {
                return false;
            }

            _personalProtectionEndsAt.Value = 0d;
            _pausedPersonalProtectionRemaining.Value = 0d;
            return true;
        }
    }

    public sealed partial class NetworkMatchState
    {
        private int _developmentPresentedShotCount;
        private Vector3 _developmentLastShotOrigin;
        private Vector3 _developmentLastShotEnd;
        private PrototypeItemId _developmentLastShotItem;
        private bool _developmentLastShotHit;
        private bool _developmentLastShotWasProjectileLike;
        private readonly List<Vector3> _developmentGrenadeTrajectory =
            new List<Vector3>();
        private readonly List<float> _developmentGrenadeSampleTimes =
            new List<float>();
        private float _developmentGrenadeRangeLimit;
        private float _developmentGrenadePlannedDistance;
        private float _developmentGrenadeFlightDuration;

        public event Action<PrototypeItemId, Vector3, Vector3, bool>
            DevelopmentShotPresented;

        public int DevelopmentPresentedShotCount =>
            _developmentPresentedShotCount;

        public int DevelopmentAuthoritativeGrenadeCount =>
            IsServer ? _boardGrenades.Count : 0;

        public int DevelopmentLocalGrenadeViewCount =>
            _grenadeViews.Count;

        public int DevelopmentAuthoritativeMineCount =>
            IsServer ? _boardMines.Count : 0;

        public Vector3 DevelopmentLastShotOrigin =>
            _developmentLastShotOrigin;

        public Vector3 DevelopmentLastShotEnd =>
            _developmentLastShotEnd;

        public PrototypeItemId DevelopmentLastShotItem =>
            _developmentLastShotItem;

        public bool DevelopmentLastShotHit =>
            _developmentLastShotHit;

        public bool DevelopmentLastShotWasProjectileLike =>
            _developmentLastShotWasProjectileLike;

        internal void DevelopmentBeginGrenadeObservation(
            Vector3 origin,
            Vector3 target,
            float rangeLimit,
            float flightDuration)
        {
            _developmentGrenadeTrajectory.Clear();
            _developmentGrenadeTrajectory.Add(origin);
            _developmentGrenadeSampleTimes.Clear();
            _developmentGrenadeSampleTimes.Add(0f);
            _developmentGrenadeRangeLimit = rangeLimit;
            _developmentGrenadePlannedDistance = Vector3.ProjectOnPlane(
                target - origin,
                Vector3.up).magnitude;
            _developmentGrenadeFlightDuration = flightDuration;
        }

        internal void DevelopmentRecordGrenadePosition(
            Vector3 position,
            float elapsedSeconds)
        {
            _developmentGrenadeTrajectory.Add(position);
            _developmentGrenadeSampleTimes.Add(elapsedSeconds);
        }

        public bool DevelopmentCopyGrenadeTrajectory(
            List<Vector3> destination,
            out float rangeLimit,
            out float plannedDistance,
            out float flightDuration,
            out float observedDuration)
        {
            rangeLimit = _developmentGrenadeRangeLimit;
            plannedDistance = _developmentGrenadePlannedDistance;
            flightDuration = _developmentGrenadeFlightDuration;
            observedDuration = _developmentGrenadeSampleTimes.Count > 0
                ? _developmentGrenadeSampleTimes[
                    _developmentGrenadeSampleTimes.Count - 1]
                : 0f;
            if (!IsServer || destination == null ||
                _developmentGrenadeTrajectory.Count == 0 ||
                _developmentGrenadeTrajectory.Count !=
                    _developmentGrenadeSampleTimes.Count)
            {
                return false;
            }

            destination.Clear();
            destination.AddRange(_developmentGrenadeTrajectory);
            return true;
        }

        internal void DevelopmentRecordPresentedShot(
            Vector3 origin,
            Vector3 end,
            PrototypeItemId item,
            bool didHit,
            bool projectileLike)
        {
            _developmentPresentedShotCount++;
            _developmentLastShotOrigin = origin;
            _developmentLastShotEnd = end;
            _developmentLastShotItem = item;
            _developmentLastShotHit = didHit;
            _developmentLastShotWasProjectileLike = projectileLike;
            DevelopmentShotPresented?.Invoke(item, origin, end, didHit);
        }

        public void DevelopmentResetPresentedShotObservation()
        {
            _developmentPresentedShotCount = 0;
            _developmentLastShotOrigin = Vector3.zero;
            _developmentLastShotEnd = Vector3.zero;
            _developmentLastShotItem = PrototypeItemId.None;
            _developmentLastShotHit = false;
            _developmentLastShotWasProjectileLike = false;
        }

        public bool DevelopmentArrangeNearPair(
            int attackerSlot,
            int targetSlot)
        {
            if (!TryGetDevelopmentPair(
                    attackerSlot,
                    targetSlot,
                    out var attacker,
                    out var target) ||
                !TryGetDevelopmentEncounterTile(
                    attacker,
                    target,
                    out var tile))
            {
                return false;
            }

            var center = tile.GetRecoveryCenter(1f);
            var separation = tile.transform.right.normalized * 0.65f;
            return attacker.DevelopmentRelocateFacingOnServer(
                       tile,
                       center - separation,
                       center + separation) &&
                   target.DevelopmentRelocateFacingOnServer(
                       tile,
                       center + separation,
                       center - separation);
        }

        public bool DevelopmentArrangeFarPair(
            int attackerSlot,
            int targetSlot,
            float minimumDistance)
        {
            if (!TryGetDevelopmentPair(
                    attackerSlot,
                    targetSlot,
                    out var attacker,
                    out var target) ||
                _boardTopology == null)
            {
                return false;
            }

            var safeMinimum = Mathf.Max(0f, minimumDistance);
            var tiles = _boardTopology.Tiles;
            for (var leftIndex = 0; leftIndex < tiles.Count; leftIndex++)
            {
                var left = tiles[leftIndex];
                if (left == null)
                {
                    continue;
                }

                var leftPosition = left.GetRecoveryCenter(1f);
                for (var rightIndex = 0; rightIndex < tiles.Count; rightIndex++)
                {
                    var right = tiles[rightIndex];
                    if (right == null || right == left)
                    {
                        continue;
                    }

                    var rightPosition = right.GetRecoveryCenter(1f);
                    var planarDistance = Vector3.ProjectOnPlane(
                        rightPosition - leftPosition,
                        Vector3.up).magnitude;
                    if (planarDistance <= safeMinimum)
                    {
                        continue;
                    }

                    if (attacker.DevelopmentRelocateFacingOnServer(
                            left,
                            leftPosition,
                            rightPosition) &&
                        target.DevelopmentRelocateFacingOnServer(
                            right,
                            rightPosition,
                            leftPosition))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Finds an authored Forest obstruction between two valid board
        /// positions. Boundary-wall visuals remain ignored by BoardItemPhysics,
        /// so success means the same ordinary-world occlusion used by gameplay.
        /// </summary>
        public bool DevelopmentArrangeOccludedPair(
            int attackerSlot,
            int targetSlot,
            float maximumDistance)
        {
            if (!TryGetDevelopmentPair(
                    attackerSlot,
                    targetSlot,
                    out var attacker,
                    out var target) ||
                _boardTopology == null)
            {
                return false;
            }

            var safeMaximum = Mathf.Max(1f, maximumDistance);
            var tiles = _boardTopology.Tiles;
            for (var leftIndex = 0; leftIndex < tiles.Count; leftIndex++)
            {
                var left = tiles[leftIndex];
                if (left == null)
                {
                    continue;
                }

                var leftPosition = left.GetRecoveryCenter(1f);
                for (var rightIndex = 0; rightIndex < tiles.Count; rightIndex++)
                {
                    var right = tiles[rightIndex];
                    if (right == null || right == left)
                    {
                        continue;
                    }

                    var rightPosition = right.GetRecoveryCenter(1f);
                    var direction = rightPosition - leftPosition;
                    var planarDistance = Vector3.ProjectOnPlane(
                        direction,
                        Vector3.up).magnitude;
                    if (planarDistance < 2f || planarDistance > safeMaximum)
                    {
                        continue;
                    }

                    if (!attacker.DevelopmentRelocateFacingOnServer(
                            left,
                            leftPosition,
                            rightPosition) ||
                        !target.DevelopmentRelocateFacingOnServer(
                            right,
                            rightPosition,
                            leftPosition))
                    {
                        continue;
                    }

                    Physics.SyncTransforms();
                    var origin = attacker.EyePivot != null
                        ? attacker.EyePivot.position
                        : attacker.transform.position +
                          Vector3.up * PlayerAvatarVisual.StandingEyeHeight;
                    var targetPoint = target.transform.position;
                    var ray = targetPoint - origin;
                    if (ray.sqrMagnitude < 0.0001f ||
                        !BoardItemPhysics.Cast(
                            origin,
                            ray.normalized,
                            Mathf.Min(safeMaximum, ray.magnitude),
                            attacker.gameObject,
                            out var hit))
                    {
                        continue;
                    }

                    var hitAvatar = hit.collider != null
                        ? hit.collider.GetComponentInParent<NetworkPlayerAvatar>()
                        : null;
                    if (hitAvatar != target)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public bool DevelopmentArrangeGroundUse(int ownerSlot)
        {
            var owner = GetAvatarForSlot(ownerSlot);
            if (!IsServer || owner == null || !owner.IsSpawned ||
                _boardTopology == null)
            {
                return false;
            }

            var tile = owner.CurrentBoardTileOnServer;
            if (tile == null && _boardTopology.Tiles.Count > 0)
            {
                tile = _boardTopology.Tiles[0];
            }

            if (tile == null)
            {
                return false;
            }

            var center = tile.GetRecoveryCenter(1f);
            var backward = tile.transform.forward.normalized * 1.5f;
            return owner.DevelopmentRelocateFacingOnServer(
                tile,
                center - backward,
                tile.WorldCenter);
        }

        public bool DevelopmentArrangeMineGroundUse(int ownerSlot)
        {
            var owner = GetAvatarForSlot(ownerSlot);
            if (!IsServer || owner == null || !owner.IsSpawned ||
                _boardTopology == null)
            {
                return false;
            }

            var mineRange = PrototypeItemCatalog.Get(
                PrototypeItemId.Mine).Range;
            for (var tileIndex = -1;
                 tileIndex < _boardTopology.Tiles.Count;
                 tileIndex++)
            {
                var tile = tileIndex < 0
                    ? owner.CurrentBoardTileOnServer
                    : _boardTopology.Tiles[tileIndex];
                if (tile == null ||
                    (tileIndex >= 0 && tile == owner.CurrentBoardTileOnServer))
                {
                    continue;
                }

                var center = tile.GetRecoveryCenter(1f);
                var backward = tile.transform.forward.normalized * 1.5f;
                if (!owner.DevelopmentRelocateFacingOnServer(
                        tile,
                        center - backward,
                        tile.WorldCenter))
                {
                    continue;
                }

                Physics.SyncTransforms();
                var origin = owner.EyePivot != null
                    ? owner.EyePivot.position
                    : owner.transform.position +
                      Vector3.up * PlayerAvatarVisual.StandingEyeHeight;
                var direction = tile.WorldCenter - origin;
                if (direction.sqrMagnitude < 0.0001f ||
                    !BoardItemPhysics.Cast(
                        origin,
                        direction.normalized,
                        mineRange,
                        owner.gameObject,
                        out var hit) ||
                    hit.collider == null ||
                    hit.collider.GetComponentInParent<NetworkPlayerAvatar>() !=
                    null ||
                    !BoardItemLifecycleRules.TryGetMinePlacementPosition(
                        _boardTopology,
                        hit.point,
                        hit.normal,
                        out _))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        public bool DevelopmentRelocateSlotToFirstMine(int targetSlot)
        {
            if (!IsServer || _boardMines.Count == 0 ||
                _boardTopology == null)
            {
                return false;
            }

            var target = GetAvatarForSlot(targetSlot);
            var mine = _boardMines[0];
            var tile = _boardTopology.FindContainingTile(mine.Position, 0.25f);
            if (target == null || !target.IsSpawned || tile == null)
            {
                return false;
            }

            var position = tile.GetClosestPointInside(mine.Position, 1f);
            position += tile.transform.up.normalized *
                        Vector3.Dot(
                            tile.GetRecoveryCenter(1f) - position,
                            tile.transform.up.normalized);
            return target.DevelopmentRelocateFacingOnServer(
                tile,
                position,
                position + tile.transform.forward);
        }

        private bool TryGetDevelopmentPair(
            int attackerSlot,
            int targetSlot,
            out NetworkPlayerAvatar attacker,
            out NetworkPlayerAvatar target)
        {
            attacker = GetAvatarForSlot(attackerSlot);
            target = GetAvatarForSlot(targetSlot);
            return IsServer && attacker != null && target != null &&
                   attacker.IsSpawned && target.IsSpawned &&
                   attacker != target;
        }

        private static bool TryGetDevelopmentEncounterTile(
            NetworkPlayerAvatar attacker,
            NetworkPlayerAvatar target,
            out BoardTile tile)
        {
            tile = target.CurrentBoardTileOnServer ??
                   attacker.CurrentBoardTileOnServer;
            return tile != null;
        }
    }
}
#endif
