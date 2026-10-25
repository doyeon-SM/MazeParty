using System;

namespace MazeParty.Gameplay.Minigames.ShellGame
{
    public enum ShellGamePhase : byte { Inactive, Countdown, Preview, Hiding, Shuffling, Choosing, Reveal, Complete }

    public enum ShellGameItem : byte { Coin, GoldBar, Money, None = 255 }

    // Explicit PRNG and identity permutations keep presentation and authority identical on every peer.
    public sealed class ShellGameRoundPlan
    {
        private readonly int[,] _positions;
        private readonly int[] _a, _b;
        public int ShuffleCount { get; }
        private readonly ShellGameItem[] _items = { ShellGameItem.Coin, ShellGameItem.GoldBar, ShellGameItem.Money };
        public ShellGameItem TargetItem { get; }
        public int TargetBox => BoxForItem(TargetItem);
        public double SwapSeconds { get; }
        public double ShuffleSeconds => ShuffleCount * SwapSeconds;
        public ShellGameRoundPlan(ulong seed, int round)
        {
            if (round < 1 || round > 5) throw new ArgumentOutOfRangeException(nameof(round));
            var random = seed ^ (0x9E3779B97F4A7C15UL * (ulong)round);
            ShuffleCount = 10 + (int)(Next(ref random) % 3);
            for (var i = _items.Length - 1; i > 0; i--)
            {
                var other = (int)(Next(ref random) % (ulong)(i + 1));
                var item = _items[i]; _items[i] = _items[other]; _items[other] = item;
            }
            TargetItem = (ShellGameItem)(Next(ref random) % 3);
            // Add half the original starting speed; preserve each later round's speed increase.
            var originalSwapSeconds = .8d - (round - 1) * .125d;
            SwapSeconds = 1d / (1d / originalSwapSeconds + .5d / .8d);
            _positions = new int[ShuffleCount + 1, 3];
            _a = new int[ShuffleCount]; _b = new int[ShuffleCount];
            for (var box = 0; box < 3; box++) _positions[0,box] = box;
            for (var step = 0; step < ShuffleCount; step++)
            {
                var a = (int)(Next(ref random) % 3);
                var b = (a + 1 + (int)(Next(ref random) % 2)) % 3;
                _a[step] = a; _b[step] = b;
                for (var box = 0; box < 3; box++) _positions[step+1,box] = _positions[step,box];
                _positions[step+1,a] = _positions[step,b];
                _positions[step+1,b] = _positions[step,a];
            }
        }
        private static ulong Next(ref ulong state)
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15UL;
                var z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
        public ShellGameItem ItemInBox(int box) => _items[box];
        public int BoxForItem(ShellGameItem item) => Array.IndexOf(_items, item);
        public int Position(int box, int completedSwaps) => _positions[Math.Max(0, Math.Min(ShuffleCount, completedSwaps)),box];
        public int FirstBox(int step) => _a[step];
        public int SecondBox(int step) => _b[step];
    }

    public sealed class ShellGameMatchState
    {
        private readonly MinigameRulesConfig _rules;
        public const int PlayerCount = 4;
        public const int RoundCount = 5;
        public const double CountdownSeconds = 3d, PreviewSeconds = 2d, LiftSeconds = .45d, ChoiceSeconds = 10d, ResultSeconds = 2d;
        private readonly int[] _choices = { -1, -1, -1, -1 };
        private readonly int[] _scores = new int[4];
        public ulong Seed { get; }
        public int RoundNumber { get; private set; } = 1;
        public ShellGamePhase Phase { get; private set; } = ShellGamePhase.Countdown;
        public ShellGameRoundPlan Plan { get; private set; }
        public double Elapsed { get; private set; }
        public double PhaseEndsAt { get; private set; } = CountdownSeconds;
        public double Remaining => Math.Max(0d, PhaseEndsAt - Elapsed);
        public ShellGameItem VisibleTargetItem => Phase == ShellGamePhase.Choosing ||
            Phase == ShellGamePhase.Reveal || Phase == ShellGamePhase.Complete ? Plan.TargetItem : ShellGameItem.None;
        public ShellGameMatchState(ulong seed, MinigameRulesConfig? rules = null) {
            _rules = MinigameRulesConfig.Resolve(ScheduledMinigameId.ShellGame, rules); Seed = seed; Plan = new ShellGameRoundPlan(seed,1); }
        public static bool IsValidSlot(int slot) => slot >= 0 && slot < 4;
        public int GetChoice(int slot) => IsValidSlot(slot) ? _choices[slot] : -1;
        public int GetVisibleChoice(int slot, int viewerSlot) =>
            Phase == ShellGamePhase.Reveal || Phase == ShellGamePhase.Complete || slot == viewerSlot ? GetChoice(slot) : -1;
        public byte RevealedChoices
        {
            get
            {
                if (Phase != ShellGamePhase.Reveal && Phase != ShellGamePhase.Complete) return 0;
                byte packed = 0;
                for (var i=0;i<4;i++) packed |= (byte)((_choices[i]+1) << (i*2));
                return packed;
            }
        }
        public int GetScore(int slot) => IsValidSlot(slot) ? _scores[slot] : 0;
        public int GetRank(int slot)
        {
            if (!IsValidSlot(slot)) return 0;
            var rank=1;
            for (var i=0;i<4;i++) if (_scores[i]>_scores[slot]) rank++;
            return rank;
        }
        public bool TryChoose(int slot, int box)
        {
            if (!IsValidSlot(slot) || box < 0 || box > 2 || Phase != ShellGamePhase.Choosing ||
                Remaining <= 0 || _choices[slot] >= 0) return false;
            _choices[slot]=box;
            if (Array.TrueForAll(_choices, c=>c>=0)) Reveal(Elapsed);
            return true;
        }
        private void Reveal(double at)
        {
            for (var i=0;i<4;i++) if (_choices[i]==Plan.TargetBox) _scores[i]++;
            Phase=ShellGamePhase.Reveal; PhaseEndsAt=at+ResultSeconds;
        }
        public void AdvanceTo(double elapsed)
        {
            if (double.IsNaN(elapsed) || double.IsInfinity(elapsed) || elapsed < Elapsed) return;
            while (Phase!=ShellGamePhase.Complete && elapsed>=PhaseEndsAt)
            {
                Elapsed=PhaseEndsAt;
                switch (Phase)
                {
                    case ShellGamePhase.Countdown: Phase=ShellGamePhase.Preview; PhaseEndsAt+=PreviewSeconds; break;
                    case ShellGamePhase.Preview: Phase=ShellGamePhase.Hiding; PhaseEndsAt+=LiftSeconds; break;
                    case ShellGamePhase.Hiding: Phase=ShellGamePhase.Shuffling; PhaseEndsAt+=Plan.ShuffleSeconds; break;
                    case ShellGamePhase.Shuffling: Phase=ShellGamePhase.Choosing; PhaseEndsAt+=ChoiceSeconds; break;
                    case ShellGamePhase.Choosing: Reveal(Elapsed); break;
                    case ShellGamePhase.Reveal:
                        if (RoundNumber==_rules.Rounds) { Phase=ShellGamePhase.Complete; break; }
                        RoundNumber++; Plan=new ShellGameRoundPlan(Seed,RoundNumber);
                        for(var i=0;i<4;i++) _choices[i]=-1;
                        Phase=ShellGamePhase.Countdown; PhaseEndsAt+=CountdownSeconds; break;
                }
            }
            Elapsed=Math.Min(elapsed,PhaseEndsAt);
        }
    }
}
