using System;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;

namespace MazeParty.Multiplayer
{
    public sealed partial class MpsRelaySessionProvider
    {
        private string[] _recoveryRosterKeys;
        private string[] _recoveryRosterNames;
        private static string RecoveryPlayerNameProperty(int slot) => "recovery-name-" + slot;
        private static string RecoveryPlayerKeyProperty(int slot) => "recovery-player-" + slot;

        public void ConfigureRecoveryRoster(string[] playerKeys, string[] names = null)
        {
            if (playerKeys != null && (playerKeys.Length != MultiplayerConstants.MaxPlayers ||
                playerKeys.Any(string.IsNullOrWhiteSpace) || playerKeys.Distinct().Count() != MultiplayerConstants.MaxPlayers))
                throw new ArgumentException("A saved room requires four unique authenticated players.");
            _recoveryRosterKeys = playerKeys == null ? null : (string[])playerKeys.Clone();
            _recoveryRosterNames = names != null && names.Length == MultiplayerConstants.MaxPlayers ? (string[])names.Clone() : null;
        }

        public async Task ApplyRecoveryRosterAsync(string[] playerKeys, string[] names)
        {
            ConfigureRecoveryRoster(playerKeys, names);
            var session = RequireSession();
            await _hostMutationGate.WaitAsync();
            try
            {
                if (!OwnsSession(session) || !session.IsHost || session.Host != _originalHostId)
                    throw new InvalidOperationException("Only the original host may bind a saved roster.");
                var host = session.AsHost();
                for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                    host.SetProperty(RecoveryPlayerKeyProperty(slot),
                        new SessionProperty(playerKeys[slot], VisibilityPropertyOptions.Member));
                for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                    host.SetProperty(RecoveryPlayerNameProperty(slot), new SessionProperty(
                        _recoveryRosterNames?[slot] ?? "Player " + (slot + 1), VisibilityPropertyOptions.Member));
                await SaveHostPropertiesWithRetryAsync(host, session);
                await AssignRecoverySeatsLockedAsync(session);
            }
            finally { _hostMutationGate.Release(); }
        }

        private static bool HasRecoveryRoster(ISession session) =>
            !string.IsNullOrEmpty(GetSessionProperty(session, RecoveryPlayerKeyProperty(0), ""));

        private static int GetRecoverySlot(ISession session, string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return -1;
            var key = MatchRecoveryFingerprint.CreatePlayerKey(playerId);
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                if (GetSessionProperty(session, RecoveryPlayerKeyProperty(slot), "") == key) return slot;
            return -1;
        }

        private async Task AssignRecoverySeatsLockedAsync(ISession session)
        {
            var host = session.AsHost();
            foreach (var player in session.Players.ToArray())
            {
                var slot = GetRecoverySlot(session, player.Id);
                if (slot < 0)
                {
                    if (player.Id == _originalHostId) throw new InvalidOperationException("The saved host does not match this room.");
                    await host.RemovePlayerAsync(player.Id);
                    continue;
                }
                host.SetProperty(MultiplayerConstants.SlotProperty(slot),
                    new SessionProperty(player.Id, VisibilityPropertyOptions.Member));
            }
            await SaveHostPropertiesWithRetryAsync(host, session);
            if (OwnsSession(session)) RebuildSnapshot();
        }
    }
}
