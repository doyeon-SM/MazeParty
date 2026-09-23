using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Presentation-only pool for the two server-authored item-shop snapshots.
    /// Markers are created once and hidden/repositioned without destruction.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemShopWorldMarker : MonoBehaviour
    {
        [SerializeField] private BoardWorldPrefabs worldPrefabs;
        private readonly BoardShopVisual[] _visuals = new BoardShopVisual[ItemShopRules.ShopCount];
        private readonly GameObject[] _markers = new GameObject[ItemShopRules.ShopCount];

        private readonly int[] _revisions = { -1, -1 };
        private readonly GameObject[] _topViewHighlights =
            new GameObject[ItemShopRules.ShopCount];
        private bool _topViewHighlightRequested;

        public void SetTopViewHighlight(bool highlighted)
        {
            _topViewHighlightRequested = highlighted;
            for (var index = 0; index < _topViewHighlights.Length; index++)
            {
                if (_topViewHighlights[index] != null)
                {
                    _topViewHighlights[index].SetActive(highlighted);
                }
            }
        }

        public GameObject GetMarkerObject(int shopIndex)
        {
            return IsValidIndex(shopIndex) ? _markers[shopIndex] : null;
        }

        public bool ApplyReplicatedState(
            int shopIndex,
            bool active,
            Vector2Int coordinate,
            bool soldOut,
            BoardTopology topology,
            int revision)
        {
            if (!IsValidIndex(shopIndex) || revision < _revisions[shopIndex])
            {
                return false;
            }

            _revisions[shopIndex] = revision;
            if (!active || topology == null ||
                !topology.TryGetTile(coordinate, out var tile) || tile == null)
            {
                SetVisible(shopIndex, false);
                return !active;
            }

            EnsureMarker(shopIndex);
            _markers[shopIndex].transform.position = tile.WorldCenter;
            _visuals[shopIndex].SetItemState(shopIndex, soldOut);
            _markers[shopIndex].SetActive(true);
            return true;
        }

        private void EnsureMarker(int shopIndex)
        {
            if (_markers[shopIndex] != null)
            {
                return;
            }

            if (worldPrefabs == null) worldPrefabs = BoardWorldPrefabs.LoadRequired();
            var visual = Instantiate(worldPrefabs.ItemShop(shopIndex), transform, false);
            var root = visual.gameObject;
            _visuals[shopIndex] = visual;
            _topViewHighlights[shopIndex] = visual.TopViewHighlight;
            visual.TopViewHighlight.SetActive(_topViewHighlightRequested);
            _markers[shopIndex] = root;

            root.SetActive(false);
        }

        private void SetVisible(int shopIndex, bool visible)
        {
            if (_markers[shopIndex] != null && _markers[shopIndex].activeSelf != visible)
            {
                _markers[shopIndex].SetActive(visible);
            }
        }

        private static bool IsValidIndex(int shopIndex)
        {
            return shopIndex >= 0 && shopIndex < ItemShopRules.ShopCount;
        }
    }
}
