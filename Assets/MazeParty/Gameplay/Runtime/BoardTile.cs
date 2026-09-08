using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public enum BoardTileType
    {
        Normal,
        Start,
        KeyShop,
        Respawn
    }

    [DisallowMultipleComponent]
    public sealed class BoardTile : MonoBehaviour
    {
        public const float RoomSize = 8f;
        public const float HalfRoomSize = RoomSize * 0.5f;

        [SerializeField] private Vector2Int coordinate;
        [SerializeField] private BoardTileType tileType = BoardTileType.Normal;
        [SerializeField] private bool drawDebugBoundary = true;

        private readonly HashSet<BoardTraversalState> _occupants = new HashSet<BoardTraversalState>();

        public Vector2Int Coordinate => coordinate;
        public BoardTileType TileType => tileType;
        public Vector3 WorldCenter => transform.position;
        public int OccupancyCount => _occupants.Count;
        public IReadOnlyCollection<BoardTraversalState> Occupants => _occupants;

        public void Configure(Vector2Int gridCoordinate, BoardTileType type)
        {
            coordinate = gridCoordinate;
            tileType = type;
        }

        public bool ContainsHorizontalPoint(Vector3 worldPoint, float tolerance = 0f)
        {
            var offset = worldPoint - WorldCenter;
            var horizontal = Vector3.Dot(offset, transform.right.normalized);
            var depth = Vector3.Dot(offset, transform.forward.normalized);
            var extent = HalfRoomSize + Mathf.Max(0f, tolerance);
            return Mathf.Abs(horizontal) <= extent && Mathf.Abs(depth) <= extent;
        }

        public Vector3 GetRecoveryCenter(float verticalOffset = 0f)
        {
            return WorldCenter + transform.up.normalized * verticalOffset;
        }

        public bool IsOccupiedBy(BoardTraversalState traversal)
        {
            return traversal != null && _occupants.Contains(traversal);
        }

        internal void Register(BoardTraversalState traversal)
        {
            if (traversal != null)
                _occupants.Add(traversal);
        }

        internal void Unregister(BoardTraversalState traversal)
        {
            if (traversal != null)
                _occupants.Remove(traversal);
        }

        private void OnDrawGizmos()
        {
            if (!drawDebugBoundary)
                return;

            var previousMatrix = Gizmos.matrix;
            var previousColor = Gizmos.color;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.color = tileType == BoardTileType.Start
                ? Color.green
                : tileType == BoardTileType.KeyShop
                    ? Color.yellow
                    : tileType == BoardTileType.Respawn
                        ? Color.cyan
                        : new Color(0.35f, 0.65f, 1f);

            var a = new Vector3(-HalfRoomSize, 0.02f, -HalfRoomSize);
            var b = new Vector3(HalfRoomSize, 0.02f, -HalfRoomSize);
            var c = new Vector3(HalfRoomSize, 0.02f, HalfRoomSize);
            var d = new Vector3(-HalfRoomSize, 0.02f, HalfRoomSize);
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
