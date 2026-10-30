using System.Collections.Generic;

namespace MazeParty.Gameplay
{
    /// <summary>Finds a shortest route using only traversable, directed gates.</summary>
    public static class BoardMapRoute
    {
        public static bool TryFind(BoardTopology topology, BoardTile source,
            BoardTile destination, List<BoardTile> route)
        {
            route.Clear();
            if (topology == null || source == null || destination == null)
            {
                return false;
            }

            var previous = new Dictionary<BoardTile, BoardTile> { [source] = null };
            var pending = new Queue<BoardTile>();
            pending.Enqueue(source);
            while (pending.Count > 0 && !previous.ContainsKey(destination))
            {
                var current = pending.Dequeue();
                var outgoing = topology.GetOutgoingGates(current);
                for (var index = 0; index < outgoing.Count; index++)
                {
                    var next = outgoing[index]?.Destination;
                    if (next == null || outgoing[index].IsTemporarilyBlocked || previous.ContainsKey(next))
                    {
                        continue;
                    }
                    previous.Add(next, current);
                    pending.Enqueue(next);
                }
            }

            if (!previous.ContainsKey(destination))
            {
                return false;
            }
            for (var tile = destination; tile != null; tile = previous[tile])
            {
                route.Add(tile);
            }
            route.Reverse();
            return true;
        }
        /// <summary>
        /// Shortest routes to one destination in a snapshot of the directed board.
        /// Recreate with the preview when its topology or destination changes.
        /// Equal-length routes retain the first authored outgoing gate, as TryFind does.
        /// </summary>
        public sealed class DestinationIndex
        {
            private readonly BoardTile _destination;
            private readonly Dictionary<BoardTile, BoardTile> _next =
                new Dictionary<BoardTile, BoardTile>();

            public DestinationIndex(BoardTopology topology, BoardTile destination)
            {
                if (topology == null || destination == null) return;
                _destination = destination;

                var incoming = new Dictionary<BoardTile, List<BoardTile>>();
                foreach (var tile in topology.Tiles)
                {
                    if (tile == null) continue;
                    var outgoing = topology.GetOutgoingGates(tile);
                    for (var i = 0; i < outgoing.Count; i++)
                    {
                        var to = outgoing[i]?.Destination;
                        if (to == null || outgoing[i].IsTemporarilyBlocked) continue;
                        if (!incoming.TryGetValue(to, out var sources))
                            incoming.Add(to, sources = new List<BoardTile>());
                        sources.Add(tile);
                    }
                }

                var distance = new Dictionary<BoardTile, int> { [destination] = 0 };
                var pending = new Queue<BoardTile>();
                pending.Enqueue(destination);
                while (pending.Count > 0)
                {
                    var current = pending.Dequeue();
                    if (!incoming.TryGetValue(current, out var sources)) continue;
                    for (var i = 0; i < sources.Count; i++)
                    {
                        var source = sources[i];
                        if (distance.ContainsKey(source)) continue;
                        distance.Add(source, distance[current] + 1);
                        pending.Enqueue(source);
                    }
                }

                foreach (var pair in distance)
                {
                    if (pair.Value == 0) continue;
                    var outgoing = topology.GetOutgoingGates(pair.Key);
                    for (var i = 0; i < outgoing.Count; i++)
                    {
                        var to = outgoing[i]?.Destination;
                        if (to != null && !outgoing[i].IsTemporarilyBlocked && distance.TryGetValue(to, out var remaining) &&
                            remaining == pair.Value - 1)
                        {
                            _next.Add(pair.Key, to);
                            break;
                        }
                    }
                }
            }

            public bool TryFind(BoardTile source, List<BoardTile> route)
            {
                route.Clear();
                if (source == null || _destination == null ||
                    (source != _destination && !_next.ContainsKey(source))) return false;
                route.Add(source);
                while (source != _destination)
                {
                    source = _next[source];
                    route.Add(source);
                }
                return true;
            }
        }
    }
}
