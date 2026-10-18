using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>Shared ground and footprint placement for local board presentations.</summary>
    public static class BoardTileWorldPlacement
    {
        public static Vector3 GroundPoint(BoardTile tile, Vector3 position)
        {
            var map = tile.GetComponentInParent<BoardMapRoot>();
            if (map == null) return position;
            foreach (var terrain in map.GetComponentsInChildren<Terrain>())
            {
                if (terrain.terrainData == null) continue;
                var offset = position - terrain.transform.position;
                var size = terrain.terrainData.size;
                if (offset.x < 0 || offset.z < 0 || offset.x > size.x || offset.z > size.z) continue;
                position.y = terrain.SampleHeight(position) + terrain.transform.position.y;
                return position;
            }
            foreach (var hit in Physics.RaycastAll(position + Vector3.up * 1.5f,
                         Vector3.down, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y > .5f && hit.collider.GetComponentInParent<BoardMapRoot>() == map &&
                    hit.collider.GetComponentInParent<BoardTile>() == null)
                { position.y = hit.point.y; break; }
            }
            return position;
        }

        public static Vector3 PeripheralPoint(BoardTile tile, float radius, int variant = 0)
        {
            var center = tile.GetRecoveryCenter();
            var map = tile.GetComponentInParent<BoardMapRoot>();
            var fallback = center;
            var fallbackDistance = -1f;
            var eye = GroundPoint(tile, center) + Vector3.up * 1.5f;
            // Keep the entire model inside the room, clear of wall edges and the central icon.
            for (var index = 0; index < 8; index++)
            {
                var angle = (45f + ((index + variant) % 8) * 45f) * Mathf.Deg2Rad;
                var direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                var candidate = tile.GetClosestPointInside(center + direction * 4f,
                    radius + .25f);
                candidate = GroundPoint(tile, candidate);
                var separation = Vector3.ProjectOnPlane(candidate - center, Vector3.up).magnitude;
                if (separation > fallbackDistance) { fallback = candidate; fallbackDistance = separation; }
                var obstructed = false;
                if (map != null)
                    foreach (var collider in Physics.OverlapCapsule(candidate + Vector3.up * (radius + .1f),
                                 candidate + Vector3.up * Mathf.Max(radius + .1f, 1.6f), radius,
                                 Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        if (!(collider is TerrainCollider) &&
                            collider.GetComponentInParent<BoardMapRoot>() == map &&
                            collider.GetComponentInParent<BoardTile>() == null)
                        { obstructed = true; break; }
                    }
                if (!obstructed && map != null)
                {
                    var aim = candidate + Vector3.up * (radius < 1f ? .4f : 1f);
                    var sight = aim - eye;
                    foreach (var hit in Physics.RaycastAll(eye, sight.normalized, sight.magnitude,
                                 Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        if (hit.collider.GetComponentInParent<BoardMapRoot>() == map &&
                            hit.collider.GetComponentInParent<BoardTile>() == null)
                        { obstructed = true; break; }
                }
                if (!obstructed && separation >= radius + .7f) return candidate;
            }
            return fallback;
        }
    }
}
