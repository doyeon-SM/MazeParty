using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class BoardMinimapView
    {
        public const int DeviceMarkerCount = 1 + ItemShopRules.ShopCount + BoardChestRules.Count;
        [SerializeField] private BoardMapIcon[] deviceIcons = new BoardMapIcon[DeviceMarkerCount];
        [SerializeField] private Vector2 deviceIconInset = new Vector2(3f, 3f);
        [SerializeField] private Color keyShopDeviceColor = new Color(.2f, .55f, 1f);
        [SerializeField] private Color itemShopDeviceColor = new Color(1f, .23f, .2f);
        [SerializeField] private Color chestDeviceColor = Color.white;
        private bool HasDeviceReferences
        {
            get
            {
                if (deviceIcons == null || deviceIcons.Length != DeviceMarkerCount) return false;
                foreach (var icon in deviceIcons) if (icon == null) return false;
                return true;
            }
        }
        private void RefreshDeviceMarkers(BoardTopology topology, NetworkMatchState match)
        {
            PresentDevice(0, match.KeyShopHasLocation, match.KeyShopLocation,
                BoardMapIconKind.Respawn, keyShopDeviceColor, topology);
            for (var i = 0; i < ItemShopRules.ShopCount; i++)
            {
                var shop = match.GetItemShopSnapshot(i);
                PresentDevice(1 + i, shop.Active, shop.Location,
                    BoardMapIconKind.Respawn, itemShopDeviceColor, topology);
            }
            for (var i = 0; i < BoardChestRules.Count; i++)
            {
                var chest = match.GetChestSnapshot(i);
                PresentDevice(1 + ItemShopRules.ShopCount + i, match.ShouldShowChestOnLocalMap(i),
                    chest.Location, BoardMapIconKind.Chest, chestDeviceColor, topology);
            }
        }
        private void PresentDevice(int index, bool visible, Vector2Int coordinate,
            BoardMapIconKind kind, Color color, BoardTopology topology)
        {
            var icon = deviceIcons[index];
            visible = visible && topology.TryGetTile(coordinate, out var candidate) && IsPositionVisible(candidate.WorldCenter);
            icon.gameObject.SetActive(visible);
            if (!visible || !topology.TryGetTile(coordinate, out var tile)) return;
            icon.SetIcon(kind, kind == BoardMapIconKind.Respawn ? homeFilledIcon : null);
            icon.color = color;
            var rect = icon.rectTransform;
            var upright = Quaternion.Inverse(projection.otherDotCanvas.localRotation);
            TranslateWorldPoint(tile.WorldCenter, rect);
            rect.localRotation = upright;
            // Anchor inside the actual tile corner at every map scale, rather than
            // using a fixed center offset that drifts on differently sized boards.
            _footprintVertices.Clear();
            tile.GetWorldFootprintVertices(_footprintVertices);
            var tileBounds = new Bounds(tile.WorldCenter, Vector3.zero);
            foreach (var vertex in _footprintVertices) tileBounds.Encapsulate(vertex);
            var world = projection.miniMapBounds.GetWorldRect();
            var halfWidth = tileBounds.size.x * projection.otherDotCanvas.rect.width / world.size.x * .5f;
            var halfHeight = tileBounds.size.z * projection.otherDotCanvas.rect.height / world.size.z * .5f;
            var offset = new Vector2(
                Mathf.Max(0f, halfWidth - rect.rect.width * .5f - deviceIconInset.x),
                Mathf.Max(0f, halfHeight - rect.rect.height * .5f - deviceIconInset.y));
            rect.anchoredPosition += (Vector2)(upright * offset);
        }
    }
}
