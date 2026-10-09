using System;
using System.Collections.Generic;
using System.Text;
using Arikan;
using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    public enum BoardMinimapDisplayContext
    {
        Minimap = 0,
        FullMap = 1,
        TurnOverview = 2,
        Standard = FullMap
    }

    /// <summary>Heading-up board projection using UnitySimpleMiniMap and authored UI only.</summary>
    public sealed class BoardMinimapView : MonoBehaviour
    {
        private const int LandingEffectCount = 9;

        /// <summary>
        /// Room icon capacity of every board map view. Authored maps may hold at most this
        /// many tiles; freeform maps place each room from its tile at runtime.
        /// </summary>
        public const int MaxRoomCount = 100;

        [Serializable]
        public sealed class Room
        {
            public Image Floor;
            public Text Symbol;
            public Image[] Walls;
            public Text[] Exits;
            public BoardMapIcon TypeIcon;
            public BoardMapIcon EffectIcon;
            public BoardMapIcon[] ProgressArrows;
        }

        [SerializeField] private MiniMapView projection;
        [SerializeField] private BoardMapTopologyGraphic topologyGraphic;
        [SerializeField] private Mask circularMask;
        [SerializeField, Min(0f)] private float radiusInTiles = 2f;
        [SerializeField] private bool followHeading = true;
        [SerializeField] private bool localPlayerOnly;
        [SerializeField] private bool showKeyShopDetails;
        [SerializeField] private bool showTravelCounts;
        [SerializeField] private Color primaryTravelCountColor =
            new Color(1f, 0.84f, 0.22f);
        [SerializeField] private Color branchTravelCountColor =
            new Color(0.42f, 0.84f, 1f);
        private Vector3 _mapCenter;
        private float _visibleWorldRadius;
        [SerializeField] private Room[] rooms = Array.Empty<Room>();
        [SerializeField] private Image[] players = Array.Empty<Image>();
        [SerializeField] private GameObject[] localHighlights = Array.Empty<GameObject>();
        [SerializeField] private RectTransform landingEffectLayer;
        [SerializeField] private Text currentTile;
        [SerializeField] private Text heading;
        [SerializeField] private BoardMapIcon shopDistanceIcon;
        [SerializeField] private Text shopDistanceText;
        [SerializeField] private string shopDistanceFormat = GameText.N(": {0} TILES");
        [SerializeField] private string shopUnavailableText = GameText.N(": NOT SPAWNED");
        [SerializeField] private string shopUnreachableText = GameText.N(": NO ROUTE");
        [SerializeField] private string shopUnknownText = ": --";
        [SerializeField] private Color[] typeIconColors =
        {
            Color.gray,
            Color.gray,
            new Color(1f, .82f, .12f),
            Color.white
        };
        [SerializeField] private Color[] effectIconColors =
        {
            Color.white,
            new Color(1f, .82f, .12f),
            new Color(1f, .2f, .18f),
            Color.white,
            new Color(.35f, 1f, .5f),
            new Color(.35f, 1f, .5f),
            new Color(1f, .2f, .18f),
            new Color(1f, .2f, .18f),
            new Color(1f, .2f, .18f)
        };
        [SerializeField] private BoardMapRouteGraphic shopRouteGraphic;
        [SerializeField] private BoardMapMineGraphic mineGraphic;
        [Header("Modern UI Pack icons")]
        [SerializeField] private Sprite homeFilledIcon;
        [SerializeField] private Sprite moneyFilledIcon;
        [SerializeField] private Sprite damageIcon;
        [SerializeField] private Sprite warningFilledIcon;
        [SerializeField] private Sprite arrowUpIcon;
        [SerializeField] private Sprite locationMarkFilledIcon;
        [SerializeField] private Sprite helpFilledIcon;
        private Bounds _mineBounds;
        private readonly List<BoardTile> _shopRoute = new List<BoardTile>();
        private readonly List<BoardTile> _orderedTiles = new List<BoardTile>(MaxRoomCount);
        private readonly List<Vector3> _footprintVertices = new List<Vector3>(BoardTileFootprint.MaxVertexCount);
        private readonly List<BoardRouteChoice> _travelChoices =
            new List<BoardRouteChoice>();
        private readonly List<BoardTravelRouteStep> _travelSteps =
            new List<BoardTravelRouteStep>();
        private readonly Dictionary<Vector2Int, string> _travelLabels =
            new Dictionary<Vector2Int, string>();
        private readonly StringBuilder _travelLabelBuilder = new StringBuilder();
        private BoardTravelRoutePreview _travelPreview;
        private BoardTopology _travelTopology;
        private BoardTile _travelStart;
        private BoardTile _travelPrimaryDestination;
        private int _travelStartingStep;
        private int _travelMaximumStep;
        private int _travelChoiceSignature = int.MinValue;
        private BoardTopology _distanceTopology;
        private Vector2Int? _distanceSource, _distanceShop;
        private bool _hasDistance;
        private int _shopDistance;
        [SerializeField] private string headingFormat = GameText.N("BOARD  /  {0} ^");
        [SerializeField] private string[] compassPoints = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        [SerializeField] private Color floorColor = new Color(0.12f, 0.2f, 0.28f);
        [SerializeField] private Color localFloorColor = new Color(0.13f, 0.43f, 0.4f);
        [SerializeField] private Color shopColor = new Color(1f, 0.82f, 0.12f);
        [SerializeField] private Color startFloorColor = new Color(0.18f, 0.4f, 0.35f);
        [SerializeField] private Color respawnFloorColor = new Color(0.22f, 0.33f, 0.44f);
        [SerializeField] private Color exitColor = new Color(0.35f, 0.8f, 1f);
        [SerializeField] private Color blockedExitColor = new Color(1f, 0.46f, 0.27f);
        [SerializeField] private string unknownTileText = GameText.N("CURRENT TILE  --");
        [SerializeField] private string tileFormat = GameText.N("CURRENT ({0}, {1})  {2}\n{3}");
        [SerializeField] private string[] tileNames = { GameText.N("ROOM"), GameText.N("START"), GameText.N("KEY SHOP"), GameText.N("RESPAWN") };
        [SerializeField] private string[] effectNames =
        {
            GameText.N("No landing effect"),
            GameText.N("Landing: Gold +3"),
            GameText.N("Landing: Gold -3"),
            GameText.N("Landing: Item"),
            GameText.N("Landing: HP +20"),
            GameText.N("Landing: HP +10"),
            GameText.N("Landing: HP -40"),
            GameText.N("Landing: HP -20"),
            GameText.N("Landing: Special Event")
        };

        public bool HasRequiredReferences
        {
            get
            {
                if (mineGraphic == null || shopRouteGraphic == null || topologyGraphic == null ||
                    homeFilledIcon == null || moneyFilledIcon == null ||
                    damageIcon == null || warningFilledIcon == null ||
                    arrowUpIcon == null || locationMarkFilledIcon == null ||
                    helpFilledIcon == null ||
                    projection == null || projection.otherDotCanvas == null ||
                    landingEffectLayer == null ||
                    landingEffectLayer.parent != projection.otherDotCanvas ||
                    projection.miniMapBounds == null || projection.miniMapBounds.topRight == null ||
                    projection.miniMapBounds.bottomLeft == null ||
                    rooms == null || players == null || localHighlights == null || tileNames == null ||
                    effectNames == null || compassPoints == null ||
                    typeIconColors == null ||
                    effectIconColors == null || typeIconColors.Length != 4 || effectIconColors.Length != LandingEffectCount ||
                    (showKeyShopDetails &&
                        (shopDistanceIcon == null || shopDistanceText == null)) ||
                    (radiusInTiles > 0f && (heading == null ||
                        circularMask == null || !circularMask.enabled ||
                        circularMask.GetComponent<BoardMapCircleGraphic>() == null)) ||
                    rooms.Length != MaxRoomCount || players.Length != MultiplayerConstants.MaxPlayers ||
                    localHighlights.Length != players.Length || tileNames.Length != 4 ||
                    effectNames.Length != LandingEffectCount || compassPoints.Length != 8)
                    return false;
                foreach (var room in rooms)
                {
                    if (room == null || room.Floor == null || room.Symbol == null || room.TypeIcon == null ||
                        room.EffectIcon == null || room.ProgressArrows == null || room.ProgressArrows.Length != 4 ||
                        room.Walls == null || room.Exits == null || room.Walls.Length != 4 || room.Exits.Length != 4)
                        return false;
                    for (var side = 0; side < 4; side++)
                        if (room.Walls[side] == null || room.Exits[side] == null || room.ProgressArrows[side] == null) return false;
                }
                for (var slot = 0; slot < players.Length; slot++)
                    if (players[slot] == null || localHighlights[slot] == null) return false;
                return true;
            }
        }

        public void Configure(MiniMapView map, Room[] mapRooms, Image[] dots,
            GameObject[] highlights, Text description, Text compass)
        {
            projection = map;
            rooms = mapRooms;
            players = dots;
            localHighlights = highlights;
            currentTile = description;
            heading = compass;
        }

        public void BindTopologyGraphic(BoardMapTopologyGraphic graphic)
        {
            topologyGraphic = graphic;
        }

        public void Refresh(
            BoardTopology topology,
            NetworkMatchState match,
            NetworkPlayerAvatar local,
            BoardMinimapDisplayContext displayContext =
                BoardMinimapDisplayContext.FullMap,
            BoardPlayerMapKnowledge knowledge = null)
        {
            if (!HasRequiredReferences || topology == null) return;
            PrepareMap(topology, local != null && local.HasLogicalBoardTile
                ? local.LogicalBoardTileCoordinate : (Vector2Int?)null,
                local != null && match.IsActionPhase ? local.LocalRemainingMoves : 0,
                match.KeyShopHasLocation ? match.KeyShopLocation : (Vector2Int?)null,
                local != null ? local.transform.position : (Vector3?)null);
            RefreshTravelCounts(topology, match, local);
            for (var slot = 0; slot < players.Length; slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                var isLocal = avatar != null && avatar == local;
                BoardTile markerTile = null;
                if (TryGetPlayerCoordinate(
                        avatar,
                        local,
                        slot,
                        displayContext,
                        knowledge,
                        out var coordinate))
                {
                    topology.TryGetTile(coordinate, out markerTile);
                }

                PresentPlayerAtTile(
                    slot,
                    markerTile,
                    avatar != null ? avatar.Appearance.BodyColor : Color.white,
                    isLocal,
                    displayContext);
            }
            mineGraphic.Present(local != null ? local.LocalMinePositions : null, _mineBounds,
                _visibleWorldRadius);
            var eye = local != null ? local.EyePivot : null;
            SetHeading(eye != null ? eye.eulerAngles.y : local != null ? local.transform.eulerAngles.y : 0f);
        }

        public void SetHeading(float yaw)
        {
            if (!followHeading) yaw = 0f;
            projection.otherDotCanvas.localRotation = Quaternion.Euler(0f, 0f, yaw);
            var upright = Quaternion.Euler(0f, 0f, -yaw);
            mineGraphic.SetIconRotation(upright);
            foreach (var room in rooms)
            {
                room.Symbol.rectTransform.localRotation = upright;
                room.TypeIcon.rectTransform.localRotation = upright;
                room.EffectIcon.rectTransform.localRotation = upright;
            }
            foreach (var dot in players) dot.rectTransform.localRotation = upright;
            if (heading != null)
            {
                heading.text = GameText.F(headingFormat,
                    compassPoints[Mathf.RoundToInt(Mathf.Repeat(yaw, 360f) / 45f) % 8]);
            }
        }

        // Also used by the editor preview; network authority remains in Refresh's source data.
        public void PrepareMap(BoardTopology topology, Vector2Int? localCoordinate,
            int remainingMoves, Vector2Int? keyShop, Vector3? focusPosition = null)
        {
            if (topology == null)
                return;

            var bounds = new Bounds();
            var hasBounds = false;
            _orderedTiles.Clear();
            foreach (var tile in topology.Tiles)
            {
                if (tile == null)
                    continue;

                _orderedTiles.Add(tile);
                _footprintVertices.Clear();
                tile.GetWorldFootprintVertices(_footprintVertices);
                for (var vertex = 0; vertex < _footprintVertices.Count; vertex++)
                {
                    if (!hasBounds)
                    {
                        bounds = new Bounds(_footprintVertices[vertex], Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(_footprintVertices[vertex]);
                    }
                }
            }
            if (!hasBounds)
                return;

            _orderedTiles.Sort(CompareTiles);
            var freeform = UsesFreeformProjection(topology);
            var referenceTileSize = GetReferenceTileSize(topology, localCoordinate);
            var extent = Mathf.Max(0.1f, Mathf.Max(bounds.size.x, bounds.size.z));
            if (radiusInTiles > 0f)
            {
                _visibleWorldRadius = radiusInTiles * referenceTileSize;
                extent = Mathf.Max(0.1f, _visibleWorldRadius * 2f);
                if (focusPosition.HasValue) bounds.center = focusPosition.Value;
                else if (localCoordinate.HasValue && topology.TryGetTile(localCoordinate.Value, out var focusTile))
                    bounds.center = focusTile.WorldCenter;
            }
            else
            {
                _visibleWorldRadius = 0f;
            }
            bounds.size = new Vector3(extent, 1f, extent);
            _mapCenter = bounds.center;
            _mineBounds = bounds;
            projection.miniMapBounds.bottomLeft.position = bounds.min;
            projection.miniMapBounds.topRight.position = bounds.max;
            if (currentTile != null)
                currentTile.text = GameText.T(unknownTileText);
            RefreshShopDistance(topology, localCoordinate, keyShop);
            if (!showKeyShopDetails && shopDistanceText != null)
                shopDistanceText.text = string.Empty;
            shopRouteGraphic.Present(_shopRoute, bounds);

            if (freeform)
            {
                topologyGraphic.Present(
                    topology,
                    bounds,
                    localCoordinate,
                    remainingMoves,
                    keyShop,
                    floorColor,
                    localFloorColor,
                    shopColor,
                    floorColor,
                    respawnFloorColor);
            }
            else
            {
                topologyGraphic.Clear();
            }

            for (var index = 0; index < rooms.Length; index++)
            {
                var room = rooms[index];
                BoardTile tile;
                Vector2Int coordinate;
                bool exists;
                if (freeform)
                {
                    exists = index < _orderedTiles.Count;
                    tile = exists ? _orderedTiles[index] : null;
                    coordinate = exists ? tile.Coordinate : default;
                }
                else
                {
                    coordinate = new Vector2Int(
                        index % BoardMapView.GridSize,
                        index / BoardMapView.GridSize);
                    exists = topology.TryGetTile(coordinate, out tile);
                }

                room.Floor.gameObject.SetActive(exists);
                room.EffectIcon.gameObject.SetActive(exists);
                if (!exists)
                    continue;

                if (freeform)
                {
                    TranslateWorldPoint(
                        tile.GetRecoveryCenter(),
                        room.Floor.rectTransform);
                }
                else
                {
                    projection.Translate(tile.transform, room.Floor.rectTransform);
                }
                room.Floor.rectTransform.localRotation = Quaternion.identity;
                room.Floor.rectTransform.sizeDelta = Vector2.one *
                    (projection.otherDotCanvas.rect.width * referenceTileSize / extent);
                var isLocal = localCoordinate.HasValue && coordinate == localCoordinate.Value;
                var isShop = keyShop.HasValue && coordinate == keyShop.Value;
                room.Floor.color = isShop ? shopColor : isLocal ? localFloorColor : floorColor;
                room.Floor.enabled = !freeform;
                // Start tiles intentionally share the normal-room design. Travel
                // counts are populated separately when the full map is active.
                room.Symbol.text = string.Empty;
                var displayedType = isShop ? BoardTileType.KeyShop : tile.TileType;
                room.TypeIcon.enabled =
                    displayedType != BoardTileType.Normal &&
                    displayedType != BoardTileType.Start &&
                    (!isShop || showKeyShopDetails);
                if (room.TypeIcon.enabled)
                {
                    var typeKind = displayedType == BoardTileType.Respawn
                        ? BoardMapIconKind.Respawn
                        : displayedType == BoardTileType.KeyShop
                            ? BoardMapIconKind.Key
                            : BoardMapIconKind.Room;
                    room.TypeIcon.SetIcon(typeKind, GetSprite(typeKind));
                    room.TypeIcon.color = typeIconColors[(int)displayedType];
                }
                var effect = tile.LandingEffect;
                room.EffectIcon.enabled = effect != BoardLandingEffectType.None;
                var effectKind = GetEffectIcon(effect);
                room.EffectIcon.SetIcon(effectKind, GetSprite(effectKind));
                room.EffectIcon.color = effectIconColors[(int)effect];
                PlaceEffectIcon(room);
                if (freeform)
                {
                    SetLegacyEdgesVisible(room, false);
                    PresentFreeformBranchArrows(
                        topology,
                        tile,
                        room);
                }
                else
                {
                    PresentLegacyEdges(
                        topology,
                        tile,
                        coordinate,
                        room,
                        isLocal,
                        remainingMoves);
                }
                if (isLocal && currentTile != null)
                {
                    var type = isShop ? BoardTileType.KeyShop : tile.TileType;
                    currentTile.text = GameText.F(tileFormat, coordinate.x, coordinate.y,
                        GameText.T(tileNames[(int)type]), GameText.T(effectNames[(int)tile.LandingEffect]));
                }
            }
        }

        public void PresentPlayer(
            int slot,
            Transform target,
            Color color,
            bool isLocal,
            BoardMinimapDisplayContext displayContext =
                BoardMinimapDisplayContext.Standard)
        {
            PresentPlayerAtWorldPosition(
                slot,
                target != null ? target.position : (Vector3?)null,
                color,
                isLocal,
                displayContext);
        }

        public void PresentPlayerAtTile(
            int slot,
            BoardTile tile,
            Color color,
            bool isLocal,
            BoardMinimapDisplayContext displayContext =
                BoardMinimapDisplayContext.Standard)
        {
            PresentPlayerAtWorldPosition(
                slot,
                tile != null ? tile.GetRecoveryCenter() : (Vector3?)null,
                color,
                isLocal,
                displayContext);
        }

        private void PresentPlayerAtWorldPosition(
            int slot,
            Vector3? worldPosition,
            Color color,
            bool isLocal,
            BoardMinimapDisplayContext displayContext)
        {
            var dot = players[slot];
            var visible = worldPosition.HasValue &&
                          IsPositionVisible(worldPosition.Value);
            dot.gameObject.SetActive(visible);
            localHighlights[slot].SetActive(visible && isLocal);
            if (!visible) return;
            TranslateWorldPoint(worldPosition.Value, dot.rectTransform);
            dot.rectTransform.localRotation = Quaternion.identity;
            dot.color = color;
            // Player locations must remain readable even when the tile also has
            // a type, landing-effect, route or mine icon at the same position.
            dot.transform.SetAsLastSibling();
        }

        public bool IsPositionVisible(Vector3 position)
        {
            if (radiusInTiles <= 0f) return true;
            var offset = position - _mapCenter;
            offset.y = 0f;
            return offset.sqrMagnitude <= _visibleWorldRadius * _visibleWorldRadius;
        }

        private static bool TryGetPlayerCoordinate(
            NetworkPlayerAvatar avatar,
            NetworkPlayerAvatar local,
            int slot,
            BoardMinimapDisplayContext displayContext,
            BoardPlayerMapKnowledge knowledge,
            out Vector2Int coordinate)
        {
            var isLocal = avatar != null && avatar == local;
            if (isLocal && avatar.IsSpawned && avatar.HasLogicalBoardTile)
            {
                coordinate = avatar.LogicalBoardTileCoordinate;
                return true;
            }

            if (knowledge == null)
            {
                coordinate = default;
                return false;
            }

            if (displayContext == BoardMinimapDisplayContext.Minimap)
            {
                if (!knowledge.IsCurrentlyVisible(slot) || avatar == null ||
                    !avatar.IsSpawned || !avatar.HasLogicalBoardTile)
                {
                    coordinate = default;
                    return false;
                }

                coordinate = avatar.LogicalBoardTileCoordinate;
                return true;
            }

            if (avatar == null || !avatar.IsSpawned)
            {
                coordinate = default;
                return false;
            }

            return knowledge.TryGetLastKnown(slot, out coordinate);
        }

        private void RefreshShopDistance(BoardTopology topology, Vector2Int? source, Vector2Int? shop)
        {
            if (!_hasDistance || topology != _distanceTopology || source != _distanceSource || shop != _distanceShop)
            {
                _hasDistance = true;
                _distanceTopology = topology; _distanceSource = source; _distanceShop = shop;
                _shopDistance = GetMinimumShopDistance(topology, source, shop);
            }
            if (shopDistanceText != null)
            {
                shopDistanceText.text = !shop.HasValue ? GameText.T(shopUnavailableText) : !source.HasValue ? shopUnknownText :
                    _shopDistance < 0 ? GameText.T(shopUnreachableText) : GameText.F(shopDistanceFormat, _shopDistance);
            }
        }

        public int GetMinimumShopDistance(BoardTopology topology, Vector2Int? source, Vector2Int? shop)
        {
            _shopRoute.Clear();
            if (!source.HasValue || !shop.HasValue || topology == null ||
                !topology.TryGetTile(source.Value, out var from) || !topology.TryGetTile(shop.Value, out var to) ||
                !BoardMapRoute.TryFind(topology, from, to, _shopRoute)) return -1;
            return _shopRoute.Count - 1;
        }

        private static BoardMapIconKind GetEffectIcon(BoardLandingEffectType effect)
        {
            switch (effect)
            {
                case BoardLandingEffectType.GoldGain:
                    return BoardMapIconKind.GoldGain;
                case BoardLandingEffectType.GoldLoss:
                    return BoardMapIconKind.GoldLoss;
                case BoardLandingEffectType.ItemReward:
                    return BoardMapIconKind.Item;
                case BoardLandingEffectType.Healing20:
                case BoardLandingEffectType.Healing10:
                    return BoardMapIconKind.Healing;
                case BoardLandingEffectType.Damage40:
                case BoardLandingEffectType.Damage20:
                    return BoardMapIconKind.Damage;
                case BoardLandingEffectType.SpecialEvent:
                    return BoardMapIconKind.SpecialEvent;
                default:
                    return BoardMapIconKind.Healing;
            }
        }

        private static int CompareTiles(BoardTile left, BoardTile right)
        {
            var x = left.Coordinate.x.CompareTo(right.Coordinate.x);
            return x != 0 ? x : left.Coordinate.y.CompareTo(right.Coordinate.y);
        }

        private static bool UsesFreeformProjection(BoardTopology topology)
        {
            for (var index = 0; index < topology.Tiles.Count; index++)
            {
                var tile = topology.Tiles[index];
                if (tile != null && tile.HasCustomFootprint)
                    return true;
            }

            for (var index = 0; index < topology.Gates.Count; index++)
            {
                var gate = topology.Gates[index];
                if (gate == null || gate.Source == null || gate.Destination == null)
                    continue;

                if (!BoardBoundaryWallPolicy.TryGetSide(
                        gate.Source.Coordinate,
                        gate.Destination.Coordinate,
                        out _))
                    return true;
            }

            return false;
        }

        private float GetReferenceTileSize(
            BoardTopology topology,
            Vector2Int? localCoordinate)
        {
            BoardTile reference = null;
            if (localCoordinate.HasValue)
                topology.TryGetTile(localCoordinate.Value, out reference);
            if (reference == null && _orderedTiles.Count > 0)
                reference = _orderedTiles[0];
            if (reference == null)
                return BoardTile.RoomSize;

            _footprintVertices.Clear();
            reference.GetWorldFootprintVertices(_footprintVertices);
            if (_footprintVertices.Count == 0)
                return BoardTile.RoomSize;

            var bounds = new Bounds(_footprintVertices[0], Vector3.zero);
            for (var index = 1; index < _footprintVertices.Count; index++)
                bounds.Encapsulate(_footprintVertices[index]);
            return Mathf.Max(0.1f, Mathf.Max(bounds.size.x, bounds.size.z));
        }

        private void TranslateWorldPoint(
            Vector3 worldPosition,
            RectTransform target)
        {
            var worldBounds = projection.miniMapBounds.GetWorldRect();
            var offset = worldPosition - worldBounds.center;
            target.localPosition = new Vector3(
                offset.x * projection.otherDotCanvas.rect.width /
                worldBounds.size.x,
                offset.z * projection.otherDotCanvas.rect.height /
                worldBounds.size.z,
                0f);
        }

        private void PresentLegacyEdges(
            BoardTopology topology,
            BoardTile tile,
            Vector2Int coordinate,
            Room room,
            bool isLocal,
            int remainingMoves)
        {
            byte connected = 0;
            byte outgoing = 0;
            var hasFirstDestination = false;
            var firstDestination = default(Vector2Int);
            var hasBranch = false;
            foreach (var gate in topology.GetOutgoingGates(tile))
            {
                if (gate != null && gate.Destination != null &&
                    BoardBoundaryWallPolicy.TryGetSide(
                        coordinate,
                        gate.Destination.Coordinate,
                        out var side))
                {
                    outgoing |= (byte)(1 << (int)side);
                    if (!hasFirstDestination)
                    {
                        firstDestination = gate.Destination.Coordinate;
                        hasFirstDestination = true;
                    }
                    else if (gate.Destination.Coordinate != firstDestination)
                    {
                        hasBranch = true;
                    }
                }
            }

            connected = outgoing;
            foreach (var gate in topology.GetIncomingGates(tile))
            {
                if (gate != null && gate.Source != null &&
                    BoardBoundaryWallPolicy.TryGetSide(
                        coordinate,
                        gate.Source.Coordinate,
                        out var side))
                {
                    connected |= (byte)(1 << (int)side);
                }
            }

            for (var side = 0; side < 4; side++)
            {
                var mask = 1 << side;
                var canExit = (outgoing & mask) != 0;
                var blocked = isLocal && (!canExit || remainingMoves <= 0);
                room.Walls[side].enabled = (connected & mask) == 0 || blocked;
                room.Exits[side].enabled = false;
                room.ProgressArrows[side].SetIcon(
                    BoardMapIconKind.Arrow,
                    arrowUpIcon);
                PlaceLegacyArrow(room.ProgressArrows[side], side);
                room.ProgressArrows[side].enabled = hasBranch && canExit;
                room.Exits[side].color = blocked ? blockedExitColor : exitColor;
            }
        }

        private void PresentFreeformBranchArrows(
            BoardTopology topology,
            BoardTile tile,
            Room room)
        {
            var outgoing = topology.GetOutgoingGates(tile);
            var destinationCount = 0;
            for (var index = 0; index < outgoing.Count; index++)
            {
                if (outgoing[index] != null &&
                    outgoing[index].Destination != null &&
                    IsFirstGateToDestination(outgoing, index))
                {
                    destinationCount++;
                }
            }

            var show = destinationCount > 1;
            var arrowIndex = 0;
            if (show)
            {
                for (var index = 0;
                     index < outgoing.Count &&
                     arrowIndex < room.ProgressArrows.Length;
                     index++)
                {
                    var gate = outgoing[index];
                    if (gate == null || gate.Destination == null)
                        continue;
                    if (!IsFirstGateToDestination(outgoing, index))
                        continue;

                    var worldDirection =
                        gate.Destination.GetRecoveryCenter() -
                        tile.GetRecoveryCenter();
                    var direction = new Vector2(
                        worldDirection.x,
                        worldDirection.z).normalized;
                    if (direction.sqrMagnitude <= 0.0001f)
                        continue;

                    var arrow = room.ProgressArrows[arrowIndex++];
                    arrow.SetIcon(BoardMapIconKind.Arrow, arrowUpIcon);
                    var rect = arrow.rectTransform;
                    rect.anchorMin = rect.anchorMax = rect.pivot =
                        Vector2.one * 0.5f;
                    rect.anchoredPosition = new Vector2(
                        direction.x * room.Floor.rectTransform.rect.width * 0.36f,
                        direction.y * room.Floor.rectTransform.rect.height * 0.36f);
                    rect.localRotation = Quaternion.Euler(
                        0f,
                        0f,
                        Vector2.SignedAngle(Vector2.up, direction));
                    arrow.enabled = true;
                }
            }

            for (; arrowIndex < room.ProgressArrows.Length; arrowIndex++)
                room.ProgressArrows[arrowIndex].enabled = false;
        }

        private static void PlaceLegacyArrow(
            BoardMapIcon arrow,
            int side)
        {
            var position = side == 0
                ? new Vector2(.5f, .9f)
                : side == 1
                    ? new Vector2(.9f, .5f)
                    : side == 2
                        ? new Vector2(.5f, .1f)
                        : new Vector2(.1f, .5f);
            var rect = arrow.rectTransform;
            rect.anchorMin = rect.anchorMax = position;
            rect.pivot = Vector2.one * .5f;
            rect.anchoredPosition = Vector2.zero;
            rect.localRotation = Quaternion.Euler(0f, 0f, -90f * side);
        }

        private static bool IsFirstGateToDestination(
            IReadOnlyList<BoardGate> gates,
            int index)
        {
            var gate = gates[index];
            if (gate == null || gate.Destination == null)
                return false;

            var coordinate = gate.Destination.Coordinate;
            for (var previous = 0; previous < index; previous++)
            {
                var candidate = gates[previous];
                if (candidate != null && candidate.Destination != null &&
                    candidate.Destination.Coordinate == coordinate)
                {
                    return false;
                }
            }
            return true;
        }

        private static void SetLegacyEdgesVisible(Room room, bool visible)
        {
            for (var side = 0; side < 4; side++)
            {
                room.Walls[side].enabled = visible;
                room.Exits[side].enabled = visible;
                room.ProgressArrows[side].enabled = visible;
            }
        }

        private void RefreshTravelCounts(
            BoardTopology topology,
            NetworkMatchState match,
            NetworkPlayerAvatar local)
        {
            if (!showTravelCounts)
                return;

            var turnRoll = local != null ? local.LocalTurnRoll : 0;
            var startingStep = local != null
                ? Mathf.Clamp(local.LocalTurnRouteStepOffset, 0, turnRoll)
                : 0;
            BoardTile start = null;
            BoardTile primaryDestination = null;
            if (match != null && match.KeyShopHasLocation)
            {
                topology.TryGetTile(
                    match.KeyShopLocation,
                    out primaryDestination);
            }
            var hasValidOrigin = local != null && local.IsOwner &&
                                 local.HasLocalTurnRouteOrigin &&
                                 turnRoll > 0 &&
                                 topology.TryGetTile(
                                     local.LocalTurnRouteOrigin,
                                     out start) &&
                                 start != null;
            if (!BoardTravelRoutePreview.ShouldDisplayForTurn(
                    match != null && match.IsActionPhase,
                    turnRoll,
                    hasValidOrigin))
            {
                start = null;
                startingStep = 0;
                turnRoll = 0;
            }

            var signature = ComputeTravelChoiceSignature(local);
            if (_travelPreview == null || _travelTopology != topology ||
                _travelStart != start ||
                _travelPrimaryDestination != primaryDestination ||
                _travelStartingStep != startingStep ||
                _travelMaximumStep != turnRoll)
            {
                _travelTopology = topology;
                _travelStart = start;
                _travelPrimaryDestination = primaryDestination;
                _travelStartingStep = startingStep;
                _travelMaximumStep = turnRoll;
                _travelPreview = start != null && turnRoll > 0
                    ? new BoardTravelRoutePreview(
                        topology,
                        start,
                        startingStep,
                        turnRoll,
                        primaryDestination)
                    : null;
                _travelChoiceSignature = int.MinValue;
            }

            if (_travelChoiceSignature != signature)
            {
                _travelChoiceSignature = signature;
                RebuildTravelLabels(local);
            }

            var freeform = UsesFreeformProjection(topology);
            for (var index = 0; index < rooms.Length; index++)
            {
                Vector2Int coordinate;
                if (freeform)
                {
                    if (index >= _orderedTiles.Count)
                        continue;
                    coordinate = _orderedTiles[index].Coordinate;
                }
                else
                {
                    coordinate = new Vector2Int(
                        index % BoardMapView.GridSize,
                        index / BoardMapView.GridSize);
                }

                rooms[index].Symbol.text = _travelLabels.TryGetValue(
                    coordinate,
                    out var label)
                    ? label
                    : string.Empty;
            }
        }

        private Sprite GetSprite(BoardMapIconKind kind)
        {
            switch (kind)
            {
                case BoardMapIconKind.Respawn:
                    return homeFilledIcon;
                case BoardMapIconKind.GoldGain:
                case BoardMapIconKind.GoldLoss:
                    return moneyFilledIcon;
                case BoardMapIconKind.Damage:
                    return damageIcon;
                case BoardMapIconKind.SpecialEvent:
                    return warningFilledIcon;
                case BoardMapIconKind.Arrow:
                    return arrowUpIcon;
                default:
                    return null;
            }
        }

        private int ComputeTravelChoiceSignature(NetworkPlayerAvatar local)
        {
            unchecked
            {
                var hash = local != null ? local.AssignedSlot : -1;
                if (local == null || !local.IsOwner)
                    return hash;

                var choices = local.LocalBoardRouteChoices;
                hash = hash * 31 + choices.Count;
                for (var index = 0; index < choices.Count; index++)
                    hash = hash * 31 + choices[index].GetHashCode();
                return hash;
            }
        }

        private void RebuildTravelLabels(NetworkPlayerAvatar local)
        {
            _travelLabels.Clear();
            _travelChoices.Clear();
            _travelSteps.Clear();
            if (_travelPreview == null || local == null || !local.IsOwner)
                return;

            var choices = local.LocalBoardRouteChoices;
            for (var index = 0; index < choices.Count; index++)
                _travelChoices.Add(choices[index]);
            _travelPreview.GetVisibleSteps(_travelChoices, _travelSteps);

            var primaryHex = ColorUtility.ToHtmlStringRGB(
                primaryTravelCountColor);
            var branchHex = ColorUtility.ToHtmlStringRGB(
                branchTravelCountColor);
            var stepIndex = 0;
            while (stepIndex < _travelSteps.Count)
            {
                var coordinate = _travelSteps[stepIndex].Coordinate;
                _travelLabelBuilder.Clear();
                while (stepIndex < _travelSteps.Count &&
                       _travelSteps[stepIndex].Coordinate == coordinate)
                {
                    var step = _travelSteps[stepIndex];
                    if (step.Step <= 0)
                    {
                        stepIndex++;
                        continue;
                    }
                    if (_travelLabelBuilder.Length > 0)
                        _travelLabelBuilder.Append(' ');
                    _travelLabelBuilder.Append("<color=#");
                    _travelLabelBuilder.Append(step.IsBranch
                        ? branchHex
                        : primaryHex);
                    _travelLabelBuilder.Append('>');
                    _travelLabelBuilder.Append(step.Step);
                    _travelLabelBuilder.Append("</color>");
                    stepIndex++;
                }
                if (_travelLabelBuilder.Length > 0)
                {
                    _travelLabels.Add(
                        coordinate,
                        _travelLabelBuilder.ToString());
                }
            }
        }

        private static void PlaceEffectIcon(Room room)
        {
            var floor = room.Floor.rectTransform;
            var icon = room.EffectIcon.rectTransform;
            icon.localRotation = Quaternion.identity;
            icon.localScale = Vector3.one;
            if (icon.parent == floor)
            {
                icon.anchorMin = icon.anchorMax = icon.pivot =
                    Vector2.one * 0.5f;
                icon.anchoredPosition = Vector2.zero;
                return;
            }

            icon.anchorMin = icon.anchorMax = icon.pivot =
                Vector2.one * 0.5f;
            icon.anchoredPosition = floor.anchoredPosition;
        }

    }
}
