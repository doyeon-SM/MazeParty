namespace MazeParty.Multiplayer
{
    public sealed partial class OnlineSessionController
    {
        public void CommitAchievements(AchievementReceipt receipt)
        {
#if MAZEPARTY_STOVE
            (_identity as StovePlayerIdentityProvider)?.CommitAchievements(receipt);
#endif
        }
    }
}
