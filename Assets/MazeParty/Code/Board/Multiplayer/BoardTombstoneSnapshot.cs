using System;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public struct BoardTombstoneSnapshot :
        INetworkSerializable, IEquatable<BoardTombstoneSnapshot>
    {
        public int Id;
        public Vector3 Position;
        public int Gold;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Gold);
        }

        public bool Equals(BoardTombstoneSnapshot other)
        {
            return Id == other.Id && Position == other.Position && Gold == other.Gold;
        }

        public override bool Equals(object obj)
        {
            return obj is BoardTombstoneSnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Position, Gold);
        }
    }
}
