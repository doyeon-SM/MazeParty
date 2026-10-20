using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;

namespace MazeParty.Multiplayer
{
    public sealed partial class OnlineSessionController
    {
        public void RequestGameSettings(MatchGameSettings settings)
        {
            if (IsBusy || !CanChangeBoardMapSelection() || settings == null) return;
            RunAsync(() => _sessions.SetGameSettingsAsync(settings),
                GameText.N("Saving match settings..."), SessionLifecycleState.Lobby);
        }
    }
}
