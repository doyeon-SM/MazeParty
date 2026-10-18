using System.Collections.Generic;
using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class BoardMinimapView
    {
        private readonly BoardTile[] _presentedPlayerTiles = new BoardTile[4];
        private readonly Dictionary<RectTransform, Vector3> _markerScales = new();
        private readonly List<RoomMarker> _roomMarkers = new(6);
        private readonly List<int> _roomPlayers = new(4);
        private struct RoomMarker
        {
            public RectTransform Rect, Backdrop;
            public Vector2 Size, Offset;
        }
        private Vector3 OriginalScale(RectTransform rect)
        {
            if (!_markerScales.TryGetValue(rect, out var scale))
            { scale = rect.localScale; _markerScales.Add(rect, scale); }
            return scale;
        }
        private Vector2 MarkerSize(RectTransform rect) =>
            Vector2.Scale(rect.rect.size, (Vector2)OriginalScale(rect));
        private void AddRoomMarker(BoardMapIcon icon, BoardMapCircleGraphic backdrop = null)
        {
            if (backdrop != null) backdrop.gameObject.SetActive(icon.isActiveAndEnabled);
            if (!icon.isActiveAndEnabled) return;
            var size = MarkerSize(icon.rectTransform);
            if (backdrop != null) size = Vector2.Max(size, MarkerSize(backdrop.rectTransform));
            _roomMarkers.Add(new RoomMarker { Rect = icon.rectTransform,
                Backdrop = backdrop != null ? backdrop.rectTransform : null, Size = size });
        }
        private void LayoutRoomMarkers(Room room)
        {
            if (room.Tile == null) return;
            _roomMarkers.Clear(); _roomPlayers.Clear();
            AddRoomMarker(room.EffectIcon, room.EffectBackdrop);
            AddRoomMarker(room.TypeIcon, room.TypeBackdrop);
            var primaryCount = _roomMarkers.Count;
            for (var i = 0; i < deviceIcons.Length; i++)
                if (_deviceVisible[i] && _deviceCoordinates[i] == room.Tile.Coordinate)
                    AddRoomMarker(deviceIcons[i]);

            var playerSize = Vector2.zero;
            for (var slot = 0; slot < players.Length; slot++)
                if (_presentedPlayerTiles[slot] == room.Tile && players[slot].gameObject.activeSelf)
                {
                    _roomPlayers.Add(slot);
                    playerSize = Vector2.Max(playerSize, MarkerSize(players[slot].rectTransform));
                }
            var largest = Vector2.zero;
            foreach (var marker in _roomMarkers) largest = Vector2.Max(largest, marker.Size);
            var gap = Mathf.Max(1f, Mathf.Min(deviceIconInset.x, deviceIconInset.y));
            var diameter = Mathf.Max(largest.x, largest.y);
            var playerDiameter = Mathf.Max(playerSize.x, playerSize.y);
            for (var i = 0; i < _roomMarkers.Count; i++)
            {
                var marker = _roomMarkers[i];
                if (_roomPlayers.Count > 0)
                {
                    var distance = (playerDiameter + diameter) * .5f + gap;
                    var corner = i < primaryCount ? 0 : i - primaryCount + 1;
                    var direction = corner % 4 == 0 ? new Vector2(-1, -1) :
                        corner % 4 == 1 ? Vector2.one : corner % 4 == 2 ? new Vector2(1, -1) : new Vector2(-1, 1);
                    marker.Offset = direction * distance;
                }
                else if (_roomMarkers.Count > 1)
                {
                    var distance = (i - (_roomMarkers.Count - 1) * .5f) * (diameter + gap);
                    marker.Offset = Vector2.one * distance;
                }
                _roomMarkers[i] = marker;
            }
            var center = room.Tile.GetRecoveryCenter();
            var world = projection.miniMapBounds.GetWorldRect();
            var pixelsPerUnit = new Vector2(projection.otherDotCanvas.rect.width / world.size.x,
                projection.otherDotCanvas.rect.height / world.size.z);
            var upright = Quaternion.Inverse(projection.otherDotCanvas.localRotation);
            var scale = 1f;
            if (!MarkersFit(room.Tile, center, upright, pixelsPerUnit, playerSize, scale))
            {
                var low = 0f; var high = 1f;
                for (var iteration = 0; iteration < 9; iteration++)
                {
                    var middle = (low + high) * .5f;
                    if (MarkersFit(room.Tile, center, upright, pixelsPerUnit, playerSize, middle)) low = middle;
                    else high = middle;
                }
                scale = low * .98f;
            }
            foreach (var marker in _roomMarkers)
            {
                PlaceMarker(marker.Rect, center, upright, marker.Offset * scale, scale);
                if (marker.Backdrop != null) PlaceMarker(marker.Backdrop, center, upright, marker.Offset * scale, scale);
            }
            foreach (var slot in _roomPlayers)
                PlaceMarker(players[slot].rectTransform, center, upright, Vector2.zero, scale);
        }
        private bool MarkersFit(BoardTile tile, Vector3 center, Quaternion upright,
            Vector2 pixelsPerUnit, Vector2 playerSize, float scale)
        {
            foreach (var marker in _roomMarkers)
                if (!MarkerFits(tile, center, upright, pixelsPerUnit, marker.Offset, marker.Size, scale)) return false;
            return _roomPlayers.Count == 0 || MarkerFits(tile, center, upright, pixelsPerUnit, Vector2.zero, playerSize, scale);
        }
        private static bool MarkerFits(BoardTile tile, Vector3 center, Quaternion upright,
            Vector2 pixelsPerUnit, Vector2 offset, Vector2 size, float scale)
        {
            for (var corner = 0; corner < 4; corner++)
            {
                var local = (Vector2)(upright * ((offset + Vector2.Scale(size * .5f,
                    new Vector2((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f))) * scale));
                var point = center + new Vector3(local.x / pixelsPerUnit.x, 0f, local.y / pixelsPerUnit.y);
                if (!tile.ContainsHorizontalPoint(point)) return false;
            }
            return true;
        }

        private void PlaceMarker(RectTransform rect, Vector3 center, Quaternion upright, Vector2 offset, float scale)
        {
            // TranslateWorldPoint handles the map projection. Convert through the
            // projection parent so room-child icons and layer-child icons agree.
            var parent = projection.otherDotCanvas;
            var world = projection.miniMapBounds.GetWorldRect();
            var normalized = new Vector2((center.x - world.min.x) / world.size.x,
                (center.z - world.min.z) / world.size.z);
            var local = Vector2.Scale(normalized - parent.pivot, parent.rect.size) + (Vector2)(upright * offset);
            rect.position = parent.TransformPoint(local);
            rect.rotation = parent.rotation * upright;
            rect.localScale = OriginalScale(rect) * scale;
        }
    }
}
