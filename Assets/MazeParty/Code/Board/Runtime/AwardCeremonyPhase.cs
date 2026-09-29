namespace MazeParty.Gameplay
{
    /// <summary>
    /// Replicated phases of the post-match presentation. The two bonus awards
    /// complete before the final podium is revealed and player input is unlocked.
    /// </summary>
    public enum AwardCeremonyPhase : byte
    {
        None = 0,
        BonusAwardOne = 1,
        BonusAwardTwo = 2,
        FinalPodiumLocked = 3,
        AwaitingReturn = 4,
        // Appended to preserve the serialized values of existing phases.
        BonusAwardOneReady = 5,
        BonusAwardTwoReady = 6
    }
}
