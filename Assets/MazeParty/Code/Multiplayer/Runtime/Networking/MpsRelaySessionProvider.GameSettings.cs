using System;
using System.Threading.Tasks;
using MazeParty.Gameplay.Minigames;
using Unity.Services.Multiplayer;

namespace MazeParty.Multiplayer
{
    public sealed partial class MpsRelaySessionProvider
    {
        private const string GameSettingsProperty = "gameSettings";
        public async Task SetGameSettingsAsync(MatchGameSettings settings)
        {
            if (settings == null || !MatchGameSettings.TryDecode(settings.Encode(), out _))
                throw new ArgumentException("Invalid match settings.");
            await _hostMutationGate.WaitAsync();
            try
            {
                var session = RequireSession();
                if (!session.IsHost || session.Host != _originalHostId ||
                    !SessionRules.CanChangeBoardMap(session.IsHost, GetSessionPhase(session)))
                    throw new InvalidOperationException("Only the original host can change settings in the lobby.");
                var host = session.AsHost();
                host.SetProperty(GameSettingsProperty, new SessionProperty(settings.Encode(), VisibilityPropertyOptions.Member));
                await SaveHostPropertiesWithRetryAsync(host, session);
                if (_ending || !ReferenceEquals(session, _session) || !session.IsHost)
                    throw new InvalidOperationException("The session changed while saving settings.");
                RebuildSnapshot();
            }
            finally { _hostMutationGate.Release(); }
        }
    }
}
