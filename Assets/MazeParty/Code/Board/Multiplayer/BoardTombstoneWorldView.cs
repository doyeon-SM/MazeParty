using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class BoardTombstoneWorldView : MonoBehaviour
    {
        [SerializeField] private BoardTombstoneMarker markerPrefab;
        private readonly Dictionary<int, BoardTombstoneMarker> _markers =
            new Dictionary<int, BoardTombstoneMarker>();
        private readonly HashSet<int> _activeIds = new HashSet<int>();
        private readonly List<int> _removedIds = new List<int>();

        public bool HasRequiredReferences => markerPrefab != null;

#if UNITY_EDITOR
        public void Configure(BoardTombstoneMarker prefab)
        {
            markerPrefab = prefab;
        }
#endif

        private void Update()
        {
            if (markerPrefab == null)
            {
                return;
            }

            _activeIds.Clear();
            var match = NetworkMatchState.Instance;
            if (match != null && match.IsSpawned && match.GameplayEnabled)
            {
                for (var index = 0; index < match.Tombstones.Count; index++)
                {
                    var snapshot = match.Tombstones[index];
                    _activeIds.Add(snapshot.Id);
                    if (!_markers.TryGetValue(snapshot.Id, out var marker) || marker == null)
                    {
                        marker = Instantiate(markerPrefab, transform);
                        marker.name = "Tombstone " + snapshot.Id;
                        _markers[snapshot.Id] = marker;
                    }

                    marker.transform.position = match.GetTombstoneDisplayPosition(index);
                    marker.Configure(snapshot.Id, snapshot.Gold);
                }
            }

            _removedIds.Clear();
            foreach (var pair in _markers)
            {
                if (!_activeIds.Contains(pair.Key))
                {
                    _removedIds.Add(pair.Key);
                }
            }
            for (var index = 0; index < _removedIds.Count; index++)
            {
                var id = _removedIds[index];
                if (_markers.TryGetValue(id, out var marker) && marker != null)
                {
                    Destroy(marker.gameObject);
                }
                _markers.Remove(id);
            }
        }
    }
}
