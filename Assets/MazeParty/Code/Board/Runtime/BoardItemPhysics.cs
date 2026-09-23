using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
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
