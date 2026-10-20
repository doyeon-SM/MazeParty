using System;
using System.Collections.Generic;

namespace MazeParty.Gameplay.Minigames.Race2
{
    public enum Race2Phase : byte { Inactive, Countdown, Running, RoundResult, Complete }

    public static class Race2Rules
    {
        public const int PlayerCount = 4, BoxCount = 7, PreviewBoxCount = 5;
        public static int RoundCount => MinigameRuntimeSettings.Rounds(ScheduledMinigameId.Race2, 3);
        public const double CountdownSeconds = 3d, ResultSeconds = 4d;
        public static double RoundSeconds => MinigameRuntimeSettings.Seconds(ScheduledMinigameId.Race2, 120d);
        public const double BaseSpeed = 5d, BoxSpacing = 10.5d, FirstBoxDistance = 6.5d;
        // Behind the rear camera as well as below its view, so recycling is never visible.
        public const double RecycleZ = -12d, CollisionHalfDepth = .85d;
        public static bool ValidSlot(int slot) => slot >= 0 && slot < PlayerCount;
        public static double Speed(double time) => BaseSpeed * (1d + 2d * Math.Max(0d, Math.Min(RoundSeconds, time)) / RoundSeconds);
        public static double Distance(double time)
        {
            var t = Math.Max(0d, Math.Min(RoundSeconds, time));
            return BaseSpeed * (t + t * t / RoundSeconds);
        }
        public static double TimeAtDistance(double distance) => distance <= 0d ? 0d :
            2d * distance / (BaseSpeed + Math.Sqrt(BaseSpeed * BaseSpeed + 4d * BaseSpeed * distance / RoundSeconds));
        public static int NextSpectator(int current, Func<int, bool> alive)
        {
            for (var offset = 1; offset <= PlayerCount; offset++)
            {
                var candidate = ((current + offset) % PlayerCount + PlayerCount) % PlayerCount;
                if (alive(candidate)) return candidate;
            }
            return -1;
        }
    }

    /// <summary>Seeded shared obstacle stream, also used by clients to place the fixed pool of recycled boxes.</summary>
    public sealed class Race2BoxStream
    {
        private readonly List<byte> _lanes = new List<byte>();
        private ulong _random;
        public Race2BoxStream(ulong seed, int round) { _random = seed ^ (0xD1B54A32D192ED03UL * (ulong)round); }
        public byte Lane(int sequence)
        {
            if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            while (_lanes.Count <= sequence)
            {
                _random += 0x9E3779B97F4A7C15UL;
                var z = _random; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; z ^= z >> 31;
                var lane = (byte)(z & 1UL); var n = _lanes.Count;
                if (n >= 3 && _lanes[n - 1] == lane && _lanes[n - 2] == lane && _lanes[n - 3] == lane) lane ^= 1;
                _lanes.Add(lane);
            }
            return _lanes[sequence];
        }
        public void Box(int index, double time, out int sequence, out double z)
        {
            if (index < 0 || index >= Race2Rules.BoxCount) throw new ArgumentOutOfRangeException(nameof(index));
            var original = Race2Rules.FirstBoxDistance + index * Race2Rules.BoxSpacing;
            var distance = Race2Rules.Distance(time); var length = Race2Rules.BoxCount * Race2Rules.BoxSpacing;
            var cycles = Math.Max(0, (int)Math.Floor((distance - original + Race2Rules.RecycleZ) / length) + 1);
            sequence = index + cycles * Race2Rules.BoxCount;
            z = original + cycles * length - distance;
        }
    }

