using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.TagChase;
using MazeParty.Multiplayer;
using UnityEngine;

namespace MazeParty.Dev.MinigameSoloTest
{
    public enum TagChaseSoloPhase
    {
        Countdown,
        Running,
        RoundResult,
        Complete
    }

    /// <summary>
    /// Deterministic local practice simulation for the four-round Tag Chase
    /// rules. The production match remains server-authoritative.
    /// </summary>
    public sealed class TagChaseSoloSession
    {
        public const int LocalPlayerSlot = 0;

        private readonly Vector2[] _positions =
            new Vector2[TagChaseRules.PlayerCount];
        private readonly Vector2[] _facings =
            new Vector2[TagChaseRules.PlayerCount];
        private readonly int[] _totalScores =
            new int[TagChaseRules.PlayerCount];
        private readonly Vector2[] _aiWanders =
            new Vector2[TagChaseRules.PlayerCount];
        private readonly float[] _aiWanderChangesAt =
            new float[TagChaseRules.PlayerCount];

        private System.Random _random;
        private int[] _taggerOrder;
        private double _elapsed;
        private double _nextAiAttackAt;
        private byte _caughtMask;
        private IReadOnlyList<TagChaseLeaderboardEntry> _leaderboard;

        public int Seed { get; private set; }
        public int RoundNumber { get; private set; }
        public int TaggerSlot { get; private set; }
        public TagChaseSoloPhase Phase { get; private set; }
        public IReadOnlyList<TagChaseLeaderboardEntry> Leaderboard =>
            _leaderboard;

        public double Remaining
        {
            get
            {
                switch (Phase)
                {
                    case TagChaseSoloPhase.Countdown:
                        return Math.Max(
                            0d,
                            NetworkTagChaseState.CountdownSeconds -
                            _elapsed);
                    case TagChaseSoloPhase.Running:
                        return Math.Max(
                            0d,
                            TagChaseRules.RoundSeconds - _elapsed);
                    case TagChaseSoloPhase.RoundResult:
                        return Math.Max(
                            0d,
                            NetworkTagChaseState.RoundResultSeconds -
                            _elapsed);
                    default:
                        return 0d;
                }
            }
        }

        public void Begin(int seed)
        {
            Seed = seed;
            _random = new System.Random(seed);
            _taggerOrder = TagChaseRules.BuildTaggerOrder(
                unchecked((ulong)(uint)seed));
            Array.Clear(_totalScores, 0, _totalScores.Length);
            _leaderboard = null;
            RoundNumber = 1;
            BeginRound();
        }

