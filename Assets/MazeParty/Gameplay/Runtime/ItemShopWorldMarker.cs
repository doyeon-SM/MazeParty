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
        private readonly GameObject[] _markers = new GameObject[ItemShopRules.ShopCount];
        private readonly TextMesh[] _labels = new TextMesh[ItemShopRules.ShopCount];
        private readonly int[] _revisions = { -1, -1 };

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
            _labels[shopIndex].text = soldOut
                ? "ITEM SHOP " + (shopIndex + 1) + "\nSOLD OUT"
                : "ITEM SHOP " + (shopIndex + 1) + "\nRMB OPEN";
            _markers[shopIndex].SetActive(true);
            return true;
        }

        private void EnsureMarker(int shopIndex)
        {
            if (_markers[shopIndex] != null)
            {
                return;
            }

            var root = new GameObject("Item Shop World Marker " + (shopIndex + 1));
            root.transform.SetParent(transform, true);
            root.layer = gameObject.layer;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Item Shop Target " + (shopIndex + 1);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            body.transform.localScale = new Vector3(1.4f, 1.7f, 1.4f);
            body.layer = gameObject.layer;
            body.AddComponent<ItemShopWorldTarget>().Configure(shopIndex);
            var renderer = body.GetComponent<Renderer>();
            if (renderer != null)
            {
                var properties = new MaterialPropertyBlock();
                var color = shopIndex == 0
                    ? new Color(0.58f, 0.2f, 0.86f, 1f)
                    : new Color(0.18f, 0.72f, 0.82f, 1f);
                properties.SetColor("_BaseColor", color);
                properties.SetColor("_Color", color);
                renderer.SetPropertyBlock(properties);
            }

            var labelObject = new GameObject("Item Shop World Text " + (shopIndex + 1));
            labelObject.transform.SetParent(root.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 1.9f, 0f);
            labelObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            labelObject.layer = gameObject.layer;
            var label = labelObject.AddComponent<TextMesh>();
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 48;
            label.characterSize = 0.09f;
            label.color = Color.white;

            _markers[shopIndex] = root;
            _labels[shopIndex] = label;
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
