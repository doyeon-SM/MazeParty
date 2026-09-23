using System;

namespace MazeParty.Gameplay
{
    /// <summary>Private per-turn dice progress; a duplicate settlement can never add movement.</summary>
    public static class BoardDiceProgress
    {
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
