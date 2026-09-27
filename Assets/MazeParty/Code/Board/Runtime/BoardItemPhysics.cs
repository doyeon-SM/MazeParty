using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public static class BoardItemLifecycleRules
    {
        public const float MineSurfaceOffset = 0.06f;
        public const float MineTileTolerance = BoardTile.HalfRoomSize * 0.05f;
        public const float MineMaximumPlaneDistance = 1f;

        public static bool CanSimulateProjectile(BoardFlowState state)
        {
            return state == BoardFlowState.Action ||
                   state == BoardFlowState.AscendingResolve;
        }

        public static bool CanSimulateMine(BoardFlowState state)
        {
            return state == BoardFlowState.Action;
        }

        /// <summary>
        /// Limits a mine simulation step to the part of the frame that still
        /// belongs to the Action phase. The remaining time is sampled at the
        /// logical start of the frame and also respects an earlier authoritative
        /// end such as the all-players-arrived grace deadline, so a long host
        /// frame cannot arm or trigger a mine after Action has ended.
        /// </summary>
        public static float GetMineSimulationDelta(
            BoardFlowStateMachine flow,
            double synchronizedNow,
            float frameDeltaSeconds,
            double earlyActionEndDeadline = 0d)
        {
            if (flow == null || flow.State != BoardFlowState.Action ||
                flow.IsPaused || double.IsNaN(synchronizedNow) ||
                double.IsInfinity(synchronizedNow) ||
                float.IsNaN(frameDeltaSeconds) ||
                float.IsInfinity(frameDeltaSeconds) ||
                frameDeltaSeconds <= 0f)
            {
                return 0f;
            }

            var logicalFrameStart =
                flow.ToFlowTime(synchronizedNow) - frameDeltaSeconds;
            var logicalRemainingAtFrameStart =
                flow.ActionClock.GetActionRemaining(logicalFrameStart);
            if (earlyActionEndDeadline > 0d)
            {
                logicalRemainingAtFrameStart = Math.Min(
                    logicalRemainingAtFrameStart,
                    Math.Max(
                        0d,
                        earlyActionEndDeadline -
                        (synchronizedNow - frameDeltaSeconds)));
            }

            return (float)Math.Min(
                frameDeltaSeconds,
                logicalRemainingAtFrameStart);
        }

        public static bool HasReachedProjectileLifetime(
            float elapsedSeconds,
            float lifetimeSeconds)
        {
            return elapsedSeconds >= lifetimeSeconds;
        }

        /// <summary>
        /// Converts an accepted board surface hit into the exact persistent
        /// mine position. Placement and recovery deliberately share this
        /// contract so a mine that can be planted can always be restored.
        /// </summary>
        public static bool TryGetMinePlacementPosition(
            BoardTopology topology,
            Vector3 surfacePoint,
            Vector3 surfaceNormal,
            out Vector3 position)
        {
            position = default;
            if (!IsFinite(surfacePoint) ||
                !IsFinite(surfaceNormal) ||
                surfaceNormal.y < 0.5f)
            {
                return false;
            }

            var candidate = surfacePoint + Vector3.up * MineSurfaceOffset;
            if (!IsValidMinePosition(topology, candidate))
            {
                return false;
            }

            position = candidate;
            return true;
        }

        public static bool IsValidMinePosition(
            BoardTopology topology,
            Vector3 position)
        {
            if (topology == null || !IsFinite(position))
            {
                return false;
            }

            var tile = topology.FindContainingTile(
                position,
                MineTileTolerance);
            if (tile == null)
            {
                return false;
            }

            var tileUp = tile.transform.up.normalized;
            return tileUp.sqrMagnitude > 0.5f &&
                   Mathf.Abs(Vector3.Dot(
                       position - tile.WorldCenter,
                       tileUp)) <= MineMaximumPlaneDistance;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }

    public static class BoardItemPhysics
    {
        public static bool IsBoundary(Collider collider) =>
            collider.GetComponentInParent<BoardBoundaryWallVisual>() != null;

        // Board barriers constrain movement only. Other solid geometry blocks items.
        public static bool Cast(Vector3 origin, Vector3 direction, float distance,
            GameObject source, out RaycastHit result, float radius = 0f, bool ignorePlayers = false)
        {
            var hits = radius > 0f
                ? Physics.SphereCastAll(origin, radius, direction, distance, ~0, QueryTriggerInteraction.Collide)
                : Physics.RaycastAll(origin, direction, distance, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var c = hit.collider;
                if (c == null || IsBoundary(c) ||
                    (source != null && c.transform.IsChildOf(source.transform))) continue;
                var player = c.GetComponentInParent<PlayerHitZoneOwner>();
                if (player != null)
                {
                    if (ignorePlayers) continue;
                    result = hit;
                    return true;
                }
                if (c.isTrigger) continue;
                result = hit;
                return true;
            }
            result = default;
            return false;
        }

        public static bool HasBlastLineOfSight(Vector3 origin, Vector3 target)
        {
            var delta = target - origin;
            return delta.sqrMagnitude < .0001f ||
                !Cast(origin, delta.normalized, delta.magnitude, null, out _, 0f, true);
        }
    }
}
