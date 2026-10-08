using System;
using System.Text;
using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public readonly struct PlayerLocalProfile
    {
        public PlayerLocalProfile(
            string displayName,
            PlayerAppearanceState appearance,
            HandEmoteFaceSelections emoteFaces)
        {
            DisplayName = displayName;
            Appearance = appearance;
            EmoteFaces = emoteFaces.Sanitized(appearance.ExpressionId);
        }

        public PlayerLocalProfile(
            string displayName,
            PlayerAppearanceState appearance)
            : this(
                displayName,
                appearance,
                HandEmoteFaceSelections.Uniform(appearance.ExpressionId))
        {
        }

        public string DisplayName { get; }
        public PlayerAppearanceState Appearance { get; }
        public HandEmoteFaceSelections EmoteFaces { get; }
    }

    [Serializable]
    public struct HandEmoteFaceSelections : IEquatable<HandEmoteFaceSelections>
    {
        public byte Greeting;
        public byte Salute;
        public byte Insult;
        public byte Heart;
        public byte Surprise;
        public byte Surrender;
        public byte Pleading;
        public byte EyesCover;

        public static HandEmoteFaceSelections Uniform(byte expressionId)
        {
            return new HandEmoteFaceSelections
            {
                Greeting = expressionId,
                Salute = expressionId,
                Insult = expressionId,
                Heart = expressionId,
                Surprise = expressionId,
                Surrender = expressionId,
                Pleading = expressionId,
                EyesCover = expressionId
            };
        }

        public byte Get(byte gestureId, byte fallback)
        {
            switch ((HandEmoteId)gestureId)
            {
                case HandEmoteId.Greeting: return Greeting;
                case HandEmoteId.Salute: return Salute;
                case HandEmoteId.Insult: return Insult;
                case HandEmoteId.Heart: return Heart;
                case HandEmoteId.Surprise: return Surprise;
                case HandEmoteId.Surrender: return Surrender;
                case HandEmoteId.Pleading: return Pleading;
                case HandEmoteId.EyesCover: return EyesCover;
                default: return fallback;
            }
        }

        public HandEmoteFaceSelections With(byte gestureId, byte expressionId)
        {
            var value = this;
            switch ((HandEmoteId)gestureId)
            {
                case HandEmoteId.Greeting: value.Greeting = expressionId; break;
                case HandEmoteId.Salute: value.Salute = expressionId; break;
                case HandEmoteId.Insult: value.Insult = expressionId; break;
                case HandEmoteId.Heart: value.Heart = expressionId; break;
                case HandEmoteId.Surprise: value.Surprise = expressionId; break;
                case HandEmoteId.Surrender: value.Surrender = expressionId; break;
                case HandEmoteId.Pleading: value.Pleading = expressionId; break;
                case HandEmoteId.EyesCover: value.EyesCover = expressionId; break;
            }
            return value;
        }

        public HandEmoteFaceSelections Sanitized(byte fallback)
        {
            fallback = PlayerExpressionCatalog.SanitizeFace(fallback);
            var value = this;
            value.Greeting = SanitizeOrFallback(value.Greeting, fallback);
            value.Salute = SanitizeOrFallback(value.Salute, fallback);
            value.Insult = SanitizeOrFallback(value.Insult, fallback);
            value.Heart = SanitizeOrFallback(value.Heart, fallback);
            value.Surprise = SanitizeOrFallback(value.Surprise, fallback);
            value.Surrender = SanitizeOrFallback(value.Surrender, fallback);
            value.Pleading = SanitizeOrFallback(value.Pleading, fallback);
            value.EyesCover = SanitizeOrFallback(value.EyesCover, fallback);
            return value;
        }

        private static byte SanitizeOrFallback(byte value, byte fallback)
        {
            var catalog = PlayerExpressionCatalog.Instance;
            return catalog != null && catalog.Faces != null &&
                   value < catalog.Faces.Length
                ? value
                : fallback;
        }

        public bool Equals(HandEmoteFaceSelections other)
        {
            return Greeting == other.Greeting &&
                   Salute == other.Salute &&
                   Insult == other.Insult &&
                   Heart == other.Heart &&
                   Surprise == other.Surprise &&
                   Surrender == other.Surrender &&
                   Pleading == other.Pleading &&
                   EyesCover == other.EyesCover;
        }

        public override bool Equals(object obj)
        {
            return obj is HandEmoteFaceSelections other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Greeting;
                hash = hash * 31 + Salute;
                hash = hash * 31 + Insult;
                hash = hash * 31 + Heart;
                hash = hash * 31 + Surprise;
                hash = hash * 31 + Surrender;
                hash = hash * 31 + Pleading;
                hash = hash * 31 + EyesCover;
                return hash;
            }
        }
    }

    public static class PlayerProfilePreferences
    {
        private const int CurrentVersion = 4;
        private const string KeyPrefix = "MazeParty.PlayerProfile.";

        [Serializable]
        private sealed class ProfileData
        {
            public int version;
            public string displayName;
            public byte bodyRed;
            public byte bodyGreen;
            public byte bodyBlue;
            public byte eyeId;
            public byte mouthId;
            public byte hatId;
            public byte outfitId;
            public byte expressionId;
            public byte greetingExpressionId;
            public byte saluteExpressionId;
            public byte insultExpressionId;
            public byte heartExpressionId;
            public byte surpriseExpressionId;
            public byte surrenderExpressionId;
            public byte pleadingExpressionId;
            public byte eyesCoverExpressionId;
        }

        public static PlayerLocalProfile Load()
        {
            var fallbackName = "Player " + UnityEngine.Random.Range(1000, 10000);
            var key = BuildKey();
            if (!PlayerPrefs.HasKey(key))
            {
                return new PlayerLocalProfile(fallbackName, PlayerAppearanceState.Default);
            }

            return Decode(PlayerPrefs.GetString(key), fallbackName);
        }

        public static PlayerLocalProfile Decode(string json, string fallbackName)
        {
            try
            {
                var data = JsonUtility.FromJson<ProfileData>(json);
                if (data == null || (data.version < 1 || data.version > CurrentVersion))
                {
                    return new PlayerLocalProfile(fallbackName, PlayerAppearanceState.Default);
                }

                var appearance = new PlayerAppearanceState
                {
                    Version = (byte)data.version,
                    BodyRed = data.bodyRed,
                    BodyGreen = data.bodyGreen,
                    BodyBlue = data.bodyBlue,
                    EyeId = data.eyeId,
                    MouthId = data.mouthId,
                    HatId = data.hatId,
                    OutfitId = data.outfitId,
                    ExpressionId = data.expressionId,
                }.Sanitized();
                var emoteFaces = data.version < CurrentVersion
                    ? HandEmoteFaceSelections.Uniform(appearance.ExpressionId)
                    : new HandEmoteFaceSelections
                    {
                        Greeting = data.greetingExpressionId,
                        Salute = data.saluteExpressionId,
                        Insult = data.insultExpressionId,
                        Heart = data.heartExpressionId,
                        Surprise = data.surpriseExpressionId,
                        Surrender = data.surrenderExpressionId,
                        Pleading = data.pleadingExpressionId,
                        EyesCover = data.eyesCoverExpressionId
                    }.Sanitized(appearance.ExpressionId);
                return new PlayerLocalProfile(
                    SanitizeDisplayName(data.displayName, fallbackName),
                    appearance,
                    emoteFaces);
            }
            catch (Exception)
            {
                return new PlayerLocalProfile(fallbackName, PlayerAppearanceState.Default);
            }
        }

        public static void Save(string displayName, PlayerAppearanceState appearance)
        {
            Save(
                displayName,
                appearance,
                HandEmoteFaceSelections.Uniform(appearance.ExpressionId));
        }

        public static void Save(
            string displayName,
            PlayerAppearanceState appearance,
            HandEmoteFaceSelections emoteFaces)
        {
            PlayerPrefs.SetString(
                BuildKey(),
                Encode(displayName, appearance.Sanitized(), emoteFaces));
            PlayerPrefs.Save();
        }

        public static string Encode(string displayName, PlayerAppearanceState appearance)
        {
            return Encode(
                displayName,
                appearance,
                HandEmoteFaceSelections.Uniform(appearance.ExpressionId));
        }

        public static string Encode(
            string displayName,
            PlayerAppearanceState appearance,
            HandEmoteFaceSelections emoteFaces)
        {
            appearance = appearance.Sanitized();
            emoteFaces = emoteFaces.Sanitized(appearance.ExpressionId);
            var data = new ProfileData
            {
                version = CurrentVersion,
                displayName = SanitizeDisplayName(displayName, "Player"),
                bodyRed = appearance.BodyRed,
                bodyGreen = appearance.BodyGreen,
                bodyBlue = appearance.BodyBlue,
                eyeId = appearance.EyeId,
                mouthId = appearance.MouthId,
                hatId = appearance.HatId,
                outfitId = appearance.OutfitId,
                expressionId = appearance.ExpressionId,
                greetingExpressionId = emoteFaces.Greeting,
                saluteExpressionId = emoteFaces.Salute,
                insultExpressionId = emoteFaces.Insult,
                heartExpressionId = emoteFaces.Heart,
                surpriseExpressionId = emoteFaces.Surprise,
                surrenderExpressionId = emoteFaces.Surrender,
                pleadingExpressionId = emoteFaces.Pleading,
                eyesCoverExpressionId = emoteFaces.EyesCover
            };
            return JsonUtility.ToJson(data);
        }

        public static string SanitizeDisplayName(string value, string fallback = "Player")
        {
            var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            candidate = candidate.Replace("\n", string.Empty).Replace("\r", string.Empty);
            candidate = candidate.Substring(0, Math.Min(candidate.Length, 16));
            while (Encoding.UTF8.GetByteCount(candidate) > 60 && candidate.Length > 0)
            {
                var newLength = candidate.Length - 1;
                if (newLength > 0 &&
                    char.IsLowSurrogate(candidate[newLength]) &&
                    char.IsHighSurrogate(candidate[newLength - 1]))
                {
                    newLength--;
                }
                candidate = candidate.Substring(0, newLength);
            }
            return candidate.Length > 0 ? candidate : fallback;
        }

        private static string BuildKey()
        {
            var profile = "default";
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (!string.Equals(
                        arguments[index],
                        "-auth-profile",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidate = arguments[index + 1].Trim();
                if (candidate.Length > 0)
                {
                    profile = candidate;
                }
                break;
            }

            return KeyPrefix + Hash128.Compute(profile);
        }
    }
}
