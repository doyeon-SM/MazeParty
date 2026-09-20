using System;

namespace MazeParty.Gameplay.Minigames.SnowySpin
{
    /// <summary>Local XZ dimensions and fixed-step movement for the shared-camera arena.</summary>
    public static class SnowySpinRules
    {
        public const int PlayerCount = 4;
        public const int RoundCount = 3;
        public const double RoundDurationSeconds = 60d;
        public const double CountdownSeconds = 3d;
        public const double ResultSeconds = 4d;
        public const int SimulationHz = 60;
        public const double SimulationStepSeconds = 1d / SimulationHz;
        public const double ArenaRadius = 8d;
        public const double BallRadius = 0.65d;
        public const double SpawnRadius = 4.5d;
        public const double Acceleration = 13d;
        public const double MaximumSpeed = 6d;
        public const double SteeringDrag = 0.4d;
        public const double CoastingDrag = 2.8d;
        public const double CollisionRestitution = 0.8d;

        public static bool IsValidPlayerSlot(int slot)
        {
            return slot >= 0 && slot < PlayerCount;
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
