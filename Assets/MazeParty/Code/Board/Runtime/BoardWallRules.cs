using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public static class BoardWallRules
    {
        public const string MapId = "maze-graybox";
        public const int LifetimeTurns = 5;
        public const float Height = 4.2f;
        public const float Thickness = 0.8f;

        public static bool IsPassageBlocked(BoardTopology topology, BoardTile from, BoardTile to)
        {
            if (topology == null || from == null || to == null) return false;
            return FindGate(topology, from.Coordinate, to.Coordinate)?.IsTemporarilyBlocked == true ||
                FindGate(topology, to.Coordinate, from.Coordinate)?.IsTemporarilyBlocked == true;
        }

        public static BoardGate FindGate(BoardTopology topology, Vector2Int from, Vector2Int to)
        {
            if (topology == null || !topology.TryGetTile(from, out var tile)) return null;
            foreach (var gate in topology.GetOutgoingGates(tile))
                if (gate != null && gate.Destination != null && gate.Destination.Coordinate == to) return gate;
            return null;
        }

        public static BoardGate Reverse(BoardTopology topology, BoardGate gate)
        {
            if (gate == null || gate.Source == null || gate.Destination == null) return null;
            var reverse = FindGate(topology, gate.Destination.Coordinate, gate.Source.Coordinate);
            return reverse != null && (reverse.PlanePoint - gate.PlanePoint).sqrMagnitude < .01f
                ? reverse : null;
        }

        public static bool IsCanonical(BoardGate gate) => gate.Source.Coordinate.x < gate.Destination.Coordinate.x ||
            gate.Source.Coordinate.x == gate.Destination.Coordinate.x && gate.Source.Coordinate.y < gate.Destination.Coordinate.y;

        public static Vector3 Position(BoardGate gate) => new Vector3(gate.PlanePoint.x,
            Mathf.Min(gate.Source.WorldCenter.y, gate.Destination.WorldCenter.y), gate.PlanePoint.z);

        public static Quaternion Rotation(BoardGate gate) =>
            Quaternion.LookRotation(Vector3.ProjectOnPlane(gate.ForwardNormal, Vector3.up), Vector3.up);

        public static bool CanClose(BoardTopology topology, BoardGate gate)
        {
            var reverse = Reverse(topology, gate);
            if (reverse == null || gate.IsTemporarilyBlocked || reverse.IsTemporarilyBlocked) return false;
            var blocked = new HashSet<BoardGate>();
            foreach (var existing in topology.Gates)
                if (existing != null && existing.IsTemporarilyBlocked) blocked.Add(existing);
            blocked.Add(gate); blocked.Add(reverse);
            return RemainsConnected(topology, blocked);
        }

        // Check both directed reachability sets: a one-way escape must not strand a room.
        public static bool RemainsConnected(BoardTopology topology, ISet<BoardGate> blocked)
        {
            if (topology == null || topology.Tiles.Count == 0) return false;
            return ReachesAll(topology, blocked, false) && ReachesAll(topology, blocked, true);
        }

        private static bool ReachesAll(BoardTopology topology, ISet<BoardGate> blocked, bool reverse)
        {
            var seen = new HashSet<BoardTile>();
            var queue = new Queue<BoardTile>();
            var first = topology.Tiles[0];
            if (first == null) return false;
            seen.Add(first); queue.Enqueue(first);
            while (queue.Count > 0)
            {
                var tile = queue.Dequeue();
                var gates = reverse ? topology.GetIncomingGates(tile) : topology.GetOutgoingGates(tile);
                foreach (var gate in gates)
                {
                    if (gate == null || blocked.Contains(gate)) continue;
                    var next = reverse ? gate.Source : gate.Destination;
                    if (next != null && seen.Add(next)) queue.Enqueue(next);
                }
            }
            return seen.Count == topology.Tiles.Count;
        }

        public static bool TryAim(BoardTopology topology, BoardTile currentTile,
            Vector3 origin, Vector3 direction, out BoardGate target, out Vector3 hitPoint)
        {
            target = null; hitPoint = default;
            if (topology == null || currentTile == null || direction.sqrMagnitude < .0001f) return false;
            var nearest = float.PositiveInfinity;
            foreach (var gate in topology.GetOutgoingGates(currentTile))
            {
                if (gate == null || gate.IsTemporarilyBlocked || Reverse(topology, gate) == null) continue;
                var normal = gate.ForwardNormal;
                var denominator = Vector3.Dot(direction, normal);
                if (denominator <= .001f) continue;
                var distance = Vector3.Dot(gate.PlanePoint - origin, normal) / denominator;
                if (distance < .05f || distance >= nearest) continue;
                var point = origin + direction * distance;
                var local = Quaternion.Inverse(Rotation(gate)) * (point - Position(gate));
                if (Mathf.Abs(local.x) > gate.GateWidth * .5f || local.y < 0f || local.y > Height) continue;
                target = gate; hitPoint = point; nearest = distance;
            }
            return target != null;
        }
    }
}
