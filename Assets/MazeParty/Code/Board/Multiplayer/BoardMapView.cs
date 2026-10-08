using System;
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
        [SerializeField] private Button fullMapCloseButton;
        private bool _fullMapOpen;
        private bool _fullMapBoardAvailable;
        private bool _turnOverviewUsesFullMap;
        private bool _fullMapCloseButtonWired;
        public bool FullMapOpen => _fullMapOpen;
        [SerializeField] private Cell[] overviewCells = Array.Empty<Cell>();
        [SerializeField] private Cell[] minimapCells = Array.Empty<Cell>();
        [Header("Map palette")]
        [SerializeField] private Color emptyColor = new Color(0.045f, 0.065f, 0.09f, 1f);
        [SerializeField] private Color roomColor = new Color(0.15f, 0.23f, 0.31f, 1f);
        [SerializeField] private Color startColor = new Color(0.18f, 0.4f, 0.35f, 1f);
        [SerializeField] private Color respawnColor = new Color(0.22f, 0.33f, 0.44f, 1f);
        [SerializeField] private Color shopColor = new Color(1f, 0.82f, 0.12f, 1f);
        [SerializeField] private Color localColor = new Color(0.22f, 0.85f, 0.72f, 1f);

        private BoardTopology _topology;
        private int _lastSignature = int.MinValue;
        private readonly BoardPlayerMapKnowledge _playerKnowledge =
            new BoardPlayerMapKnowledge();
        private readonly RaycastHit[] _playerSightHits = new RaycastHit[32];
        private NetworkMatchState _knowledgeMatch;
        private BoardTopology _knowledgeTopology;
        private GameplayCameraDirector _cameraDirector;

        public bool HasRequiredReferences =>
            overviewPanel != null && minimapPanel != null &&
            HasCells(overviewCells) && HasCells(minimapCells) &&
            liveMinimap != null && liveMinimap.HasRequiredReferences &&
            fullMapPanel != null && fullMap != null && fullMap.HasRequiredReferences &&
            fullMapCloseButton != null;

        public void Configure(GameObject overview, GameObject minimap,
            Cell[] overviewMapCells, Cell[] minimapMapCells)
        {
            overviewPanel = overview;
            minimapPanel = minimap;
            overviewCells = overviewMapCells;
            minimapCells = minimapMapCells;
            _lastSignature = int.MinValue;
        }

        private void OnEnable()
        {
            WireFullMapCloseButton(true);
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
            var rawToggleRequested = !typing && keyboard != null &&
                                     keyboard.mKey.wasPressedThisFrame;
            var toggleRequested = rawToggleRequested &&
                (!LocalInputGate.BlocksGameplayInput ||
                 (_fullMapOpen && UiPopupStack.IsTop(fullMapPanel)));
            if (!typing && _fullMapOpen &&
                UiPopupStack.IsTop(fullMapPanel) &&
                keyboard != null &&
                keyboard.escapeKey.wasPressedThisFrame)
            {
                var menu = FindAnyObjectByType<GameMenuView>();
                if (menu == null || !menu.isActiveAndEnabled)
                {
                    CloseFullMap();
                    toggleRequested = false;
                }
            }
            if (!overview && !action)
            {
                UpdateFullMapState(false, toggleRequested);
                SetActive(overviewPanel, false);
                SetActive(minimapPanel, false);
                return;
            }

            if (_topology == null)
            {
                _topology = FindAnyObjectByType<BoardTopology>();
            }
            if (_topology == null)
            {
                UpdateFullMapState(false, false);
                SetActive(overviewPanel, false);
                SetActive(minimapPanel, false);
                return;
            }

            UpdateFullMapState(
                true,
                toggleRequested,
                overview);
            // Every topology now uses the authored full-map presentation during
            // turn overview so Modern UI tile and player icons stay consistent.
            SetActive(overviewPanel, false);
            SetActive(minimapPanel, action && !_fullMapOpen);

            var manager = NetworkManager.Singleton;
            var localObject = manager != null && manager.SpawnManager != null
                ? manager.SpawnManager.GetLocalPlayerObject()
                : null;
            var localAvatar = localObject != null
                ? localObject.GetComponent<NetworkPlayerAvatar>()
                : null;
            RefreshPlayerKnowledge(match, localAvatar);
            if (_fullMapOpen || overview || action)
            {
                if (_fullMapOpen || overview)
                {
                    fullMap.Refresh(
                        _topology,
                        match,
                        localAvatar,
                        overview
                            ? BoardMinimapDisplayContext.TurnOverview
                            : BoardMinimapDisplayContext.FullMap,
                        _playerKnowledge);
                }
                else
                {
                    liveMinimap.Refresh(
                        _topology,
                        match,
                        localAvatar,
                        BoardMinimapDisplayContext.Minimap,
                        _playerKnowledge);
                }
                return;
            }
            var signature = ComputeSignature(match, localAvatar, overview);
            if (signature == _lastSignature)
            {
                return;
            }
            _lastSignature = signature;

            var cells = overview ? overviewCells : minimapCells;
            for (var y = 0; y < GridSize; y++)
            {
                for (var x = 0; x < GridSize; x++)
                {
                    RefreshCell(cells[y * GridSize + x], new Vector2Int(x, y),
                        match, localAvatar, overview);
                }
            }
        }

        public void UpdateFullMapState(bool boardAvailable, bool toggleRequested)
        {
            UpdateFullMapState(boardAvailable, toggleRequested, false);
        }

        internal void UpdateFullMapState(
            bool boardAvailable,
            bool toggleRequested,
            bool turnOverviewUsesFullMap)
        {
            _fullMapBoardAvailable = boardAvailable;
            _turnOverviewUsesFullMap = turnOverviewUsesFullMap;
            if (!boardAvailable)
            {
                SetFullMapOpen(false);
            }
            else if (toggleRequested)
            {
                SetFullMapOpen(!_fullMapOpen);
            }

            RefreshFullMapVisibility();
        }

        public void CloseFullMap()
        {
            SetFullMapOpen(false);
            RefreshFullMapVisibility();
        }

        private void SetFullMapOpen(bool open)
        {
            if (_fullMapOpen == open)
            {
                if (!open)
                {
                    UiPopupStack.Remove(fullMapPanel);
                }
                return;
            }

            _fullMapOpen = open;
            if (open)
            {
                if (fullMapPanel != null)
                {
                    fullMapPanel.SetActive(true);
                    UiPopupStack.Push(fullMapPanel, CloseFullMap);
                }
            }
            else
            {
                UiPopupStack.Remove(fullMapPanel);
            }
        }

        private void RefreshFullMapVisibility()
        {
            if (fullMapPanel != null)
            {
                SetActive(
                    fullMapPanel,
                    _fullMapBoardAvailable &&
                    (_fullMapOpen || _turnOverviewUsesFullMap));
            }

            if (fullMapCloseButton != null)
            {
                fullMapCloseButton.gameObject.SetActive(
                    _fullMapBoardAvailable && _fullMapOpen);
            }
        }

        private void OnDisable()
        {
            WireFullMapCloseButton(false);
            _fullMapBoardAvailable = false;
            _turnOverviewUsesFullMap = false;
            CloseFullMap();
        }

        private void WireFullMapCloseButton(bool subscribe)
        {
            if (fullMapCloseButton == null ||
                subscribe == _fullMapCloseButtonWired)
            {
                return;
            }

            _fullMapCloseButtonWired = subscribe;
            if (subscribe)
            {
                fullMapCloseButton.onClick.AddListener(CloseFullMap);
            }
            else
            {
                fullMapCloseButton.onClick.RemoveListener(CloseFullMap);
            }
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

            var color = tile.TileType == BoardTileType.Respawn
                    ? respawnColor
                    : roomColor;
            var isShop = match.KeyShopHasLocation &&
                         match.KeyShopLocation == coordinate;
            if (localAvatar != null && localAvatar.HasLogicalBoardTile &&
                localAvatar.LogicalBoardTileCoordinate == coordinate && !isShop)
            {
                color = localColor;
            }
            if (isShop) color = shopColor;
            cell.Background.color = color;

            var marker = string.Empty;
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                if (TryGetVisiblePosition(match, localAvatar,
                        slot, out var visibleCoordinate) &&
                    visibleCoordinate == coordinate)
                {
                    marker += (slot + 1).ToString();
                }
            }
            if (overview)
                marker = ResolveOverviewMarker(
                    marker,
                    string.Empty);
            cell.Marker.text = marker;
        }

        internal static string ResolveOverviewMarker(
            string occupiedMarker,
            string routeMarker)
        {
            if (!string.IsNullOrEmpty(occupiedMarker))
                return occupiedMarker;
            if (!string.IsNullOrEmpty(routeMarker))
                return routeMarker;
            return string.Empty;
        }

        private bool TryGetVisiblePosition(
            NetworkMatchState match,
            NetworkPlayerAvatar localAvatar,
            int slot,
            out Vector2Int coordinate)
        {
            if (localAvatar != null &&
                slot == localAvatar.AssignedSlot &&
                localAvatar.HasLogicalBoardTile)
            {
                coordinate = localAvatar.LogicalBoardTileCoordinate;
                return true;
            }

            var remoteAvatar = match != null
                ? match.GetAvatarForSlot(slot)
                : null;
            if (remoteAvatar == null || !remoteAvatar.IsSpawned)
            {
                coordinate = default;
                return false;
            }

            return _playerKnowledge.TryGetLastKnown(slot, out coordinate);
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
                    hash = hash * 31 + (TryGetVisiblePosition(
                            match, localAvatar, slot,
                            out var visibleCoordinate)
                        ? visibleCoordinate.GetHashCode() : -1);
                }
                hash = hash * 31 + _playerKnowledge.Revision;
                return hash;
            }
        }

        private void RefreshPlayerKnowledge(
            NetworkMatchState match,
            NetworkPlayerAvatar localAvatar)
        {
            if (_knowledgeMatch != match || _knowledgeTopology != _topology)
            {
                _knowledgeMatch = match;
                _knowledgeTopology = _topology;
                _playerKnowledge.Reset();
            }
            SeedInitialPlayerPositions();

            _playerKnowledge.BeginObservationFrame();
            if (localAvatar != null && localAvatar.HasLogicalBoardTile)
            {
                _playerKnowledge.Observe(
                    localAvatar.AssignedSlot,
                    localAvatar.LogicalBoardTileCoordinate);
            }

            if (_fullMapOpen || localAvatar == null)
            {
                _playerKnowledge.EndObservationFrame();
                return;
            }

            if (_cameraDirector == null)
                _cameraDirector = FindAnyObjectByType<GameplayCameraDirector>();
            if (_cameraDirector == null ||
                _cameraDirector.ActiveMode != GameplayMode.FirstPerson ||
                _cameraDirector.CompletedMode != GameplayMode.FirstPerson ||
                _cameraDirector.IsTransitioning ||
                _cameraDirector.OutputCamera == null)
            {
                _playerKnowledge.EndObservationFrame();
                return;
            }

            var outputCamera = _cameraDirector.OutputCamera;
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                if (slot == localAvatar.AssignedSlot ||
                    !match.IsPlayerPresent(slot))
                {
                    continue;
                }

                var target = match.GetAvatarForSlot(slot);
                if (target == null || !target.IsSpawned ||
                    !target.HasLogicalBoardTile || target.IsCloaked ||
                    !IsDirectlyVisible(outputCamera, target))
                {
                    continue;
                }

                _playerKnowledge.Observe(
                    slot,
                    target.LogicalBoardTileCoordinate);
            }

            _playerKnowledge.EndObservationFrame();

        }

        private void SeedInitialPlayerPositions()
        {
            var mapRoot = BoardMapRuntimeLoader.ResolveActiveMapRoot(_topology);
            if (mapRoot == null)
                return;

            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                var avatar = _knowledgeMatch != null
                    ? _knowledgeMatch.GetAvatarForSlot(slot)
                    : null;
                if (avatar == null || !avatar.IsSpawned)
                    continue;

                var start = mapRoot.GetStartTile(slot);
                if (start != null)
                    _playerKnowledge.SeedIfUnknown(slot, start.Coordinate);
            }
        }

        private bool IsDirectlyVisible(
            Camera outputCamera,
            NetworkPlayerAvatar target)
        {
            var visual = target.AvatarVisual;
            var bindings = visual != null ? visual.Bindings : null;
            var targetPoint = bindings != null && bindings.HeadAnchor != null
                ? bindings.HeadAnchor.position
                : target.EyePivot != null
                    ? target.EyePivot.position
                    : target.transform.position + Vector3.up * 0.75f;
            return IsPointDirectlyVisible(
                outputCamera,
                targetPoint,
                _playerSightHits);
        }

        internal static bool IsPointDirectlyVisible(
            Camera outputCamera,
            Vector3 targetPoint,
            RaycastHit[] sightHits)
        {
            if (outputCamera == null || sightHits == null ||
                sightHits.Length == 0)
            {
                return false;
            }

            var viewport = outputCamera.WorldToViewportPoint(targetPoint);
            if (viewport.z <= outputCamera.nearClipPlane ||
                viewport.x < 0f || viewport.x > 1f ||
                viewport.y < 0f || viewport.y > 1f)
            {
                return false;
            }

            var origin = outputCamera.transform.position;
            var offset = targetPoint - origin;
            var distance = offset.magnitude;
            if (distance <= 0.001f)
                return true;

            var hitCount = Physics.RaycastNonAlloc(
                origin,
                offset / distance,
                sightHits,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (var index = 0; index < hitCount; index++)
            {
                var collider = sightHits[index].collider;
                if (collider == null ||
                    collider.GetComponentInParent<NetworkPlayerAvatar>() != null)
                {
                    continue;
                }

                var boundaryWall =
                    collider.GetComponentInParent<BoardBoundaryWallVisual>();
                if (boundaryWall != null && !boundaryWall.IsVisible)
                    continue;

                return false;
            }

            return hitCount < sightHits.Length;
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
