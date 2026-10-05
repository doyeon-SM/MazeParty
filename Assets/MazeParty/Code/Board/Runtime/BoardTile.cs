using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public enum BoardTileType
    {
        Normal,
        Start,
        KeyShop,
        Respawn
    }

    [DisallowMultipleComponent]
    public sealed class BoardTile : MonoBehaviour
    {
        public const float RoomSize = 8f;
        public const float HalfRoomSize = RoomSize * 0.5f;

        [SerializeField] private Vector2Int coordinate;
        [SerializeField] private BoardTileType tileType = BoardTileType.Normal;
        [SerializeField] private bool drawDebugBoundary = true;
        [SerializeField] private Renderer landingEffectRenderer;
        private BoardTileFootprint footprint;

        private readonly HashSet<BoardTraversalState> _occupants = new HashSet<BoardTraversalState>();
        private readonly List<Vector3> _debugFootprintVertices = new List<Vector3>(
            BoardTileFootprint.MaxVertexCount);
        private BoardLandingEffectType _landingEffect;

        public Vector2Int Coordinate => coordinate;
        public BoardTileType TileType => tileType;
        public Vector3 WorldCenter => transform.position;
        public BoardTileFootprint Footprint
        {
            get
            {
                ResolveFootprint();
                return footprint;
            }
        }
        public bool HasCustomFootprint =>
            Footprint != null && Footprint.HasAuthoredShape;
        public int OccupancyCount => _occupants.Count;
        public IReadOnlyCollection<BoardTraversalState> Occupants => _occupants;
        public BoardLandingEffectType LandingEffect => _landingEffect;

        private void Awake()
        {
            ResolveFootprint();
            ResolveLandingEffectRenderer();
            HidePersistentWorldPresentation();
        }

        public void Configure(Vector2Int gridCoordinate, BoardTileType type)
        {
            coordinate = gridCoordinate;
            tileType = type;
        }

        public void ApplyLandingEffectPresentation(BoardLandingEffectType effect)
        {
            _landingEffect = tileType == BoardTileType.Respawn
                ? BoardLandingEffectType.None
                : effect;
            ResolveLandingEffectRenderer();
            HidePersistentWorldPresentation();
        }

        private void HidePersistentWorldPresentation()
        {
            // Tiles remain authoritative navigation and landing-effect anchors,
            // but their persistent world geometry is intentionally map-only.
            // The minimap reads LandingEffect above and renders its own icon.
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                renderers[index].enabled = false;
            }

            if (landingEffectRenderer != null)
            {
                landingEffectRenderer.SetPropertyBlock(null);
            }
        }

        public bool ContainsHorizontalPoint(Vector3 worldPoint, float tolerance = 0f)
        {
            if (TryGetValidFootprint(out var authoredFootprint))
            {
                return authoredFootprint.ContainsHorizontalPoint(
                    worldPoint,
                    tolerance);
            }

            var offset = worldPoint - WorldCenter;
            var horizontal = Vector3.Dot(offset, transform.right.normalized);
            var depth = Vector3.Dot(offset, transform.forward.normalized);
            var extent = HalfRoomSize + Mathf.Max(0f, tolerance);
            return Mathf.Abs(horizontal) <= extent && Mathf.Abs(depth) <= extent;
        }

        public bool CanContainHorizontalInset(float inset)
        {
            if (!float.IsFinite(inset))
            {
                return false;
            }

            var safeInset = Mathf.Max(0f, inset);
            if (TryGetValidFootprint(out var authoredFootprint))
            {
                return authoredFootprint.CanContainInset(safeInset);
            }

            return safeInset <= HalfRoomSize;
        }

        public bool ContainsHorizontalDisc(Vector3 worldPoint, float radius)
        {
            if (!float.IsFinite(radius))
            {
                return false;
            }

            var safeRadius = Mathf.Max(0f, radius);
            if (!CanContainHorizontalInset(safeRadius))
            {
                return false;
            }

            var closest = GetClosestPointInside(worldPoint, safeRadius);
            var delta = closest - worldPoint;
            var up = transform.up.normalized;
            delta -= up * Vector3.Dot(delta, up);
            return delta.sqrMagnitude <= 0.000001f;
        }

        public Vector3 GetRecoveryCenter(
            float verticalOffset = 0f,
            float horizontalInset = 0f)
        {
            var center = TryGetValidFootprint(out var authoredFootprint)
                ? authoredFootprint.GetSafeCenter(Mathf.Max(0f, horizontalInset))
                : WorldCenter;
            return center + transform.up.normalized * verticalOffset;
        }

        public Vector3 GetClosestPointInside(Vector3 worldPoint, float inset = 0f)
        {
            if (TryGetValidFootprint(out var authoredFootprint))
            {
                return authoredFootprint.GetClosestPointInside(worldPoint, inset);
            }

            var offset = worldPoint - WorldCenter;
            var right = transform.right.normalized;
            var forward = transform.forward.normalized;
            var up = transform.up.normalized;
            var extent = Mathf.Max(0f, HalfRoomSize - Mathf.Max(0f, inset));
            return WorldCenter +
                   right * Mathf.Clamp(Vector3.Dot(offset, right), -extent, extent) +
                   forward * Mathf.Clamp(Vector3.Dot(offset, forward), -extent, extent) +
                   up * Vector3.Dot(offset, up);
        }

        public void GetWorldFootprintVertices(List<Vector3> target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (TryGetValidFootprint(out var authoredFootprint))
            {
                authoredFootprint.GetWorldVertices(target);
                return;
            }

            target.Clear();
            var right = transform.right.normalized * HalfRoomSize;
            var forward = transform.forward.normalized * HalfRoomSize;
            target.Add(WorldCenter - right - forward);
            target.Add(WorldCenter + right - forward);
            target.Add(WorldCenter + right + forward);
            target.Add(WorldCenter - right + forward);
        }

        public Bounds GetWorldFootprintBounds()
        {
            if (TryGetValidFootprint(out var authoredFootprint))
            {
                return authoredFootprint.GetWorldBounds();
            }

            var right = transform.right.normalized * HalfRoomSize;
            var forward = transform.forward.normalized * HalfRoomSize;
            var bounds = new Bounds(WorldCenter - right - forward, Vector3.zero);
            bounds.Encapsulate(WorldCenter + right - forward);
            bounds.Encapsulate(WorldCenter + right + forward);
            bounds.Encapsulate(WorldCenter - right + forward);
            return bounds;
        }

        public bool IsOccupiedBy(BoardTraversalState traversal)
        {
            return traversal != null && _occupants.Contains(traversal);
        }

        internal void Register(BoardTraversalState traversal)
        {
            if (traversal != null)
                _occupants.Add(traversal);
        }

        internal void Unregister(BoardTraversalState traversal)
        {
            if (traversal != null)
                _occupants.Remove(traversal);
        }

        private void ResolveLandingEffectRenderer()
        {
            var surface = transform.Find("Landing Effect Surface");
            var surfaceRenderer = surface != null
                ? surface.GetComponent<Renderer>()
                : null;
            if (surfaceRenderer != null)
            {
                landingEffectRenderer = surfaceRenderer;
                return;
            }

            if (landingEffectRenderer == null)
            {
                landingEffectRenderer = GetComponent<Renderer>();
            }
        }

        internal void BindFootprint(BoardTileFootprint authoredFootprint)
        {
            footprint = authoredFootprint;
        }

        private void ResolveFootprint()
        {
            if (footprint == null)
            {
                footprint = GetComponent<BoardTileFootprint>();
            }
        }

        private bool TryGetValidFootprint(out BoardTileFootprint authoredFootprint)
        {
            authoredFootprint = Footprint;
            return authoredFootprint != null && authoredFootprint.TryValidate(out _);
        }

        private void OnDrawGizmos()
        {
            if (!drawDebugBoundary)
                return;

            var previousColor = Gizmos.color;
            Gizmos.color = tileType == BoardTileType.Start
                ? Color.green
                : tileType == BoardTileType.KeyShop
                    ? Color.yellow
                    : tileType == BoardTileType.Respawn
                        ? Color.cyan
                        : new Color(0.35f, 0.65f, 1f);

            GetWorldFootprintVertices(_debugFootprintVertices);
            var lift = transform.up.normalized * 0.02f;
            for (var i = 0; i < _debugFootprintVertices.Count; i++)
            {
                Gizmos.DrawLine(
                    _debugFootprintVertices[i] + lift,
                    _debugFootprintVertices[(i + 1) % _debugFootprintVertices.Count] + lift);
            }

            Gizmos.color = previousColor;
        }
    }
}
