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
                    if (next == null || previous.ContainsKey(next))
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
    }
}
