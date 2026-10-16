using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public static class BoardDeathGoldPresentationRules
    {
        public const double DropSeconds = 0.3d;
        public const double TravelSeconds = 0.8d;
        public const double SpreadSeconds = 0.45d;

        public static double ArrivalTime(int index, int amount) =>
            DropSeconds + TravelSeconds +
            (amount > 1 ? SpreadSeconds * index / (amount - 1) : 0d);

        public static double Duration(int amount) =>
            amount <= 0 ? 0d : ArrivalTime(amount - 1, amount);

        public static bool IsComplete(int amount, double elapsed) =>
            elapsed >= Duration(amount);

        public static Vector3 Position(Vector3 origin, Vector3 destination,
            int index, int amount, double elapsed, int scatterSeed = 0)
        {
            if (elapsed >= ArrivalTime(index, amount)) return destination;
            var angle = (index + scatterSeed) * 2.39996323f;
            var radius = 0.3f + ((index + scatterSeed) % 7) * 0.065f;
            var scatter = origin + new Vector3(Mathf.Cos(angle) * radius,
                0.2f, Mathf.Sin(angle) * radius);
            var drop = Mathf.Clamp01((float)(elapsed / DropSeconds));
            if (elapsed < DropSeconds)
                return Vector3.Lerp(origin + Vector3.up * 0.5f, scatter, drop) +
                    Vector3.up * (Mathf.Sin(drop * Mathf.PI) * 0.65f);

            var launch = ArrivalTime(index, amount) - TravelSeconds;
            var progress = Mathf.Clamp01((float)((elapsed - launch) / TravelSeconds));
            var eased = progress * progress * (3f - 2f * progress);
            // Destination is re-evaluated from the recipient every frame. No physics queries.
            return Vector3.Lerp(scatter, destination, eased) +
                Vector3.up * (Mathf.Sin(progress * Mathf.PI) * 0.4f);
        }
    }
}
