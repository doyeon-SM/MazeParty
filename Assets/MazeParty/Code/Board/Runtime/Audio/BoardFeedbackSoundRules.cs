namespace MazeParty.Gameplay
{
    /// <summary>
    /// Local feedback sounds derived from replicated state changes, so no
    /// extra network messages are needed.
    /// </summary>
    public static class BoardFeedbackSoundRules
    {
        /// <summary>
        /// Sound for a change of the local player's gold, keys or items, or
        /// null. A purchase changes gold and keys/items together and plays
        /// the purchase sound only.
        /// </summary>
        public static string ClassifyWalletChange(
            int previousGold,
            int gold,
            int previousKeys,
            int keys,
            int previousItems,
            int items)
        {
            if (keys > previousKeys)
            {
                return SoundKeys.BoardKeyBuy;
            }

            if (items > previousItems)
            {
                return SoundKeys.BoardShopBuy;
            }

            if (gold > previousGold)
            {
                return SoundKeys.BoardGoldGain;
            }

            return gold < previousGold ? SoundKeys.BoardGoldLoss : null;
        }

        /// <summary>Sound for a board flow transition, or null.</summary>
        public static string ClassifyFlowTransition(
            BoardFlowState previous,
            BoardFlowState current)
        {
            if (previous == current)
            {
                return null;
            }

            if (current == BoardFlowState.TurnOverview)
            {
                return SoundKeys.BoardTurnStart;
            }

            return previous == BoardFlowState.MinigamePlaying &&
                   current == BoardFlowState.MinigameResult
                ? SoundKeys.MinigameFinish
                : null;
        }
    }
}
