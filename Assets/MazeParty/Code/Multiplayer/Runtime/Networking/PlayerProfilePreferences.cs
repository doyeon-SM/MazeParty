using System;
using System.Text;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public readonly struct PlayerLocalProfile
    {
        public PlayerLocalProfile(string displayName, PlayerAppearanceState appearance)
        {
            DisplayName = displayName;
            Appearance = appearance;
        }

        public string DisplayName { get; }
        public PlayerAppearanceState Appearance { get; }
    }

    public static class PlayerProfilePreferences
    {
        private const int CurrentVersion = 2;
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
                    Version = PlayerAppearanceState.CurrentVersion,
                    BodyRed = data.bodyRed,
                    BodyGreen = data.bodyGreen,
                    BodyBlue = data.bodyBlue,
                    EyeId = data.eyeId,
                    MouthId = data.mouthId,
                    HatId = data.hatId,
                    OutfitId = data.outfitId,
                    ExpressionId = data.expressionId
                }.Sanitized();
                return new PlayerLocalProfile(
                    SanitizeDisplayName(data.displayName, fallbackName),
                    appearance);
            }
            catch (Exception)
            {
                return new PlayerLocalProfile(fallbackName, PlayerAppearanceState.Default);
            }
        }

        public static void Save(string displayName, PlayerAppearanceState appearance)
        {
            PlayerPrefs.SetString(BuildKey(), Encode(displayName, appearance.Sanitized()));
            PlayerPrefs.Save();
        }

        public static string Encode(string displayName, PlayerAppearanceState appearance)
        {
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
                expressionId = appearance.ExpressionId
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
