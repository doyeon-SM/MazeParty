using System;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    [Serializable]
    public struct MagicMazeEffectSnapshot : INetworkSerializable, IEquatable<MagicMazeEffectSnapshot>
    {
        public Vector2Int Coordinate;
        public byte Effect;
        public MagicMazeEffectSnapshot(Vector2Int coordinate, BoardLandingEffectType effect)
        { Coordinate = coordinate; Effect = (byte)effect; }
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        { serializer.SerializeValue(ref Coordinate); serializer.SerializeValue(ref Effect); }
        public bool Equals(MagicMazeEffectSnapshot other) => Coordinate == other.Coordinate && Effect == other.Effect;
    }
}
