using System;
using System.Globalization;

namespace MazeParty.Multiplayer
{
    /// <summary>Where the local player is, which decides the menu's exit behaviour.</summary>
    public enum GameMenuContext : byte
    {
        /// <summary>Not in a session: the exit button quits the game.</summary>
        Lobby,

        /// <summary>In a room before or after a match: the exit button leaves the room.</summary>
        WaitingRoom,

        /// <summary>A match is running: the exit button asks for confirmation and ends the match.</summary>
        InGame
    }

    public enum GameMenuExitAction : byte
    {
        QuitApplication,
        LeaveWaitingRoom,
        ConfirmMatchLeave
    }

    public static class GameMenuRules
    {
        public static GameMenuExitAction GetExitAction(GameMenuContext context)
        {
            switch (context)
            {
                case GameMenuContext.WaitingRoom:
                    return GameMenuExitAction.LeaveWaitingRoom;
                case GameMenuContext.InGame:
                    return GameMenuExitAction.ConfirmMatchLeave;
                default:
                    return GameMenuExitAction.QuitApplication;
            }
        }

        /// <summary>English source text for the exit button (localized by the view).</summary>
        public static string GetExitLabelSource(GameMenuContext context)
        {
            return context == GameMenuContext.Lobby ? "Quit Game" : "Leave Game";
        }

        public static bool ShowsPauseButton(GameMenuContext context)
        {
            return context == GameMenuContext.InGame;
        }

        public static bool ShowsGearButton(GameMenuContext context)
        {
            return context != GameMenuContext.InGame;
        }

        /// <summary>Player pause countdown, e.g. 300 seconds -> "5:00".</summary>
        public static string FormatPauseClock(double seconds)
        {
            var whole = seconds > 0d ? (int)Math.Ceiling(seconds) : 0;
            return (whole / 60).ToString(CultureInfo.InvariantCulture) + ":" +
                   (whole % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
