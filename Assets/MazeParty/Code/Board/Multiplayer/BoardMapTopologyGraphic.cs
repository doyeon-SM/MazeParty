using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Prefab-authored map surface used when a board is not representable by the
    /// legacy 7x7 square grid. Runtime code supplies topology data only; this
    /// component owns the actual UI mesh and never creates Canvas objects.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BoardMapTopologyGraphic : MaskableGraphic
    {
        [SerializeField, Min(0.5f)] private float outlineWidth = 1.5f;
        [SerializeField] private Color outlineColor =
            new Color(0.62f, 0.73f, 0.82f, 0.92f);
        [SerializeField] private Color tilesOnlyColor =
            new Color(0.82f, 0.9f, 1f, 0.94f);

        private readonly List<TileMesh> _tiles = new List<TileMesh>(49);
        [SerializeField, Min(0.5f)] private float wallWidth = 2.5f;
        [SerializeField] private Color wallColor = new Color(0.82f, 0.88f, 0.94f, 1f);
        private readonly List<WallMesh> _walls = new List<WallMesh>(128);
        private BoardMapRoot _wallMap;
        private BoxCollider[] _wallColliders = Array.Empty<BoxCollider>();
        private bool _tilesOnly;
        private readonly List<Vector3> _worldVertices = new List<Vector3>(5);
        private Bounds _worldBounds;

        public int PresentedTileCount => _tiles.Count;
        public int PresentedWallCount => _walls.Count;

        public void Clear()
        {
            if (_tiles.Count == 0 && _walls.Count == 0)
                return;

            _tiles.Clear();
            _walls.Clear();
            SetVerticesDirty();
        }

        public void Present(
            BoardTopology topology,
            Bounds worldBounds,
            Vector2Int? localCoordinate,
            int remainingMoves,
            Vector2Int? keyShop,
            Color floorColor,
            Color localFloorColor,
            Color shopColor,
            Color startColor,
            Color respawnColor)
        {
            _tiles.Clear();
            _walls.Clear();
            _worldBounds = worldBounds;
            _tilesOnly = false;

            if (topology == null || worldBounds.size.x <= 0f ||
                worldBounds.size.z <= 0f)
            {
                SetVerticesDirty();
                return;
            }

            BoardTile localTile = null;
            if (localCoordinate.HasValue)
                topology.TryGetTile(localCoordinate.Value, out localTile);

            for (var index = 0; index < topology.Tiles.Count; index++)
            {
                var tile = topology.Tiles[index];
                if (tile == null)
                    continue;

                _worldVertices.Clear();
                tile.GetWorldFootprintVertices(_worldVertices);
                if (_worldVertices.Count < BoardTileFootprint.MinVertexCount)
                    continue;

                var points = new Vector2[_worldVertices.Count];
                for (var vertex = 0; vertex < _worldVertices.Count; vertex++)
                    points[vertex] = Project(_worldVertices[vertex]);

                var isLocal = tile == localTile;
                var tileColor = isLocal
                        ? localFloorColor
                        : tile.TileType == BoardTileType.Start
                            ? startColor
                            : tile.TileType == BoardTileType.Respawn
                                ? respawnColor
                                : floorColor;
                _tiles.Add(new TileMesh(points, tileColor));
            }

            PresentWalls(BoardMapRuntimeLoader.ResolveActiveMapRoot(topology));

            SetVerticesDirty();
        }

        /// <summary>
        /// Presents the authored board footprint without route, portal, player,
        /// shop, start, or respawn information. Lobby map previews intentionally
        /// show only the physical board tiles.
        /// </summary>
        public void PresentTilesOnly(BoardTopology topology)
        {
            _tilesOnly = true;
            _tiles.Clear();
            _walls.Clear();

            if (topology == null)
            {
                SetVerticesDirty();
                return;
            }

            var bounds = new Bounds();
            var hasBounds = false;
            for (var index = 0; index < topology.Tiles.Count; index++)
            {
                var tile = topology.Tiles[index];
                if (tile == null)
                    continue;

                _worldVertices.Clear();
                tile.GetWorldFootprintVertices(_worldVertices);
                for (var vertex = 0; vertex < _worldVertices.Count; vertex++)
                {
                    if (!hasBounds)
                    {
                        bounds = new Bounds(
                            _worldVertices[vertex],
                            Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(_worldVertices[vertex]);
                    }
                }
            }

            if (!hasBounds)
            {
                SetVerticesDirty();
                return;
            }

            var extent = Mathf.Max(0.1f, Mathf.Max(bounds.size.x, bounds.size.z));
            extent *= 1.08f;
            bounds.size = new Vector3(extent, 1f, extent);
            _worldBounds = bounds;

            for (var index = 0; index < topology.Tiles.Count; index++)
            {
                var tile = topology.Tiles[index];
                if (tile == null)
                    continue;

                _worldVertices.Clear();
                tile.GetWorldFootprintVertices(_worldVertices);
                if (_worldVertices.Count < BoardTileFootprint.MinVertexCount)
                    continue;

                var points = new Vector2[_worldVertices.Count];
                for (var vertex = 0; vertex < _worldVertices.Count; vertex++)
                    points[vertex] = Project(_worldVertices[vertex]);
                _tiles.Add(new TileMesh(points, tilesOnlyColor));
            }

            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            for (var index = 0; index < _tiles.Count; index++)
            {
                var tile = _tiles[index];
                AddPolygon(vh, tile.Points, tile.Color);
                for (var edge = 0; edge < tile.Points.Length; edge++)
                {
                    AddLine(
                        vh,
                        tile.Points[edge],
                        tile.Points[(edge + 1) % tile.Points.Length],
                        outlineWidth,
                        _tilesOnly ? outlineColor : new Color(outlineColor.r,
                            outlineColor.g, outlineColor.b, outlineColor.a * 0.18f));
                }
            }

            for (var index = 0; index < _walls.Count; index++)
            {
                var wall = _walls[index];
                AddLine(vh, wall.Start, wall.End, wall.Width, wallColor);
            }
        }

        private Vector2 Project(Vector3 worldPoint)
        {
            var offset = worldPoint - _worldBounds.center;
            var rect = rectTransform.rect;
            return rect.center + new Vector2(
                offset.x * rect.width / _worldBounds.size.x,
                offset.z * rect.height / _worldBounds.size.z);
        }

        private void PresentWalls(BoardMapRoot map)
        {
            if (_wallMap != map)
            {
                _wallMap = map;
                var colliders = new HashSet<BoxCollider>();
                if (map != null && map.MapWallSources != null)
                    foreach (var source in map.MapWallSources)
                        if (source != null)
                            foreach (var collider in source.GetComponentsInChildren<BoxCollider>(true))
                                colliders.Add(collider);
                _wallColliders = new BoxCollider[colliders.Count];
                colliders.CopyTo(_wallColliders);
            }

            foreach (var wall in _wallColliders)
            {
                if (wall == null || !wall.enabled || wall.isTrigger ||
                    !wall.gameObject.activeInHierarchy) continue;
                var halfX = wall.transform.TransformVector(Vector3.right * wall.size.x * 0.5f);
                var halfZ = wall.transform.TransformVector(Vector3.forward * wall.size.z * 0.5f);
                var alongX = new Vector2(halfX.x, halfX.z).sqrMagnitude >=
                             new Vector2(halfZ.x, halfZ.z).sqrMagnitude;
                var along = alongX ? halfX : halfZ;
                var across = alongX ? halfZ : halfX;
                var center = wall.transform.TransformPoint(wall.center);
                var width = Vector2.Distance(Project(center - across), Project(center + across));
                _walls.Add(new WallMesh(Project(center - along), Project(center + along),
                    Mathf.Max(wallWidth, width)));
            }
        }

        private static void AddPolygon(
            VertexHelper vh,
            IReadOnlyList<Vector2> points,
            Color color)
        {
            if (points == null || points.Count < 3)
                return;

            var start = vh.currentVertCount;
            for (var index = 0; index < points.Count; index++)
                vh.AddVert(points[index], color, Vector2.zero);
            for (var index = 1; index + 1 < points.Count; index++)
                vh.AddTriangle(start, start + index, start + index + 1);
        }

        private static void AddLine(
            VertexHelper vh,
            Vector2 start,
            Vector2 end,
            float width,
            Color color)
        {
            var delta = end - start;
            if (delta.sqrMagnitude <= 0.000001f)
                return;

            var perpendicular = new Vector2(-delta.y, delta.x).normalized *
                                (Mathf.Max(0.5f, width) * 0.5f);
            var vertex = vh.currentVertCount;
            vh.AddVert(start - perpendicular, color, Vector2.zero);
            vh.AddVert(start + perpendicular, color, Vector2.zero);
            vh.AddVert(end + perpendicular, color, Vector2.zero);
            vh.AddVert(end - perpendicular, color, Vector2.zero);
            vh.AddTriangle(vertex, vertex + 1, vertex + 2);
            vh.AddTriangle(vertex, vertex + 2, vertex + 3);
        }

        private readonly struct TileMesh
        {
            public TileMesh(Vector2[] points, Color color)
            {
                Points = points;
                Color = color;
            }

            public Vector2[] Points { get; }
            public Color Color { get; }
        }

        private readonly struct WallMesh
        {
            public WallMesh(Vector2 start, Vector2 end, float width)
            {
                Start = start;
                End = end;
                Width = width;
            }

            public Vector2 Start { get; }
            public Vector2 End { get; }
            public float Width { get; }
        }
    }
}
