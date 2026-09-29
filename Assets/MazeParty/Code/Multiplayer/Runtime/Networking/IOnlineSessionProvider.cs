using System;
using System.Threading.Tasks;
using MazeParty.Gameplay;

namespace MazeParty.Multiplayer
{
    public interface IOnlineSessionProvider : IDisposable
    {
        event Action Changed;
        event Action<string> Ended;

        bool IsInSession { get; }
        string CurrentSessionId { get; }
        SessionSnapshot Current { get; }

        /// <summary>
        /// Host only. Asked when a player leaves while the room is in the
        /// playing phase. True keeps the room open because the match is
        /// already over (final ranking locked or returning to the lobby);
        /// null or false ends the fixed four-player session.
        /// </summary>
        Func<bool> KeepRoomOnPlayingDeparture { get; set; }

        Task CreateAsync(
            string roomName,
            string displayName,
            BoardMapSelection initialBoardMapSelection);
        Task JoinByCodeAsync(string code, string displayName);
        Task ReconnectToSessionAsync(string sessionId, string displayName);
        Task PublishLocalNetworkClientIdAsync(ulong clientId);
        bool TryGetAuthoritativeSlot(ulong clientId, out int slot);
        Task<bool> RemoveDisconnectedLobbyPlayerAsync(
            string expectedSessionId,
            ulong clientId,
            bool honorLeaveMarker = true);
        Task SetReadyAsync(bool ready);
        Task SetBoardMapAsync(BoardMapSelection selection);
        Task SetPlayingAsync(bool playing);
        Task LeaveAsync();
    }

    // TODO(STEAM-SESSION): UI and game code intentionally depend on this interface and
    // SessionSnapshot, not Unity Services types. A future Steam Lobby implementation can
    // replace the provider, while a Steam NetworkingSockets transport adapter can replace
    // UnityTransport without rewriting ready/roster/game-start presentation logic.
}
