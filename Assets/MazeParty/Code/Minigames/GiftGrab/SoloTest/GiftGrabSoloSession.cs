using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.GiftGrab;
using MazeParty.Multiplayer;
using UnityEngine;

namespace MazeParty.Dev.MinigameSoloTest
{
    public enum GiftGrabSoloPhase : byte
    {
        Countdown = 0,
        Running = 1,
        RoundResult = 2,
        Complete = 3
    }

    public enum GiftGrabSoloActionType : byte
    {
        None = 0,
        Pickup = 1,
        Deposit = 2,
        Throw = 3,
        ThrownHit = 4,
        Push = 5,
        Drop = 6,
        GiftSpawned = 7,
        BoundsReturn = 8
    }

    /// <summary>
    /// Deterministic local authoritative practice match. It exercises the same
    /// arena coordinates, timing, carrying, stealing, throwing, pushing and
    /// scoring contracts without NGO or a room connection.
    /// </summary>
    public sealed class GiftGrabSoloSession
    {
        public const int LocalPlayerSlot = 0;
        public const double CountdownSeconds = 3d;
        public const double ResultSeconds = 4d;

        private const float BaseRadius = NetworkGiftGrabState.BaseRadius;
        private const float PickupRadius =
            NetworkGiftGrabState.PlayerCollisionRadius +
            NetworkGiftGrabState.GiftCollisionRadius;
        private const float PlayerRadius =
            NetworkGiftGrabState.PlayerCollisionRadius;
        private const float ThrowHitRadius =
            NetworkGiftGrabState.PlayerCollisionRadius +
            NetworkGiftGrabState.GiftCollisionRadius;
        private const double FinalInteractionEpsilonSeconds = 0.000001d;

        private readonly GiftGrabSoloPlayer[] _players =
            new GiftGrabSoloPlayer[GiftGrabRules.PlayerCount];
        private readonly GiftGrabSoloGift[] _gifts =
            new GiftGrabSoloGift[GiftGrabRules.TotalGiftCount];
        private readonly GiftGrabRoundResult[] _roundResults =
            new GiftGrabRoundResult[GiftGrabRules.RoundCount];
        private readonly Vector2[] _moves =
            new Vector2[GiftGrabRules.PlayerCount];
        private readonly double[] _botActionAt =
            new double[GiftGrabRules.PlayerCount];

        private System.Random _random;
        private IReadOnlyList<GiftGrabLeaderboardEntry> _leaderboard =
            Array.Empty<GiftGrabLeaderboardEntry>();
        private int _seed;
        private double _phaseElapsed;
        private double _roundElapsed;
        private int _spawnedGiftCount;

        public GiftGrabSoloPhase Phase { get; private set; }
        public int Seed => _seed;
        public int RoundNumber { get; private set; }
        public double RemainingSeconds { get; private set; }
        public int SpawnedGiftCount => _spawnedGiftCount;
        public uint ActionRevision { get; private set; }
        public GiftGrabSoloActionType LastActionType { get; private set; }
        public int LastActionActorSlot { get; private set; } = -1;
        public int LastActionTargetSlot { get; private set; } = -1;
        public int LastActionGiftId { get; private set; } = -1;
        public IReadOnlyList<GiftGrabLeaderboardEntry> Leaderboard =>
            _leaderboard;

        public void Begin(int seed)
        {
            _seed = seed;
            RoundNumber = 1;
            Array.Clear(_roundResults, 0, _roundResults.Length);
            _leaderboard = Array.Empty<GiftGrabLeaderboardEntry>();
            BeginRound();
        }

        public void Tick(float unscaledDeltaTime, Vector2 localMove)
        {
            if (unscaledDeltaTime < 0f ||
                float.IsNaN(unscaledDeltaTime) ||
                float.IsInfinity(unscaledDeltaTime))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(unscaledDeltaTime));
            }

