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
            transform.position = BoardTileWorldPlacement.PeripheralPoint(tile, .55f);
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
