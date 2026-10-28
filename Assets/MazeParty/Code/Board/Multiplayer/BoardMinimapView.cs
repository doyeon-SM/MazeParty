using System;
using System.Collections.Generic;
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
    public sealed partial class BoardMinimapView : MonoBehaviour
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
            [NonSerialized] public BoardTile Tile;
            public Image Floor;
            public Image[] Walls;
            public Text[] Exits;
            public BoardMapIcon TypeIcon;
            public BoardMapIcon EffectIcon;
            public BoardMapCircleGraphic TypeBackdrop;
            public BoardMapCircleGraphic EffectBackdrop;
            public BoardMapIcon[] ProgressArrows;
        }

        [SerializeField] private MiniMapView projection;
        [SerializeField] private BoardMapTopologyGraphic topologyGraphic;
        [SerializeField] private BoardMapVisionGraphic visionGraphic;
        [SerializeField] private Mask circularMask;
        [SerializeField, Min(0f)] private float radiusInTiles = 2f;
        [SerializeField] private bool followHeading = true;
        [SerializeField] private bool localPlayerOnly;
        [SerializeField] private bool showKeyShopDetails;
        private Vector3 _mapCenter;
        private float _visibleWorldRadius;
        public float VisibleWorldRadius => _visibleWorldRadius > 0f
            ? _visibleWorldRadius
            : radiusInTiles * BoardTile.RoomSize;
        [SerializeField] private Room[] rooms = Array.Empty<Room>();
        [SerializeField] private Image[] players = Array.Empty<Image>();
        [SerializeField, Range(0f, 1f)] private float rememberedPlayerDarken = 0.6f;
        [SerializeField] private GameObject[] localHighlights = Array.Empty<GameObject>();
        [SerializeField] private BoardMapPlayerDirectionGraphic[] localDirections =
            Array.Empty<BoardMapPlayerDirectionGraphic>();
        private float _localFacingYaw;
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
        [SerializeField] private BoardMapRouteGraphic travelRouteGraphic;
        [SerializeField] private BoardMapRouteGraphic preferredTravelRouteGraphic;
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
        private readonly List<Vector3> _dangerPositions = new List<Vector3>();

        public bool TryGetGroundPoint(BoardTopology topology, Vector2 screen, Camera camera, out Vector3 point)
        {
            point = default;
            var rect = projection.otherDotCanvas;
            if (!RectTransformUtility.RectangleContainsScreenPoint(rect, screen, camera) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, camera, out var local)) return false;
            var area = rect.rect;
            if (area.width <= 0f || area.height <= 0f) return false;
            var requested = new Vector3(
                Mathf.Lerp(_mineBounds.min.x, _mineBounds.max.x, Mathf.InverseLerp(area.xMin, area.xMax, local.x)),
                _mineBounds.center.y,
                Mathf.Lerp(_mineBounds.min.z, _mineBounds.max.z, Mathf.InverseLerp(area.yMin, area.yMax, local.y)));
            return BoardBombardmentRules.TryGetGroundPosition(topology, requested, out point);
        }
        private readonly List<BoardTile> _shopRoute = new List<BoardTile>();
        private readonly List<BoardTile> _orderedTiles = new List<BoardTile>(MaxRoomCount);
        private readonly List<Vector3> _footprintVertices = new List<Vector3>(BoardTileFootprint.MaxVertexCount);
        private readonly List<BoardRouteChoice> _travelChoices =
            new List<BoardRouteChoice>();
        private readonly List<BoardRouteChoice> _travelEdges =
            new List<BoardRouteChoice>();
        private readonly List<BoardRouteChoice> _preferredTravelEdges =
            new List<BoardRouteChoice>();
        private readonly List<Vector3> _yellowRoutePoints = new List<Vector3>();
        private readonly List<Vector3> _blueRoutePoints = new List<Vector3>();
        private readonly List<Vector3> _preferredRoutePoints = new List<Vector3>();
        private readonly List<Vector3> _yellowVisiblePoints = new List<Vector3>();
        private readonly List<Vector3> _blueVisiblePoints = new List<Vector3>();
        private readonly List<Vector3> _blueMainPoints = new List<Vector3>();
        private readonly List<Vector3> _blueBranchPoints = new List<Vector3>();
        private readonly HashSet<Vector2Int> _preferredPointKeys =
            new HashSet<Vector2Int>();
        private readonly BoardRouteDotLayout _routeDotLayout =
            new BoardRouteDotLayout();
        private BoardTravelRoutePreview _travelPreview;
        private BoardTopology _travelTopology;
        private BoardTile _travelStart;
        private BoardTile _travelPrimaryDestination;
        private int _travelStartingStep;
        private int _travelMaximumStep, _travelRevision;
        private BoardTopology _travelDataTopology;
        private int _travelDataSignature;
        private bool _travelDataValid, _travelDrawValid;
        private Bounds _travelDrawBounds;
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
                if (!HasDeviceReferences || mineGraphic == null || shopRouteGraphic == null ||
                    travelRouteGraphic == null ||
                    preferredTravelRouteGraphic == null ||
                    topologyGraphic == null ||
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
                        circularMask.GetComponent<BoardMapCircleGraphic>() == null ||
                        visionGraphic == null ||
                        visionGraphic.transform.parent != projection.otherDotCanvas)) ||
                    rooms.Length != MaxRoomCount || players.Length != MultiplayerConstants.MaxPlayers ||
                    localHighlights.Length != players.Length ||
                    localDirections.Length != players.Length || tileNames.Length != 4 ||
                    effectNames.Length != LandingEffectCount || compassPoints.Length != 8)
                    return false;
                foreach (var room in rooms)
                {
                    if (room == null || room.Floor == null || room.TypeIcon == null ||
                        room.EffectIcon == null || room.TypeBackdrop == null ||
                        room.EffectBackdrop == null || room.ProgressArrows == null || room.ProgressArrows.Length != 4 ||
                        room.Walls == null || room.Exits == null || room.Walls.Length != 4 || room.Exits.Length != 4)
                        return false;
                    for (var side = 0; side < 4; side++)
                        if (room.Walls[side] == null || room.Exits[side] == null || room.ProgressArrows[side] == null) return false;
                }
                for (var slot = 0; slot < players.Length; slot++)
                    if (players[slot] == null || localHighlights[slot] == null ||
                        localDirections[slot] == null) return false;
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

        public void BindVisionGraphic(BoardMapVisionGraphic graphic)
        {
            visionGraphic = graphic;
        }

        public void PresentVision(
            float halfAngleDegrees,
            float[] clearFractions)
        {
            if (visionGraphic != null)
                visionGraphic.Present(halfAngleDegrees, clearFractions);
        }

        public void PresentVisionUnavailable()
        {
            if (visionGraphic != null)
                visionGraphic.PresentUnavailable();
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
            RefreshTravelRoute(topology, match, local);
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
                    GetPlayerMarkerColor(avatar != null ? avatar.Appearance.BodyColor : Color.white,
                        isLocal || knowledge != null && knowledge.IsCurrentlyVisible(slot)),
                    isLocal,
                    displayContext);
            }
            _dangerPositions.Clear();
            if (local != null)
            {
                _dangerPositions.AddRange(local.LocalMinePositions);
                _dangerPositions.AddRange(local.CaptureBombardmentTargets());
            }
            _dangerPositions.AddRange(match.BombardmentWarnings);
            mineGraphic.Present(_dangerPositions, _mineBounds, _visibleWorldRadius);
            var eye = local != null ? local.EyePivot : null;
            _localFacingYaw = eye != null ? eye.eulerAngles.y :
                local != null ? local.transform.eulerAngles.y : 0f;
            SetHeading(_localFacingYaw);
            RefreshDeviceMarkers(topology, match);
        }

        public void SetHeading(float yaw)
        {
            if (!followHeading) yaw = 0f;
            projection.otherDotCanvas.localRotation = Quaternion.Euler(0f, 0f, yaw);
            var upright = Quaternion.Euler(0f, 0f, -yaw);
            if (visionGraphic != null)
                visionGraphic.rectTransform.localRotation = upright;
            mineGraphic.SetIconRotation(upright);
            foreach (var room in rooms)
            {
                PlaceRoomIcons(room);
            }
            foreach (var dot in players) dot.rectTransform.localRotation = upright;
            for (var slot = 0; slot < localDirections.Length; slot++)
                localDirections[slot].rectTransform.localRotation =
                    Quaternion.Euler(0f, 0f, yaw - _localFacingYaw);
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
            if (!Application.isPlaying)
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

                room.Tile = exists ? tile : null;
                room.Floor.gameObject.SetActive(exists);
                room.EffectIcon.gameObject.SetActive(exists);
                if (!exists)
                {
                    room.TypeBackdrop.gameObject.SetActive(false);
                    room.EffectBackdrop.gameObject.SetActive(false);
                    continue;
                }

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
                room.Floor.color = isLocal ? localFloorColor : floorColor;
                room.Floor.enabled = !freeform;
                // Start tiles intentionally share the normal-room design.
                var displayedType = tile.TileType;
                // Dynamic shops have a single blue/red device icon; never add a second key icon.
                room.TypeIcon.enabled = displayedType == BoardTileType.Respawn;
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
                PlaceRoomIcons(room);
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
            _presentedPlayerTiles[slot] = null;
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
            _presentedPlayerTiles[slot] = tile;
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
            localDirections[slot].gameObject.SetActive(visible && isLocal);
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

        public void GetEffectAppearance(BoardLandingEffectType effect,
            out BoardMapIconKind kind, out Sprite sprite, out Color tint)
        {
            kind = GetEffectIcon(effect);
            sprite = GetSprite(kind);
            tint = effectIconColors[(int)effect];
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

        private Color GetPlayerMarkerColor(Color playerColor, bool currentlyVisible)
        {
            if (currentlyVisible) return playerColor;
            var brightness = 1f - rememberedPlayerDarken;
            return new Color(playerColor.r * brightness, playerColor.g * brightness,
                playerColor.b * brightness, playerColor.a);
        }

        private static bool UsesFreeformProjection(BoardTopology topology)
        {
            // Authored maps show physical walls, including square-grid maps.
            if (BoardMapRuntimeLoader.ResolveActiveMapRoot(topology) != null)
                return true;
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
                if (gate != null && !gate.IsTemporarilyBlocked && gate.Destination != null &&
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
                if (gate != null && !gate.IsTemporarilyBlocked && gate.Source != null &&
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
                if (outgoing[index] != null && !outgoing[index].IsTemporarilyBlocked &&
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
                    if (gate == null || gate.IsTemporarilyBlocked || gate.Destination == null)
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
            if (gate == null || gate.IsTemporarilyBlocked || gate.Destination == null)
                return false;

            var coordinate = gate.Destination.Coordinate;
            for (var previous = 0; previous < index; previous++)
            {
                var candidate = gates[previous];
                if (candidate != null && !candidate.IsTemporarilyBlocked && candidate.Destination != null &&
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

        private void RefreshTravelRoute(
            BoardTopology topology,
            NetworkMatchState match,
            NetworkPlayerAvatar local)
        {
            var signature = unchecked(ComputeTravelStateSignature(match, local) * 31 + topology.TraversalRevision);
            var dataChanged = !_travelDataValid ||
                              _travelDataTopology != topology ||
                              _travelDataSignature != signature;
            if (dataChanged)
            {
                _travelDataValid = true;
                _travelDataTopology = topology;
                _travelDataSignature = signature;
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

            if (_travelPreview == null || _travelTopology != topology ||
                _travelStart != start ||
                _travelPrimaryDestination != primaryDestination ||
                _travelStartingStep != startingStep ||
                _travelMaximumStep != turnRoll || _travelRevision != topology.TraversalRevision)
            {
                _travelTopology = topology;
                _travelStart = start;
                _travelPrimaryDestination = primaryDestination;
                _travelStartingStep = startingStep;
                _travelMaximumStep = turnRoll;
                _travelRevision = topology.TraversalRevision;
                _travelPreview = start != null && turnRoll > 0
                    ? new BoardTravelRoutePreview(
                        topology,
                        start,
                        startingStep,
                        turnRoll,
                        primaryDestination)
                    : null;
            }

            _travelChoices.Clear();
            _travelEdges.Clear();
            _preferredTravelEdges.Clear();
            if (_travelPreview != null && local != null && local.IsOwner)
            {
                var choices = local.LocalBoardRouteChoices;
                for (var index = 0; index < choices.Count; index++)
                    _travelChoices.Add(choices[index]);
                var consumedStep = Mathf.Max(startingStep,
                    turnRoll - local.LocalRemainingMoves);
                _travelPreview.GetVisibleEdges(
                    _travelChoices, consumedStep, _travelEdges);
                _travelPreview.GetPreferredEdges(
                    _travelChoices, consumedStep, _preferredTravelEdges);
            }
            BoardShopRouteView.SampleRoute(
                _shopRoute, .8f, 0f, _yellowRoutePoints);
            _routeDotLayout.SampleEdges(
                topology, _travelEdges, .8f, 0f, _blueRoutePoints);
            _routeDotLayout.SampleEdges(
                topology, _preferredTravelEdges, .8f, 0f,
                _preferredRoutePoints);
            _routeDotLayout.AlternateOverlap(
                _yellowRoutePoints, _blueRoutePoints,
                _yellowVisiblePoints, _blueVisiblePoints);
            _preferredPointKeys.Clear();
            for (var index = 0; index < _preferredRoutePoints.Count; index++)
                _preferredPointKeys.Add(RoutePointKey(_preferredRoutePoints[index]));
            _blueMainPoints.Clear();
            _blueBranchPoints.Clear();
            for (var index = 0; index < _blueVisiblePoints.Count; index++)
            {
                var point = _blueVisiblePoints[index];
                if (_preferredPointKeys.Contains(RoutePointKey(point)))
                    _blueMainPoints.Add(point);
                else
                    _blueBranchPoints.Add(point);
            }
            }
            if (dataChanged || !_travelDrawValid ||
                _travelDrawBounds.center != _mineBounds.center ||
                _travelDrawBounds.size != _mineBounds.size)
            {
                _travelDrawValid = true;
                _travelDrawBounds = _mineBounds;
                shopRouteGraphic.PresentPoints(
                    _yellowVisiblePoints, _mineBounds);
                travelRouteGraphic.PresentPoints(
                    _blueBranchPoints, _mineBounds);
                preferredTravelRouteGraphic.PresentPoints(
                    _blueMainPoints, _mineBounds);
            }
        }

        private static int ComputeTravelStateSignature(
            NetworkMatchState match, NetworkPlayerAvatar local)
        {
            unchecked
            {
                var hash = match != null && match.IsActionPhase ? 1 : 0;
                hash = hash * 31 + (match != null && match.KeyShopHasLocation
                    ? match.KeyShopLocation.GetHashCode() : 0);
                if (local == null) return hash;
                hash = hash * 31 + local.AssignedSlot;
                hash = hash * 31 + (local.HasLogicalBoardTile
                    ? local.LogicalBoardTileCoordinate.GetHashCode() : 0);
                hash = hash * 31 + local.LocalTurnRoll;
                hash = hash * 31 + local.LocalRemainingMoves;
                hash = hash * 31 + local.LocalTurnRouteStepOffset;
                hash = hash * 31 + (local.HasLocalTurnRouteOrigin
                    ? local.LocalTurnRouteOrigin.GetHashCode() : 0);
                var choices = local.LocalBoardRouteChoices;
                hash = hash * 31 + choices.Count;
                for (var index = 0; index < choices.Count; index++)
                    hash = hash * 31 + choices[index].GetHashCode();
                return hash;
            }
        }

        private static Vector2Int RoutePointKey(Vector3 point)
        {
            return new Vector2Int(
                Mathf.RoundToInt(point.x * 100f),
                Mathf.RoundToInt(point.z * 100f));
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

        private void PlaceRoomIcons(Room room) => LayoutRoomMarkers(room);
    }
}
