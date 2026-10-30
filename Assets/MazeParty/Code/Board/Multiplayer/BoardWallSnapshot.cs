using System;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    [Serializable]
    public struct BoardWallSnapshot : INetworkSerializable, IEquatable<BoardWallSnapshot>
    {
        public int FromX, FromY, ToX, ToY, ExpiresAtTurn;
        public Vector2Int From => new Vector2Int(FromX, FromY);
        public Vector2Int To => new Vector2Int(ToX, ToY);
        public BoardWallSnapshot(Vector2Int from, Vector2Int to, int expiresAtTurn)
        {
            var swap = from.x > to.x || from.x == to.x && from.y > to.y;
            var a = swap ? to : from; var b = swap ? from : to;
            FromX = a.x; FromY = a.y; ToX = b.x; ToY = b.y; ExpiresAtTurn = expiresAtTurn;
        }
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref FromX); serializer.SerializeValue(ref FromY);
            serializer.SerializeValue(ref ToX); serializer.SerializeValue(ref ToY);
            serializer.SerializeValue(ref ExpiresAtTurn);
        }
        public bool Equals(BoardWallSnapshot other) => FromX == other.FromX && FromY == other.FromY &&
            ToX == other.ToX && ToY == other.ToY && ExpiresAtTurn == other.ExpiresAtTurn;
    }
}
