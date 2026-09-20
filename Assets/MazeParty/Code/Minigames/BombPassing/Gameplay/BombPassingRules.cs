using System;

namespace MazeParty.Gameplay.Minigames.BombPassing
{
    /// <summary>
    /// Shared, local XZ-plane dimensions for the server-authoritative bomb game.
    /// The scene may offset the entire arena without changing these coordinates.
    /// </summary>
    public static class BombPassingRules
    {
        public const int PlayerCount = 4;
        public const int RoundCount = 1;
        public const double CountdownSeconds = 3d;
        public const double ResultSeconds = 4d;
        public const double MinimumFuseSeconds = 20d;
        public const double MaximumFuseSeconds = 25d;
        public const double ChaseStartRemainingRatio = 0.5d;
        public const double ArenaHalfExtent = 8d;
        public const double PlayerRadius = 0.45d;
        public const double PickupRadius = 0.95d;
        public const double CarrierSpeed = 5d;
        // Matches the project's 5 m/s base and 55% walking movement tier.
        public const double EmptyHandSpeed = 2.75d;
        public const double BombChaseSpeed = 6.5d;
        public const double AttackDepth = 2.2d;
        public const double AttackHalfWidth = 0.9d;
        public const double AttackCooldownSeconds = 0.65d;
        public const double StunSeconds = 0.5d;
        public const int SimulationHz = 60;
        public const double SimulationStepSeconds = 1d / SimulationHz;
        public const int NoHolderSlot = -1;

        public static bool IsValidPlayerSlot(int slot)
        {
            return slot >= 0 && slot < PlayerCount;
        }

        public static bool IsInsideAttackHitbox(
            double attackerX,
            double attackerZ,
            double forwardX,
            double forwardZ,
            double targetX,
            double targetZ)
        {
            ValidateFinite(attackerX, nameof(attackerX));
            ValidateFinite(attackerZ, nameof(attackerZ));
            ValidateFinite(forwardX, nameof(forwardX));
            ValidateFinite(forwardZ, nameof(forwardZ));
            ValidateFinite(targetX, nameof(targetX));
            ValidateFinite(targetZ, nameof(targetZ));

            var magnitude = Math.Sqrt(
                (forwardX * forwardX) + (forwardZ * forwardZ));
            if (magnitude <= 0d)
            {
                return false;
            }

            forwardX /= magnitude;
            forwardZ /= magnitude;
            var deltaX = targetX - attackerX;
            var deltaZ = targetZ - attackerZ;
            var forward =
                (deltaX * forwardX) + (deltaZ * forwardZ);
            var sideways =
                (deltaX * -forwardZ) + (deltaZ * forwardX);
            return forward >= -PlayerRadius &&
                forward <= AttackDepth + PlayerRadius &&
                Math.Abs(sideways) <= AttackHalfWidth + PlayerRadius;
        }

        internal static void ValidateFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }
}
