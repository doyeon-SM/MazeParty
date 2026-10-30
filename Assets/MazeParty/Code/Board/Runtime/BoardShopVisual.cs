using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>Keep the interaction targets when replacing the Visuals children.</summary>
    public sealed class BoardShopVisual : MonoBehaviour
    {
        [SerializeField] private Collider[] interactionColliders =
            Array.Empty<Collider>();
        [SerializeField] private Collider[] physicalColliders =
            Array.Empty<Collider>();
        public Collider[] InteractionColliders => interactionColliders;
        public Collider[] PhysicalColliders => physicalColliders;

        private BoardTile _placedTile;
        private Vector3 _placedCenter;
        public void PlaceOnTile(BoardTile tile)
        {
            if (tile == null || (_placedTile == tile && _placedCenter == tile.WorldCenter)) return;
            _placedTile = tile;
            _placedCenter = tile.WorldCenter;
            transform.position = BoardTileWorldPlacement.PeripheralPoint(tile, 1.45f);
        }

        public bool HasRequiredReferences
        {
            get
            {
                if (interactionColliders == null ||
                    interactionColliders.Length == 0 || physicalColliders == null ||
                    physicalColliders.Length == 0)
                {
                    return false;
                }
                foreach (var target in interactionColliders)
                {
                    if (target == null || !target.isTrigger ||
                        (target.GetComponent<KeyShopWorldTarget>() == null &&
                         target.GetComponent<ItemShopWorldTarget>() == null))
                    {
                        return false;
                    }
                }
                foreach (var physical in physicalColliders)
                {
                    if (physical == null || physical.isTrigger ||
                        physical.GetComponent<KeyShopWorldTarget>() != null ||
                        physical.GetComponent<ItemShopWorldTarget>() != null)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        public void SetItemState(int index, bool soldOut)
        {
            _ = soldOut;
            foreach (var target in interactionColliders)
            {
                var item = target.GetComponent<ItemShopWorldTarget>();
                if (item != null)
                {
                    item.Configure(index);
                }
            }
        }

    }
}
