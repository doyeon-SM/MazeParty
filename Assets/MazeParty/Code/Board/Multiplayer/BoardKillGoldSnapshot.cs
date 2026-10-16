using System;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public struct BoardKillGoldSnapshot : INetworkSerializable, IEquatable<BoardKillGoldSnapshot>
    {
        public int Id;
        public int RecipientSlot;
        public int Amount;
        public Vector3 Origin;
        public double StartedAt;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref RecipientSlot);
            serializer.SerializeValue(ref Amount);
            serializer.SerializeValue(ref Origin);
            serializer.SerializeValue(ref StartedAt);
        }

        public bool Equals(BoardKillGoldSnapshot other) =>
            Id == other.Id && RecipientSlot == other.RecipientSlot &&
            Amount == other.Amount && Origin == other.Origin && StartedAt == other.StartedAt;
        public override bool Equals(object obj) => obj is BoardKillGoldSnapshot other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Id, RecipientSlot, Amount, Origin, StartedAt);
    }
}