        public void Tick(
            double deltaSeconds,
            Vector2 localMove,
            float localViewYaw,
            bool localAttack)
        {
            if (deltaSeconds <= 0d || Phase == TagChaseSoloPhase.Complete)
            {
                return;
            }

            _elapsed += deltaSeconds;
            if (Phase == TagChaseSoloPhase.Countdown)
            {
                if (_elapsed >= NetworkTagChaseState.CountdownSeconds)
                {
                    _elapsed = 0d;
                    Phase = TagChaseSoloPhase.Running;
                }
                return;
            }

            if (Phase == TagChaseSoloPhase.RoundResult)
            {
                if (_elapsed < NetworkTagChaseState.RoundResultSeconds)
                {
                    return;
                }

                if (RoundNumber >= TagChaseRules.RoundCount)
                {
                    _leaderboard =
                        TagChaseRules.BuildFinalLeaderboard(_totalScores);
                    Phase = TagChaseSoloPhase.Complete;
                    _elapsed = 0d;
                    return;
                }

                RoundNumber++;
                BeginRound();
                return;
            }

            Simulate(
                Mathf.Min((float)deltaSeconds, 0.05f),
                Vector2.ClampMagnitude(localMove, 1f),
                localViewYaw);

            if (localAttack && TaggerSlot == LocalPlayerSlot)
            {
                TryCatch(TaggerSlot);
            }
            if (TaggerSlot != LocalPlayerSlot &&
                _elapsed >= _nextAiAttackAt)
            {
                _nextAiAttackAt = _elapsed + 0.35d;
                TryCatch(TaggerSlot);
            }

            if (TagChaseRules.AreAllRunnersCaught(
                    TaggerSlot,
                    _caughtMask) ||
                _elapsed >= TagChaseRules.RoundSeconds)
            {
                CompleteRound();
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

        public int GetTotalScore(int slot)
        {
            ValidateSlot(slot);
            return _totalScores[slot];
        }

        public bool IsTagger(int slot)
        {
            return TagChaseRules.IsValidPlayerSlot(slot) &&
                   slot == TaggerSlot;
        }

        public bool IsCaught(int slot)
        {
            return TagChaseRules.IsValidPlayerSlot(slot) &&
                   slot != TaggerSlot &&
                   (_caughtMask & (1 << slot)) != 0;
        }

        private void BeginRound()
        {
            TaggerSlot = _taggerOrder[RoundNumber - 1];
            _caughtMask = 0;
            _elapsed = 0d;
            _nextAiAttackAt = 0d;
            Phase = TagChaseSoloPhase.Countdown;
            for (var slot = 0; slot < TagChaseRules.PlayerCount; slot++)
            {
                _positions[slot] =
                    NetworkTagChaseState.GetRoundStartPosition(
                        TaggerSlot,
                        slot);
                var towardCenter =
                    new Vector2(NetworkTagChaseState.ArenaCenterX, 0f) -
                    _positions[slot];
                _facings[slot] =
                    towardCenter.sqrMagnitude > 0.0001f
                        ? towardCenter.normalized
                        : Vector2.up;
                _aiWanders[slot] = NextDirection();
                _aiWanderChangesAt[slot] =
                    0.7f + (float)_random.NextDouble() * 1.4f;
            }
        }

        private void Simulate(
            float deltaSeconds,
            Vector2 localMove,
            float localViewYaw)
        {
            for (var slot = 0; slot < TagChaseRules.PlayerCount; slot++)
            {
                if (IsCaught(slot))
                {
                    continue;
                }

                Vector2 input;
                if (slot == LocalPlayerSlot)
                {
                    input = localMove;
                    if (slot == TaggerSlot)
                    {
                        var radians = localViewYaw * Mathf.Deg2Rad;
                        _facings[slot] = new Vector2(
                            Mathf.Sin(radians),
                            Mathf.Cos(radians));
                    }
                    else if (input.sqrMagnitude > 0.0001f)
                    {
                        _facings[slot] = input.normalized;
                    }
                }
                else
                {
                    input = BuildAiInput(slot);
                    if (input.sqrMagnitude > 0.0001f)
                    {
                        _facings[slot] = input.normalized;
                    }
                }

                var speed = slot == TaggerSlot
                    ? NetworkTagChaseState.TaggerMoveSpeed
                    : NetworkTagChaseState.RunnerMoveSpeed;
                MovePlayer(
                    slot,
                    Vector2.ClampMagnitude(input, 1f) *
                    (speed * deltaSeconds));
            }
        }

        private Vector2 BuildAiInput(int slot)
        {
            if (slot == TaggerSlot)
            {
                var target = FindClosestRunner(slot);
                return target >= 0
                    ? (_positions[target] - _positions[slot]).normalized
                    : Vector2.zero;
            }

            if (_elapsed >= _aiWanderChangesAt[slot])
            {
                _aiWanders[slot] = NextDirection();
                _aiWanderChangesAt[slot] =
                    (float)_elapsed + 0.7f +
                    (float)_random.NextDouble() * 1.4f;
            }

            var away = _positions[slot] - _positions[TaggerSlot];
            var dangerWeight = away.sqrMagnitude < 36f ? 1.5f : 0.45f;
            return (away.normalized * dangerWeight +
                    _aiWanders[slot] * 0.6f).normalized;
        }

        private void MovePlayer(int slot, Vector2 displacement)
        {
            var origin = _positions[slot];
            var xOnly = ClampToArena(
                origin + new Vector2(displacement.x, 0f));
            if (CanOccupy(slot, xOnly))
            {
                origin = xOnly;
            }

            var yOnly = ClampToArena(
                origin + new Vector2(0f, displacement.y));
            if (CanOccupy(slot, yOnly))
            {
                origin = yOnly;
            }
            _positions[slot] = origin;
        }

        private bool CanOccupy(int slot, Vector2 candidate)
        {
            for (var index = 0;
                 index < NetworkTagChaseState.ObstacleCount;
                 index++)
            {
                var rect = NetworkTagChaseState.GetObstacleRect(index);
                var radius = NetworkTagChaseState.PlayerCollisionRadius;
                if (candidate.x >= rect.xMin - radius &&
                    candidate.x <= rect.xMax + radius &&
                    candidate.y >= rect.yMin - radius &&
                    candidate.y <= rect.yMax + radius)
                {
                    return false;
                }
            }

            var minimum = NetworkTagChaseState.PlayerCollisionRadius * 2f;
            var minimumSquared = minimum * minimum;
            for (var other = 0;
                 other < TagChaseRules.PlayerCount;
                 other++)
            {
                if (other != slot && !IsCaught(other) &&
                    (_positions[other] - candidate).sqrMagnitude <
                    minimumSquared)
                {
                    return false;
                }
            }
            return true;
        }

        private bool TryCatch(int taggerSlot)
        {
            var target = FindClosestRunner(taggerSlot);
            if (target < 0)
            {
                return false;
            }

            var offset = _positions[target] - _positions[taggerSlot];
            if (offset.sqrMagnitude >
                NetworkTagChaseState.CatchRange *
                NetworkTagChaseState.CatchRange ||
                Vector2.Dot(
                    _facings[taggerSlot],
                    offset.normalized) <
                NetworkTagChaseState.CatchFacingCosine)
            {
                return false;
            }

            _caughtMask = (byte)(_caughtMask | (1 << target));
            Debug.Log(
                "[Minigame Solo Test] Tagger P" +
                (taggerSlot + 1) + " caught P" +
                (target + 1) + ".");
            return true;
        }

        private int FindClosestRunner(int taggerSlot)
        {
            var bestSlot = -1;
            var bestDistance = float.MaxValue;
            for (var slot = 0; slot < TagChaseRules.PlayerCount; slot++)
            {
                if (slot == taggerSlot || IsCaught(slot))
                {
                    continue;
                }

                var distance =
                    (_positions[slot] - _positions[taggerSlot])
                    .sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestSlot = slot;
                }
            }
            return bestSlot;
        }

        private void CompleteRound()
        {
            var points =
                TagChaseRules.BuildRoundPoints(
                    TaggerSlot,
                    _caughtMask);
            for (var slot = 0; slot < TagChaseRules.PlayerCount; slot++)
            {
                _totalScores[slot] += points[slot];
            }
            Phase = TagChaseSoloPhase.RoundResult;
            _elapsed = 0d;
        }

        private static Vector2 ClampToArena(Vector2 position)
        {
            var radius = NetworkTagChaseState.PlayerCollisionRadius;
            position.x = Mathf.Clamp(
                position.x,
                NetworkTagChaseState.ArenaCenterX -
                NetworkTagChaseState.ArenaHalfWidth + radius,
                NetworkTagChaseState.ArenaCenterX +
                NetworkTagChaseState.ArenaHalfWidth - radius);
            position.y = Mathf.Clamp(
                position.y,
                -NetworkTagChaseState.ArenaHalfDepth + radius,
                NetworkTagChaseState.ArenaHalfDepth - radius);
            return position;
        }

        private Vector2 NextDirection()
        {
            var angle = (float)_random.NextDouble() * Mathf.PI * 2f;
            return new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
        }

        private static void ValidateSlot(int slot)
        {
            if (!TagChaseRules.IsValidPlayerSlot(slot))
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }
    }
}
