using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public static class BoardBombardmentRules
    {
        public const int TargetCount = 3;
        public const float Radius = 10f;
        public const double WarningSeconds = 1d;
        public const double IntervalSeconds = .5d;

        public static int DamageAtDistance(float distance)
        {
            if (float.IsNaN(distance) || distance < 0f || distance > Radius) return 0;
            return distance <= 3f ? 80 : distance <= 7f ? 50 : 20;
        }

        // Recast on the authority; a client supplies X/Z, never a trusted hit.
        public static bool TryGetGroundPosition(BoardTopology topology, Vector3 requested,
            out Vector3 position)
        {
            position = default;
            if (topology == null || !IsFinite(requested)) return false;
            var tile = topology.FindContainingTile(requested, 0f);
            if (tile == null) return false;
            var origin = new Vector3(requested.x, tile.WorldCenter.y + 32f, requested.z);
            return BoardItemPhysics.Cast(origin, Vector3.down, 34f, null, out var hit,
                       ignorePlayers: true) &&
                   BoardItemLifecycleRules.TryGetMinePlacementPosition(
                       topology, hit.point, hit.normal, out position);
        }

        public static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    /// <summary>A committed salvo is independent of its user's lifetime.</summary>
    public sealed class BoardBombardmentSequence
    {
        private readonly Vector3[] _targets;
        private double _elapsed;
        private double _lastSample;
        public int FiredCount { get; private set; }
        public bool Complete => FiredCount == BoardBombardmentRules.TargetCount;
        public IReadOnlyList<Vector3> Targets => _targets;

        public BoardBombardmentSequence(IReadOnlyList<Vector3> targets, double now)
        {
            if (targets == null || targets.Count != BoardBombardmentRules.TargetCount ||
                double.IsNaN(now) || double.IsInfinity(now))
                throw new ArgumentException("A salvo requires three finite ground positions and a clock.");
            _targets = new Vector3[targets.Count];
            for (var i = 0; i < targets.Count; i++)
            {
                if (!BoardBombardmentRules.IsFinite(targets[i]))
                    throw new ArgumentException("Invalid bombardment target.");
                _targets[i] = targets[i];
            }
            _lastSample = now;
        }

        public void Tick(double now, bool paused)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < _lastSample) return;
            var delta = now - _lastSample;
            _lastSample = now;
            if (!paused) _elapsed += delta;
        }

        public bool TryTakeDue(out Vector3 position)
        {
            position = default;
            if (Complete || _elapsed < BoardBombardmentRules.WarningSeconds +
                FiredCount * BoardBombardmentRules.IntervalSeconds) return false;
            position = _targets[FiredCount++];
            return true;
        }
    }
}
