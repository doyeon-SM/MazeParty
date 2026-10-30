using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>Shared contact and playback for board and arena combat.</summary>
    public static class CombatHitVfx
    {
        public static void ResolveContact(RaycastHit contact, Vector3 origin,
            Vector3 direction, float range, out Vector3 point, out Vector3 normal)
        {
            // Resolve before damage can disable a victim's collider. Overlapping
            // sphere casts do not supply a reliable point or normal.
            point = contact.distance > 0.0001f
                ? contact.point : contact.collider.ClosestPoint(origin);
            if (contact.distance <= 0.0001f && (point - origin).sqrMagnitude < 0.0001f)
                point = contact.collider.ClosestPoint(origin - direction *
                    (contact.collider.bounds.size.magnitude + range));
            normal = contact.distance > 0.0001f && contact.normal.sqrMagnitude > 0.0001f
                ? contact.normal.normalized : -direction;
        }

        public static void Play(GameObject prefab, Vector3 point, Vector3 normal)
        {
            if (prefab == null) return;
            var outward = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
            OneShotVfxPool.Play(prefab, point + outward * 0.025f,
                Quaternion.LookRotation(outward), 0.7f);
        }
    }
}