    /// <summary>All transitions and swept collision times use the authoritative match clock.</summary>
    public sealed class Race2MatchState
    {
        private readonly byte[] _lanes = new byte[4];
        private readonly bool[] _alive = new bool[4];
        private readonly double[] _eliminatedAt = new double[4];
        private readonly int[] _scores = new int[4], _roundRanks = new int[4];
        private double _runningStartedAt, _roundElapsed;
        private int _nextCrossing;
        public ulong Seed { get; }
        public Race2Phase Phase { get; private set; }
        public int RoundNumber { get; private set; }
        public double Elapsed { get; private set; }
        public double PhaseEndsAt { get; private set; }
        public double RoundElapsed => _roundElapsed;
        public double Remaining => Phase == Race2Phase.Complete ? 0d : Math.Max(0d, PhaseEndsAt - Elapsed);
        public Race2BoxStream Boxes { get; private set; }
        public Race2MatchState(ulong seed) { Seed = seed; BeginRound(1, 0d); }
        public bool IsAlive(int slot) => Race2Rules.ValidSlot(slot) && _alive[slot];
        public byte GetLane(int slot) => Race2Rules.ValidSlot(slot) ? _lanes[slot] : (byte)0;
        public int GetScore(int slot) => Race2Rules.ValidSlot(slot) ? _scores[slot] : 0;
        public int GetRoundRank(int slot) => Race2Rules.ValidSlot(slot) ? _roundRanks[slot] : 0;
        public int GetRank(int slot) => Race2Rules.ValidSlot(slot) ? CompetitionRanking.Rank(_scores, slot) : 0;
        public bool TrySetLane(int slot, byte lane)
        {
            if (Phase != Race2Phase.Running || !IsAlive(slot) || lane > 1 || _lanes[slot] == lane) return false;
            _lanes[slot] = lane;
            // Entering a box while its volume overlaps the player is a collision too.
            ResolveOverlaps();
            return true;
        }
        public void AdvanceTo(double target)
        {
            if (double.IsNaN(target) || double.IsInfinity(target) || target < Elapsed) return;
            while (Elapsed < target && Phase != Race2Phase.Complete)
            {
                if (Phase == Race2Phase.Running)
                {
                    var crossing = Race2Rules.TimeAtDistance(Race2Rules.FirstBoxDistance +
                        _nextCrossing * Race2Rules.BoxSpacing - Race2Rules.CollisionHalfDepth);
                    var eventAt = _runningStartedAt + crossing;
                    var until = Math.Min(target, PhaseEndsAt);
                    if (eventAt <= until)
                    {
                        Elapsed = eventAt; _roundElapsed = crossing;
                        var lane = Boxes.Lane(_nextCrossing++);
                        for (var i = 0; i < 4; i++) if (_alive[i] && _lanes[i] == lane) Eliminate(i);
                        FinishIfDecided();
                        continue;
                    }
                    Elapsed = until; _roundElapsed = Elapsed - _runningStartedAt;
                    if (Elapsed >= PhaseEndsAt) FinishRound();
                }
                else
                {
                    Elapsed = Math.Min(target, PhaseEndsAt);
                    if (Elapsed < PhaseEndsAt) break;
                    if (Phase == Race2Phase.Countdown)
                    {
                        Phase = Race2Phase.Running; _runningStartedAt = Elapsed;
                        PhaseEndsAt = Elapsed + Race2Rules.RoundSeconds;
                    }
                    else if (RoundNumber < Race2Rules.RoundCount) BeginRound(RoundNumber + 1, Elapsed);
                    else { Phase = Race2Phase.Complete; PhaseEndsAt = Elapsed; }
                }
            }
        }
        private void BeginRound(int round, double now)
        {
            RoundNumber = round; Phase = Race2Phase.Countdown; PhaseEndsAt = now + Race2Rules.CountdownSeconds;
            _roundElapsed = 0d; _nextCrossing = 0; Boxes = new Race2BoxStream(Seed, round);
            for (var i = 0; i < 4; i++) { _alive[i] = true; _lanes[i] = 0; _eliminatedAt[i] = 0; _roundRanks[i] = 0; }
        }
        private void ResolveOverlaps()
        {
            for (var b = 0; b < Race2Rules.BoxCount; b++)
            {
                Boxes.Box(b, _roundElapsed, out var sequence, out var z);
                if (Math.Abs(z) > Race2Rules.CollisionHalfDepth + 1e-9) continue;
                var lane = Boxes.Lane(sequence);
                for (var i = 0; i < 4; i++) if (_alive[i] && _lanes[i] == lane) Eliminate(i);
            }
            FinishIfDecided();
        }
        private void Eliminate(int slot) { _alive[slot] = false; _eliminatedAt[slot] = _roundElapsed; }
        private void FinishIfDecided()
        {
            var count = 0; for (var i = 0; i < 4; i++) if (_alive[i]) count++;
            if (count <= 1) FinishRound();
        }
        private void FinishRound()
        {
            // Batch collisions are resolved before ranking: a shared impact never depends on slot order.
            for (var i = 0; i < 4; i++)
            {
                var rank = 1;
                if (!_alive[i]) for (var j = 0; j < 4; j++)
                    if (_alive[j] || _eliminatedAt[j] > _eliminatedAt[i]) rank++;
                _roundRanks[i] = rank; _scores[i] += 4 - rank;
            }
            Phase = Race2Phase.RoundResult; PhaseEndsAt = Elapsed + Race2Rules.ResultSeconds;
        }
    }
}
