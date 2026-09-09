using System;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    [Serializable]
    public struct PlayerAppearanceState : INetworkSerializable, IEquatable<PlayerAppearanceState>
    {
        public const byte CurrentVersion = 1;

        public byte Version;
        public byte BodyRed;
        public byte BodyGreen;
        public byte BodyBlue;
        public byte EyeId;
        public byte MouthId;
        public byte HatId;
        public byte OutfitId;

        public Color BodyColor => new Color32(BodyRed, BodyGreen, BodyBlue, 255);

        public static PlayerAppearanceState Default => FromColor(
            new Color32(242, 64, 64, 255),
            0,
            0,
            0,
            0);

        public static PlayerAppearanceState FromColor(
            Color color,
            byte eyeId,
            byte mouthId,
            byte hatId,
            byte outfitId)
        {
            var value = (Color32)color;
            return new PlayerAppearanceState
            {
                Version = CurrentVersion,
                BodyRed = value.r,
                BodyGreen = value.g,
                BodyBlue = value.b,
                EyeId = eyeId,
                MouthId = mouthId,
                HatId = hatId,
                OutfitId = outfitId
            }.Sanitized();
        }

        public PlayerAppearanceState Sanitized()
        {
            var value = this;
            value.Version = CurrentVersion;
            value.EyeId = 0;
            value.MouthId = 0;
            value.HatId = (byte)(value.HatId == 1 ? 1 : 0);
            value.OutfitId = 0;
            return value;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Version);
            serializer.SerializeValue(ref BodyRed);
            serializer.SerializeValue(ref BodyGreen);
            serializer.SerializeValue(ref BodyBlue);
            serializer.SerializeValue(ref EyeId);
            serializer.SerializeValue(ref MouthId);
            serializer.SerializeValue(ref HatId);
            serializer.SerializeValue(ref OutfitId);
        }

        public bool Equals(PlayerAppearanceState other)
        {
            return Version == other.Version &&
                   BodyRed == other.BodyRed &&
                   BodyGreen == other.BodyGreen &&
                   BodyBlue == other.BodyBlue &&
                   EyeId == other.EyeId &&
                   MouthId == other.MouthId &&
                   HatId == other.HatId &&
                   OutfitId == other.OutfitId;
        }

        public override bool Equals(object obj)
        {
            return obj is PlayerAppearanceState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Version;
                hash = hash * 31 + BodyRed;
                hash = hash * 31 + BodyGreen;
                hash = hash * 31 + BodyBlue;
                hash = hash * 31 + EyeId;
                hash = hash * 31 + MouthId;
                hash = hash * 31 + HatId;
                hash = hash * 31 + OutfitId;
                return hash;
            }
        }
    }
}
