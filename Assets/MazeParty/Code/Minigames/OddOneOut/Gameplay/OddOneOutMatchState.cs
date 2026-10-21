using System;

namespace MazeParty.Gameplay.Minigames.OddOneOut
{
    public enum OddOneOutPhase : byte { Inactive, Choosing, Reveal, Complete }

    /// <summary>Server-owned choices stay private until the exact countdown boundary.</summary>
    public sealed class OddOneOutMatchState
    {
        private readonly MinigameRulesConfig _rules;
        public OddOneOutMatchState(MinigameRulesConfig? rules = null)
        { _rules = MinigameRulesConfig.Resolve(ScheduledMinigameId.OddOneOut, rules); }
        public const int PlayerCount = 4;
        public const int RoundCount = 10;
        public const double CountdownSeconds = 3d, ResultSeconds = 2d;
        private readonly bool[] _choices = new bool[PlayerCount];
        private readonly int[] _scores = new int[PlayerCount];
        public int RoundNumber { get; private set; } = 1;
        public OddOneOutPhase Phase { get; private set; } = OddOneOutPhase.Choosing;
        public int WinnerSlot { get; private set; } = -1;
        public double Elapsed { get; private set; }
        public double PhaseEndsAt { get; private set; } = CountdownSeconds;
        public double Remaining => Math.Max(0d, PhaseEndsAt - Elapsed);
        public static bool IsValidSlot(int slot) => slot >= 0 && slot < PlayerCount;
        public bool GetChoice(int slot) => IsValidSlot(slot) && _choices[slot];
        public bool GetVisibleChoice(int slot, int viewerSlot) =>
            IsValidSlot(slot) && (Phase != OddOneOutPhase.Choosing || slot == viewerSlot) && _choices[slot];
        public byte RevealedMask
        {
            get
            {
                if (Phase == OddOneOutPhase.Choosing) return 0;
                byte mask = 0;
                for (var i = 0; i < PlayerCount; i++) if (_choices[i]) mask |= (byte)(1 << i);
                return mask;
            }
        }
        public int GetScore(int slot) => IsValidSlot(slot) ? _scores[slot] : 0;
        public int GetRank(int slot)
        {
            if (!IsValidSlot(slot)) return 0;
            var rank = 1;
            for (var i = 0; i < PlayerCount; i++) if (_scores[i] > _scores[slot]) rank++;
            return rank;
        }
        public bool TrySetChoice(int slot, bool sitting)
        {
            if (!IsValidSlot(slot) || Phase != OddOneOutPhase.Choosing) return false;
            _choices[slot] = sitting;
            return true;
        }
        public void AdvanceTo(double elapsed)
        {
            if (double.IsNaN(elapsed) || double.IsInfinity(elapsed) || elapsed < Elapsed) return;
            while (Phase != OddOneOutPhase.Complete && elapsed >= PhaseEndsAt)
            {
                if (Phase == OddOneOutPhase.Choosing)
                {
                    var count = 0;
                    for (var i = 0; i < PlayerCount; i++) if (_choices[i]) count++;
                    WinnerSlot = -1;
                    if (count == 1 || count == 3)
                        for (var i = 0; i < PlayerCount; i++)
                            if (_choices[i] == (count == 1)) { WinnerSlot = i; _scores[i]++; break; }
                    Phase = OddOneOutPhase.Reveal;
                    PhaseEndsAt += ResultSeconds;
                }
                else if (RoundNumber == _rules.Rounds) Phase = OddOneOutPhase.Complete;
                else
                {
                    RoundNumber++;
                    Array.Clear(_choices, 0, _choices.Length);
                    WinnerSlot = -1;
                    Phase = OddOneOutPhase.Choosing;
                    PhaseEndsAt += CountdownSeconds;
                }
            }
            Elapsed = Math.Min(elapsed, PhaseEndsAt);
        }
    }
}
