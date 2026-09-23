using System;
using System.Collections.Generic;
using Arikan;
using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>Heading-up board projection using UnitySimpleMiniMap and authored UI only.</summary>
    public sealed class BoardMinimapView : MonoBehaviour
    {
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
        [SerializeField] private Mask circularMask;
        [SerializeField, Min(0f)] private float radiusInTiles = 2f;
        [SerializeField] private bool followHeading = true;
        private Vector3 _mapCenter;
        [SerializeField] private Room[] rooms = Array.Empty<Room>();
        [SerializeField] private Image[] players = Array.Empty<Image>();
        [SerializeField] private GameObject[] localHighlights = Array.Empty<GameObject>();
        [SerializeField] private Text currentTile;
        [SerializeField] private Text heading;
        [SerializeField] private BoardMapIcon shopDistanceIcon;
        [SerializeField] private Text shopDistanceText;
        [SerializeField] private string shopDistanceFormat = ": {0} TILES";
        [SerializeField] private string shopUnavailableText = ": NOT SPAWNED";
        [SerializeField] private string shopUnreachableText = ": NO ROUTE";
        [SerializeField] private string shopUnknownText = ": --";
        [SerializeField] private Color[] typeIconColors = { Color.gray, Color.white, new Color(1f, .8f, .2f), new Color(.4f, .85f, 1f) };
        [SerializeField] private Color[] effectIconColors = { Color.white, new Color(1f, .8f, .2f), new Color(1f, .3f, .3f), new Color(.85f, .5f, 1f), new Color(.35f, 1f, .5f) };
        [SerializeField] private BoardMapRouteGraphic shopRouteGraphic;
        private readonly List<BoardTile> _shopRoute = new List<BoardTile>();
        private BoardTopology _distanceTopology;
        private Vector2Int? _distanceSource, _distanceShop;
        private bool _hasDistance;
        private int _shopDistance;
        [SerializeField] private string headingFormat = "BOARD  /  {0} ^";
        [SerializeField] private string[] compassPoints = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        [SerializeField] private Color floorColor = new Color(0.12f, 0.2f, 0.28f);
        [SerializeField] private Color localFloorColor = new Color(0.13f, 0.43f, 0.4f);
        [SerializeField] private Color shopColor = new Color(0.6f, 0.39f, 0.08f);
        [SerializeField] private Color exitColor = new Color(0.35f, 0.8f, 1f);
        [SerializeField] private Color blockedExitColor = new Color(1f, 0.46f, 0.27f);
        [SerializeField] private string unknownTileText = "CURRENT TILE  --";
        [SerializeField] private string tileFormat = "CURRENT ({0}, {1})  {2}\n{3}";
        [SerializeField] private string[] tileNames = { "ROOM", "START", "KEY SHOP", "RESPAWN" };
        [SerializeField] private string[] effectNames =
            { "No landing effect", "Landing: Gold +3", "Landing: Gold -3", "Landing: Item", "Landing: HP +50" };

        public bool HasRequiredReferences
        {
            get
            {
                if (shopRouteGraphic == null || projection == null || projection.otherDotCanvas == null ||
                    projection.miniMapBounds == null || projection.miniMapBounds.topRight == null ||
                    projection.miniMapBounds.bottomLeft == null || currentTile == null || heading == null ||
                    rooms == null || players == null || localHighlights == null || tileNames == null ||
                    effectNames == null || compassPoints == null ||
                    shopDistanceIcon == null || shopDistanceText == null || typeIconColors == null ||
                    effectIconColors == null || typeIconColors.Length != 4 || effectIconColors.Length != 5 ||
                    (radiusInTiles > 0f && (circularMask == null || !circularMask.enabled ||
                        circularMask.GetComponent<BoardMapCircleGraphic>() == null)) ||
                    rooms.Length != BoardMapView.CellCount || players.Length != MultiplayerConstants.MaxPlayers ||
                    localHighlights.Length != players.Length || tileNames.Length != 4 || effectNames.Length != 5 || compassPoints.Length != 8)
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

        public void Refresh(BoardTopology topology, NetworkMatchState match, NetworkPlayerAvatar local)
        {
            if (!HasRequiredReferences || topology == null) return;
            PrepareMap(topology, local != null && local.HasLogicalBoardTile
                ? local.LogicalBoardTileCoordinate : (Vector2Int?)null,
                local != null && match.IsActionPhase ? local.LocalRemainingMoves : 0,
                match.KeyShopHasLocation ? match.KeyShopLocation : (Vector2Int?)null,
                local != null ? local.transform.position : (Vector3?)null);
            for (var slot = 0; slot < players.Length; slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                PresentPlayer(slot, avatar != null && avatar.IsSpawned && avatar.HasLogicalBoardTile
                    ? avatar.transform : null, avatar != null ? avatar.Appearance.BodyColor : Color.white,
                    avatar != null && avatar == local);
            }
            var eye = local != null ? local.EyePivot : null;
            SetHeading(eye != null ? eye.eulerAngles.y : local != null ? local.transform.eulerAngles.y : 0f);
        }

        public void SetHeading(float yaw)
        {
            if (!followHeading) yaw = 0f;
            projection.otherDotCanvas.localRotation = Quaternion.Euler(0f, 0f, yaw);
            var upright = Quaternion.Euler(0f, 0f, -yaw);
            foreach (var room in rooms)
            {
                room.Symbol.rectTransform.localRotation = upright;
                room.TypeIcon.rectTransform.localRotation = upright;
                room.EffectIcon.rectTransform.localRotation = upright;
            }
            foreach (var dot in players) dot.rectTransform.localRotation = upright;
            heading.text = string.Format(headingFormat,
                compassPoints[Mathf.RoundToInt(Mathf.Repeat(yaw, 360f) / 45f) % 8]);
        }

        // Also used by the editor preview; network authority remains in Refresh's source data.
        public void PrepareMap(BoardTopology topology, Vector2Int? localCoordinate,
            int remainingMoves, Vector2Int? keyShop, Vector3? focusPosition = null)
        {
            var bounds = new Bounds();
            var hasBounds = false;
            foreach (var tile in topology.Tiles)
            {
                if (tile == null) continue;
                var tileBounds = new Bounds(tile.WorldCenter,
                    new Vector3(BoardTile.RoomSize, 1f, BoardTile.RoomSize));
                if (!hasBounds) { bounds = tileBounds; hasBounds = true; }
                else bounds.Encapsulate(tileBounds);
            }
            if (!hasBounds) return;
            // Keep one scale for both axes so square rooms remain square.
            var extent = Mathf.Max(bounds.size.x, bounds.size.z);
            if (radiusInTiles > 0f)
            {
                extent = radiusInTiles * BoardTile.RoomSize * 2f;
                if (focusPosition.HasValue) bounds.center = focusPosition.Value;
                else if (localCoordinate.HasValue && topology.TryGetTile(localCoordinate.Value, out var focusTile))
                    bounds.center = focusTile.WorldCenter;
            }
            bounds.size = new Vector3(extent, 1f, extent);
            _mapCenter = bounds.center;
            projection.miniMapBounds.bottomLeft.position = bounds.min;
            projection.miniMapBounds.topRight.position = bounds.max;
            currentTile.text = unknownTileText;
            RefreshShopDistance(topology, localCoordinate, keyShop);
            shopRouteGraphic.Present(_shopRoute, bounds);

            for (var index = 0; index < rooms.Length; index++)
            {
                var room = rooms[index];
                var coordinate = new Vector2Int(index % BoardMapView.GridSize, index / BoardMapView.GridSize);
                var exists = topology.TryGetTile(coordinate, out var tile);
                room.Floor.gameObject.SetActive(exists);
                if (!exists) continue;
                projection.Translate(tile.transform, room.Floor.rectTransform);
                room.Floor.rectTransform.localRotation = Quaternion.identity;
                room.Floor.rectTransform.sizeDelta = Vector2.one *
                    (projection.otherDotCanvas.rect.width * BoardTile.RoomSize / extent);
                var isLocal = localCoordinate.HasValue && coordinate == localCoordinate.Value;
                var isShop = keyShop.HasValue && coordinate == keyShop.Value;
                room.Floor.color = isShop ? shopColor : isLocal ? localFloorColor : floorColor;
                room.Symbol.text = string.Empty;
                var displayedType = isShop ? BoardTileType.KeyShop : tile.TileType;
                room.TypeIcon.SetIcon(displayedType == BoardTileType.Start ? BoardMapIconKind.Start :
                    displayedType == BoardTileType.Respawn ? BoardMapIconKind.Respawn :
                    displayedType == BoardTileType.KeyShop ? BoardMapIconKind.Key : BoardMapIconKind.Room);
                room.TypeIcon.color = typeIconColors[(int)displayedType];
                var effect = tile.LandingEffect;
                room.EffectIcon.enabled = effect != BoardLandingEffectType.None;
                room.EffectIcon.SetIcon(effect == BoardLandingEffectType.GoldGain ? BoardMapIconKind.GoldGain :
                    effect == BoardLandingEffectType.GoldLoss ? BoardMapIconKind.GoldLoss :
                    effect == BoardLandingEffectType.ItemReward ? BoardMapIconKind.Item : BoardMapIconKind.Healing);
                room.EffectIcon.color = effectIconColors[(int)effect];
                byte connected = 0, outgoing = 0;
                foreach (var gate in topology.GetOutgoingGates(tile))
                    if (gate != null && gate.Destination != null &&
                        BoardBoundaryWallPolicy.TryGetSide(coordinate, gate.Destination.Coordinate, out var side))
                        outgoing |= (byte)(1 << (int)side);
                connected = outgoing;
                foreach (var gate in topology.GetIncomingGates(tile))
                    if (gate != null && gate.Source != null &&
                        BoardBoundaryWallPolicy.TryGetSide(coordinate, gate.Source.Coordinate, out var side))
                        connected |= (byte)(1 << (int)side);
                for (var side = 0; side < 4; side++)
                {
                    var mask = 1 << side;
                    var canExit = (outgoing & mask) != 0;
                    var blocked = isLocal && (!canExit || remainingMoves <= 0);
                    room.Walls[side].enabled = (connected & mask) == 0 || blocked;
                    room.Exits[side].enabled = false;
                    room.ProgressArrows[side].enabled = isLocal && canExit && remainingMoves > 0;
                    room.Exits[side].color = blocked ? blockedExitColor : exitColor;
                }
                if (isLocal)
                {
                    var type = isShop ? BoardTileType.KeyShop : tile.TileType;
                    currentTile.text = string.Format(tileFormat, coordinate.x, coordinate.y,
                        tileNames[(int)type], effectNames[(int)tile.LandingEffect]);
                }
            }
        }

        public void PresentPlayer(int slot, Transform target, Color color, bool isLocal)
        {
            var dot = players[slot];
            var visible = target != null && IsPositionVisible(target.position);
            dot.gameObject.SetActive(visible);
            localHighlights[slot].SetActive(isLocal);
            if (!visible) return;
            projection.Translate(target, dot.rectTransform);
            dot.rectTransform.localRotation = Quaternion.identity;
            dot.color = color;
            if (isLocal) dot.transform.SetAsLastSibling();
        }

        public bool IsPositionVisible(Vector3 position)
        {
            if (radiusInTiles <= 0f) return true;
            var offset = position - _mapCenter;
            offset.y = 0f;
            var radius = radiusInTiles * BoardTile.RoomSize;
            return offset.sqrMagnitude <= radius * radius;
        }

        private void RefreshShopDistance(BoardTopology topology, Vector2Int? source, Vector2Int? shop)
        {
            if (!_hasDistance || topology != _distanceTopology || source != _distanceSource || shop != _distanceShop)
            {
                _hasDistance = true;
                _distanceTopology = topology; _distanceSource = source; _distanceShop = shop;
                _shopDistance = GetMinimumShopDistance(topology, source, shop);
            }
            shopDistanceText.text = !shop.HasValue ? shopUnavailableText : !source.HasValue ? shopUnknownText :
                _shopDistance < 0 ? shopUnreachableText : string.Format(shopDistanceFormat, _shopDistance);
        }

        public int GetMinimumShopDistance(BoardTopology topology, Vector2Int? source, Vector2Int? shop)
        {
            _shopRoute.Clear();
            if (!source.HasValue || !shop.HasValue || topology == null ||
                !topology.TryGetTile(source.Value, out var from) || !topology.TryGetTile(shop.Value, out var to) ||
                !BoardMapRoute.TryFind(topology, from, to, _shopRoute)) return -1;
            return _shopRoute.Count - 1;
        }
    }
}
