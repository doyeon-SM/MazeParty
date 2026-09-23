using MazeParty.Gameplay;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        private MatchAwardProgress _matchAwardProgress;

        public MatchAwardStats CreateMatchAwardStatsOnServer()
        {
            return IsServer
                ? _matchAwardProgress.ToStats(_minigameWins.Value)
                : default;
        }

        public int AddKeyAwardOnServer(int amount)
        {
            return AddKeysOnServer(amount);
        }

        public void RecordMinigamePlacementOnServer(int rank)
        {
            if (!IsServer)
            {
                return;
            }

            if (rank == 1)
            {
                AddMinigameWinOnServer();
            }
            if (rank == MultiplayerConstants.MaxPlayers)
            {
                _matchAwardProgress.RecordMinigameLastPlace();
            }
        }

        private void RecordItemUseOnServer()
        {
            if (IsServer)
            {
                _matchAwardProgress.RecordItemUse();
            }
        }

        private void RecordDamageOnServer(
            int amount,
            NetworkPlayerAvatar attacker)
        {
            if (!IsServer || amount <= 0)
            {
                return;
            }

            _matchAwardProgress.RecordDamageTaken(amount);
            if (attacker != null && attacker != this && attacker.IsServer)
            {
                attacker._matchAwardProgress.RecordPlayerDamageDealt(amount);
            }
        }
    }
}
