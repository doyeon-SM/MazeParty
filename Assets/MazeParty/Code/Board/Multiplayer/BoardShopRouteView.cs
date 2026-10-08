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
        [SerializeField, Min(.1f)] private float dotSpacing = .8f;
        [SerializeField, Min(0f)] private float surfaceOffset = .012f;
        private readonly List<BoardTile> _route = new List<BoardTile>();
        private readonly List<Vector3> _points = new List<Vector3>();
        private readonly List<GameObject> _dots = new List<GameObject>();
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
                match == null || !match.IsSpawned || !match.GameplayEnabled || !match.KeyShopHasLocation)
            {
                SetVisible(false);
                return;
            }
            if (_topology == null) _topology = GetComponent<BoardTopology>();
            if (_topology == null) _topology = FindAnyObjectByType<BoardTopology>();
            if (_topology == null || !_topology.TryGetTile(local.LogicalBoardTileCoordinate, out var source) ||
                !_topology.TryGetTile(match.KeyShopLocation, out var shop))
            {
                SetVisible(false);
                return;
            }
            PresentRouteForPhase(_topology, source, shop, match.FlowState);
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

        private void OnDisable() { SetVisible(false); }

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
            if (floor is BoxCollider box)
                point.y = box.transform.TransformPoint(box.center + Vector3.up * (box.size.y * .5f)).y;
            else if (floor != null) point.y = floor.bounds.max.y;
            point.y += Mathf.Max(0f, lift);
            return point;
        }
    }
}
