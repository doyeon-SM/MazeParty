using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>Keep the interaction targets when replacing the Visuals children.</summary>
    public sealed class BoardShopVisual : MonoBehaviour
    {
        public const float LocationHighlightSeconds = 3f;

        [SerializeField] private GameObject topViewHighlight;
        [SerializeField] private GameObject locationHighlightVfx;
        [SerializeField] private Collider[] interactionColliders =
            Array.Empty<Collider>();
        [SerializeField] private Collider[] physicalColliders =
            Array.Empty<Collider>();
        private float _locationHighlightEndsAt = float.NegativeInfinity;
        private bool _topViewHighlightVisible;
        private bool _locationHighlightVisible;

        public GameObject TopViewHighlight => topViewHighlight;
        public GameObject LocationHighlightVfx => locationHighlightVfx;
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

        private void Awake()
        {
            RefreshHighlightVisibility();
        }

        private void OnEnable()
        {
            RefreshHighlightVisibility();
        }

        private void Update()
        {
            if (_locationHighlightVisible &&
                Time.unscaledTime >= _locationHighlightEndsAt)
            {
                SetLocationHighlightVisible(false);
            }
        }

        private void OnDisable()
        {
            _locationHighlightVisible = false;
            _locationHighlightEndsAt = float.NegativeInfinity;
            SetHighlightObjectVisible(topViewHighlight, false);
            if (locationHighlightVfx != topViewHighlight)
            {
                SetHighlightObjectVisible(locationHighlightVfx, false);
            }
        }

        public bool HasRequiredReferences
        {
            get
            {
                if (topViewHighlight == null || locationHighlightVfx == null ||
                    interactionColliders == null ||
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

        public void PlayLocationHighlight()
        {
            if (locationHighlightVfx == null)
            {
                return;
            }

            _locationHighlightEndsAt = Time.unscaledTime +
                                       LocationHighlightSeconds;
            SetLocationHighlightVisible(true);
        }

        public void SetTopViewHighlightVisible(bool visible)
        {
            _topViewHighlightVisible = visible;
            RefreshHighlightVisibility();
        }

        public void StopLocationHighlight()
        {
            SetLocationHighlightVisible(false);
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

        private void SetLocationHighlightVisible(bool visible)
        {
            _locationHighlightVisible = visible;
            if (!visible)
            {
                _locationHighlightEndsAt = float.NegativeInfinity;
            }

            RefreshHighlightVisibility();
        }

        private void RefreshHighlightVisibility()
        {
            var canShow = isActiveAndEnabled;
            if (topViewHighlight == locationHighlightVfx)
            {
                SetHighlightObjectVisible(
                    topViewHighlight,
                    canShow && (_topViewHighlightVisible ||
                                _locationHighlightVisible));
                return;
            }

            SetHighlightObjectVisible(
                topViewHighlight,
                canShow && _topViewHighlightVisible);
            SetHighlightObjectVisible(
                locationHighlightVfx,
                canShow && _locationHighlightVisible);
        }

        private static void SetHighlightObjectVisible(
            GameObject highlight,
            bool visible)
        {
            if (highlight != null && highlight.activeSelf != visible)
            {
                highlight.SetActive(visible);
            }
        }
    }
}
