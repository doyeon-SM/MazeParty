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
    /// ExitMask describes topology; PassableMask additionally accounts for moves.
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
    /// Pure cardinal policy shared by online avatars and the editor testbed.
    /// A topology exit is passable only while the player has at least one move.
    /// With zero moves all four walls remain blocking, leaving the room interior free.
    /// </summary>
    public static class BoardBoundaryWallPolicy
    {
        public const int SideCount = 4;
        public const byte AllSidesMask = 0b0000_1111;

        public static BoardBoundaryWallLayout Evaluate(
            Vector2Int source,
            IReadOnlyList<Vector2Int> outgoingDestinations,
            int remainingMoves)
        {
            byte exitMask = 0;
            if (outgoingDestinations != null)
            {
                for (var i = 0; i < outgoingDestinations.Count; i++)
                {
                    if (TryGetSide(source, outgoingDestinations[i], out var side))
                    {
                        exitMask |= ToMask(side);
                    }
                }
            }

            var passableMask = remainingMoves > 0 ? exitMask : (byte)0;
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
    }
}
