using System;
using System.Threading.Tasks;

namespace MazeParty.Multiplayer
{
    public interface IOnlineSessionProvider : IDisposable
    {
        event Action Changed;
        event Action<string> Ended;

        bool IsInSession { get; }
        string CurrentSessionId { get; }
        SessionSnapshot Current { get; }

        Task CreateAsync(string roomName, string displayName);
        Task JoinByCodeAsync(string code, string displayName);
        Task PublishLocalNetworkClientIdAsync(ulong clientId);
        Task<bool> RemoveDisconnectedLobbyPlayerAsync(
            string expectedSessionId,
            ulong clientId,
            bool honorLeaveMarker = true);
        Task SetReadyAsync(bool ready);
        Task SetPlayingAsync(bool playing);
        Task LeaveAsync();
    }

    // TODO(STEAM-SESSION): UI and game code intentionally depend on this interface and
    // SessionSnapshot, not Unity Services types. A future Steam Lobby implementation can
    // replace the provider, while a Steam NetworkingSockets transport adapter can replace
    // UnityTransport without rewriting ready/roster/game-start presentation logic.
}
