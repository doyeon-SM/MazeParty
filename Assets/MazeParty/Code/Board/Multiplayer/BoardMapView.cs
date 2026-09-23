using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Updates the 7x7 map cells authored in BoardCanvas.prefab. The board is
    /// north-up in the overview; the live minimap follows the local eye heading.
    /// </summary>
    public sealed class BoardMapView : MonoBehaviour
    {
        public const int GridSize = 7;
        public const int CellCount = GridSize * GridSize;

        [Serializable]
        public sealed class Cell
        {
            public Image Background;
            public Text Marker;
        }

        [SerializeField] private GameObject overviewPanel;
        [SerializeField] private GameObject minimapPanel;
        [SerializeField] private BoardMinimapView liveMinimap;
        [SerializeField] private GameObject fullMapPanel;
        [SerializeField] private BoardMinimapView fullMap;
        private bool _fullMapOpen;
        public bool FullMapOpen => _fullMapOpen;
        [SerializeField] private Cell[] overviewCells = Array.Empty<Cell>();
        [SerializeField] private Cell[] minimapCells = Array.Empty<Cell>();
        [Header("Map palette")]
        [SerializeField] private Color emptyColor = new Color(0.045f, 0.065f, 0.09f, 1f);
        [SerializeField] private Color roomColor = new Color(0.15f, 0.23f, 0.31f, 1f);
        [SerializeField] private Color startColor = new Color(0.18f, 0.4f, 0.35f, 1f);
        [SerializeField] private Color respawnColor = new Color(0.22f, 0.33f, 0.44f, 1f);
        [SerializeField] private Color routeColor = new Color(0.16f, 0.58f, 0.88f, 1f);
        [SerializeField] private Color shopColor = new Color(0.88f, 0.58f, 0.12f, 1f);
        [SerializeField] private Color localColor = new Color(0.22f, 0.85f, 0.72f, 1f);

        private BoardTopology _topology;
        private readonly List<BoardTile> _route = new List<BoardTile>();
        private readonly HashSet<BoardTile> _routeTiles = new HashSet<BoardTile>();
        private int _lastSignature = int.MinValue;

        public bool HasRequiredReferences =>
            overviewPanel != null && minimapPanel != null &&
            HasCells(overviewCells) && HasCells(minimapCells) &&
            liveMinimap != null && liveMinimap.HasRequiredReferences &&
            fullMapPanel != null && fullMap != null && fullMap.HasRequiredReferences;

        public void Configure(GameObject overview, GameObject minimap,
            Cell[] overviewMapCells, Cell[] minimapMapCells)
        {
            overviewPanel = overview;
            minimapPanel = minimap;
            overviewCells = overviewMapCells;
            minimapCells = minimapMapCells;
            _lastSignature = int.MinValue;
        }

        private void LateUpdate()
        {
            if (!HasRequiredReferences)
            {
                return;
            }

            var match = NetworkMatchState.Instance;
            var isReady = match != null && match.IsSpawned && match.GameplayEnabled;
            var overview = isReady && match.FlowState == BoardFlowState.TurnOverview;
            var action = isReady && (match.FlowState == BoardFlowState.Descending ||
                match.FlowState == BoardFlowState.Action ||
                match.FlowState == BoardFlowState.AscendingResolve ||
                match.FlowState == BoardFlowState.CombatResolve ||
                match.FlowState == BoardFlowState.LandingEffectResolve);
            var keyboard = Keyboard.current;
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var typing = selected != null && selected.TryGetComponent<InputField>(out var input) && input.isFocused;
            UpdateFullMapState(overview || action, !typing && keyboard != null && keyboard.mKey.wasPressedThisFrame);
            SetActive(overviewPanel, overview && !_fullMapOpen);
            SetActive(minimapPanel, action && !_fullMapOpen);
            if (!overview && !action)
            {
                return;
            }

            if (_topology == null)
            {
                _topology = FindAnyObjectByType<BoardTopology>();
            }
            if (_topology == null)
            {
                return;
            }

            var manager = NetworkManager.Singleton;
            var localObject = manager != null && manager.SpawnManager != null
                ? manager.SpawnManager.GetLocalPlayerObject()
                : null;
            var localAvatar = localObject != null
                ? localObject.GetComponent<NetworkPlayerAvatar>()
                : null;
            if (_fullMapOpen || action)
            {
                if (_fullMapOpen) fullMap.Refresh(_topology, match, localAvatar);
                else liveMinimap.Refresh(_topology, match, localAvatar);
                return;
            }
            var signature = ComputeSignature(match, localAvatar, overview);
            if (signature == _lastSignature)
            {
                if (overview) PulseRoute();
                return;
            }
            _lastSignature = signature;

            _route.Clear();
            _routeTiles.Clear();
            if (overview && localAvatar != null && localAvatar.HasLogicalBoardTile &&
                match.KeyShopHasLocation &&
                _topology.TryGetTile(localAvatar.LogicalBoardTileCoordinate, out var source) &&
                _topology.TryGetTile(match.KeyShopLocation, out var shop) &&
                BoardMapRoute.TryFind(_topology, source, shop, _route))
            {
                for (var index = 0; index < _route.Count; index++)
                {
                    _routeTiles.Add(_route[index]);
                }
            }

            var cells = overview ? overviewCells : minimapCells;
            for (var y = 0; y < GridSize; y++)
            {
                for (var x = 0; x < GridSize; x++)
                {
                    RefreshCell(cells[y * GridSize + x], new Vector2Int(x, y),
                        match, localAvatar, overview);
                }
            }
            if (overview) PulseRoute();
        }

        public void UpdateFullMapState(bool boardAvailable, bool toggleRequested)
        {
            if (!boardAvailable) _fullMapOpen = false;
            else if (toggleRequested) _fullMapOpen = !_fullMapOpen;
            if (fullMapPanel != null) SetActive(fullMapPanel, _fullMapOpen);
        }

        private void OnDisable()
        {
            UpdateFullMapState(false, false);
        }

        private void RefreshCell(Cell cell, Vector2Int coordinate,
            NetworkMatchState match, NetworkPlayerAvatar localAvatar,
            bool overview)
        {
            if (!_topology.TryGetTile(coordinate, out var tile))
            {
                cell.Background.color = emptyColor;
                cell.Marker.text = string.Empty;
                return;
            }

            var color = tile.TileType == BoardTileType.Start
                ? startColor
                : tile.TileType == BoardTileType.Respawn
                    ? respawnColor
                    : roomColor;
            if (overview && _routeTiles.Contains(tile))
            {
                color = routeColor;
            }
            var isShop = match.KeyShopHasLocation &&
                         match.KeyShopLocation == coordinate;
            if (localAvatar != null && localAvatar.HasLogicalBoardTile &&
                localAvatar.LogicalBoardTileCoordinate == coordinate && !isShop)
            {
                color = localColor;
            }
            if (isShop) color = shopColor;
            cell.Background.color = color;

            var marker = isShop ? "K" : string.Empty;
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                if (TryGetVisiblePosition(match, localAvatar, avatar,
                        slot, overview, out var visibleCoordinate) &&
                    visibleCoordinate == coordinate)
                {
                    marker += (slot + 1).ToString();
                }
            }
            if (marker.Length == 0)
            {
                marker = overview ? RouteArrow(tile) : string.Empty;
            }
            cell.Marker.text = marker;
        }

        private void PulseRoute()
        {
            var pulse = 0.78f + 0.22f *
                (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f));
            foreach (var tile in _routeTiles)
            {
                var coordinate = tile.Coordinate;
                if (coordinate.x < 0 || coordinate.x >= GridSize ||
                    coordinate.y < 0 || coordinate.y >= GridSize ||
                    _route.Count > 0 &&
                    (tile == _route[0] || tile == _route[_route.Count - 1]))
                {
                    continue;
                }
                var color = routeColor;
                color.a = pulse;
                overviewCells[coordinate.y * GridSize + coordinate.x]
                    .Background.color = color;
            }
        }

        private static bool TryGetVisiblePosition(NetworkMatchState match,
            NetworkPlayerAvatar localAvatar, NetworkPlayerAvatar avatar,
            int slot, bool overview, out Vector2Int coordinate)
        {
            if (avatar != null && avatar.HasLogicalBoardTile &&
                (overview || localAvatar != null &&
                    slot == localAvatar.AssignedSlot))
            {
                coordinate = avatar.LogicalBoardTileCoordinate;
                return true;
            }
            if (!overview)
            {
                return match.TryGetOverviewEndTile(slot, out coordinate);
            }
            coordinate = default;
            return false;
        }

        private string RouteArrow(BoardTile tile)
        {
            for (var index = 0; index + 1 < _route.Count; index++)
            {
                if (_route[index] != tile)
                {
                    continue;
                }
                var delta = _route[index + 1].Coordinate - tile.Coordinate;
                if (delta.x > 0) return ">";
                if (delta.x < 0) return "<";
                if (delta.y > 0) return "^";
                if (delta.y < 0) return "v";
            }
            return string.Empty;
        }

        private int ComputeSignature(NetworkMatchState match,
            NetworkPlayerAvatar localAvatar, bool overview)
        {
            unchecked
            {
                var hash = _topology.GetHashCode();
                hash = hash * 31 + (overview ? 1 : 0);
                hash = hash * 31 + match.KeyShopRevision;
                hash = hash * 31 + (localAvatar != null
                    ? localAvatar.LogicalBoardTileCoordinate.GetHashCode() : -1);
                for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                {
                    var avatar = match.GetAvatarForSlot(slot);
                    hash = hash * 31 + (TryGetVisiblePosition(match,
                            localAvatar, avatar, slot, overview,
                            out var visibleCoordinate)
                        ? visibleCoordinate.GetHashCode() : -1);
                }
                return hash;
            }
        }

        private static bool HasCells(Cell[] cells)
        {
            if (cells == null || cells.Length != CellCount) return false;
            for (var index = 0; index < cells.Length; index++)
            {
                if (cells[index] == null || cells[index].Background == null ||
                    cells[index].Marker == null)
                {
                    return false;
                }
            }
            return true;
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target.activeSelf != active) target.SetActive(active);
        }
    }
}
