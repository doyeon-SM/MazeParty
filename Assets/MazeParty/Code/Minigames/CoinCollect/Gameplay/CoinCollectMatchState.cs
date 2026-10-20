using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay.Minigames.CoinCollect
{
    public enum CoinCollectItemKind : byte { Coin, Money, Bomb }

    public static class CoinCollectRules
    {
        public const int PlayerCount = 4;
        public static int RoundCount => MinigameRuntimeSettings.Rounds(ScheduledMinigameId.CoinCollect, 2);
        public static double RoundSeconds => MinigameRuntimeSettings.Seconds(ScheduledMinigameId.CoinCollect, 60d);
        public const int CoinCount = 50, MoneyCount = 10, BombCount = 10;
        public const int RainCount = CoinCount + MoneyCount + BombCount;
        public const float MoveSpeed = 5f, FallSpeed = 6f;
        public const float SpawnHeight = 8f, GroundHeight = 0.55f;
        public const float PickupHeight = 2.1f, PickupRadius = 0.9f;
        public const float ArenaCenterX = 700f, PlayerLimit = 7.4f;
        public const double BombStunSeconds = 1d;
        public const double BombDropPickupLockSeconds = 1d;
        public const float BombDropRadius = 2.3f;
        public const double PushCastSeconds = 0.1d, PushStunSeconds = 0.5d;
        public const double PushCooldownSeconds = 1d;
        public static bool IsValidSlot(int slot) => slot >= 0 && slot < PlayerCount;
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public static Vector2 StartPosition(int slot) => new Vector2(
            ArenaCenterX + ((slot & 1) == 0 ? -5f : 5f), (slot & 2) == 0 ? -5f : 5f);
        public static Vector2 Clamp(Vector2 position, float limit = PlayerLimit) => new Vector2(
            Mathf.Clamp(position.x, ArenaCenterX - limit, ArenaCenterX + limit),
            Mathf.Clamp(position.y, -limit, limit));
        public static float Height(double elapsed, double bornAt, float initialHeight) =>
            Mathf.Max(GroundHeight, initialHeight - (float)Math.Max(0d, elapsed - bornAt) * FallSpeed);
    }

    public readonly struct CoinCollectPlayerSnapshot
    {
        public readonly Vector2 Position, Facing;
        public readonly int Score;
        public readonly double StunRemaining, CastRemaining, CooldownRemaining;
        public CoinCollectPlayerSnapshot(Vector2 position, Vector2 facing, int score,
            double stun, double cast, double cooldown)
        {
            Position = position; Facing = facing; Score = score;
            StunRemaining = stun; CastRemaining = cast; CooldownRemaining = cooldown;
        }
    }

    public readonly struct CoinCollectItemSnapshot
    {
        public readonly CoinCollectItemKind Kind;
        public readonly Vector2 Position;
        public readonly double BornAt, PickupUnlockAt;
        public readonly float InitialHeight;
        public readonly bool Active;
        public CoinCollectItemSnapshot(CoinCollectItemKind kind, Vector2 position,
            double bornAt, float initialHeight, bool active = true, double pickupUnlockAt = 0d)
        {
            Kind = kind; Position = position; BornAt = bornAt;
            InitialHeight = initialHeight; Active = active; PickupUnlockAt = pickupUnlockAt;
        }
        public float HeightAt(double elapsed) => CoinCollectRules.Height(elapsed, BornAt, InitialHeight);
        public bool IsPickupLockedAt(double elapsed) => elapsed < PickupUnlockAt;
        public CoinCollectItemSnapshot Consumed() => new CoinCollectItemSnapshot(
            Kind, Position, BornAt, InitialHeight, false, PickupUnlockAt);
    }

    /// <summary>Fixed-step authority shared by network play and offline practice.</summary>
    public sealed class CoinCollectMatchState
    {
        private sealed class Player
        {
            public Vector2 Position, Facing, Input, PushFacing;
            public int Score;
            public double StunnedUntil, CastAt = -1d, CooldownUntil;
        }
        private readonly Player[] _players = new Player[4];
        private readonly List<CoinCollectItemSnapshot> _items = new List<CoinCollectItemSnapshot>();
        private readonly CoinCollectItemSnapshot[] _rain = new CoinCollectItemSnapshot[CoinCollectRules.RainCount];
        private readonly int[] _tieOrder = { 0, 1, 2, 3 };
        private readonly ulong _seed;
        private int _spawned;
        public int RoundNumber { get; private set; }
        public double Elapsed { get; private set; }
        public bool IsRoundComplete => Elapsed >= CoinCollectRules.RoundSeconds;
        public bool IsComplete => IsRoundComplete && RoundNumber == CoinCollectRules.RoundCount;
        public int ItemCount => _items.Count;
        public int RainSpawnedCount => _spawned;
        public event Action<CoinCollectItemKind, int> ItemCollected;
        public event Action<int> PushStarted;

        public CoinCollectMatchState(ulong seed)
        {
            _seed = seed;
            for (var slot = 0; slot < 4; slot++) _players[slot] = new Player();
            var random = new StableRandom(seed);
            for (var i = 3; i > 0; i--)
            {
                var j = random.Next(i + 1);
                var temp = _tieOrder[i]; _tieOrder[i] = _tieOrder[j]; _tieOrder[j] = temp;
            }
            BeginRound();
        }

        public CoinCollectPlayerSnapshot GetPlayer(int slot)
        {
            ValidateSlot(slot);
            var p = _players[slot];
            return new CoinCollectPlayerSnapshot(p.Position, p.Facing, p.Score,
                Math.Max(0d, p.StunnedUntil - Elapsed),
                Math.Max(0d, p.CastAt - Elapsed), Math.Max(0d, p.CooldownUntil - Elapsed));
        }
        public CoinCollectItemSnapshot GetItem(int index) => _items[index];
        public CoinCollectItemSnapshot GetScheduledItem(int index) => _rain[index];
        public int GetRank(int slot)
        {
            ValidateSlot(slot);
            var rank = 1;
            for (var other = 0; other < 4; other++)
                if (_players[other].Score > _players[slot].Score) rank++;
            return rank;
        }
        public bool BeginNextRound()
        {
            if (!IsRoundComplete || IsComplete) return false;
            BeginRound();
            return true;
        }
        public bool SetMovementInput(int slot, Vector2 input)
        {
            if (!CoinCollectRules.IsValidSlot(slot) || !CoinCollectRules.IsFinite(input.x) ||
                !CoinCollectRules.IsFinite(input.y)) return false;
            _players[slot].Input = CanMove(_players[slot]) ? Vector2.ClampMagnitude(input, 1f) : Vector2.zero;
            return !IsRoundComplete;
        }
        public void InterruptInputs()
        {
            foreach (var p in _players) { p.Input = Vector2.zero; p.CastAt = -1d; }
        }
        public bool TryPush(int slot)
        {
            if (!CoinCollectRules.IsValidSlot(slot)) return false;
            var p = _players[slot];
            if (!CanMove(p) || Elapsed < p.CooldownUntil) return false;
            p.PushFacing = p.Facing;
            p.CastAt = Elapsed + CoinCollectRules.PushCastSeconds;
            p.CooldownUntil = Elapsed + CoinCollectRules.PushCooldownSeconds;
            p.Input = Vector2.zero;
            PushStarted?.Invoke(slot);
            return true;
        }
        public void AdvanceTo(double target)
        {
            if (!CoinCollectRules.IsFinite(target) || target < Elapsed)
                throw new ArgumentOutOfRangeException(nameof(target));
            target = Math.Min(CoinCollectRules.RoundSeconds, target);
            SpawnDue();
            while (Elapsed < target)
            {
                var next = Math.Min(target, Elapsed + 0.02d);
                if (_spawned < _rain.Length && _rain[_spawned].BornAt > Elapsed)
                    next = Math.Min(next, _rain[_spawned].BornAt);
                foreach (var p in _players)
                {
                    if (p.CastAt > Elapsed) next = Math.Min(next, p.CastAt);
                    if (p.StunnedUntil > Elapsed) next = Math.Min(next, p.StunnedUntil);
                }
                var dt = (float)(next - Elapsed);
                foreach (var p in _players)
                    if (CanMove(p) && p.Input.sqrMagnitude > 0f)
                    {
                        p.Facing = p.Input.normalized;
                        p.Position = CoinCollectRules.Clamp(p.Position + p.Input * (CoinCollectRules.MoveSpeed * dt));
                    }
                Elapsed = next;
                SpawnDue();
                if (IsRoundComplete) { InterruptInputs(); break; }
                ResolvePushes();
                // New bomb drops participate on the following simulation step.
                var count = _items.Count;
                for (var i = 0; i < count; i++)
                    for (var turn = 0; turn < 4 && _items[i].Active; turn++)
                        TryCollect(_tieOrder[turn], i);
            }
        }
        public bool TryCollect(int slot, int itemIndex)
        {
            if (!CoinCollectRules.IsValidSlot(slot) || itemIndex < 0 || itemIndex >= _items.Count || IsRoundComplete) return false;
            var p = _players[slot];
            var item = _items[itemIndex];
            if (!item.Active || item.IsPickupLockedAt(Elapsed) || Elapsed < p.StunnedUntil || item.HeightAt(Elapsed) > CoinCollectRules.PickupHeight ||
                (p.Position - item.Position).sqrMagnitude > CoinCollectRules.PickupRadius * CoinCollectRules.PickupRadius) return false;
            _items[itemIndex] = item.Consumed();
            if (item.Kind == CoinCollectItemKind.Bomb)
            {
                var dropped = p.Score / 2;
                p.Score -= dropped;
                Stun(p, CoinCollectRules.BombStunSeconds);
                var pieces = dropped / 5 + dropped % 5;
                for (var i = 0; i < pieces; i++)
                {
                    var angle = (float)(i * Math.PI * 2d / Math.Max(1, pieces));
                    var position = CoinCollectRules.Clamp(p.Position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * CoinCollectRules.BombDropRadius, 7.5f);
                    _items.Add(new CoinCollectItemSnapshot(i < dropped / 5 ? CoinCollectItemKind.Money : CoinCollectItemKind.Coin,
                        position, Elapsed, 2.5f, pickupUnlockAt: Elapsed + CoinCollectRules.BombDropPickupLockSeconds));
                }
            }
            else p.Score += item.Kind == CoinCollectItemKind.Money ? 5 : 1;
            ItemCollected?.Invoke(item.Kind, slot);
            return true;
        }
        private bool CanMove(Player p) => !IsRoundComplete && Elapsed >= p.StunnedUntil && p.CastAt < 0d;
        private void ResolvePushes()
        {
            for (var slot = 0; slot < 4; slot++)
            {
                var actor = _players[slot];
                if (actor.CastAt < 0d || Elapsed < actor.CastAt) continue;
                actor.CastAt = -1d;
                if (Elapsed < actor.StunnedUntil) continue;
                for (var target = 0; target < 4; target++)
                {
                    if (slot == target) continue;
                    var victim = _players[target];
                    var offset = victim.Position - actor.Position;
                    var forward = Vector2.Dot(offset, actor.PushFacing);
                    var lateral = offset - actor.PushFacing * forward;
                    if (forward < 0f || forward > 1.36f || lateral.sqrMagnitude > 0.7f * 0.7f) continue;
                    victim.Position = CoinCollectRules.Clamp(victim.Position + actor.PushFacing * 1.25f);
                    Stun(victim, CoinCollectRules.PushStunSeconds);
                }
            }
        }
        private void Stun(Player player, double seconds)
        {
            player.StunnedUntil = Math.Max(player.StunnedUntil, Elapsed + seconds);
            player.Input = Vector2.zero; player.CastAt = -1d;
        }
        private void SpawnDue()
        {
            while (_spawned < _rain.Length && _rain[_spawned].BornAt <= Elapsed) _items.Add(_rain[_spawned++]);
        }
        private void BeginRound()
        {
            RoundNumber++; Elapsed = 0d; _spawned = 0; _items.Clear();
            for (var slot = 0; slot < 4; slot++)
            {
                var p = _players[slot];
                p.Position = CoinCollectRules.StartPosition(slot);
                p.Facing = (new Vector2(CoinCollectRules.ArenaCenterX, 0f) - p.Position).normalized;
                p.Input = Vector2.zero; p.CastAt = -1d; p.StunnedUntil = p.CooldownUntil = 0d;
            }
            var random = new StableRandom(_seed ^ ((ulong)RoundNumber * 0x9E3779B97F4A7C15UL));
            var kinds = new CoinCollectItemKind[CoinCollectRules.RainCount];
            for (var i = 0; i < kinds.Length; i++)
                kinds[i] = i < 50 ? CoinCollectItemKind.Coin : i < 60 ? CoinCollectItemKind.Money : CoinCollectItemKind.Bomb;
            for (var i = kinds.Length - 1; i > 0; i--)
            {
                var j = random.Next(i + 1); var temp = kinds[i]; kinds[i] = kinds[j]; kinds[j] = temp;
            }
            // Even the last item reaches the floor before the round closes.
            var lastSpawn = CoinCollectRules.RoundSeconds -
                (CoinCollectRules.SpawnHeight - CoinCollectRules.GroundHeight) / CoinCollectRules.FallSpeed - 0.5d;
            for (var i = 0; i < _rain.Length; i++)
                _rain[i] = new CoinCollectItemSnapshot(kinds[i], new Vector2(
                    CoinCollectRules.ArenaCenterX + random.Range(-7f, 7f), random.Range(-7f, 7f)),
                    i * lastSpawn / (_rain.Length - 1), CoinCollectRules.SpawnHeight);
        }
        private static void ValidateSlot(int slot)
        {
            if (!CoinCollectRules.IsValidSlot(slot)) throw new ArgumentOutOfRangeException(nameof(slot));
        }
        private struct StableRandom
        {
            private ulong _state;
            public StableRandom(ulong seed) { _state = seed ^ 0xD1B54A32D192ED03UL; if (_state == 0) _state = 1; }
            private ulong NextValue()
            {
                var v = _state; v ^= v >> 12; v ^= v << 25; v ^= v >> 27; _state = v;
                return v * 2685821657736338717UL;
            }
            public int Next(int count) => (int)(NextValue() % (ulong)count);
            public float Range(float low, float high) => low + (high - low) * (Next(1000001) / 1000000f);
        }
    }
}
