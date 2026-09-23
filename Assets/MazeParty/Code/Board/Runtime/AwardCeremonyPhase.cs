namespace MazeParty.Gameplay
{
    /// <summary>
    /// Replicated phases of the post-match presentation. The two bonus awards
    /// complete before the final podium is revealed and player input is unlocked.
    /// </summary>
    public enum AwardCeremonyPhase : byte
    {
        None,
        BonusAwardOne,
        BonusAwardTwo,
        FinalPodiumLocked,
        AwaitingReturn
    }
}