            _phaseElapsed += unscaledDeltaTime;
            switch (Phase)
            {
                case GiftGrabSoloPhase.Countdown:
                    RemainingSeconds = Math.Max(
                        0d,
                        CountdownSeconds - _phaseElapsed);
                    if (_phaseElapsed >= CountdownSeconds)
                    {
                        Phase = GiftGrabSoloPhase.Running;
                        _phaseElapsed = 0d;
                        RemainingSeconds = GiftGrabRules.RoundSeconds;
                    }
                    break;
                case GiftGrabSoloPhase.Running:
                    TickRunning(unscaledDeltaTime, localMove);
                    break;
                case GiftGrabSoloPhase.RoundResult:
                    RemainingSeconds = Math.Max(0d, ResultSeconds - _phaseElapsed);
                    if (_phaseElapsed >= ResultSeconds)
                    {
                        AdvanceAfterResult();
                    }
                    break;
                case GiftGrabSoloPhase.Complete:
                    RemainingSeconds = 0d;
                    break;
            }
        }

        public bool TryLocalAction()
        {
            return TryAction(LocalPlayerSlot);
        }

        public void RestartCurrentRound()
        {
            if (RoundNumber < 1 || RoundNumber > GiftGrabRules.RoundCount)
            {
                RoundNumber = 1;
            }
            BeginRound();
        }

        public GiftGrabSoloPlayer GetPlayer(int slot)
        {
            ValidatePlayerSlot(slot);
            return _players[slot];
        }

        public GiftGrabSoloGift GetGift(int giftId)
        {
            if (!GiftGrabRules.IsValidGiftId(giftId))
            {
                throw new ArgumentOutOfRangeException(nameof(giftId));
            }
            return _gifts[giftId];
        }

        public GiftGrabRoundResult GetRoundResult(int roundNumber)
        {
            if (roundNumber < 1 || roundNumber > GiftGrabRules.RoundCount)
            {
                throw new ArgumentOutOfRangeException(nameof(roundNumber));
            }
            return _roundResults[roundNumber - 1];
        }

        public int CountLooseGifts()
        {
            var count = 0;
            for (var giftId = 0; giftId < _spawnedGiftCount; giftId++)
            {
                var gift = _gifts[giftId];
                if (gift.IsActive && gift.CarrierSlot < 0 &&
                    gift.StoredOwnerSlot < 0 &&
                    gift.Velocity.sqrMagnitude <= 0.01f)
                {
                    count++;
                }
            }
            return count;
        }

        private void BeginRound()
        {
            _random = new System.Random(unchecked(
                _seed * 397 ^ RoundNumber * 7919));
            _phaseElapsed = 0d;
            _roundElapsed = 0d;
            _spawnedGiftCount = 0;
            Phase = GiftGrabSoloPhase.Countdown;
            RemainingSeconds = CountdownSeconds;
            LastActionType = GiftGrabSoloActionType.None;
            LastActionActorSlot = -1;
            LastActionTargetSlot = -1;
            LastActionGiftId = -1;

            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                var start = NetworkGiftGrabState.GetPlayerStartPosition(slot);
                _players[slot] = new GiftGrabSoloPlayer(slot)
                {
                    Position = start,
                    Facing = (GetArenaCenter() - start).normalized,
                    CarriedGiftId = GiftGrabRules.NoGiftId
                };
                _moves[slot] = Vector2.zero;
                _botActionAt[slot] = 0.2d + slot * 0.11d;
            }
            for (var giftId = 0; giftId < GiftGrabRules.TotalGiftCount; giftId++)
            {
                _gifts[giftId] = new GiftGrabSoloGift(giftId);
            }
            SpawnUntil(GiftGrabRules.InitialGiftCount);
        }

        private void TickRunning(float deltaTime, Vector2 localMove)
        {
            var previousElapsed = _roundElapsed;
            var targetElapsed = Math.Min(
                GiftGrabRules.RoundSeconds,
                _roundElapsed + deltaTime);
            var interactionElapsed = Math.Min(
                targetElapsed,
                GiftGrabRules.RoundSeconds -
                FinalInteractionEpsilonSeconds);
            var activeDelta = Math.Max(
                0d,
                interactionElapsed - previousElapsed);
            _roundElapsed = interactionElapsed;
            RemainingSeconds = Math.Max(
                0d,
                GiftGrabRules.RoundSeconds - targetElapsed);

            SpawnUntil(GiftGrabRules.GetScheduledGiftCount(_roundElapsed));
            AccumulateStoredGiftSeconds(activeDelta);
            var simulationDelta = (float)activeDelta;
            TickPlayerTimers(simulationDelta);
            _moves[LocalPlayerSlot] = Vector2.ClampMagnitude(localMove, 1f);
            ThinkBots();
            MovePlayers(simulationDelta);
            MoveThrownGifts(simulationDelta);
            ResolveDepositsAndPickups();
            ResolveBotActions();

            if (targetElapsed >= GiftGrabRules.RoundSeconds)
            {
                AccumulateStoredGiftSeconds(
                    GiftGrabRules.RoundSeconds - _roundElapsed);
                _roundElapsed = GiftGrabRules.RoundSeconds;
                RemainingSeconds = 0d;
                EndRound();
            }
            else
            {
                _roundElapsed = targetElapsed;
            }
        }

        private void SpawnUntil(int targetCount)
        {
            while (_spawnedGiftCount < targetCount &&
                   _spawnedGiftCount < GiftGrabRules.TotalGiftCount)
            {
                var giftId = _spawnedGiftCount++;
                var angle = (giftId * 2.39996323f) +
                            (float)(_random.NextDouble() * 0.24d);
                var radius = 1.2f + (giftId % 5) * 0.72f;
                var center = GetArenaCenter();
                var gift = _gifts[giftId];
                gift.IsActive = true;
                gift.Position = center + new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle)) * radius;
                gift.SpawnPosition = gift.Position;
                gift.Velocity = Vector2.zero;
                gift.CarrierSlot = GiftGrabRules.NoPlayerSlot;
                gift.StoredOwnerSlot = GiftGrabRules.NoPlayerSlot;
                gift.ThrowOwnerSlot = GiftGrabRules.NoPlayerSlot;
                gift.ThrowImmunityUntil = 0d;
                RecordAction(
                    GiftGrabSoloActionType.GiftSpawned,
                    -1,
                    -1,
                    giftId);
            }
        }

        private void AccumulateStoredGiftSeconds(double deltaTime)
        {
            if (deltaTime <= 0d)
            {
                return;
            }
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                _players[slot].CumulativeStoredGiftSeconds +=
                    _players[slot].StoredGiftCount * deltaTime;
            }
        }

        private void TickPlayerTimers(float deltaTime)
        {
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                var player = _players[slot];
                player.StunRemaining = Math.Max(
                    0d,
                    player.StunRemaining - deltaTime);
                player.ActionCooldownRemaining = Math.Max(
                    0d,
                    player.ActionCooldownRemaining - deltaTime);
            }
        }

        private void ThinkBots()
        {
            for (var slot = 1; slot < GiftGrabRules.PlayerCount; slot++)
            {
                var player = _players[slot];
                if (player.StunRemaining > 0d)
                {
                    _moves[slot] = Vector2.zero;
                    continue;
                }

                Vector2 target;
                if (player.CarriedGiftId >= 0)
                {
                    target = NetworkGiftGrabState.GetBaseCenter(slot);
                }
                else if (TryFindNearestLooseGift(player.Position, out var giftId))
                {
                    target = _gifts[giftId].Position;
                }
                else
                {
                    var rival = (slot + 1 + RoundNumber) %
                                GiftGrabRules.PlayerCount;
                    target = NetworkGiftGrabState.GetBaseCenter(rival);
                }

                var direction = target - player.Position;
                _moves[slot] = direction.sqrMagnitude > 0.02f
                    ? direction.normalized
                    : Vector2.zero;
            }
        }

        private void MovePlayers(float deltaTime)
        {
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                var player = _players[slot];
                if (player.StunRemaining > 0d)
                {
                    continue;
                }
                var move = Vector2.ClampMagnitude(_moves[slot], 1f);
                if (move.sqrMagnitude > 0.001f)
                {
                    player.Facing = move.normalized;
                }
                var speed = player.CarriedGiftId >= 0
                    ? GiftGrabRules.CarryMoveSpeed
                    : GiftGrabRules.NormalMoveSpeed;
                player.Position = ResolvePlayerMovement(
                    slot,
                    player.Position,
                    move * speed * deltaTime);
            }
        }

        private void MoveThrownGifts(float deltaTime)
        {
            var frameStartElapsed = _roundElapsed - deltaTime;
            for (var giftId = 0; giftId < _spawnedGiftCount; giftId++)
            {
                var gift = _gifts[giftId];
                if (!gift.IsActive || gift.Velocity.sqrMagnitude < 0.01f ||
                    gift.CarrierSlot >= 0 || gift.StoredOwnerSlot >= 0)
                {
                    continue;
                }

                var start = gift.Position;
                var travelDelta = Mathf.Min(
                    deltaTime,
                    (float)Math.Max(
                        0d,
                        gift.ThrownEndsAt - frameStartElapsed));
                var end = start + gift.Velocity * travelDelta;
                var hitSlot = GiftGrabRules.NoPlayerSlot;
                var earliestFraction = float.MaxValue;
                for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
                {
                    if (!TrySegmentCircleIntersection(
                            start,
                            end,
                            _players[slot].Position,
                            ThrowHitRadius,
                            out var hitFraction))
                    {
                        continue;
                    }
                    var hitElapsed = frameStartElapsed +
                        travelDelta * hitFraction;
                    if (slot == gift.ThrowOwnerSlot &&
                        hitElapsed < gift.ThrowImmunityUntil)
                    {
                        continue;
                    }

                    if (hitFraction < earliestFraction ||
                        (Mathf.Approximately(hitFraction, earliestFraction) &&
                         slot < hitSlot))
                    {
                        earliestFraction = hitFraction;
                        hitSlot = slot;
                    }
                }
                if (hitSlot >= 0)
                {
                    var hitElapsed = frameStartElapsed +
                        travelDelta * earliestFraction;
                    var incomingVelocity = gift.Velocity;
                    gift.Position = Vector2.Lerp(
                        start,
                        end,
                        earliestFraction);
                    gift.Velocity = Vector2.zero;
                    gift.PickupLockedPlayerSlot = gift.ThrowOwnerSlot;
                    gift.PickupLockUntil = hitElapsed +
                        GiftGrabRules.RegrabLockSeconds;
                    gift.ThrownEndsAt = 0d;
                    var droppedGift = ApplyStunAndDrop(
                        hitSlot,
                        GiftGrabRules.ThrowStunSeconds,
                        gift.ThrowOwnerSlot,
                        GiftGrabSoloActionType.ThrownHit,
                        hitElapsed);
                    if (droppedGift >= 0)
                    {
                        var perpendicular = new Vector2(
                            -incomingVelocity.y,
                            incomingVelocity.x);
                        if (perpendicular.sqrMagnitude < 0.0001f)
                        {
                            perpendicular = Vector2.right;
                        }
                        _gifts[droppedGift].Position = ClampToArena(
                            gift.Position + perpendicular.normalized * 0.55f,
                            NetworkGiftGrabState.GiftCollisionRadius);
                    }
                    LastActionGiftId = giftId;
                    continue;
                }
                if (!IsInsideArena(end, 0f))
                {
                    var previousThrower = gift.ThrowOwnerSlot;
                    gift.Position = gift.SpawnPosition;
                    gift.Velocity = Vector2.zero;
                    gift.ThrowOwnerSlot = GiftGrabRules.NoPlayerSlot;
                    gift.ThrowImmunityUntil = 0d;
                    gift.ThrownEndsAt = 0d;
                    gift.PickupLockedPlayerSlot =
                        GiftGrabRules.NoPlayerSlot;
                    gift.PickupLockUntil = 0d;
                    RecordAction(
                        GiftGrabSoloActionType.BoundsReturn,
                        previousThrower,
                        -1,
                        giftId);
                    continue;
                }
                gift.Position = end;
                if (frameStartElapsed + travelDelta + 0.000000001d >=
                    gift.ThrownEndsAt)
                {
                    var landedAt = gift.ThrownEndsAt;
                    gift.Velocity = Vector2.zero;
                    gift.PickupLockedPlayerSlot = gift.ThrowOwnerSlot;
                    gift.PickupLockUntil = landedAt +
                        GiftGrabRules.RegrabLockSeconds;
                    gift.ThrownEndsAt = 0d;
                    RecordAction(
                        GiftGrabSoloActionType.Drop,
                        gift.ThrowOwnerSlot,
                        -1,
                        giftId);
                }
            }
        }

        private void ResolveDepositsAndPickups()
        {
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                var player = _players[slot];
                if (player.StunRemaining > 0d)
                {
                    continue;
                }

                if (player.CarriedGiftId >= 0 &&
                    Vector2.Distance(
                        player.Position,
                        NetworkGiftGrabState.GetBaseCenter(slot)) <= BaseRadius)
                {
                    var depositedGift = player.CarriedGiftId;
                    var gift = _gifts[depositedGift];
                    gift.CarrierSlot = GiftGrabRules.NoPlayerSlot;
                    gift.StoredOwnerSlot = slot;
                    gift.Position = GetStoredGiftPosition(slot, depositedGift);
                    gift.Velocity = Vector2.zero;
                    gift.ThrowOwnerSlot = GiftGrabRules.NoPlayerSlot;
                    gift.PickupLockedPlayerSlot =
                        GiftGrabRules.NoPlayerSlot;
                    player.StoredGiftCount++;
                    player.CarriedGiftId = GiftGrabRules.NoGiftId;
                    RecordAction(
                        GiftGrabSoloActionType.Deposit,
                        slot,
                        slot,
                        depositedGift);
                }

                if (player.CarriedGiftId >= 0)
                {
                    continue;
                }

                var pickupRadiusSquared = PickupRadius * PickupRadius;
                for (var giftId = 0;
                     giftId < _spawnedGiftCount;
                     giftId++)
                {
                    var gift = _gifts[giftId];
                    if (!gift.IsActive || gift.CarrierSlot >= 0 ||
                        gift.Velocity.sqrMagnitude > 0.01f ||
                        gift.StoredOwnerSlot == slot ||
                        (gift.Position - player.Position).sqrMagnitude >
                        pickupRadiusSquared ||
                        (gift.PickupLockedPlayerSlot == slot &&
                         _roundElapsed + 0.000000001d <
                         gift.PickupLockUntil))
                    {
                        continue;
                    }

                    var previousOwner = gift.StoredOwnerSlot;
                    if (previousOwner >= 0)
                    {
                        _players[previousOwner].StoredGiftCount--;
                    }
                    CarryGift(slot, giftId, GiftGrabSoloActionType.Pickup);
                    LastActionTargetSlot = previousOwner;
                    break;
                }
            }
        }

        private void ResolveBotActions()
        {
            for (var slot = 1; slot < GiftGrabRules.PlayerCount; slot++)
            {
                if (_roundElapsed >= _botActionAt[slot])
                {
                    var bot = _players[slot];
                    if (bot.CarriedGiftId < 0 ||
                        HasThrowTarget(slot))
                    {
                        TryAction(slot);
                    }
                    _botActionAt[slot] = _roundElapsed +
                        0.22d + slot * 0.035d;
                }
            }
        }

        private bool TryAction(int slot)
        {
            ValidatePlayerSlot(slot);
            if (Phase != GiftGrabSoloPhase.Running)
            {
                return false;
            }
            var player = _players[slot];
            if (player.StunRemaining > 0d)
            {
                return false;
            }

            if (player.CarriedGiftId >= 0)
            {
                ThrowGift(slot);
                return true;
            }
            if (player.ActionCooldownRemaining > 0d)
            {
                return false;
            }
            return TryPush(slot);
        }

        private void CarryGift(
            int slot,
            int giftId,
            GiftGrabSoloActionType action)
        {
            var player = _players[slot];
            var gift = _gifts[giftId];
            player.CarriedGiftId = giftId;
            gift.CarrierSlot = slot;
            gift.StoredOwnerSlot = GiftGrabRules.NoPlayerSlot;
            gift.Velocity = Vector2.zero;
            gift.ThrowOwnerSlot = GiftGrabRules.NoPlayerSlot;
            gift.PickupLockedPlayerSlot = GiftGrabRules.NoPlayerSlot;
            gift.PickupLockUntil = 0d;
            RecordAction(action, slot, -1, giftId);
        }

        private void ThrowGift(int slot)
        {
            var player = _players[slot];
            var giftId = player.CarriedGiftId;
            var gift = _gifts[giftId];
            var facing = player.Facing.sqrMagnitude > 0.001f
                ? player.Facing.normalized
                : Vector2.up;
            player.CarriedGiftId = GiftGrabRules.NoGiftId;
            gift.CarrierSlot = GiftGrabRules.NoPlayerSlot;
            gift.Position = player.Position + facing *
                (NetworkGiftGrabState.PlayerCollisionRadius +
                 NetworkGiftGrabState.GiftCollisionRadius + 0.1f);
            gift.Velocity = facing * GiftGrabRules.ThrowSpeed;
            gift.ThrowOwnerSlot = slot;
            gift.ThrowImmunityUntil = _roundElapsed +
                                      GiftGrabRules.ThrowSelfHitImmunitySeconds;
            gift.ThrownEndsAt = _roundElapsed +
                                NetworkGiftGrabState.ThrownFlightSeconds;
            gift.PickupLockedPlayerSlot = slot;
            gift.PickupLockUntil = _roundElapsed +
                                   GiftGrabRules.RegrabLockSeconds;
            RecordAction(GiftGrabSoloActionType.Throw, slot, -1, giftId);
        }

        private bool TryPush(int slot)
        {
            var player = _players[slot];
            var selected = -1;
            var bestForward = float.MaxValue;
            for (var target = 0; target < GiftGrabRules.PlayerCount; target++)
            {
                if (target == slot)
                {
                    continue;
                }
                var offset = _players[target].Position - player.Position;
                var facing = player.Facing.sqrMagnitude > 0.0001f
                    ? player.Facing.normalized
                    : Vector2.up;
                var forward = Vector2.Dot(offset, facing);
                var lateral = Mathf.Abs(
                    facing.x * offset.y - facing.y * offset.x);
                if (forward < 0f || forward > GiftGrabRules.PushRange ||
                    lateral > GiftGrabRules.PushRadius + PlayerRadius)
                {
                    continue;
                }
                if (forward < bestForward ||
                    (Mathf.Approximately(forward, bestForward) &&
                     target < selected))
                {
                    bestForward = forward;
                    selected = target;
                }
            }
            if (selected < 0)
            {
                player.ActionCooldownRemaining =
                    GiftGrabRules.PushCooldownSeconds;
                RecordAction(
                    GiftGrabSoloActionType.Push,
                    slot,
                    GiftGrabRules.NoPlayerSlot,
                    GiftGrabRules.NoGiftId);
                return true;
            }

            player.ActionCooldownRemaining = GiftGrabRules.PushCooldownSeconds;
            var direction = player.Facing.sqrMagnitude > 0.0001f
                ? player.Facing.normalized
                : Vector2.up;
            _players[selected].Position = ResolvePlayerMovement(
                selected,
                _players[selected].Position,
                direction * GiftGrabRules.PushKnockbackDistance);
            ApplyStunAndDrop(
                selected,
                GiftGrabRules.PushStunSeconds,
                slot,
                GiftGrabSoloActionType.Push,
                _roundElapsed);
            return true;
        }

        private bool HasThrowTarget(int attacker)
        {
            var player = _players[attacker];
            var facing = player.Facing.sqrMagnitude > 0.0001f
                ? player.Facing.normalized
                : Vector2.up;
            for (var target = 0; target < GiftGrabRules.PlayerCount; target++)
            {
                if (target == attacker)
                {
                    continue;
                }
                var offset = _players[target].Position - player.Position;
                if (offset.sqrMagnitude <= 12.25f &&
                    offset.sqrMagnitude > 0.0001f &&
                    Vector2.Dot(offset.normalized, facing) >= 0.86f)
                {
                    return true;
                }
            }
            return false;
        }

        private int ApplyStunAndDrop(
            int target,
            double seconds,
            int actor,
            GiftGrabSoloActionType action,
            double eventElapsed)
        {
            var player = _players[target];
            var elapsedSinceEvent = Math.Max(
                0d,
                _roundElapsed - eventElapsed);
            player.StunRemaining = Math.Max(
                player.StunRemaining,
                Math.Max(0d, seconds - elapsedSinceEvent));
            var dropped = player.CarriedGiftId;
            if (dropped >= 0)
            {
                var gift = _gifts[dropped];
                gift.CarrierSlot = GiftGrabRules.NoPlayerSlot;
                gift.StoredOwnerSlot = GiftGrabRules.NoPlayerSlot;
                gift.Position = player.Position;
                gift.Velocity = Vector2.zero;
                gift.ThrowOwnerSlot = GiftGrabRules.NoPlayerSlot;
                gift.PickupLockedPlayerSlot = target;
                gift.PickupLockUntil = eventElapsed +
                                       GiftGrabRules.RegrabLockSeconds;
                player.CarriedGiftId = GiftGrabRules.NoGiftId;
            }
            RecordAction(action, actor, target, dropped);
            return dropped;
        }

        private bool TryFindNearestLooseGift(
            Vector2 position,
            out int giftId)
        {
            giftId = -1;
            var best = float.MaxValue;
            for (var index = 0; index < _spawnedGiftCount; index++)
            {
                var gift = _gifts[index];
                if (!gift.IsActive || gift.CarrierSlot >= 0 ||
                    gift.StoredOwnerSlot >= 0 ||
                    gift.Velocity.sqrMagnitude > 0.01f)
                {
                    continue;
                }
                var distance = (gift.Position - position).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    giftId = index;
                }
            }
            return giftId >= 0;
        }

        private void EndRound()
        {
            var outcomes = new GiftGrabRoundOutcome[GiftGrabRules.PlayerCount];
            for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
            {
                outcomes[slot] = GiftGrabRoundOutcome.Create(
                    slot,
                    _players[slot].StoredGiftCount,
                    _players[slot].CumulativeStoredGiftSeconds);
            }
            _roundResults[RoundNumber - 1] =
                GiftGrabRoundScoring.Score(outcomes);
            Phase = GiftGrabSoloPhase.RoundResult;
            _phaseElapsed = 0d;
            RemainingSeconds = ResultSeconds;
            foreach (var player in _players)
            {
                player.StunRemaining = 0d;
                player.ActionCooldownRemaining = 0d;
            }
        }

        private void AdvanceAfterResult()
        {
            if (RoundNumber >= GiftGrabRules.RoundCount)
            {
                _leaderboard = GiftGrabMatchScoring.BuildLeaderboard(
                    _roundResults);
                Phase = GiftGrabSoloPhase.Complete;
                _phaseElapsed = 0d;
                RemainingSeconds = 0d;
                return;
            }
            RoundNumber++;
            BeginRound();
        }

        private void RecordAction(
            GiftGrabSoloActionType type,
            int actor,
            int target,
            int giftId)
        {
            ActionRevision = ActionRevision == uint.MaxValue
                ? 1u
                : ActionRevision + 1u;
            LastActionType = type;
            LastActionActorSlot = actor;
            LastActionTargetSlot = target;
            LastActionGiftId = giftId;
        }

        private static Vector2 GetArenaCenter()
        {
            return new Vector2(NetworkGiftGrabState.ArenaCenterX, 0f);
        }

        private static bool IsInsideArena(Vector2 position, float inset)
        {
            var extent = NetworkGiftGrabState.ArenaHalfExtent - inset;
            return Mathf.Abs(position.x - NetworkGiftGrabState.ArenaCenterX) <=
                   extent && Mathf.Abs(position.y) <= extent;
        }

        private static Vector2 ClampToArena(Vector2 position, float inset)
        {
            var extent = NetworkGiftGrabState.ArenaHalfExtent - inset;
            position.x = Mathf.Clamp(
                position.x,
                NetworkGiftGrabState.ArenaCenterX - extent,
                NetworkGiftGrabState.ArenaCenterX + extent);
            position.y = Mathf.Clamp(position.y, -extent, extent);
            return position;
        }

        private Vector2 ResolvePlayerMovement(
            int slot,
            Vector2 origin,
            Vector2 displacement)
        {
            var proposed = ClampToArena(origin + displacement, PlayerRadius);
            if (CanPlayerOccupy(slot, proposed))
            {
                return proposed;
            }

            var xOnly = ClampToArena(
                origin + new Vector2(displacement.x, 0f),
                PlayerRadius);
            if (CanPlayerOccupy(slot, xOnly))
            {
                origin = xOnly;
            }
            var yOnly = ClampToArena(
                origin + new Vector2(0f, displacement.y),
                PlayerRadius);
            return CanPlayerOccupy(slot, yOnly) ? yOnly : origin;
        }

        private bool CanPlayerOccupy(int slot, Vector2 position)
        {
            var minimum = PlayerRadius * 2f;
            var minimumSquared = minimum * minimum;
            for (var other = 0; other < GiftGrabRules.PlayerCount; other++)
            {
                if (other != slot &&
                    (_players[other].Position - position).sqrMagnitude <
                    minimumSquared)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TrySegmentCircleIntersection(
            Vector2 start,
            Vector2 end,
            Vector2 center,
            float radius,
            out float fraction)
        {
            var segment = end - start;
            var fromCenter = start - center;
            var a = Vector2.Dot(segment, segment);
            var c = Vector2.Dot(fromCenter, fromCenter) - radius * radius;
            if (c <= 0f)
            {
                fraction = 0f;
                return true;
            }
            if (a <= 0.0000001f)
            {
                fraction = 0f;
                return false;
            }
            var b = 2f * Vector2.Dot(fromCenter, segment);
            var discriminant = b * b - 4f * a * c;
            if (discriminant < 0f)
            {
                fraction = 0f;
                return false;
            }
            var root = (-b - Mathf.Sqrt(discriminant)) / (2f * a);
            fraction = root;
            return root >= 0f && root <= 1f;
        }

        private static Vector2 GetStoredGiftPosition(int owner, int giftId)
        {
            var column = giftId % 5;
            var row = (giftId / 5) % 4;
            var offset = new Vector2(
                (column - 2f) * 0.43f,
                (row - 1.5f) * 0.43f);
            return NetworkGiftGrabState.GetBaseCenter(owner) + offset;
        }

        private static void ValidatePlayerSlot(int slot)
        {
            if (!GiftGrabRules.IsValidPlayerSlot(slot))
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }
    }

    public sealed class GiftGrabSoloPlayer
    {
        internal GiftGrabSoloPlayer(int playerSlot)
        {
            PlayerSlot = playerSlot;
        }

        public int PlayerSlot { get; }
        public Vector2 Position { get; internal set; }
        public Vector2 Facing { get; internal set; }
        public int CarriedGiftId { get; internal set; }
        public int StoredGiftCount { get; internal set; }
        public double StunRemaining { get; internal set; }
        public double ActionCooldownRemaining { get; internal set; }
        public double CumulativeStoredGiftSeconds { get; internal set; }
    }

    public sealed class GiftGrabSoloGift
    {
        internal GiftGrabSoloGift(int giftId)
        {
            GiftId = giftId;
            CarrierSlot = GiftGrabRules.NoPlayerSlot;
            StoredOwnerSlot = GiftGrabRules.NoPlayerSlot;
            ThrowOwnerSlot = GiftGrabRules.NoPlayerSlot;
            PickupLockedPlayerSlot = GiftGrabRules.NoPlayerSlot;
        }

        public int GiftId { get; }
        public bool IsActive { get; internal set; }
        public Vector2 Position { get; internal set; }
        public Vector2 SpawnPosition { get; internal set; }
        public Vector2 Velocity { get; internal set; }
        public int CarrierSlot { get; internal set; }
        public int StoredOwnerSlot { get; internal set; }
        internal int ThrowOwnerSlot { get; set; }
        internal double ThrowImmunityUntil { get; set; }
        internal double ThrownEndsAt { get; set; }
        internal int PickupLockedPlayerSlot { get; set; }
        internal double PickupLockUntil { get; set; }
    }
}
