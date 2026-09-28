using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Optional authored horizontal footprint for a board tile. Vertices use
    /// local X/Z coordinates and must form a strictly convex polygon.
    /// BoardTile keeps its legacy 8x8 footprint when this component is absent.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoardTile))]
    public sealed class BoardTileFootprint : MonoBehaviour
    {
        public const int MinVertexCount = 3;
        public const int MaxVertexCount = 5;

        private const float GeometryEpsilon = 0.0001f;
        private const int ClipBufferCapacity = 16;

        [SerializeField] private Vector2[] localVertices = Array.Empty<Vector2>();

        private Vector2[] _planeVertices = Array.Empty<Vector2>();
        private readonly Vector2[] _clipBufferA = new Vector2[ClipBufferCapacity];
        private readonly Vector2[] _clipBufferB = new Vector2[ClipBufferCapacity];

        public IReadOnlyList<Vector2> LocalVertices =>
            localVertices ?? Array.Empty<Vector2>();
        public int VertexCount => localVertices?.Length ?? 0;
        public bool HasAuthoredShape => VertexCount > 0;

        private void Awake()
        {
            BindToTile();
        }

        private void Reset()
        {
            localVertices = new[]
            {
                new Vector2(-BoardTile.HalfRoomSize, -BoardTile.HalfRoomSize),
                new Vector2(BoardTile.HalfRoomSize, -BoardTile.HalfRoomSize),
                new Vector2(BoardTile.HalfRoomSize, BoardTile.HalfRoomSize),
                new Vector2(-BoardTile.HalfRoomSize, BoardTile.HalfRoomSize)
            };
            BindToTile();
        }

        private void OnValidate()
        {
            localVertices ??= Array.Empty<Vector2>();
            BindToTile();
        }

        public void Configure(IReadOnlyList<Vector2> vertices)
        {
            if (vertices == null)
            {
                localVertices = Array.Empty<Vector2>();
                return;
            }

            localVertices = new Vector2[vertices.Count];
            for (var i = 0; i < vertices.Count; i++)
            {
                localVertices[i] = vertices[i];
            }
        }

        public bool ContainsHorizontalPoint(Vector3 worldPoint, float tolerance = 0f)
        {
            if (!TryPrepareGeometry(out var winding))
            {
                return false;
            }

            var point = WorldToPlane(worldPoint);
            return ContainsPoint(
                _planeVertices,
                VertexCount,
                point,
                Mathf.Max(0f, tolerance),
                winding);
        }

        /// <summary>
        /// Returns whether offsetting every polygon edge inward by the supplied
        /// world-space distance leaves a usable interior. This lets authoring and
        /// runtime validation reject footprints that cannot contain an object's
        /// conservative circular support before closest-point fallback is used.
        /// </summary>
        public bool CanContainInset(float inset)
        {
            if (!float.IsFinite(inset) || !TryPrepareGeometry(out var winding))
            {
                return false;
            }

            return TryBuildUsableInsetPolygon(
                Mathf.Max(0f, inset),
                winding,
                out _,
                out _);
        }

        /// <summary>
        /// Returns the closest horizontal point inside the footprint while
        /// preserving the input's height along the tile's local up axis.
        /// A positive inset keeps the point that distance away from every edge.
        /// </summary>
        public Vector3 GetClosestPointInside(Vector3 worldPoint, float inset = 0f)
        {
            if (!TryPrepareGeometry(out var winding))
            {
                return worldPoint;
            }

            var planePoint = WorldToPlane(worldPoint);
            var height = Vector3.Dot(
                worldPoint - transform.position,
                transform.up.normalized);
            if (!TryBuildUsableInsetPolygon(
                    Mathf.Max(0f, inset),
                    winding,
                    out var insetPolygon,
                    out var count))
            {
                var fallback = CalculateCentroid(_planeVertices, VertexCount);
                return PlaneToWorld(fallback, height);
            }

            var insetWinding = SignedArea(insetPolygon, count) >= 0f ? 1f : -1f;
            if (ContainsPoint(
                    insetPolygon,
                    count,
                    planePoint,
                    0f,
                    insetWinding))
            {
                return worldPoint;
            }

            var closest = insetPolygon[0];
            var closestDistance = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
            {
                var candidate = ClosestPointOnSegment(
                    insetPolygon[i],
                    insetPolygon[(i + 1) % count],
                    planePoint);
                var distance = (candidate - planePoint).sqrMagnitude;
                if (distance < closestDistance)
                {
                    closest = candidate;
                    closestDistance = distance;
                }
            }

            return PlaneToWorld(closest, height);
        }

        public Vector3 GetSafeCenter(float inset = 0f)
        {
            if (!TryPrepareGeometry(out var winding))
            {
                return transform.position;
            }

            var hasUsableInset = TryBuildUsableInsetPolygon(
                Mathf.Max(0f, inset),
                winding,
                out var insetPolygon,
                out var count);
            var center = hasUsableInset
                ? CalculateCentroid(insetPolygon, count)
                : CalculateCentroid(_planeVertices, VertexCount);
            return PlaneToWorld(center, 0f);
        }

        public void GetWorldVertices(List<Vector3> target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            target.Clear();
            if (localVertices == null)
            {
                return;
            }

            for (var i = 0; i < localVertices.Length; i++)
            {
                var vertex = localVertices[i];
                target.Add(transform.TransformPoint(
                    new Vector3(vertex.x, 0f, vertex.y)));
            }
        }

        public Bounds GetWorldBounds()
        {
            if (localVertices == null || localVertices.Length == 0)
            {
                return new Bounds(transform.position, Vector3.zero);
            }

            var first = localVertices[0];
            var bounds = new Bounds(
                transform.TransformPoint(new Vector3(first.x, 0f, first.y)),
                Vector3.zero);
            for (var i = 1; i < localVertices.Length; i++)
            {
                var vertex = localVertices[i];
                bounds.Encapsulate(transform.TransformPoint(
                    new Vector3(vertex.x, 0f, vertex.y)));
            }

            return bounds;
        }

        public bool TryValidate(out string message)
        {
            var count = VertexCount;
            if (count < MinVertexCount || count > MaxVertexCount)
            {
                message = $"Footprint requires {MinVertexCount}-{MaxVertexCount} vertices, but has {count}.";
                return false;
            }

            for (var i = 0; i < count; i++)
            {
                if (!IsFinite(localVertices[i]))
                {
                    message = $"Footprint vertex {i} is not finite.";
                    return false;
                }

                for (var other = i + 1; other < count; other++)
                {
                    if ((localVertices[i] - localVertices[other]).sqrMagnitude <=
                        GeometryEpsilon * GeometryEpsilon)
                    {
                        message = $"Footprint vertices {i} and {other} overlap.";
                        return false;
                    }
                }
            }

            for (var first = 0; first < count; first++)
            {
                var firstNext = (first + 1) % count;
                for (var second = first + 1; second < count; second++)
                {
                    var secondNext = (second + 1) % count;
                    if (first == second || firstNext == second ||
                        secondNext == first)
                    {
                        continue;
                    }

                    if (SegmentsIntersect(
                            localVertices[first],
                            localVertices[firstNext],
                            localVertices[second],
                            localVertices[secondNext]))
                    {
                        message = "Footprint edges must not intersect.";
                        return false;
                    }
                }
            }

            var area = SignedArea(localVertices, count);
            if (Mathf.Abs(area) <= GeometryEpsilon)
            {
                message = "Footprint must enclose a non-zero area.";
                return false;
            }

            var winding = area > 0f ? 1f : -1f;
            for (var i = 0; i < count; i++)
            {
                var previous = localVertices[(i + count - 1) % count];
                var current = localVertices[i];
                var next = localVertices[(i + 1) % count];
                var turn = Cross(current - previous, next - current) * winding;
                if (turn <= GeometryEpsilon)
                {
                    message = "Footprint must be strictly convex with consistently ordered vertices.";
                    return false;
                }
            }

            message = string.Empty;
            return true;
        }

        private void BindToTile()
        {
            var tile = GetComponent<BoardTile>();
            if (tile != null)
            {
                tile.BindFootprint(this);
            }
        }

        private bool TryPrepareGeometry(out float winding)
        {
            winding = 1f;
            if (!TryValidate(out _))
            {
                return false;
            }

            if (_planeVertices.Length != VertexCount)
            {
                _planeVertices = new Vector2[VertexCount];
            }

            var origin = transform.position;
            var right = transform.right.normalized;
            var forward = transform.forward.normalized;
            for (var i = 0; i < VertexCount; i++)
            {
                var local = localVertices[i];
                var world = transform.TransformPoint(new Vector3(local.x, 0f, local.y));
                var offset = world - origin;
                _planeVertices[i] = new Vector2(
                    Vector3.Dot(offset, right),
                    Vector3.Dot(offset, forward));
            }

            var area = SignedArea(_planeVertices, VertexCount);
            if (Mathf.Abs(area) <= GeometryEpsilon)
            {
                return false;
            }

            winding = area > 0f ? 1f : -1f;
            return true;
        }

        private int BuildInsetPolygon(
            float inset,
            float winding,
            out Vector2[] result)
        {
            var input = _clipBufferA;
            var output = _clipBufferB;
            var inputCount = VertexCount;
            for (var i = 0; i < inputCount; i++)
            {
                input[i] = _planeVertices[i];
            }

            for (var edgeIndex = 0; edgeIndex < VertexCount; edgeIndex++)
            {
                var a = _planeVertices[edgeIndex];
                var b = _planeVertices[(edgeIndex + 1) % VertexCount];
                var edge = b - a;
                var inward = winding > 0f
                    ? new Vector2(-edge.y, edge.x)
                    : new Vector2(edge.y, -edge.x);
                inward.Normalize();
                var threshold = Vector2.Dot(inward, a) + inset;

                var outputCount = 0;
                if (inputCount == 0)
                {
                    result = input;
                    return 0;
                }

                var previous = input[inputCount - 1];
                var previousInside =
                    Vector2.Dot(inward, previous) >= threshold - GeometryEpsilon;
                for (var i = 0; i < inputCount; i++)
                {
                    var current = input[i];
                    var currentInside =
                        Vector2.Dot(inward, current) >= threshold - GeometryEpsilon;
                    if (currentInside != previousInside)
                    {
                        output[outputCount++] = IntersectSegmentWithLine(
                            previous,
                            current,
                            inward,
                            threshold);
                    }

                    if (currentInside)
                    {
                        output[outputCount++] = current;
                    }

                    previous = current;
                    previousInside = currentInside;
                }

                var swap = input;
                input = output;
                output = swap;
                inputCount = outputCount;
            }

            result = input;
            return inputCount;
        }

        private bool TryBuildUsableInsetPolygon(
            float inset,
            float winding,
            out Vector2[] result,
            out int count)
        {
            count = BuildInsetPolygon(inset, winding, out result);
            return count >= MinVertexCount &&
                   Mathf.Abs(SignedArea(result, count)) > GeometryEpsilon;
        }

        private Vector2 WorldToPlane(Vector3 worldPoint)
        {
            var offset = worldPoint - transform.position;
            return new Vector2(
                Vector3.Dot(offset, transform.right.normalized),
                Vector3.Dot(offset, transform.forward.normalized));
        }

        private Vector3 PlaneToWorld(Vector2 planePoint, float height)
        {
            return transform.position +
                   transform.right.normalized * planePoint.x +
                   transform.forward.normalized * planePoint.y +
                   transform.up.normalized * height;
        }

        private static bool ContainsPoint(
            IReadOnlyList<Vector2> vertices,
            int count,
            Vector2 point,
            float tolerance,
            float winding)
        {
            for (var i = 0; i < count; i++)
            {
                var a = vertices[i];
                var edge = vertices[(i + 1) % count] - a;
                var side = Cross(edge, point - a) * winding;
                if (side < -tolerance * edge.magnitude - GeometryEpsilon)
                {
                    return false;
                }
            }

            return true;
        }

        private static Vector2 IntersectSegmentWithLine(
            Vector2 from,
            Vector2 to,
            Vector2 normal,
            float threshold)
        {
            var direction = to - from;
            var denominator = Vector2.Dot(normal, direction);
            if (Mathf.Abs(denominator) <= GeometryEpsilon)
            {
                return from;
            }

            var distance = threshold - Vector2.Dot(normal, from);
            return from + direction * Mathf.Clamp01(distance / denominator);
        }

        private static Vector2 ClosestPointOnSegment(
            Vector2 a,
            Vector2 b,
            Vector2 point)
        {
            var edge = b - a;
            var lengthSquared = edge.sqrMagnitude;
            if (lengthSquared <= GeometryEpsilon * GeometryEpsilon)
            {
                return a;
            }

            var t = Mathf.Clamp01(Vector2.Dot(point - a, edge) / lengthSquared);
            return a + edge * t;
        }

        private static Vector2 CalculateCentroid(
            IReadOnlyList<Vector2> vertices,
            int count)
        {
            var weighted = Vector2.zero;
            var areaTwice = 0f;
            for (var i = 0; i < count; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % count];
                var cross = Cross(a, b);
                areaTwice += cross;
                weighted += (a + b) * cross;
            }

            if (Mathf.Abs(areaTwice) <= GeometryEpsilon)
            {
                var average = Vector2.zero;
                for (var i = 0; i < count; i++)
                {
                    average += vertices[i];
                }

                return count > 0 ? average / count : Vector2.zero;
            }

            return weighted / (3f * areaTwice);
        }

        private static float SignedArea(IReadOnlyList<Vector2> vertices, int count)
        {
            var areaTwice = 0f;
            for (var i = 0; i < count; i++)
            {
                areaTwice += Cross(vertices[i], vertices[(i + 1) % count]);
            }

            return areaTwice * 0.5f;
        }

        private static bool SegmentsIntersect(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d)
        {
            var abC = Cross(b - a, c - a);
            var abD = Cross(b - a, d - a);
            var cdA = Cross(d - c, a - c);
            var cdB = Cross(d - c, b - c);

            if (((abC > GeometryEpsilon && abD < -GeometryEpsilon) ||
                 (abC < -GeometryEpsilon && abD > GeometryEpsilon)) &&
                ((cdA > GeometryEpsilon && cdB < -GeometryEpsilon) ||
                 (cdA < -GeometryEpsilon && cdB > GeometryEpsilon)))
            {
                return true;
            }

            return Mathf.Abs(abC) <= GeometryEpsilon && IsOnSegment(a, b, c) ||
                   Mathf.Abs(abD) <= GeometryEpsilon && IsOnSegment(a, b, d) ||
                   Mathf.Abs(cdA) <= GeometryEpsilon && IsOnSegment(c, d, a) ||
                   Mathf.Abs(cdB) <= GeometryEpsilon && IsOnSegment(c, d, b);
        }

        private static bool IsOnSegment(Vector2 a, Vector2 b, Vector2 point)
        {
            return point.x >= Mathf.Min(a.x, b.x) - GeometryEpsilon &&
                   point.x <= Mathf.Max(a.x, b.x) + GeometryEpsilon &&
                   point.y >= Mathf.Min(a.y, b.y) - GeometryEpsilon &&
                   point.y <= Mathf.Max(a.y, b.y) + GeometryEpsilon;
        }

        private static bool IsFinite(Vector2 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y);
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }
    }
}
