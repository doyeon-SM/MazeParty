using System;

namespace MazeParty.Gameplay
{
    public static class TileAmplifierRules
    {
        public static bool IsAutomaticTurn(int turn) => turn > 0 && turn % 3 == 0;

        public static int Multiplier(byte users, bool automatic = false)
        {
            var result = automatic ? 2 : 1;
            for (var slot = 0; slot < BoardFlowStateMachine.RequiredPlayerCount; slot++)
                if ((users & (1 << slot)) != 0) result++;
            return result;
        }

        // Stable across processes and seat changes; never use string.GetHashCode here.
        public static int RewardSeed(int boardSeed, int turn, string playerKey)
        {
            if (string.IsNullOrEmpty(playerKey)) throw new ArgumentException("Player identity is required.", nameof(playerKey));
            unchecked
            {
                uint hash = 2166136261;
                foreach (var character in playerKey) hash = (hash ^ character) * 16777619;
                return boardSeed ^ (turn * 486187739) ^ (int)hash;
            }
        }
    }
}
