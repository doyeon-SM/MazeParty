using System;

namespace MazeParty.Gameplay.Minigames.CliffBarrage
{
    public static class CliffBarrageRules
    {
        public const int PlayerCount = 4;
        public static int RoundCount => MinigameRuntimeSettings.Rounds(ScheduledMinigameId.CliffBarrage, 3);
        public static double RoundDurationSeconds => MinigameRuntimeSettings.Seconds(ScheduledMinigameId.CliffBarrage, 60d);
        public const double CountdownSeconds = 3d;
        public const double ResultSeconds = 4d;
        public const int SimulationHz = 60;
        public const double SimulationStepSeconds = 1d / SimulationHz;
        public const double ArenaHalfExtent = 8d;
        public const double PlayerRadius = 0.45d;
        public const double SpawnRadius = 4.5d;
        public const double MovementSpeed = 4.5d;
        public const double PushCastSeconds = 0.1d;
        public const double PushStunSeconds = 0.5d;
        public const double PushRecoverySeconds = 0.9d;
        public const double PushCooldownSeconds =
            PushCastSeconds + PushRecoverySeconds;
        public const double PushRange = 2.04d;
        public const double PushHalfWidth = 0.95d;
        public const double PushDistance = 2.35d;
        public const double HitInvulnerabilitySeconds = 1d;
        public const int MaximumProjectiles = 10;
        public const int MaximumLasers = 3;
        public const double ProjectileRadius = 0.32d;
        public const double ProjectileSpeed = 7.5d;
        public const double LaserHalfWidth = 0.32d;
        public const double LaserWarningSeconds = 1d;
        public const double LaserFiringSeconds = 0.5d;
        public const double HazardFrequencyStepSeconds = 10d;
        public const double HazardFrequencyIncreasePerStep = 0.2d;
        public const int MaximumHazardFrequencyStep = 5;

        public static bool IsValidSlot(int slot)
        {
            return slot >= 0 && slot < PlayerCount;
        }

        public static int GetHazardFrequencyStep(
            double roundElapsedSeconds)
        {
            if (roundElapsedSeconds < 0d ||
                double.IsNaN(roundElapsedSeconds) ||
                double.IsInfinity(roundElapsedSeconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(roundElapsedSeconds));
            }

            var rawStep = Math.Floor(
                roundElapsedSeconds /
                HazardFrequencyStepSeconds);
            return rawStep >= MaximumHazardFrequencyStep
                ? MaximumHazardFrequencyStep
                : (int)rawStep;
        }

        public static double GetHazardIntervalScale(int frequencyStep)
        {
            if (frequencyStep < 0 ||
                frequencyStep > MaximumHazardFrequencyStep)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frequencyStep));
            }

            return 1d /
                (1d + frequencyStep *
                    HazardFrequencyIncreasePerStep);
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

    internal sealed class CliffBarragePushTimingState
    {
        public bool IsCasting { get; private set; }
        public double CastEndsAt { get; private set; }
        public double StunnedUntil { get; private set; }
        public double NextPushAllowedAt { get; private set; }

        public bool CanMove(double now)
        {
            ValidateTime(now);
            return !IsCasting && now >= StunnedUntil;
        }

        public bool CanStartPush(double now)
        {
            ValidateTime(now);
            return CanMove(now) && now >= NextPushAllowedAt;
        }

        public bool TryStartPush(double now)
        {
            if (!CanStartPush(now))
            {
                return false;
            }

            IsCasting = true;
            CastEndsAt = now + CliffBarrageRules.PushCastSeconds;
            NextPushAllowedAt = CastEndsAt +
                                CliffBarrageRules.PushRecoverySeconds;
            return true;
        }

        public bool TryCompletePush(double now)
        {
            ValidateTime(now);
            if (!IsCasting || now < CastEndsAt)
            {
                return false;
            }

            IsCasting = false;
            CastEndsAt = 0d;
            return true;
        }

        public void ApplyStun(double now)
        {
            ValidateTime(now);
            StunnedUntil = Math.Max(
                StunnedUntil,
                now + CliffBarrageRules.PushStunSeconds);
        }

        public void Reset()
        {
            IsCasting = false;
            CastEndsAt = 0d;
            StunnedUntil = 0d;
            NextPushAllowedAt = 0d;
        }

        private static void ValidateTime(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(now));
            }
        }
    }
}
