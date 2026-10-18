using UnityEngine;

namespace MazeParty.Gameplay
{
    public static class BoardDamageReactionRules
    {
        public const int KnockdownDamage = 50;
        public const double KnockdownSeconds = 1d;
        public static float HitScale(int damage)
        {
            var tier = Mathf.Clamp(damage / 5, 1, KnockdownDamage / 5);
            return 1f + (tier - 1) / 9f;
        }
    }
}
