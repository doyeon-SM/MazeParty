using System;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    [Serializable]
    public struct BoardChestSnapshot : INetworkSerializable, IEquatable<BoardChestSnapshot>
    {
        public bool Active;
        public bool Consumed;
        public Vector2Int Location;
        public int Revision;
        public int AppearedTurn;
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Active);
            serializer.SerializeValue(ref Consumed);
            serializer.SerializeValue(ref Location);
            serializer.SerializeValue(ref Revision);
            serializer.SerializeValue(ref AppearedTurn);
        }
        public bool Equals(BoardChestSnapshot other) => Active == other.Active &&
            Consumed == other.Consumed && Location == other.Location &&
            Revision == other.Revision && AppearedTurn == other.AppearedTurn;
        public override bool Equals(object obj) => obj is BoardChestSnapshot other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Active, Consumed, Location, Revision, AppearedTurn);
    }

    // A public position is not evidence that the viewer has seen the chest's contents/status.
    public sealed class BoardChestMapKnowledge
    {
        private readonly int[] _turns = new int[MazeParty.Gameplay.BoardChestRules.Count];
        private readonly bool[] _consumed = new bool[MazeParty.Gameplay.BoardChestRules.Count];
        public bool ShouldShow(int index, BoardChestSnapshot state, bool directlyVisible)
        {
            if (!state.Active)
            {
                _turns[index] = 0;
                _consumed[index] = false;
                return false;
            }
            if (_turns[index] != state.AppearedTurn)
            {
                _turns[index] = state.AppearedTurn;
                _consumed[index] = false;
            }
            if (directlyVisible) _consumed[index] = state.Consumed;
            return !_consumed[index];
        }
    }
}
