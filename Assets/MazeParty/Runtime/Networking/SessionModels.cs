using System;
using System.Collections.Generic;
using System.Linq;

namespace MazeParty.Multiplayer
{
    [Serializable]
    public sealed class OnlinePlayerSnapshot
    {
        public OnlinePlayerSnapshot(string playerId, string displayName, int slot, bool isReady, bool isHost)
        {
            PlayerId = playerId;
            DisplayName = displayName;
            Slot = slot;
            IsReady = isReady;
            IsHost = isHost;
        }

        public string PlayerId { get; }
        public string DisplayName { get; }
        public int Slot { get; }
        public bool IsReady { get; }
        public bool IsHost { get; }
    }

    public sealed class SessionSnapshot
    {
        private static readonly IReadOnlyList<OnlinePlayerSnapshot> NoPlayers =
            Array.Empty<OnlinePlayerSnapshot>();

        public static readonly SessionSnapshot Empty =
            new SessionSnapshot(string.Empty, false, MultiplayerConstants.LobbyPhase, string.Empty, NoPlayers);

        public SessionSnapshot(
            string code,
            bool isHost,
            string phase,
            string localPlayerId,
            IReadOnlyList<OnlinePlayerSnapshot> players)
        {
            Code = code;
            IsHost = isHost;
            Phase = phase;
            LocalPlayerId = localPlayerId;
            Players = players ?? NoPlayers;
        }

        public string Code { get; }
        public bool IsHost { get; }
        public string Phase { get; }
        public string LocalPlayerId { get; }
        public IReadOnlyList<OnlinePlayerSnapshot> Players { get; }
        public bool CanStart => SessionRules.CanStart(Players);

        public int LocalSlot
        {
            get
            {
                var player = Players.FirstOrDefault(value => value.PlayerId == LocalPlayerId);
                return player != null ? player.Slot : -1;
            }
        }

        public bool LocalReady
        {
            get
            {
                var player = Players.FirstOrDefault(value => value.PlayerId == LocalPlayerId);
                return player != null && player.IsReady;
            }
        }
    }

    public static class SessionRules
    {
        public static int FindLowestAvailableSlot(IEnumerable<int> occupiedSlots)
        {
            var occupied = new HashSet<int>(occupiedSlots ?? Array.Empty<int>());
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                if (!occupied.Contains(slot))
                {
                    return slot;
                }
            }

            return -1;
        }

        public static bool CanStart(IReadOnlyList<OnlinePlayerSnapshot> players)
        {
            if (players == null || players.Count != MultiplayerConstants.MaxPlayers)
            {
                return false;
            }

            var uniqueSlots = new HashSet<int>();
            foreach (var player in players)
            {
                if (!player.IsReady ||
                    player.Slot < 0 ||
                    player.Slot >= MultiplayerConstants.MaxPlayers ||
                    !uniqueSlots.Add(player.Slot))
                {
                    return false;
                }
            }

            return uniqueSlots.Count == MultiplayerConstants.MaxPlayers;
        }
    }
}
