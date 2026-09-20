using System;

namespace MazeParty.Gameplay.Minigames.CliffBarrage
{
    public static class CliffBarrageRules
    {
        public const int PlayerCount = 4;
        public const int RoundCount = 3;
        public const double RoundDurationSeconds = 60d;
        public const double CountdownSeconds = 3d;
        public const double ResultSeconds = 4d;
        public const int SimulationHz = 60;
        public const double SimulationStepSeconds = 1d / SimulationHz;
        public const double ArenaHalfExtent = 8d;
        public const double PlayerRadius = 0.45d;
        public const double SpawnRadius = 4.5d;
        public const double MovementSpeed = 4.5d;
        public const double PushCooldownSeconds = 0.65d;
        public const double PushRange = 2.4d;
        public const double PushHalfWidth = 0.95d;
        public const double PushDistance = 2.35d;
        public const double HitInvulnerabilitySeconds = 1d;
        public const int MaximumProjectiles = 5;
        public const int MaximumLasers = 2;
        public const double ProjectileRadius = 0.32d;
        public const double ProjectileSpeed = 7.5d;
        public const double LaserHalfWidth = 0.32d;
        public const double LaserWarningSeconds = 1d;
        public const double LaserFiringSeconds = 0.5d;

        public static bool IsValidSlot(int slot)
        {
            return slot >= 0 && slot < PlayerCount;
        }

        /// <summary>
        /// Swept circle contact prevents a fast projectile from skipping a
        /// player between two 60 Hz samples.
        /// </summary>
        public static bool SegmentIntersectsCircle(
            double centerX, double centerZ,
            double startX, double startZ,
            double endX, double endZ, double radius)
        {
            if (radius < 0d || double.IsNaN(radius))
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }
            var dx = endX - startX;
            var dz = endZ - startZ;
            var denominator = dx * dx + dz * dz;
            var t = denominator > 0d
                ? ((centerX - startX) * dx +
                   (centerZ - startZ) * dz) / denominator
                : 0d;
            t = Math.Max(0d, Math.Min(1d, t));
            var awayX = centerX - (startX + dx * t);
            var awayZ = centerZ - (startZ + dz * t);
            return awayX * awayX + awayZ * awayZ <=
                radius * radius;
        }
    }
}
