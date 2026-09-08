namespace MazeParty.Multiplayer
{
    public static class MultiplayerConstants
    {
        public const int MaxPlayers = 4;
        public const string SessionType = "mazeparty.online";
        public const string LobbyPhase = "lobby";
        public const string PlayingPhase = "playing";
        public const string OnlineBootstrapScene = "OnlineBootstrap";
        public const string BoardScene = "Board";

        public const string DisplayNameProperty = "displayName";
        public const string NetworkClientIdProperty = "ngoClientId";
        public const string LeavingProperty = "leaving";
        public const string ReadyProperty = "ready";
        public const string PhaseProperty = "phase";
        public const string BuildVersionProperty = "buildVersion";

        public static string SlotProperty(int slot)
        {
            return "slot" + slot;
        }
    }
}
