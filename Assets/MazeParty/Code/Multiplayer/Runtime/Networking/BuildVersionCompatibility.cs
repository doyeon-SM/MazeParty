using System;
using System.Text;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public static class BuildVersionCompatibility
    {
        private const string PayloadPrefix = "MazeParty.BuildVersion:";
        private static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(false, true);

        public const string RejectionReason =
            "MazeParty.BuildVersionMismatch";

        public static string CurrentVersion => Application.version;
        public static string DisplayText => FormatDisplayText(CurrentVersion);

        public static byte[] CreateConnectionPayload()
        {
            return CreateConnectionPayload(CurrentVersion);
        }

        internal static byte[] CreateConnectionPayload(string version)
        {
            return StrictUtf8.GetBytes(PayloadPrefix + (version ?? string.Empty));
        }

        public static bool IsCompatibleVersion(string remoteVersion)
        {
            return IsCompatibleVersion(remoteVersion, CurrentVersion);
        }

        internal static bool IsCompatibleVersion(
            string remoteVersion,
            string expectedVersion)
        {
            return !string.IsNullOrWhiteSpace(expectedVersion) &&
                   string.Equals(
                       remoteVersion,
                       expectedVersion,
                       StringComparison.Ordinal);
        }

        public static bool IsCompatiblePayload(byte[] payload)
        {
            return IsCompatiblePayload(payload, CurrentVersion);
        }

        internal static bool IsCompatiblePayload(
            byte[] payload,
            string expectedVersion)
        {
            if (payload == null ||
                string.IsNullOrWhiteSpace(expectedVersion))
            {
                return false;
            }

            var expectedPayload = CreateConnectionPayload(expectedVersion);
            if (payload.Length != expectedPayload.Length)
            {
                return false;
            }

            for (var index = 0; index < payload.Length; index++)
            {
                if (payload[index] != expectedPayload[index])
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsMismatchDisconnectReason(string reason)
        {
            return !string.IsNullOrEmpty(reason) &&
                   reason.IndexOf(
                       RejectionReason,
                       StringComparison.Ordinal) >= 0;
        }

        internal static string FormatDisplayText(string version)
        {
            var normalized = Normalize(version);
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(1);
            }

            return "v" + normalized;
        }

        private static string Normalize(string version)
        {
            return string.IsNullOrWhiteSpace(version)
                ? string.Empty
                : version.Trim();
        }
    }
}
