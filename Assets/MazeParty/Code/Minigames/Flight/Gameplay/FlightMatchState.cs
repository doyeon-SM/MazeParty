using System;

namespace MazeParty.Gameplay.Minigames.Flight
{
    public enum FlightPhase : byte { Inactive, Countdown, Running, Result, Complete }

    public static class FlightRules
    {
        public const int PlayerCount = 4, GatePoolCount = 5;
        public const double CountdownSeconds = 3, ResultSeconds = 2;
        public const double RunSeconds = 120;
        public const double Step = 1d / 120, Gravity = 12, FlapVelocity = 4.5, MaxFallSpeed = 4.5;
        public const double BodyHeight = 2, HalfWidth = .42, GateWidth = .9, Spacing = 5.5, FirstGate = 5.25;
        public const double ViewHalfHeight = 7.4, CameraOffsetX = 2.7, RecycleX = -11.1;
        public const double Ceiling = ViewHalfHeight - BodyHeight / 2, Bottom = -ViewHalfHeight - BodyHeight / 2;
        public const double CollisionRadiusX = (GateWidth / 2) + HalfWidth;
        public static bool ValidSlot(int slot) => slot >= 0 && slot < PlayerCount;
        public static double Speed(double time, double duration = 120d) => 5 * (1 + 2 * Math.Max(0, Math.Min(duration, time)) / duration);
        public static double Distance(double time, double duration = 120d)
        {
            var t = Math.Max(0, Math.Min(duration, time));
            return 5 * (t + t * t / duration) + Math.Max(0, time - duration) * 15;
        }
        public static double TimeAtDistance(double distance, double duration = 120d) => distance <= 0 ? 0 :
            distance >= 10 * duration ? duration + (distance - 10 * duration) / 15 :
            2 * distance / (5 + Math.Sqrt(25 + 20 * distance / duration));

        // Shared by authority, prediction and the course's witness path.
        public static void Integrate(ref double y, ref double velocity, double duration)
        {
            if (duration <= 0) return;
            if (velocity > 0 && y + velocity * velocity / (2 * Gravity) >= Ceiling)
            {
                var untilCeiling = (velocity - Math.Sqrt(Math.Max(0,
                    velocity * velocity - 2 * Gravity * Math.Max(0, Ceiling - y)))) / Gravity;
                if (untilCeiling <= duration)
                {
                    y = Ceiling;
                    velocity = 0;
                    duration -= untilCeiling;
                }
            }
            var falling = Math.Max(0, Math.Min(duration, (velocity + MaxFallSpeed) / Gravity));
            y += velocity * falling - Gravity * falling * falling / 2;
            velocity = Math.Max(-MaxFallSpeed, velocity - Gravity * falling);
            y += velocity * (duration - falling);
        }
    }

    public readonly struct FlightGate
    {
        public FlightGate(int index, double center, double height, double duration = 120d)
        {
            Index = index; Center = center; Height = height;
            Distance = FlightRules.FirstGate + index * FlightRules.Spacing;
            EntersAt = FlightRules.TimeAtDistance(Distance - FlightRules.CollisionRadiusX, duration);
            ExitsAt = FlightRules.TimeAtDistance(Distance + FlightRules.CollisionRadiusX, duration);
        }
        public int Index { get; }
        public double Center { get; }
        public double Height { get; }
        public double Distance { get; }
        public double EntersAt { get; }
        public double ExitsAt { get; }
        public double Lower => Center - Height / 2;
        public double Upper => Center + Height / 2;
    }

