using System.Collections.Generic;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace MazeParty.Multiplayer
{
    /// <summary>Client-local floor guide. No route objects or positions are network spawned.</summary>
    [DisallowMultipleComponent]
    public sealed class BoardShopRouteView : MonoBehaviour
    {
        [FormerlySerializedAs("hemispherePrefab")]
        [SerializeField] private GameObject routeMarkerPrefab;
        [SerializeField] private GameObject travelMarkerPrefab;
        [SerializeField, Min(.1f)] private float dotSpacing = .8f;
        [SerializeField, Min(0f)] private float surfaceOffset = .012f;
        [SerializeField, Range(0f, 1f)] private float branchOpacity = .65f;
        private readonly List<BoardTile> _route = new List<BoardTile>();
        private readonly List<Vector3> _points = new List<Vector3>();
        private readonly List<GameObject> _dots = new List<GameObject>();
        private readonly List<GameObject> _travelDots = new List<GameObject>();
        private readonly List<GameObject> _travelBranchDots = new List<GameObject>();
        private readonly List<BoardRouteChoice> _choices = new List<BoardRouteChoice>();
        private readonly List<BoardRouteChoice> _edges = new List<BoardRouteChoice>();
        private readonly List<BoardRouteChoice> _preferredEdges =
            new List<BoardRouteChoice>();
        private readonly List<Vector3> _travelPoints = new List<Vector3>();
        private readonly List<Vector3> _preferredPoints = new List<Vector3>();
        private readonly List<Vector3> _yellowVisible = new List<Vector3>();
        private readonly List<Vector3> _blueVisible = new List<Vector3>();
        private readonly List<Vector3> _blueMain = new List<Vector3>();
        private readonly List<Vector3> _blueBranch = new List<Vector3>();
        private readonly HashSet<Vector2Int> _preferredPointKeys =
            new HashSet<Vector2Int>();
        private readonly BoardRouteDotLayout _routeDotLayout =
            new BoardRouteDotLayout();
        private BoardTravelRoutePreview _travelPreview;
        private BoardTile _travelOrigin, _travelDestination;
        private int _travelRoll, _travelOffset;
        private int _travelStateSignature;
        private bool _travelStateValid;
        private BoardTopology _travelSignatureTopology;
        private BoardTopology _topology;
        private BoardTopology _lastTopology;
        private BoardTile _lastSource, _lastShop;
        private bool _hasRoute, _visible;
        private int _dotCount;

        public bool HasRequiredReferences
        {
            get
            {
                if (routeMarkerPrefab == null ||
                    routeMarkerPrefab.GetComponentInChildren<Collider>(true) != null ||
                    routeMarkerPrefab.GetComponentInChildren<Collider2D>(true) != null ||
                    routeMarkerPrefab.GetComponentInChildren<NetworkObject>(true) != null)
                {
                    return false;
                }

                var particles = routeMarkerPrefab.GetComponentsInChildren<
                    ParticleSystem>(true);
                var renderer = particles.Length == 1
                    ? particles[0].GetComponent<ParticleSystemRenderer>()
                    : null;
                if (renderer == null || renderer.sharedMaterial == null ||
                    renderer.sharedMaterial.shader == null)
                {
                    return false;
                }

                var main = particles[0].main;
                return main.loop && main.playOnAwake;
            }
        }

#if UNITY_EDITOR
        public void Configure(GameObject prefab) { routeMarkerPrefab = prefab; }
#endif

        private void LateUpdate()
        {
            var manager = NetworkManager.Singleton;
            var match = NetworkMatchState.Instance;
            var playerObject = manager != null && manager.IsClient && manager.SpawnManager != null
                ? manager.SpawnManager.GetLocalPlayerObject() : null;
            var local = playerObject != null ? playerObject.GetComponent<NetworkPlayerAvatar>() : null;
            if (local == null || !local.IsOwner || !local.IsSpawned || !local.HasLogicalBoardTile ||
                match == null || !match.IsSpawned || !match.GameplayEnabled)
            {
                SetVisible(false);
                SetTravelVisible(false);
                _travelStateValid = false;
                return;
            }
            if (match.ActiveBoardTopology != null) _topology = match.ActiveBoardTopology;
            if (_topology == null) _topology = GetComponent<BoardTopology>();
            if (_topology == null) _topology = FindAnyObjectByType<BoardTopology>();
            if (_topology == null || !_topology.TryGetTile(local.LogicalBoardTileCoordinate, out var source))
            {
                SetVisible(false);
                SetTravelVisible(false);
                _travelStateValid = false;
                return;
            }
            BoardTile shop = null;
            if (match.KeyShopHasLocation)
                _topology.TryGetTile(match.KeyShopLocation, out shop);
            if (match.IsActionPhase && travelMarkerPrefab != null)
                PresentTravelRoute(_topology, local, source, shop);
            else
            {
                _hasRoute = false;
                _travelStateValid = false;
                SetTravelVisible(false);
                PresentRouteForPhase(_topology, source, shop, match.FlowState);
            }
        }

        private void PresentTravelRoute(
            BoardTopology topology,
            NetworkPlayerAvatar local,
            BoardTile source,
            BoardTile shop)
        {
            var signature = ComputeTravelSignature(local, source, shop);
            if (_travelStateValid && topology == _travelSignatureTopology &&
                signature == _travelStateSignature)
                return;
            _travelStateSignature = signature;
            _travelStateValid = true;
            _travelSignatureTopology = topology;
            _route.Clear();
            if (shop != null && BoardMapRoute.TryFind(topology, source, shop, _route))
                SampleRoute(_route, dotSpacing, surfaceOffset, _points);
            else
                _points.Clear();

            var roll = local.LocalTurnRoll;
            var offset = Mathf.Clamp(local.LocalTurnRouteStepOffset, 0, roll);
            BoardTile origin = null;
            if (local.HasLocalTurnRouteOrigin)
                topology.TryGetTile(local.LocalTurnRouteOrigin, out origin);
            if (origin == null || roll <= 0)
                _travelPreview = null;
            else if (_travelPreview == null || topology != _lastTopology ||
                     origin != _travelOrigin || shop != _travelDestination ||
                     roll != _travelRoll || offset != _travelOffset)
            {
                _travelPreview = new BoardTravelRoutePreview(
                    topology, origin, offset, roll, shop);
                _travelOrigin = origin;
                _travelDestination = shop;
                _travelRoll = roll;
                _travelOffset = offset;
            }
            _lastTopology = topology;
            _choices.Clear();
            _edges.Clear();
            _preferredEdges.Clear();
            if (_travelPreview != null)
            {
                var choices = local.LocalBoardRouteChoices;
                for (var index = 0; index < choices.Count; index++)
                    _choices.Add(choices[index]);
                var consumedStep = Mathf.Max(
                    offset, roll - local.LocalRemainingMoves);
                _travelPreview.GetVisibleEdges(
                    _choices, consumedStep, _edges);
                _travelPreview.GetPreferredEdges(
                    _choices, consumedStep, _preferredEdges);
            }
            _routeDotLayout.SampleEdges(
                topology, _edges, dotSpacing, surfaceOffset, _travelPoints);
            _routeDotLayout.SampleEdges(
                topology, _preferredEdges, dotSpacing, surfaceOffset,
                _preferredPoints);
            _routeDotLayout.AlternateOverlap(
                _points, _travelPoints, _yellowVisible, _blueVisible);
            _preferredPointKeys.Clear();
            for (var index = 0; index < _preferredPoints.Count; index++)
                _preferredPointKeys.Add(RoutePointKey(_preferredPoints[index]));
            _blueMain.Clear();
            _blueBranch.Clear();
            for (var index = 0; index < _blueVisible.Count; index++)
            {
                var point = _blueVisible[index];
                if (_preferredPointKeys.Contains(RoutePointKey(point)))
                    _blueMain.Add(point);
                else
                    _blueBranch.Add(point);
            }
            PresentDots(_dots, routeMarkerPrefab, _yellowVisible,
                "Local key shop route glow ", false);
            PresentDots(_travelDots, travelMarkerPrefab, _blueMain,
                "Local travel route glow ", false);
            PresentDots(_travelBranchDots, travelMarkerPrefab, _blueBranch,
                "Local travel branch glow ", true);
            _dotCount = _yellowVisible.Count;
            _visible = true;
            _hasRoute = false;
        }

        private static Vector2Int RoutePointKey(Vector3 point)
        {
            return new Vector2Int(
                Mathf.RoundToInt(point.x * 100f),
                Mathf.RoundToInt(point.z * 100f));
        }

        private static int ComputeTravelSignature(
            NetworkPlayerAvatar local, BoardTile source, BoardTile shop)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + source.Coordinate.GetHashCode();
                hash = hash * 31 + (shop != null ? shop.Coordinate.GetHashCode() : 0);
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

        private void PresentDots(List<GameObject> pool, GameObject prefab,
            IReadOnlyList<Vector3> positions, string prefix, bool dim)
        {
            if (prefab == null) return;
            while (pool.Count < positions.Count)
            {
                var dot = Instantiate(prefab, transform);
                dot.name = prefix + pool.Count;
                dot.SetActive(false);
                if (dim)
                {
                    foreach (var particles in dot.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        var main = particles.main;
                        var tint = main.startColor;
                        var low = tint.colorMin;
                        var high = tint.colorMax;
                        low.a *= branchOpacity;
                        high.a *= branchOpacity;
                        tint.colorMin = low;
                        tint.colorMax = high;
                        main.startColor = tint;
                    }
                }
                pool.Add(dot);
            }
            for (var index = 0; index < pool.Count; index++)
            {
                if (index < positions.Count)
                    pool[index].transform.position = positions[index];
                pool[index].SetActive(index < positions.Count);
            }
        }

        private void SetTravelVisible(bool visible)
        {
            for (var index = 0; index < _travelDots.Count; index++)
                if (_travelDots[index] != null)
                    _travelDots[index].SetActive(visible && index < _blueMain.Count);
            for (var index = 0; index < _travelBranchDots.Count; index++)
                if (_travelBranchDots[index] != null)
                    _travelBranchDots[index].SetActive(visible && index < _blueBranch.Count);
        }

        public void PresentRouteForPhase(BoardTopology topology, BoardTile source, BoardTile shop, BoardFlowState phase)
        {
            if (phase != BoardFlowState.TurnOverview && phase != BoardFlowState.Descending && phase != BoardFlowState.Action)
            {
                SetVisible(false);
                return;
            }
            PresentRoute(topology, source, shop);
        }
        public void PresentRoute(BoardTopology topology, BoardTile source, BoardTile shop)
        {
            if (routeMarkerPrefab == null) return;
            if (!_hasRoute || topology != _lastTopology || source != _lastSource || shop != _lastShop)
            {
                _hasRoute = true;
                _lastTopology = topology; _lastSource = source; _lastShop = shop;
                _points.Clear();
                if (BoardMapRoute.TryFind(topology, source, shop, _route))
                    SampleRoute(_route, dotSpacing, surfaceOffset, _points);
                _dotCount = _points.Count;
                while (_dots.Count < _dotCount)
                {
                    var dot = Instantiate(routeMarkerPrefab, transform);
                    dot.name = "Local key shop route glow " + _dots.Count;
                    _dots.Add(dot);
                }
                for (var index = 0; index < _dots.Count; index++)
                {
                    if (index < _dotCount) _dots[index].transform.position = _points[index];
                    _dots[index].SetActive(index < _dotCount);
                }
                _visible = true;
            }
            SetVisible(true);
        }

        public void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            for (var index = 0; index < _dots.Count; index++)
                if (_dots[index] != null) _dots[index].SetActive(visible && index < _dotCount);
        }

        private void OnDisable()
        {
            SetVisible(false);
            SetTravelVisible(false);
            _travelStateValid = false;
        }

        public static void SampleRoute(IReadOnlyList<BoardTile> route, float spacing, float lift, List<Vector3> points)
        {
            points.Clear();
            if (route == null || route.Count < 2) return;
            spacing = Mathf.Max(.1f, spacing);
            for (var segment = 0; segment + 1 < route.Count; segment++)
            {
                var from = FloorCenter(route[segment], lift);
                var to = FloorCenter(route[segment + 1], lift);
                var intervals = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / spacing));
                for (var step = segment == 0 ? 0 : 1; step <= intervals; step++)
                    points.Add(Vector3.Lerp(from, to, (float)step / intervals));
            }
        }

        private static Vector3 FloorCenter(BoardTile tile, float lift)
        {
            var point = tile.WorldCenter;
            var floor = tile.GetComponent<Collider>();
            // Read the authored box top directly; Collider.bounds can lag behind
            // a transform update until the next physics synchronization.
            if (floor != null && floor.isTrigger) floor = null;
            if (floor is BoxCollider box)
                point.y = box.transform.TransformPoint(box.center + Vector3.up * (box.size.y * .5f)).y;
            else if (floor != null) point.y = floor.bounds.max.y;
            point.y += Mathf.Max(0f, lift);
            return point;
        }
    }
}
