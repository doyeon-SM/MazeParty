using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.TerritoryPaint;
using UnityEngine;

namespace MazeParty.Dev.MinigameSoloTest
{
    public enum TerritoryPaintSoloPhase
    {
        Countdown,
        Running,
        Complete
    }

    /// <summary>
    /// Local deterministic practice simulation used only by the editor solo
    /// launcher. Production networking remains owned by
    /// NetworkTerritoryPaintState.
    /// </summary>
    public sealed class TerritoryPaintSoloSession
    {
        public const int LocalPlayerSlot = 0;
        private const double CountdownSeconds = 3d;
        private const float PlayerRadius = 0.58f;
        private const float MoveSpeed = 5.4f;
        private const float BrushRadius = 1.15f;

        private readonly Vector2[] _positions =
            new Vector2[TerritoryPaintRules.PlayerCount];
        private readonly Vector2[] _facings =
            new Vector2[TerritoryPaintRules.PlayerCount];
        private readonly float[] _aiTurnEndsAt =
            new float[TerritoryPaintRules.PlayerCount];
        private readonly Vector2[] _aiDirections =
            new Vector2[TerritoryPaintRules.PlayerCount];
        private TerritoryPaintSurface _surface;
        private System.Random _random;
        private double _elapsed;
        private IReadOnlyList<TerritoryPaintLeaderboardEntry>
            _leaderboard;

        public int Seed { get; private set; }
        public TerritoryPaintSoloPhase Phase { get; private set; }
        public double Remaining
        {
            get
            {
                switch (Phase)
                {
                    case TerritoryPaintSoloPhase.Countdown:
                        return Math.Max(0d, CountdownSeconds - _elapsed);
                    case TerritoryPaintSoloPhase.Running:
                        return Math.Max(
                            0d,
                            TerritoryPaintRules.RoundSeconds - _elapsed);
                    default:
                        return 0d;
                }
            }
        }
        public uint PaintRevision { get; private set; }
        public IReadOnlyList<TerritoryPaintLeaderboardEntry>
            Leaderboard => _leaderboard;

        public void Begin(int seed)
        {
            Seed = seed;
            _random = new System.Random(seed);
            _surface = new TerritoryPaintSurface(
                TerritoryPaintRules.SurfaceResolution,
                0f,
                9f);
            _elapsed = 0d;
            _leaderboard = null;
            PaintRevision = 0U;
            Phase = TerritoryPaintSoloPhase.Countdown;
            for (var slot = 0;
                 slot < TerritoryPaintRules.PlayerCount;
                 slot++)
            {
                _positions[slot] = GetStartPosition(slot);
                _facings[slot] = -_positions[slot].normalized;
                _aiTurnEndsAt[slot] = 0f;
                _aiDirections[slot] = _facings[slot];
            }
        }

        public void Tick(float deltaSeconds, Vector2 localInput)
        {
            if (_surface == null ||
                float.IsNaN(deltaSeconds) ||
                float.IsInfinity(deltaSeconds) ||
                deltaSeconds <= 0f ||
                Phase == TerritoryPaintSoloPhase.Complete)
            {
                return;
            }

            if (Phase == TerritoryPaintSoloPhase.Countdown)
            {
                _elapsed += deltaSeconds;
                if (_elapsed >= CountdownSeconds)
                {
                    Phase = TerritoryPaintSoloPhase.Running;
                    _elapsed = 0d;
                    PaintStarts();
                }
                return;
            }

            var remaining =
                (float)Math.Max(
                    0d,
                    TerritoryPaintRules.RoundSeconds - _elapsed);
            var simulated = Mathf.Min(deltaSeconds, remaining);
            var input = new Vector2[
                TerritoryPaintRules.PlayerCount];
            input[LocalPlayerSlot] =
                Vector2.ClampMagnitude(localInput, 1f);
            for (var slot = 1;
                 slot < TerritoryPaintRules.PlayerCount;
                 slot++)
            {
                input[slot] = GetPracticeInput(slot);
            }

            var stepRemaining = simulated;
            while (stepRemaining > 0f)
            {
                var step = Mathf.Min(0.05f, stepRemaining);
                SimulateStep(step, input);
                stepRemaining -= step;
            }

            _elapsed += simulated;
            if (_elapsed + 0.000001d >=
                TerritoryPaintRules.RoundSeconds)
            {
                Complete();
            }
        }

        public Vector2 GetPlayerPosition(int slot)
        {
            ValidateSlot(slot);
            return _positions[slot];
        }

        public Vector2 GetPlayerFacing(int slot)
        {
            ValidateSlot(slot);
            return _facings[slot];
        }

        public int GetScore(int slot)
        {
            ValidateSlot(slot);
            return _surface != null
                ? _surface.GetNormalizedScore(slot)
                : 0;
        }

        public byte GetPaintOwner(int x, int y)
        {
            return _surface != null
                ? _surface.GetOwnerAt(x, y)
                : TerritoryPaintRules.UnpaintedOwner;
        }

