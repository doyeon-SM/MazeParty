using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public readonly struct KeyShopWorldMarkerEvent
    {
        public KeyShopWorldMarkerEvent(
            KeyShopLifecycleState state,
            bool hasLocation,
            Vector2Int coordinate,
            BoardTile tile,
            Transform marker,
            int revision)
        {
            State = state;
            HasLocation = hasLocation;
            Coordinate = coordinate;
            Tile = tile;
            Marker = marker;
            Revision = revision;
        }

        public KeyShopLifecycleState State { get; }
        public bool HasLocation { get; }
        public Vector2Int Coordinate { get; }
        public BoardTile Tile { get; }
        public Transform Marker { get; }
        public int Revision { get; }
    }

    /// <summary>
    /// Presentation-only adapter for replicated key-shop state. It owns exactly
    /// one lazily-created marker, hides rather than destroys it, and has no
    /// dependency on the multiplayer assembly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyShopWorldMarker : MonoBehaviour
    {
        public const string WorldLabel = "KEY SHOP";
        public const float DefaultVerticalOffset = 0.12f;

        [SerializeField, Min(0f)] private float verticalOffset = DefaultVerticalOffset;
        [SerializeField] private Color markerColor = new Color(1f, 0.66f, 0.08f, 1f);
        [SerializeField] private Color labelColor = Color.white;

        private GameObject _markerObject;
        private TextMesh _worldText;
        private GameObject _topViewHighlight;
        private bool _topViewHighlightRequested;
        private bool _hasAppliedSnapshot;
        private KeyShopLifecycleState _appliedState = KeyShopLifecycleState.Inactive;
        private bool _appliedHasLocation;
        private Vector2Int _appliedCoordinate;
        private int _lastAppliedRevision = -1;
        private KeyShopLifecycleState _lastRaisedState = KeyShopLifecycleState.Inactive;
        private Vector2Int _lastRaisedCoordinate;
        private int _lastRaisedRevision = -1;

        public event Action<KeyShopWorldMarkerEvent> StateApplied;
        public event Action<KeyShopWorldMarkerEvent> Preparing;
        public event Action<KeyShopWorldMarkerEvent> Appearing;
        public event Action<KeyShopWorldMarkerEvent> Activated;

        public GameObject MarkerObject => _markerObject;
        public TextMesh WorldTextMesh => _worldText;
        public KeyShopLifecycleState AppliedState => _appliedState;
        public bool AppliedHasLocation => _appliedHasLocation;
        public Vector2Int AppliedCoordinate => _appliedCoordinate;
        public int LastAppliedRevision => _lastAppliedRevision;
        public bool IsVisible => _markerObject != null && _markerObject.activeSelf;

        public void SetTopViewHighlight(bool highlighted)
        {
            _topViewHighlightRequested = highlighted;
            if (_topViewHighlight != null)
            {
                _topViewHighlight.SetActive(highlighted);
            }
        }

        /// <summary>
        /// Applies a complete replicated snapshot. Calls with an older revision,
        /// or a lifecycle regression within the same revision, are ignored.
        /// Appearing and Active both display the marker at the resolved tile.
        /// </summary>
        public bool ApplyReplicatedState(
            KeyShopLifecycleState state,
            bool hasLocation,
            Vector2Int coordinate,
            BoardTopology topology,
            int revision)
        {
            if (revision < 0)
                throw new ArgumentOutOfRangeException(nameof(revision));
            if (_hasAppliedSnapshot && revision < _lastAppliedRevision)
                return false;
            if (_hasAppliedSnapshot && revision == _lastAppliedRevision &&
                LifecycleOrder(state) < LifecycleOrder(_appliedState))
            {
                return false;
            }

            var snapshotChanged = !_hasAppliedSnapshot ||
                                  revision != _lastAppliedRevision ||
                                  state != _appliedState ||
                                  hasLocation != _appliedHasLocation ||
                                  coordinate != _appliedCoordinate;

            _hasAppliedSnapshot = true;
            _appliedState = state;
            _appliedHasLocation = hasLocation;
            _appliedCoordinate = coordinate;
            _lastAppliedRevision = revision;

            BoardTile resolvedTile = null;
            var shouldDisplay = state == KeyShopLifecycleState.Appearing ||
                                state == KeyShopLifecycleState.Active;
            var locationResolved = !shouldDisplay;
            if (shouldDisplay && hasLocation && topology != null &&
                topology.TryGetTile(coordinate, out resolvedTile) && resolvedTile != null)
            {
                EnsureMarker();
                _markerObject.transform.position =
                    resolvedTile.WorldCenter + Vector3.up * verticalOffset;
                _markerObject.SetActive(true);
                locationResolved = true;
            }
            else
            {
                SetMarkerVisible(false);
            }

            if (snapshotChanged)
            {
                var appliedEvent = new KeyShopWorldMarkerEvent(
                    state,
                    hasLocation,
                    coordinate,
                    resolvedTile,
                    _markerObject != null ? _markerObject.transform : null,
                    revision);
                StateApplied?.Invoke(appliedEvent);
                RaiseStageHook(appliedEvent, locationResolved);
            }
            else if (locationResolved)
            {
                // A topology can become available after the replicated snapshot.
                // Permit a same-revision retry to raise a previously deferred hook.
                var appliedEvent = new KeyShopWorldMarkerEvent(
                    state,
                    hasLocation,
                    coordinate,
                    resolvedTile,
                    _markerObject != null ? _markerObject.transform : null,
                    revision);
                RaiseStageHook(appliedEvent, true);
            }

            return locationResolved;
        }

        private void RaiseStageHook(KeyShopWorldMarkerEvent appliedEvent, bool locationResolved)
        {
            var state = appliedEvent.State;
            var requiresResolvedLocation = state == KeyShopLifecycleState.Appearing ||
                                           state == KeyShopLifecycleState.Active;
            if (requiresResolvedLocation && !locationResolved)
                return;
            if (_lastRaisedRevision == appliedEvent.Revision &&
                _lastRaisedState == state &&
                (!requiresResolvedLocation || _lastRaisedCoordinate == appliedEvent.Coordinate))
            {
                return;
            }

            _lastRaisedRevision = appliedEvent.Revision;
            _lastRaisedState = state;
            _lastRaisedCoordinate = appliedEvent.Coordinate;
            switch (state)
            {
                case KeyShopLifecycleState.Preparing:
                    Preparing?.Invoke(appliedEvent);
                    break;
                case KeyShopLifecycleState.Appearing:
                    Appearing?.Invoke(appliedEvent);
                    break;
                case KeyShopLifecycleState.Active:
                    Activated?.Invoke(appliedEvent);
                    break;
            }
        }

        private void EnsureMarker()
        {
            if (_markerObject != null)
                return;

            _markerObject = new GameObject("Key Shop World Marker");
            _markerObject.transform.SetParent(transform, true);
            _markerObject.layer = gameObject.layer;

            var baseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseObject.name = "Marker Base";
            baseObject.transform.SetParent(_markerObject.transform, false);
            baseObject.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            baseObject.transform.localScale = new Vector3(1.65f, 0.12f, 1.65f);
            baseObject.layer = gameObject.layer;
            baseObject.AddComponent<KeyShopWorldTarget>();

            var renderer = baseObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                WorldTextOcclusion.ApplyBuildSafeSurface(renderer);
                var properties = new MaterialPropertyBlock();
                properties.SetColor("_BaseColor", markerColor);
                properties.SetColor("_Color", markerColor);
                renderer.SetPropertyBlock(properties);
            }

            var bodyObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bodyObject.name = "Key Shop Target";
            bodyObject.transform.SetParent(_markerObject.transform, false);
            bodyObject.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            bodyObject.transform.localScale = new Vector3(1.35f, 1.7f, 1.35f);
            bodyObject.layer = gameObject.layer;
            bodyObject.AddComponent<KeyShopWorldTarget>();
            var bodyRenderer = bodyObject.GetComponent<Renderer>();
            if (bodyRenderer != null)
            {
                WorldTextOcclusion.ApplyBuildSafeSurface(bodyRenderer);
                var bodyProperties = new MaterialPropertyBlock();
                bodyProperties.SetColor("_BaseColor", markerColor);
                bodyProperties.SetColor("_Color", markerColor);
                bodyRenderer.SetPropertyBlock(bodyProperties);
            }

            var textObject = new GameObject("Key Shop World Text");
            textObject.transform.SetParent(_markerObject.transform, false);
            textObject.transform.localPosition = new Vector3(0f, 1.95f, 0f);
            textObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            textObject.layer = gameObject.layer;
            _worldText = textObject.AddComponent<TextMesh>();
            _worldText.text = WorldLabel + "\nRMB BUY  20 GOLD";
            _worldText.anchor = TextAnchor.MiddleCenter;
            _worldText.alignment = TextAlignment.Center;
            _worldText.fontSize = 48;
            _worldText.characterSize = 0.09f;
            _worldText.color = labelColor;
            WorldTextOcclusion.Apply(_worldText);

            _topViewHighlight = TopViewHighlightUtility.CreateSquareOutline(
                _markerObject.transform,
                "Key Shop Top View Highlight",
                1.12f,
                0.1f,
                0.04f);
            _topViewHighlight.SetActive(_topViewHighlightRequested);

            _markerObject.SetActive(false);
        }

        private void SetMarkerVisible(bool visible)
        {
            if (_markerObject != null && _markerObject.activeSelf != visible)
                _markerObject.SetActive(visible);
        }

        private static int LifecycleOrder(KeyShopLifecycleState state)
        {
            switch (state)
            {
                case KeyShopLifecycleState.Inactive: return 0;
                case KeyShopLifecycleState.Preparing: return 1;
                case KeyShopLifecycleState.Appearing: return 2;
                case KeyShopLifecycleState.Active: return 3;
                default: throw new ArgumentOutOfRangeException(nameof(state));
            }
        }
    }
}
