using System;

namespace MazeParty.Gameplay
{
    /// <summary>Private per-turn dice progress; a duplicate settlement can never add movement.</summary>
    public static class BoardDiceProgress
    {
        // Stable across process restarts and independent of visual/physics randomness.
        // The match seed is already persisted with the turn-overview checkpoint.
        public static int RollFace(int matchSeed, int turn, int slot, int die, int minimum = 1, int maximum = 12)
        {
            if (turn < 1 || slot < 0 || slot >= 4 || die < 0 || die > 1 ||
                minimum < 1 || maximum > 12 || minimum > maximum)
                throw new ArgumentOutOfRangeException(nameof(turn), "Invalid board dice context.");
            unchecked
            {
                uint value = Mix((uint)matchSeed ^ 0xD1CEB00Du);
                value = Mix(value ^ (uint)turn);
                value = Mix(value ^ ((uint)slot * 2u + (uint)die));
                return minimum + (int)(value % (uint)(maximum - minimum + 1));
            }
        }

        private static uint Mix(uint value)
        {
            unchecked
            {
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                return value ^ (value >> 16);
            }
        }
        public static bool TrySettle(bool doubled, int first, int second, int die, int face,
            out int nextFirst, out int nextSecond, out int total)
        {
            nextFirst = first; nextSecond = second; total = 0;
            if (first < 0 || first > 12 || second < 0 || second > 12 || face < 1 || face > 12 ||
                die < 0 || die > 1 || (!doubled && die != 0) || (die == 0 ? first : second) != 0) return false;
            if (die == 0) nextFirst = face; else nextSecond = face;
            if (nextFirst > 0 && (!doubled || nextSecond > 0)) total = nextFirst + (doubled ? nextSecond : 0);
            return true;
        }
        public static int MissingDieOnTimeout(bool doubled, int first, int second) =>
            !doubled || (first == 0) == (second == 0) ? -1 : first == 0 ? 0 : 1;
    }
}
