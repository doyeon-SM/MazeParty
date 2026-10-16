using System;
using System.Collections.Generic;
using System.Globalization;

namespace MazeParty.Multiplayer
{
    // One host-owned property keeps a four-player recovery room within the
    // MPS 20-property limit, including the SDK's network metadata.
    public static class PlatformSessionAdmission
    {
        public const string Property = "platformAdmission";
        public static string Empty(string channel, string environment) => "mpa2|" + channel + "|" + environment + "||||";

        public static bool HasScope(string value, string channel, string environment) =>
            TryRead(value, out var fields) && fields[1] == channel && fields[2] == environment;

        public static string WithConnection(string value, int slot, ulong clientId, string playerId)
        {
            if (!TryRead(value, out var fields) || slot < 0 || slot >= 4 || string.IsNullOrEmpty(playerId) ||
                playerId.Length > 128 || playerId.IndexOfAny(new[] { '|', ':' }) >= 0)
                throw new ArgumentException("Invalid verified connection binding.");
            fields[slot + 3] = clientId.ToString(CultureInfo.InvariantCulture) + ":" + playerId;
            return string.Join("|", fields);
        }

        public static bool TryGetClient(string value, int slot, string playerId, out ulong clientId)
        {
            clientId = 0;
            if (!TryRead(value, out var fields) || slot < 0 || slot >= 4 || string.IsNullOrEmpty(playerId)) return false;
            var binding = fields[slot + 3];
            var separator = binding.IndexOf(':');
            return separator > 0 && binding.Substring(separator + 1) == playerId &&
                ulong.TryParse(binding.Substring(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out clientId);
        }

        public static bool TryGetSlot(string value, IReadOnlyList<string> seatOwners, ulong clientId, out int slot)
        {
            slot = -1;
            if (seatOwners == null || seatOwners.Count != 4) return false;
            for (var candidate = 0; candidate < 4; candidate++)
                if (TryGetClient(value, candidate, seatOwners[candidate], out var boundClient) && boundClient == clientId)
                {
                    if (slot >= 0) { slot = -1; return false; }
                    slot = candidate;
                }
            return slot >= 0;
        }

        private static bool TryRead(string value, out string[] fields)
        {
            fields = null;
            if (string.IsNullOrEmpty(value) || value.Length > 1024) return false;
            fields = value.Split('|');
            return fields.Length == 7 && fields[0] == "mpa2";
        }
    }
}
