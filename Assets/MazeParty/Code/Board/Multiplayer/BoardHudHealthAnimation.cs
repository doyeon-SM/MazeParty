using System;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>Tracks interrupted health trails and the cumulative one-second pulse.</summary>
    public sealed class BoardHudHealthAnimation
    {
        public const double Duration = 1d;
        private double _startedAt = double.NegativeInfinity;
        private float _from;
        private float _target;
        private int _amount;
        public int AccumulatedAmount => _amount;
        public bool IsActive(double now) => now < _startedAt + Duration;

        public void Reset(float ratio)
        {
            _from = _target = Mathf.Clamp01(ratio);
            _amount = 0;
            _startedAt = double.NegativeInfinity;
        }

        public void Change(int before, int after, int maximum, double now)
        {
            var amount = (int)Math.Min(int.MaxValue, Math.Abs((long)after - before));
            if (amount == 0) return;
            var active = IsActive(now);
            _from = active ? Trail(now) : Mathf.Clamp01(before / (float)Math.Max(1, maximum));
            _amount = (int)Math.Min(int.MaxValue, (active ? (long)_amount : 0L) + amount);
            _target = Mathf.Clamp01(after / (float)Math.Max(1, maximum));
            _startedAt = now;
        }

        public float Trail(double now) => Mathf.Lerp(_from, _target, Progress(now));
        public float Scale(double now)
        {
            var enlargement = Mathf.Lerp(0.05f, 0.3f, Mathf.Clamp01((_amount - 10f) / 40f));
            return 1f + enlargement * (1f - Progress(now));
        }
        private float Progress(double now) => Mathf.Clamp01((float)((now - _startedAt) / Duration));
    }
}