    /// <summary>
    /// Constructive reachability: generate one continuous legal flap trajectory, then choose each
    /// random opening around ALL of that trajectory during the obstacle's horizontal overlap.
    /// The same path proves the complete stream is traversable, including acceleration, body size,
    /// obstacle thickness and the initial fall. It does not adapt to or assist a particular player.
    /// </summary>
    public sealed class FlightCourse
    {
        private readonly double _duration;
        private const double Margin = .12;
        private readonly FlightGate[] _gates;
        private readonly bool[] _witnessFlaps;
        private readonly double[] _witnessY;
        public int Count => _gates.Length;
        public int WitnessTicks => _witnessFlaps.Length;
        public FlightGate this[int index] => _gates[index];
        public bool WitnessFlap(int tick) => tick >= 0 && tick < _witnessFlaps.Length && _witnessFlaps[tick];
        public double WitnessY(int tick) => _witnessY[Math.Max(0, Math.Min(_witnessY.Length - 1, tick))];

        public FlightCourse(ulong seed, double duration = 120d)
        {
            _duration = duration;
            _gates = new FlightGate[(int)Math.Ceiling((FlightRules.Distance(_duration, _duration) + 28) / FlightRules.Spacing) + 1];
            var lastDistance = FlightRules.FirstGate + (_gates.Length - 1) * FlightRules.Spacing;
            var ticks = (int)Math.Ceiling(FlightRules.TimeAtDistance(lastDistance + FlightRules.CollisionRadiusX, _duration) / FlightRules.Step) + 2;
            _witnessFlaps = new bool[ticks];
            _witnessY = new double[ticks + 1];
            var random = seed ^ 0xC13FA9A902A6328FUL;
            double y = 0, velocity = 0, target = 0;
            for (var tick = 0; tick < ticks; tick++)
            {
                if (tick % 96 == 0) target = -4.1 + NextUnit(ref random) * 8.2;
                // At most about three flaps per second on the witness, with a brief initial glide.
                if (tick >= 15 && velocity <= .1 && y < target)
                {
                    velocity = FlightRules.FlapVelocity;
                    _witnessFlaps[tick] = true;
                }
                FlightRules.Integrate(ref y, ref velocity, FlightRules.Step);
                _witnessY[tick + 1] = y;
            }
            for (var index = 0; index < _gates.Length; index++)
            {
                var shell = new FlightGate(index, 0, 0, _duration);
                var first = Math.Max(0, (int)Math.Floor(shell.EntersAt / FlightRules.Step));
                var last = Math.Min(ticks, (int)Math.Ceiling(shell.ExitsAt / FlightRules.Step));
                var minimum = double.PositiveInfinity;
                var maximum = double.NegativeInfinity;
                for (var tick = first; tick <= last; tick++)
                {
                    minimum = Math.Min(minimum, _witnessY[tick]);
                    maximum = Math.Max(maximum, _witnessY[tick]);
                }
                // Margin also encloses extrema between 120 Hz witness samples (< .038 units).
                var required = maximum - minimum + FlightRules.BodyHeight + 2 * Margin;
                var height = Math.Max(required, FlightRules.BodyHeight * (1.5 + NextUnit(ref random)));
                if (height > FlightRules.BodyHeight * 2.5)
                    throw new InvalidOperationException("Flight witness exceeded the supported opening.");
                var low = maximum + FlightRules.BodyHeight / 2 + Margin - height / 2;
                var high = minimum - FlightRules.BodyHeight / 2 - Margin + height / 2;
                var center = low + (high - low) * NextUnit(ref random);
                _gates[index] = new FlightGate(index, center, height, _duration);
            }
        }
        private static double NextUnit(ref ulong state)
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15UL;
                var z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return ((z ^ (z >> 31)) >> 11) * (1d / 9007199254740992d);
            }
        }
        public void PooledGate(int index, double time, out int sequence, out double x)
        {
            var original = FlightRules.FirstGate + index * FlightRules.Spacing;
            var distance = FlightRules.Distance(time, _duration);
            var length = FlightRules.GatePoolCount * FlightRules.Spacing;
            var cycles = Math.Max(0, (int)Math.Floor((distance - original + FlightRules.RecycleX) / length) + 1);
            sequence = index + cycles * FlightRules.GatePoolCount;
            x = original + cycles * length - distance;
        }
    }

    public sealed class FlightMatchState
    {
        private readonly MinigameRulesConfig _rules;
        private readonly double[] _y = new double[4], _velocity = new double[4], _eliminatedAt = new double[4];
        private readonly int[] _flaps = new int[4];
        private readonly double[] _lastFlap = { -10, -10, -10, -10 };
        private readonly bool[] _alive = { true, true, true, true };
        private readonly double[] _hits = new double[4], _orderedHits = new double[4];
        private double _nextTick = FlightRules.Step;
        public FlightMatchState(ulong seed, MinigameRulesConfig? rules = null) {
            _rules = MinigameRulesConfig.Resolve(ScheduledMinigameId.Flight, rules); Seed = seed; Course = new FlightCourse(seed, _rules.Seconds); }
        public ulong Seed { get; }
        public FlightCourse Course { get; }
        public FlightPhase Phase { get; private set; } = FlightPhase.Countdown;
        public double Elapsed { get; private set; }
        public double RunElapsed { get; private set; }
        public double PhaseEndsAt { get; private set; } = FlightRules.CountdownSeconds;
        public double Remaining => Math.Max(0, PhaseEndsAt - Elapsed);
        public bool IsAlive(int slot) => FlightRules.ValidSlot(slot) && _alive[slot];
        public double Height(int slot) => FlightRules.ValidSlot(slot) ? _y[slot] : 0;
        public double Velocity(int slot) => FlightRules.ValidSlot(slot) ? _velocity[slot] : 0;
        public int Flaps(int slot) => FlightRules.ValidSlot(slot) ? _flaps[slot] : 0;
        public double LastFlap(int slot) => FlightRules.ValidSlot(slot) ? _lastFlap[slot] : -10;
        public double Survival(int slot) => FlightRules.ValidSlot(slot) ? _alive[slot] ? RunElapsed : _eliminatedAt[slot] : 0;
        public int Rank(int slot)
        {
            if (!FlightRules.ValidSlot(slot)) return 0;
            var rank = 1;
            for (var i = 0; i < 4; i++)
                if ((!_alive[slot] && _alive[i]) || (_alive[i] == _alive[slot] && Survival(i) > Survival(slot) + 1e-7)) rank++;
            return rank;
        }
        public bool TryFlap(int slot)
        {
            if (Phase != FlightPhase.Running || !IsAlive(slot) || Remaining <= 0 ||
                RunElapsed - _lastFlap[slot] < FlightRules.Step - 1e-9) return false;
            _velocity[slot] = FlightRules.FlapVelocity;
            _lastFlap[slot] = RunElapsed; _flaps[slot]++;
            return true;
        }
        public void AdvanceTo(double target)
        {
            if (double.IsNaN(target) || double.IsInfinity(target) || target < Elapsed) return;
            while (Elapsed < target && Phase != FlightPhase.Complete)
            {
                if (Phase == FlightPhase.Running)
                {
                    var next = Math.Min(target, Math.Min(PhaseEndsAt, FlightRules.CountdownSeconds + _nextTick));
                    var to = next - FlightRules.CountdownSeconds;
                    AdvanceFlight(to);
                    Elapsed = FlightRules.CountdownSeconds + RunElapsed;
                    if (RunElapsed + 1e-10 >= _nextTick) _nextTick += FlightRules.Step;
                    if (AliveCount() <= 1 || RunElapsed >= _rules.Seconds - 1e-9)
                    { Phase = FlightPhase.Result; PhaseEndsAt = Elapsed + FlightRules.ResultSeconds; }
                    continue;
                }
                Elapsed = Math.Min(target, PhaseEndsAt);
                if (Elapsed < PhaseEndsAt) break;
                if (Phase == FlightPhase.Countdown)
                { Phase = FlightPhase.Running; PhaseEndsAt = Elapsed + _rules.Seconds; }
                else if (Phase == FlightPhase.Result) Phase = FlightPhase.Complete;
            }
        }
        private int AliveCount()
        {
            var count = 0; for (var i = 0; i < 4; i++) if (_alive[i]) count++; return count;
        }
        private void AdvanceFlight(double to)
        {
            var hits = _hits;
            for (var i = 0; i < 4; i++) hits[i] = _alive[i] ? FirstCollision(i, RunElapsed, to) : double.PositiveInfinity;
            // Stop at the exact death which decides the match, including all simultaneous contacts.
            var end = to;
            var survivors = AliveCount();
            Array.Copy(hits, _orderedHits, 4);
            var ordered = _orderedHits; Array.Sort(ordered);
            foreach (var hit in ordered)
            {
                if (double.IsPositiveInfinity(hit)) break;
                survivors--;
                if (survivors <= 1) { end = Math.Min(end, hit); break; }
            }
            var dt = Math.Max(0, end - RunElapsed);
            for (var i = 0; i < 4; i++)
            {
                if (!_alive[i]) continue;
                var hit = hits[i] <= end + 1e-8;
                var duration = hit ? Math.Max(0, hits[i] - RunElapsed) : dt;
                FlightRules.Integrate(ref _y[i], ref _velocity[i], duration);
                if (hit) { _alive[i] = false; _eliminatedAt[i] = hits[i]; }
            }
            RunElapsed = end;
        }
        private double FirstCollision(int slot, double from, double to)
        {
            var result = FirstOutside(slot, from, to, double.NegativeInfinity, FlightRules.Bottom);
            // At most two neighbouring gates can overlap a step; this also handles long frame catch-up.
            var first = Math.Max(0, (int)Math.Floor((FlightRules.Distance(from, _rules.Seconds) - FlightRules.FirstGate - FlightRules.CollisionRadiusX) / FlightRules.Spacing));
            var last = Math.Min(Course.Count - 1, first + 2);
            for (var i = first; i <= last; i++)
            {
                var gate = Course[i];
                var a = Math.Max(from, gate.EntersAt); var b = Math.Min(to, gate.ExitsAt);
                if (a > b) continue;
                result = Math.Min(result, FirstOutside(slot, a, b,
                    gate.Upper - FlightRules.BodyHeight / 2, gate.Lower + FlightRules.BodyHeight / 2));
            }
            return result;
        }
        // upper=+infinity means no ceiling obstacle. The bottom-only call uses -infinity sentinel.
        private double FirstOutside(int slot, double from, double to, double upper, double lower)
        {
            if (double.IsNegativeInfinity(upper)) upper = double.PositiveInfinity;
            var start = RunElapsed;
            var apex = _velocity[slot] > 0 ? start + _velocity[slot] / FlightRules.Gravity : start;
            var discriminant = _velocity[slot] * _velocity[slot] -
                2 * FlightRules.Gravity * Math.Max(0, FlightRules.Ceiling - _y[slot]);
            if (_velocity[slot] > 0 && discriminant >= 0)
                apex = start + (_velocity[slot] - Math.Sqrt(discriminant)) / FlightRules.Gravity;
            var split = Math.Max(from, Math.Min(to, apex));
            var hit = SegmentContact(slot, start, from, split, upper, lower);
            return Math.Min(hit, SegmentContact(slot, start, split, to, upper, lower));
        }
        private double SampleY(int slot, double duration)
        {
            var y = _y[slot]; var velocity = _velocity[slot];
            FlightRules.Integrate(ref y, ref velocity, Math.Max(0, duration)); return y;
        }
        private double SegmentContact(int slot, double origin, double a, double b, double upper, double lower)
        {
            var ya = SampleY(slot, a - origin);
            if (ya >= upper || ya <= lower) return a;
            var yb = SampleY(slot, b - origin);
            if (yb < upper && yb > lower) return double.PositiveInfinity;
            for (var iteration = 0; iteration < 36; iteration++)
            {
                var mid = (a + b) / 2; var y = SampleY(slot, mid - origin);
                if (y >= upper || y <= lower) b = mid; else a = mid;
            }
            return b;
        }
    }
}
