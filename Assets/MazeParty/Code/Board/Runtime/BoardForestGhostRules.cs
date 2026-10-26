using System;

namespace MazeParty.Gameplay
{
    public enum ForestGhostResource : byte { Gold, Health, Key }

    public readonly struct ForestGhostChoice
    {
        public ForestGhostChoice(int slot, bool strong, ForestGhostResource resource, int amount, int emptyIndex)
        { Slot = slot; Strong = strong; Resource = resource; Amount = amount; EmptyIndex = emptyIndex; }
        public int Slot { get; }
        public bool Strong { get; }
        public ForestGhostResource Resource { get; }
        public int Amount { get; }
        public int EmptyIndex { get; }
    }

    public static class BoardForestGhostRules
    {
        public const string MapId = "forest-graybox";
        public const double Duration = 3d;
        public const double ApplyAt = 1.25d;

        public static ForestGhostChoice Resolve(int seed, int turn, byte eligibleMask, int emptyCount)
        {
            if (emptyCount < 0) throw new ArgumentOutOfRangeException(nameof(emptyCount));
            uint state = unchecked((uint)seed ^ (uint)turn * 0x9E3779B9u ^ 0x47484F53u);
            if (state == 0) state = 1;
            var target = Next(ref state, 5);
            var resource = Next(ref state, 100);
            var gain = Next(ref state, 2) == 0;
            var choice = ResolveRolls(turn, target, resource, gain, eligibleMask);
            return new ForestGhostChoice(choice.Slot, choice.Strong, choice.Resource,
                choice.Amount, emptyCount > 0 ? Next(ref state, emptyCount) : -1);
        }

        public static ForestGhostChoice ResolveRolls(int turn, int targetRoll, int resourceRoll,
            bool gain, byte eligibleMask)
        {
            if (turn < 1) throw new ArgumentOutOfRangeException(nameof(turn));
            if (targetRoll < 0 || targetRoll >= 5) throw new ArgumentOutOfRangeException(nameof(targetRoll));
            if (resourceRoll < 0 || resourceRoll >= 100) throw new ArgumentOutOfRangeException(nameof(resourceRoll));
            var strong = turn % 5 == 0;
            var goldLimit = strong ? 50 : 60;
            var healthLimit = strong ? 80 : 90;
            var resource = resourceRoll < goldLimit ? ForestGhostResource.Gold :
                resourceRoll < healthLimit ? ForestGhostResource.Health : ForestGhostResource.Key;
            var amount = resource == ForestGhostResource.Gold ? (strong ? 20 : 10) :
                resource == ForestGhostResource.Health ? (strong ? 50 : 30) : 1;
            // An excluded seat keeps its 20% bucket; it becomes an empty-tile visit.
            var slot = targetRoll < 4 && (eligibleMask & (1 << targetRoll)) != 0 ? targetRoll : -1;
            return new ForestGhostChoice(slot, strong, resource, gain ? amount : -amount, -1);
        }

        private static int Next(ref uint state, int maximum)
        {
            // Rejection sampling avoids changing the documented bucket weights.
            var limit = uint.MaxValue - uint.MaxValue % (uint)maximum;
            do { state ^= state << 13; state ^= state >> 17; state ^= state << 5; } while (state >= limit);
            return (int)(state % (uint)maximum);
        }
    }
}
