using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public enum BoardBoundarySide
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3
    }

    /// <summary>
    /// Immutable four-wall decision for one player's current logical room.
    /// ExitMask describes physical topology in either direction. PassableMask
    /// describes outgoing directions that can currently consume a move.
    /// </summary>
    public readonly struct BoardBoundaryWallLayout
    {
        private readonly byte _exitMask;
        private readonly byte _passableMask;

        public BoardBoundaryWallLayout(byte exitMask, byte passableMask)
        {
            _exitMask = (byte)(exitMask & BoardBoundaryWallPolicy.AllSidesMask);
            _passableMask = (byte)(passableMask & _exitMask);
        }

        public byte ExitMask => _exitMask;
        public byte PassableMask => _passableMask;

        public bool HasExit(BoardBoundarySide side)
        {
            return (_exitMask & BoardBoundaryWallPolicy.ToMask(side)) != 0;
        }

        public bool IsPassable(BoardBoundarySide side)
        {
            return (_passableMask & BoardBoundaryWallPolicy.ToMask(side)) != 0;
        }

        public bool IsPhysicallyBlocked(BoardBoundarySide side)
        {
            return !IsPassable(side);
        }
    }

    /// <summary>
    /// One physical connection opening around the current tile. Reciprocal
    /// directed gates that share the same neighbor and gate plane collapse into
    /// one portal. Passability still follows the outgoing directed gate only.
    /// </summary>
    public readonly struct BoardBoundaryPortal
    {
        internal BoardBoundaryPortal(
            BoardGate representativeGate,
            BoardTile connectedTile,
            Vector3 planePoint,
            Vector3 outwardNormal,
            float width,
            bool hasOutgoingGate,
            bool isPassable)
        {
            RepresentativeGate = representativeGate;
            ConnectedTile = connectedTile;
            PlanePoint = planePoint;
            OutwardNormal = outwardNormal.sqrMagnitude > 0.0001f
                ? outwardNormal.normalized
                : Vector3.forward;
            Width = Mathf.Max(0.1f, width);
            HasOutgoingGate = hasOutgoingGate;
            IsPassable = isPassable && hasOutgoingGate;
        }

        public BoardGate RepresentativeGate { get; }
        public BoardTile ConnectedTile { get; }
        public Vector3 PlanePoint { get; }
        public Vector3 OutwardNormal { get; }
        public float Width { get; }
        public bool HasOutgoingGate { get; }
        public bool IsPassable { get; }
        public bool IsPhysicallyBlocked => !IsPassable;

        internal BoardBoundaryPortal Merge(
            BoardGate gate,
            Vector3 planePoint,
            Vector3 outwardNormal,
            float width,
            bool outgoing,
            int remainingMoves)
        {
            var preferCandidate = outgoing && !HasOutgoingGate;
            var hasOutgoing = HasOutgoingGate || outgoing;
            return new BoardBoundaryPortal(
                preferCandidate ? gate : RepresentativeGate,
                ConnectedTile,
                preferCandidate ? planePoint : PlanePoint,
                preferCandidate ? outwardNormal : OutwardNormal,
                Mathf.Max(Width, width),
                hasOutgoing,
                hasOutgoing && remainingMoves > 0);
        }
    }

    /// <summary>
    /// Shared boundary policy for online avatars and the editor testbed. The
    /// cardinal mask API remains for legacy board consumers, while portal
    /// evaluation supports arbitrary authored gate positions and directions.
    /// </summary>
    public static class BoardBoundaryWallPolicy
    {
        public const int SideCount = 4;
        public const byte AllSidesMask = 0b0000_1111;

        private const float PortalMergePointTolerance = 0.05f;
        private const float PortalMergeNormalAlignment = 0.999f;

        /// <summary>
        /// Builds one state per unique physical gate portal connected to the
        /// supplied tile. Incoming-only portals stay visible but blocked;
        /// outgoing portals are passable only while a move remains.
        /// </summary>
        public static void EvaluatePortals(
            BoardTopology topology,
            BoardTile source,
            int remainingMoves,
            List<BoardBoundaryPortal> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            if (topology == null || source == null)
            {
                return;
            }

            var outgoing = topology.GetOutgoingGates(source);
            for (var i = 0; i < outgoing.Count; i++)
            {
                var gate = outgoing[i];
                if (gate == null || gate.Source != source || gate.Destination == null)
                {
                    continue;
                }

                AddOrMergePortal(
                    results,
                    gate,
                    gate.Destination,
                    gate.ForwardNormal,
                    true,
                    remainingMoves);
            }

            var incoming = topology.GetIncomingGates(source);
            for (var i = 0; i < incoming.Count; i++)
            {
                var gate = incoming[i];
                if (gate == null || gate.Destination != source || gate.Source == null)
                {
                    continue;
                }

                AddOrMergePortal(
                    results,
                    gate,
                    gate.Source,
                    -gate.ForwardNormal,
                    false,
                    remainingMoves);
            }
        }

        public static BoardBoundaryWallLayout Evaluate(
            Vector2Int source,
            IReadOnlyList<Vector2Int> outgoingDestinations,
            int remainingMoves)
        {
            return Evaluate(
                source,
                outgoingDestinations,
                outgoingDestinations,
                remainingMoves);
        }

        public static BoardBoundaryWallLayout Evaluate(
            Vector2Int source,
            IReadOnlyList<Vector2Int> connectedDestinations,
            IReadOnlyList<Vector2Int> outgoingDestinations,
            int remainingMoves)
        {
            byte exitMask = 0;
            if (connectedDestinations != null)
            {
                for (var i = 0; i < connectedDestinations.Count; i++)
                {
                    if (TryGetSide(source, connectedDestinations[i], out var side))
                    {
                        exitMask |= ToMask(side);
                    }
                }
            }

            byte passableMask = 0;
            if (remainingMoves > 0 && outgoingDestinations != null)
            {
                for (var i = 0; i < outgoingDestinations.Count; i++)
                {
                    if (TryGetSide(source, outgoingDestinations[i], out var side))
                    {
                        passableMask |= ToMask(side);
                    }
                }
            }

            return new BoardBoundaryWallLayout(exitMask, passableMask);
        }

        public static bool TryGetSide(
            Vector2Int source,
            Vector2Int destination,
            out BoardBoundarySide side)
        {
            var delta = destination - source;
            if (delta == Vector2Int.up)
            {
                side = BoardBoundarySide.North;
                return true;
            }
            if (delta == Vector2Int.right)
            {
                side = BoardBoundarySide.East;
                return true;
            }
            if (delta == Vector2Int.down)
            {
                side = BoardBoundarySide.South;
                return true;
            }
            if (delta == Vector2Int.left)
            {
                side = BoardBoundarySide.West;
                return true;
            }

            side = default;
            return false;
        }

        internal static byte ToMask(BoardBoundarySide side)
        {
            var index = (int)side;
            return index >= 0 && index < SideCount
                ? (byte)(1 << index)
                : (byte)0;
        }

        private static void AddOrMergePortal(
            List<BoardBoundaryPortal> portals,
            BoardGate gate,
            BoardTile connectedTile,
            Vector3 outwardNormal,
            bool outgoing,
            int remainingMoves)
        {
            for (var i = 0; i < portals.Count; i++)
            {
                if (!IsSamePhysicalPortal(
                        portals[i],
                        connectedTile,
                        gate.PlanePoint,
                        outwardNormal))
                {
                    continue;
                }

                portals[i] = portals[i].Merge(
                    gate,
                    gate.PlanePoint,
                    outwardNormal,
                    gate.GateWidth,
                    outgoing,
                    remainingMoves);
                return;
            }

            portals.Add(new BoardBoundaryPortal(
                gate,
                connectedTile,
                gate.PlanePoint,
                outwardNormal,
                gate.GateWidth,
                outgoing,
                outgoing && remainingMoves > 0));
        }

        private static bool IsSamePhysicalPortal(
            BoardBoundaryPortal existing,
            BoardTile connectedTile,
            Vector3 planePoint,
            Vector3 outwardNormal)
        {
            if (existing.ConnectedTile != connectedTile ||
                (existing.PlanePoint - planePoint).sqrMagnitude >
                PortalMergePointTolerance * PortalMergePointTolerance)
            {
                return false;
            }

            var normal = outwardNormal.sqrMagnitude > 0.0001f
                ? outwardNormal.normalized
                : Vector3.forward;
            return Mathf.Abs(Vector3.Dot(existing.OutwardNormal, normal)) >=
                   PortalMergeNormalAlignment;
        }
    }
}
