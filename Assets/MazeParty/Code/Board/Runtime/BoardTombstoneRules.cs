using UnityEngine;

namespace MazeParty.Gameplay
{
    public static class BoardTombstoneRules
    {
        public const float InteractionDistance = 4.5f;
        private const float Spacing = 0.72f;

        public static Vector3 DisplayPosition(Vector3 deathPosition, int samePositionOrdinal)
        {
            if (samePositionOrdinal <= 0)
            {
                return deathPosition;
            }

            // Stable square spiral: each grave remains individually aimable.
            var ring = Mathf.CeilToInt((Mathf.Sqrt(samePositionOrdinal + 1f) - 1f) * 0.5f);
            var side = ring * 2;
            var maxIndex = (side + 1) * (side + 1) - 1;
            var offset = maxIndex - samePositionOrdinal;
            int x;
            int z;
            if (offset < side)
            {
                x = ring - offset;
                z = -ring;
            }
            else if (offset < side * 2)
            {
                x = -ring;
                z = -ring + offset - side;
            }
            else if (offset < side * 3)
            {
                x = -ring + offset - side * 2;
                z = ring;
            }
            else
            {
                x = ring;
                z = ring - offset + side * 3;
            }
            return deathPosition + new Vector3(x * Spacing, 0f, z * Spacing);
        }
    }
}