        private void PaintStarts()
        {
            var changed = 0;
            for (var slot = 0;
                 slot < TerritoryPaintRules.PlayerCount;
                 slot++)
            {
                changed += _surface.PaintStroke(
                    slot,
                    _positions[slot],
                    _positions[slot],
                    BrushRadius);
            }
            if (changed > 0)
            {
                PaintRevision++;
            }
        }

        private void SimulateStep(
            float deltaSeconds,
            Vector2[] inputs)
        {
            var changed = 0;
            for (var slot = 0;
                 slot < TerritoryPaintRules.PlayerCount;
                 slot++)
            {
                var input =
                    Vector2.ClampMagnitude(inputs[slot], 1f);
                if (input.sqrMagnitude <= 0.0001f)
                {
                    continue;
                }

                var origin = _positions[slot];
                var next = ResolveMovement(
                    slot,
                    origin,
                    input * MoveSpeed * deltaSeconds);
                _positions[slot] = next;
                _facings[slot] = input.normalized;
                changed += _surface.PaintStroke(
                    slot,
                    origin,
                    next,
                    BrushRadius);
            }

            if (changed > 0)
            {
                unchecked
                {
                    PaintRevision =
                        PaintRevision == uint.MaxValue
                            ? 1U
                            : PaintRevision + 1U;
                }
            }
        }

        private Vector2 GetPracticeInput(int slot)
        {
            if (_elapsed >= _aiTurnEndsAt[slot])
            {
                _aiTurnEndsAt[slot] =
                    (float)_elapsed +
                    Mathf.Lerp(
                        0.65f,
                        1.45f,
                        (float)_random.NextDouble());
                var angle =
                    (float)_random.NextDouble() *
                    Mathf.PI * 2f;
                var randomDirection =
                    new Vector2(
                        Mathf.Cos(angle),
                        Mathf.Sin(angle));
                var towardCenter =
                    (-_positions[slot]).normalized;
                _aiDirections[slot] =
                    Vector2.Lerp(
                        randomDirection,
                        towardCenter,
                        0.38f).normalized;
            }

            var limit = 9f - PlayerRadius - 0.7f;
            if (Mathf.Abs(_positions[slot].x) > limit)
            {
                _aiDirections[slot].x =
                    -Mathf.Sign(_positions[slot].x) *
                    Mathf.Abs(_aiDirections[slot].x);
            }
            if (Mathf.Abs(_positions[slot].y) > limit)
            {
                _aiDirections[slot].y =
                    -Mathf.Sign(_positions[slot].y) *
                    Mathf.Abs(_aiDirections[slot].y);
            }

            return _aiDirections[slot].normalized;
        }

        private Vector2 ResolveMovement(
            int slot,
            Vector2 origin,
            Vector2 displacement)
        {
            var proposed = Clamp(origin + displacement);
            if (CanOccupy(slot, proposed))
            {
                return proposed;
            }

            var xOnly = Clamp(
                origin + new Vector2(displacement.x, 0f));
            if (CanOccupy(slot, xOnly))
            {
                origin = xOnly;
            }
            var yOnly = Clamp(
                origin + new Vector2(0f, displacement.y));
            return CanOccupy(slot, yOnly) ? yOnly : origin;
        }

        private bool CanOccupy(int slot, Vector2 position)
        {
            var minimum = PlayerRadius * 2f;
            var minimumSquared = minimum * minimum;
            for (var other = 0;
                 other < TerritoryPaintRules.PlayerCount;
                 other++)
            {
                if (other != slot &&
                    (_positions[other] - position).sqrMagnitude <
                    minimumSquared)
                {
                    return false;
                }
            }
            return true;
        }

        private static Vector2 Clamp(Vector2 position)
        {
            var limit = 9f - PlayerRadius;
            position.x = Mathf.Clamp(position.x, -limit, limit);
            position.y = Mathf.Clamp(position.y, -limit, limit);
            return position;
        }

        private static Vector2 GetStartPosition(int slot)
        {
            var right = (slot & 1) != 0;
            var top = (slot & 2) != 0;
            const float inset = 7.57f;
            return new Vector2(
                right ? inset : -inset,
                top ? inset : -inset);
        }

        private void Complete()
        {
            var counts =
                new int[TerritoryPaintRules.PlayerCount];
            for (var slot = 0; slot < counts.Length; slot++)
            {
                counts[slot] =
                    _surface.GetOwnedCellCount(slot);
            }
            _leaderboard =
                TerritoryPaintScoring.BuildLeaderboard(
                    counts,
                    _surface.CellCount);
            Phase = TerritoryPaintSoloPhase.Complete;
            _elapsed = TerritoryPaintRules.RoundSeconds;
        }

        private static void ValidateSlot(int slot)
        {
            if (!TerritoryPaintRules.IsValidPlayerSlot(slot))
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }
    }
}
