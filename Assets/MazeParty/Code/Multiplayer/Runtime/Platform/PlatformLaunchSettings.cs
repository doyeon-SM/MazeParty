using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    [Serializable]
    public sealed class PlatformLaunchSettings
    {
        public string ugsEnvironment = "production";
        public string stoveEnvironment = "LIVE";
        public string stoveGameId = "";
        public string stoveAppKey = "";

        // PC SDK 3.5.3 treats the old "real" value as Sandbox. Never pass it through.
        // Keep it only as an explicit migration alias for previously generated local settings.
        public string StoveSdkEnvironment
        {
            get
            {
                switch ((stoveEnvironment ?? "").Trim().ToUpperInvariant())
                {
                    case "REAL":
                    case "LIVE": return "LIVE";
                    case "SANDBOX": return "SANDBOX";
                    default: throw new InvalidOperationException(
                        "Configure STOVE environment as LIVE for the standard launcher, or SANDBOX for a provisioned Sandbox launcher.");
                }
            }
        }

        public void Validate(string expectedChannel)
        {
            if ((expectedChannel != "stove" && expectedChannel != "steam") ||
                !Regex.IsMatch(ugsEnvironment ?? "", "^[a-z0-9][a-z0-9-]{0,39}$"))
                throw new InvalidOperationException("Configure an existing UGS environment name.");
            if (expectedChannel == "stove")
            {
                if (string.IsNullOrWhiteSpace(stoveGameId) || string.IsNullOrWhiteSpace(stoveAppKey))
                    throw new InvalidOperationException("Configure STOVE Game ID and AppKey.");
                _ = StoveSdkEnvironment;
            }
        }

        public string ToClientJson(string channel) => channel == "stove"
            ? JsonUtility.ToJson(new PlatformLaunchSettings
            {
                ugsEnvironment = ugsEnvironment,
                stoveEnvironment = StoveSdkEnvironment,
                stoveGameId = stoveGameId,
                stoveAppKey = stoveAppKey
            }, true)
            : JsonUtility.ToJson(new UgsSettings { ugsEnvironment = ugsEnvironment }, true);

        [Serializable] private sealed class UgsSettings { public string ugsEnvironment; }
    }

    public static class PlatformAccountScope
    {
        // Different from development/default and the retired server-linked profile.
        // The STOVE ID only selects a LOCAL cache; it is not a UGS login credential.
        public static string AuthenticationProfile(string channel, PlatformLaunchSettings settings, ulong stoveUser)
        {
            if (stoveUser == 0) throw new ArgumentException("A locally verified STOVE account is required.");
            return "mp-local2-" + Hash128.Compute(channel + "/" + settings.ugsEnvironment + "/" +
                settings.StoveSdkEnvironment + "/" + settings.stoveGameId + "/" +
                stoveUser.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToString().Substring(0, 20);
        }
    }

    public static class PlatformContext
    {
#if MAZEPARTY_STOVE && MAZEPARTY_STEAM
#error STOVE and Steam cannot be enabled in the same build.
#endif
#if MAZEPARTY_STOVE
        public const string Channel = "stove";
#elif MAZEPARTY_STEAM
        public const string Channel = "steam";
#else
        public const string Channel = "development";
#endif
        public static bool IsStoreBuild => Channel != "development";
        public const string SettingsFileName = "mazeparty-platform.json";
        public const string LocalSettingsDirectory = "Assets/Stove&Steam/.local";
        private static PlatformLaunchSettings _settings;
        public static PlatformLaunchSettings Settings => _settings ?? (_settings = ReadSettings());
        public static string EnvironmentName => IsStoreBuild ? Settings.ugsEnvironment : "production";
        public static string SessionType => "mazeparty.online." + Channel;
        public static string StorageScope
        {
            get
            {
                if (!IsStoreBuild) return "";
                try { return Channel + "/" + EnvironmentName + "/release-local-v2"; }
                catch (Exception) { return Channel + "/unconfigured/release-local-v2"; }
            }
        }

        private static PlatformLaunchSettings ReadSettings()
        {
            if (!IsStoreBuild) return new PlatformLaunchSettings();
#if UNITY_EDITOR
            var path = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                LocalSettingsDirectory, Channel + ".json");
#else
            var path = Path.Combine(Application.streamingAssetsPath, SettingsFileName);
#endif
            if (!File.Exists(path))
                throw new InvalidOperationException("Platform settings are missing. Configure " + LocalSettingsDirectory + "/" + Channel + ".json before building.");
            var value = JsonUtility.FromJson<PlatformLaunchSettings>(File.ReadAllText(path));
            if (value == null) throw new InvalidOperationException("Invalid platform settings.");
            value.Validate(Channel);
            return value;
        }

        public static IPlayerIdentityProvider CreateIdentity()
        {
#if MAZEPARTY_STOVE
            return new StovePlayerIdentityProvider();
#elif MAZEPARTY_STEAM
            return new UnconfiguredSteamIdentityProvider();
#else
            return new UnityAnonymousIdentityProvider();
#endif
        }

        private sealed class UnconfiguredSteamIdentityProvider : IPlayerIdentityProvider
        {
            public bool IsSignedIn => false;
            public string PlayerId => "";
            public string DisplayName => "Player";
            public System.Threading.Tasks.Task SignInAsync(string requestedDisplayName) =>
                System.Threading.Tasks.Task.FromException(new InvalidOperationException(
                    "Steam builds are isolated. Steam SDK authentication must be integrated before online play."));
        }
    }

    public interface IPlatformIdentityProvider : IPlayerIdentityProvider, IDisposable
    {
        AdmissionProof Proof { get; }
        void Tick();
    }
}
