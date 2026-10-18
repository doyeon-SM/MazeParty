using UnityEngine;

namespace MazeParty.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class BoardChestVisual : MonoBehaviour
    {
        [SerializeField] private Animator chestAnimator;
        [SerializeField] private Collider interactionCollider;
        [SerializeField] private Transform observationPoint;
        private BoardTile _placedTile;
        private Vector3 _placedTilePosition;
        private bool _initialized;
        private bool _consumed;
        public int ChestIndex { get; private set; }
        public Vector3 ObservationPoint => observationPoint.position;
        public bool HasRequiredReferences => chestAnimator != null &&
            interactionCollider != null && interactionCollider.isTrigger && observationPoint != null;
        public void PlaceOnTile(BoardTile tile)
        {
            if (tile == null || (_placedTile == tile && _placedTilePosition == tile.WorldCenter)) return;
            _placedTile = tile;
            _placedTilePosition = tile.WorldCenter;
            var position = tile.WorldCenter;
            var map = tile.GetComponentInParent<BoardMapRoot>();
            // The authored chest pivot is its floor contact, even when its root
            // scale is edited. Logical tile height is not necessarily terrain height.
            if (map != null)
            {
                var hits = Physics.RaycastAll(position + Vector3.up * 1.5f,
                    Vector3.down, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                var highest = float.NegativeInfinity;
                var terrainHeight = float.NegativeInfinity;
                foreach (var hit in hits)
                {
                    if (hit.normal.y < 0.5f ||
                        hit.collider.GetComponentInParent<BoardMapRoot>() != map ||
                        hit.collider.GetComponentInParent<BoardTile>() != null) continue;
                    highest = Mathf.Max(highest, hit.point.y);
                    if (hit.collider is TerrainCollider)
                        terrainHeight = Mathf.Max(terrainHeight, hit.point.y);
                }
                // Decorations such as fallen logs must not lift the chest above
                // the tile's ground. Non-terrain maps can still use mesh floors.
                if (!float.IsNegativeInfinity(terrainHeight)) position.y = terrainHeight;
                else if (!float.IsNegativeInfinity(highest)) position.y = highest;
            }
            transform.position = position;
        }

        public void Present(int index, bool consumed)
        {
            ChestIndex = index;
            interactionCollider.enabled = !consumed;
            if (_initialized && _consumed == consumed) return;
            _initialized = true;
            _consumed = consumed;
            // Keep the empty chest visible in the world so it can be directly observed.
            chestAnimator.Play(consumed ? "EpicChest_Open_ANIM" : "EpicChest_Idle_ANIM",
                0, consumed ? 1f : 0f);
        }
    }
}
