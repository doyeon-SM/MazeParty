using System;

namespace MazeParty.Gameplay
{
    public enum BoardSwapProgress { Idle, Casting, Completed, Cancelled }

    public sealed class BoardSwapChannel
    {
        public int TargetSlot { get; private set; } = -1;
        public double Remaining { get; private set; }
        public bool Active => TargetSlot >= 0;
        public bool Begin(int sourceSlot, int targetSlot, double seconds)
        {
            if (Active || sourceSlot < 0 || sourceSlot >= 4 || targetSlot < 0 || targetSlot >= 4 ||
                sourceSlot == targetSlot || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) return false;
            TargetSlot = targetSlot; Remaining = seconds; return true;
        }
        public BoardSwapProgress Tick(double elapsed, bool paused, bool actionAvailable, bool targetAvailable)
        {
            if (!Active) return BoardSwapProgress.Idle;
            if (!actionAvailable || !targetAvailable) { Clear(); return BoardSwapProgress.Cancelled; }
            if (paused) return BoardSwapProgress.Casting;
            Remaining = Math.Max(0, Remaining - Math.Max(0, elapsed));
            if (Remaining > .0000001d) return BoardSwapProgress.Casting;
            Clear(); return BoardSwapProgress.Completed;
        }
        public bool InterruptByDamage(int actualDamage)
        { if (!Active || actualDamage <= 0) return false; Clear(); return true; }
        public void Clear() { Remaining = 0; TargetSlot = -1; }
    }
}
