using System;
using MazeParty.Gameplay;

namespace MazeParty.Multiplayer
{
    public static class MatchRecoveryResumeRules
    {
        public static (BoardFlowState State, int Turn) Resolve(MatchRecoverySnapshot snapshot)
        {
            if (snapshot == null || snapshot.turn < 1 || snapshot.turn > snapshot.scheduleTurnCount)
                throw new ArgumentException("Invalid recovery turn.", nameof(snapshot));
            switch (snapshot.checkpoint)
            {
                case MatchRecoveryCheckpoint.TurnOverview: return (BoardFlowState.TurnOverview, snapshot.turn);
                case MatchRecoveryCheckpoint.MinigameIntroReady: return (BoardFlowState.MinigameIntroReady, snapshot.turn);
                case MatchRecoveryCheckpoint.MatchComplete: return (BoardFlowState.MatchComplete, snapshot.turn);
                case MatchRecoveryCheckpoint.MinigameSettled:
                    if (snapshot.settledMinigameTurn != snapshot.turn)
                        throw new ArgumentException("The minigame result has not been settled.", nameof(snapshot));
                    return snapshot.turn == snapshot.scheduleTurnCount
                        ? (BoardFlowState.MatchComplete, snapshot.turn)
                        : (BoardFlowState.TurnOverview, snapshot.turn + 1);
                default: throw new ArgumentOutOfRangeException(nameof(snapshot));
            }
        }

        public static string OriginalHostKey(MatchRecoverySnapshot snapshot) =>
            !string.IsNullOrWhiteSpace(snapshot?.originalHostPlayerKey) ? snapshot.originalHostPlayerKey :
            snapshot?.players != null && snapshot.players.Length == 4 ? snapshot.players[0]?.playerKey : string.Empty;

        public static bool IsOriginalHost(MatchRecoverySnapshot snapshot, string playerId) =>
            !string.IsNullOrWhiteSpace(playerId) && string.Equals(OriginalHostKey(snapshot),
                MatchRecoveryFingerprint.CreatePlayerKey(playerId), StringComparison.Ordinal);
    }
}
