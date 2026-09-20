using System;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public static class LobbyColorPalette
    {
        private static readonly Color32[] Colors =
        {
            new Color32(226, 61, 56, 255),
            new Color32(242, 142, 43, 255),
            new Color32(242, 214, 55, 255),
            new Color32(65, 178, 92, 255),
            new Color32(55, 125, 230, 255),
            new Color32(58, 65, 130, 255),
            new Color32(146, 78, 210, 255),
            new Color32(28, 31, 38, 255)
        };

        private static readonly string[] DisplayNames =
        {
            "Red",
            "Orange",
            "Yellow",
            "Green",
            "Blue",
            "Indigo",
            "Purple",
            "Black"
        };

        public static int Count => Colors.Length;

        public static Color32 GetColor(int index)
        {
            return Colors[Mathf.Clamp(index, 0, Colors.Length - 1)];
        }

        public static string GetDisplayName(int index)
        {
            return DisplayNames[Mathf.Clamp(index, 0, DisplayNames.Length - 1)];
        }

        public static bool TryGetIndex(Color32 color, out int index)
        {
            for (var candidate = 0; candidate < Colors.Length; candidate++)
            {
                var paletteColor = Colors[candidate];
                if (paletteColor.r == color.r && paletteColor.g == color.g &&
                    paletteColor.b == color.b)
                {
                    index = candidate;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        public static int FindClosestIndex(Color32 color)
        {
            var bestIndex = 0;
            var bestDistance = int.MaxValue;
            for (var candidate = 0; candidate < Colors.Length; candidate++)
            {
                var paletteColor = Colors[candidate];
                var red = color.r - paletteColor.r;
                var green = color.g - paletteColor.g;
                var blue = color.b - paletteColor.b;
                var distance = red * red + green * green + blue * blue;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = candidate;
                }
            }

            return bestIndex;
        }

        public static int FindFirstAvailable(byte occupiedMask)
        {
            for (var index = 0; index < Colors.Length; index++)
            {
                if ((occupiedMask & (1 << index)) == 0)
                {
                    return index;
                }
            }

            return -1;
        }
    }

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
            LobbyColorPalette.GetColor(0),
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
            var paletteColor = LobbyColorPalette.GetColor(
                LobbyColorPalette.FindClosestIndex(
                    new Color32(value.BodyRed, value.BodyGreen, value.BodyBlue, 255)));
            value.Version = CurrentVersion;
            value.BodyRed = paletteColor.r;
            value.BodyGreen = paletteColor.g;
            value.BodyBlue = paletteColor.b;
            value.EyeId = 0;
            value.MouthId = 0;
            value.HatId = (byte)(value.HatId == 1 ? 1 : 0);
            value.OutfitId = 0;
            return value;
        }

        public PlayerAppearanceState WithPaletteColor(int paletteIndex)
        {
            var value = Sanitized();
            var color = LobbyColorPalette.GetColor(paletteIndex);
            value.BodyRed = color.r;
            value.BodyGreen = color.g;
            value.BodyBlue = color.b;
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
