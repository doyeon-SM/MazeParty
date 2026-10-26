using System.Collections.Generic;
using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>Shared world/map dot positions for the yellow shop and blue travel guides.</summary>
    public sealed class BoardRouteDotLayout
    {
        private readonly HashSet<Vector2Int> _seen = new HashSet<Vector2Int>();
        private readonly Dictionary<Vector2Int, int> _yellowIndices =
            new Dictionary<Vector2Int, int>();
        private readonly HashSet<Vector2Int> _blueKeys = new HashSet<Vector2Int>();
        private readonly BoardTile[] _pair = new BoardTile[2];
        private readonly List<Vector3> _segment = new List<Vector3>();

        public void SampleEdges(
            BoardTopology topology,
            IReadOnlyList<BoardRouteChoice> edges,
            float spacing,
            float lift,
            List<Vector3> points)
        {
            points.Clear();
            if (topology == null || edges == null) return;
            _seen.Clear();
            var perimeterOnly = BoardMapRuntimeLoader.ResolveActiveMapRoot(topology)?
                .Definition?.OuterRingTravelGuideOnly == true;
            var min = new Vector2Int(int.MaxValue, int.MaxValue);
            var max = new Vector2Int(int.MinValue, int.MinValue);
            if (perimeterOnly)
                foreach (var tile in topology.Tiles)
                    if (tile != null)
                    {
                        min = Vector2Int.Min(min, tile.Coordinate);
                        max = Vector2Int.Max(max, tile.Coordinate);
                    }
            for (var index = 0; index < edges.Count; index++)
            {
                if (!topology.TryGetTile(edges[index].Source, out _pair[0]) ||
                    !topology.TryGetTile(edges[index].Destination, out _pair[1]))
                    continue;
                var from = edges[index].Source;
                var to = edges[index].Destination;
                // Apply the same authored blue-guide policy to floor, full map,
                // and minimap. Yellow shortest routes use SampleRoute separately.
                if (perimeterOnly &&
                    !(from.x == min.x && to.x == min.x) &&
                    !(from.x == max.x && to.x == max.x) &&
                    !(from.y == min.y && to.y == min.y) &&
                    !(from.y == max.y && to.y == max.y))
                    continue;
                BoardShopRouteView.SampleRoute(_pair, spacing, lift, _segment);
                for (var dot = 0; dot < _segment.Count; dot++)
                    if (_seen.Add(Key(_segment[dot]))) points.Add(_segment[dot]);
            }
        }

        public void AlternateOverlap(
            IReadOnlyList<Vector3> yellow,
            IReadOnlyList<Vector3> blue,
            List<Vector3> yellowVisible,
            List<Vector3> blueVisible)
        {
            yellowVisible.Clear();
            blueVisible.Clear();
            _yellowIndices.Clear();
            _blueKeys.Clear();
            for (var index = 0; index < yellow.Count; index++)
                _yellowIndices[Key(yellow[index])] = index;
            for (var index = 0; index < blue.Count; index++)
                _blueKeys.Add(Key(blue[index]));
            for (var index = 0; index < yellow.Count; index++)
                if (!_blueKeys.Contains(Key(yellow[index])) || index % 2 == 0)
                    yellowVisible.Add(yellow[index]);
            for (var index = 0; index < blue.Count; index++)
            {
                var key = Key(blue[index]);
                if (!_yellowIndices.TryGetValue(key, out var yellowIndex) ||
                    yellowIndex % 2 != 0)
                    blueVisible.Add(blue[index]);
            }
        }

        private static Vector2Int Key(Vector3 position)
        {
            return new Vector2Int(
                Mathf.RoundToInt(position.x * 100f),
                Mathf.RoundToInt(position.z * 100f));
        }
    }
}
