namespace MazeParty.Multiplayer
{
    public sealed partial class OnlineSessionController
    {
        public void CommitAchievements(AchievementReceipt receipt)
        {
#if MAZEPARTY_STOVE
            (_identity as StovePlayerIdentityProvider)?.CommitAchievements(receipt);
#elif MAZEPARTY_STEAM
            (_identity as SteamPlayerIdentityProvider)?.CommitAchievements(receipt);
#endif
        }
    }
}
