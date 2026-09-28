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
        [SerializeField, Min(1f)] private float portalWidth = 3f;
        [SerializeField] private Color outlineColor =
            new Color(0.62f, 0.73f, 0.82f, 0.92f);
        [SerializeField] private Color neutralPortalColor =
            new Color(0.75f, 0.82f, 0.88f, 0.72f);
        [SerializeField] private Color passablePortalColor =
            new Color(0.3f, 0.82f, 1f, 0.62f);
        [SerializeField] private Color blockedPortalColor =
            new Color(1f, 0.24f, 0.2f, 0.62f);

        private readonly List<TileMesh> _tiles = new List<TileMesh>(49);
        private readonly List<PortalMesh> _portals = new List<PortalMesh>(64);
        private readonly List<Vector3> _worldVertices = new List<Vector3>(5);
        private Bounds _worldBounds;

        public int PresentedTileCount => _tiles.Count;
        public int PresentedPortalCount => _portals.Count;

        public void Clear()
        {
            if (_tiles.Count == 0 && _portals.Count == 0)
                return;

            _tiles.Clear();
            _portals.Clear();
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
            _portals.Clear();
            _worldBounds = worldBounds;

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

                var isShop = keyShop.HasValue && tile.Coordinate == keyShop.Value;
                var isLocal = tile == localTile;
                var tileColor = isShop
                    ? shopColor
                    : isLocal
                        ? localFloorColor
                        : tile.TileType == BoardTileType.Start
                            ? startColor
                            : tile.TileType == BoardTileType.Respawn
                                ? respawnColor
                                : floorColor;
                _tiles.Add(new TileMesh(points, tileColor));
            }

            var seenPortals = new HashSet<PortalKey>();
            for (var index = 0; index < topology.Gates.Count; index++)
            {
                var gate = topology.Gates[index];
                if (gate == null || gate.Source == null || gate.Destination == null)
                    continue;

                var key = new PortalKey(gate);
                if (!seenPortals.Add(key))
                    continue;

                var color = neutralPortalColor;
                if (localTile == gate.Source || localTile == gate.Destination)
                {
                    var canExit = remainingMoves > 0 && HasOutgoingPortal(
                        topology,
                        localTile,
                        gate);
                    color = canExit ? passablePortalColor : blockedPortalColor;
                }

                var halfWidth = gate.transform.right.normalized *
                                (gate.GateWidth * 0.5f);
                _portals.Add(new PortalMesh(
                    Project(gate.PlanePoint - halfWidth),
                    Project(gate.PlanePoint + halfWidth),
                    color));
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
                        outlineColor);
                }
            }

            for (var index = 0; index < _portals.Count; index++)
            {
                var portal = _portals[index];
                AddLine(vh, portal.Start, portal.End, portalWidth, portal.Color);
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

        private static bool HasOutgoingPortal(
            BoardTopology topology,
            BoardTile localTile,
            BoardGate representative)
        {
            if (localTile == null)
                return false;

            var outgoing = topology.GetOutgoingGates(localTile);
            for (var index = 0; index < outgoing.Count; index++)
            {
                var candidate = outgoing[index];
                if (candidate == null || candidate.Destination == null)
                    continue;

                if (new PortalKey(candidate).Equals(new PortalKey(representative)))
                    return true;
            }

            return false;
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

        private readonly struct PortalMesh
        {
            public PortalMesh(Vector2 start, Vector2 end, Color color)
            {
                Start = start;
                End = end;
                Color = color;
            }

            public Vector2 Start { get; }
            public Vector2 End { get; }
            public Color Color { get; }
        }

        private readonly struct PortalKey : IEquatable<PortalKey>
        {
            private const float Quantization = 1000f;
            private readonly Vector2Int _first;
            private readonly Vector2Int _second;
            private readonly Vector3Int _position;

            public PortalKey(BoardGate gate)
            {
                var source = gate.Source.Coordinate;
                var destination = gate.Destination.Coordinate;
                if (Compare(source, destination) <= 0)
                {
                    _first = source;
                    _second = destination;
                }
                else
                {
                    _first = destination;
                    _second = source;
                }

                var position = gate.PlanePoint;
                _position = new Vector3Int(
                    Mathf.RoundToInt(position.x * Quantization),
                    Mathf.RoundToInt(position.y * Quantization),
                    Mathf.RoundToInt(position.z * Quantization));
            }

            public bool Equals(PortalKey other)
            {
                return _first == other._first && _second == other._second &&
                       _position == other._position;
            }

            public override bool Equals(object obj)
            {
                return obj is PortalKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = _first.GetHashCode();
                    hash = hash * 397 ^ _second.GetHashCode();
                    return hash * 397 ^ _position.GetHashCode();
                }
            }

            private static int Compare(Vector2Int left, Vector2Int right)
            {
                var x = left.x.CompareTo(right.x);
                return x != 0 ? x : left.y.CompareTo(right.y);
            }
        }
    }
}
