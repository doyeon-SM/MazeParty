using System;

namespace MazeParty.Gameplay.Minigames.GiftGrab
{
    public enum GiftGrabGiftState : byte
    {
        Unspawned,
        Loose,
        Carried,
        Stored,
        Thrown
    }

    public enum GiftGrabRoundEndReason : byte
    {
        None,
        TimeLimit
    }

    public enum GiftGrabPickupStatus : byte
    {
        PickedUp,
        IgnoredRoundComplete,
        IgnoredStunned,
        IgnoredAlreadyCarrying,
        IgnoredUnavailable,
        IgnoredOwnStoredGift,
        IgnoredRegrabLock
    }

    public enum GiftGrabDepositStatus : byte
    {
        Deposited,
        IgnoredRoundComplete,
        IgnoredStunned,
        IgnoredNotCarrying
    }

    public enum GiftGrabThrowStatus : byte
    {
        Thrown,
        IgnoredRoundComplete,
        IgnoredStunned,
        IgnoredNotCarrying
    }

    public enum GiftGrabGiftLandingStatus : byte
    {
        Landed,
        IgnoredRoundComplete,
        IgnoredNotThrown
    }

    public enum GiftGrabThrownHitStatus : byte
    {
        Hit,
        IgnoredRoundComplete,
        IgnoredNotThrown,
        IgnoredThrowerImmunity
    }

    public enum GiftGrabPushStatus : byte
    {
        Hit,
        Missed,
        IgnoredRoundComplete,
        IgnoredStunned,
        IgnoredCarryingGift,
        IgnoredCooldown
    }

    public enum GiftGrabOutOfBoundsStatus : byte
    {
        Returned,
        IgnoredRoundComplete,
        IgnoredUnspawned
    }

    public readonly struct GiftGrabPickupResolution
    {
        internal GiftGrabPickupResolution(
            int playerSlot,
            int giftId,
            GiftGrabPickupStatus status,
            int previousStoredOwnerSlot)
        {
            PlayerSlot = playerSlot;
            GiftId = giftId;
            Status = status;
            PreviousStoredOwnerSlot = previousStoredOwnerSlot;
        }

        public int PlayerSlot { get; }
        public int GiftId { get; }
        public GiftGrabPickupStatus Status { get; }
        public int PreviousStoredOwnerSlot { get; }
        public bool WasPickedUp => Status == GiftGrabPickupStatus.PickedUp;
    }

    public readonly struct GiftGrabDepositResolution
    {
        internal GiftGrabDepositResolution(
            int playerSlot,
            int giftId,
            GiftGrabDepositStatus status)
        {
            PlayerSlot = playerSlot;
            GiftId = giftId;
            Status = status;
        }

        public int PlayerSlot { get; }
        public int GiftId { get; }
        public GiftGrabDepositStatus Status { get; }
        public bool WasDeposited => Status == GiftGrabDepositStatus.Deposited;
    }

    public readonly struct GiftGrabThrowResolution
    {
        internal GiftGrabThrowResolution(
            int playerSlot,
            int giftId,
            GiftGrabThrowStatus status,
            double throwerImmunityEndsAtSeconds)
        {
            PlayerSlot = playerSlot;
            GiftId = giftId;
            Status = status;
            ThrowerImmunityEndsAtSeconds =
                throwerImmunityEndsAtSeconds;
        }

        public int PlayerSlot { get; }
        public int GiftId { get; }
        public GiftGrabThrowStatus Status { get; }
        public double ThrowerImmunityEndsAtSeconds { get; }
        public bool WasThrown => Status == GiftGrabThrowStatus.Thrown;
    }

    public readonly struct GiftGrabGiftLandingResolution
    {
        internal GiftGrabGiftLandingResolution(
            int giftId,
            GiftGrabGiftLandingStatus status,
            int pickupLockedPlayerSlot,
            double pickupLockEndsAtSeconds)
        {
            GiftId = giftId;
            Status = status;
            PickupLockedPlayerSlot = pickupLockedPlayerSlot;
            PickupLockEndsAtSeconds = pickupLockEndsAtSeconds;
        }

        public int GiftId { get; }
        public GiftGrabGiftLandingStatus Status { get; }
        public int PickupLockedPlayerSlot { get; }
        public double PickupLockEndsAtSeconds { get; }
        public bool WasLanded => Status == GiftGrabGiftLandingStatus.Landed;
    }

    public readonly struct GiftGrabThrownHitResolution
    {
        internal GiftGrabThrownHitResolution(
            int giftId,
            int victimSlot,
            GiftGrabThrownHitStatus status,
            int droppedGiftId,
            double stunnedUntilSeconds,
            int pickupLockedPlayerSlot,
            double pickupLockEndsAtSeconds)
        {
            GiftId = giftId;
            VictimSlot = victimSlot;
            Status = status;
            DroppedGiftId = droppedGiftId;
            StunnedUntilSeconds = stunnedUntilSeconds;
            PickupLockedPlayerSlot = pickupLockedPlayerSlot;
            PickupLockEndsAtSeconds = pickupLockEndsAtSeconds;
        }

        public int GiftId { get; }
        public int VictimSlot { get; }
        public GiftGrabThrownHitStatus Status { get; }
        public int DroppedGiftId { get; }
        public double StunnedUntilSeconds { get; }
        public int PickupLockedPlayerSlot { get; }
        public double PickupLockEndsAtSeconds { get; }
        public bool WasHit => Status == GiftGrabThrownHitStatus.Hit;
    }

    public readonly struct GiftGrabPushResolution
    {
        internal GiftGrabPushResolution(
            int attackerSlot,
            int victimSlot,
            GiftGrabPushStatus status,
            int droppedGiftId,
            double cooldownEndsAtSeconds,
            double stunnedUntilSeconds)
        {
            AttackerSlot = attackerSlot;
            VictimSlot = victimSlot;
            Status = status;
            DroppedGiftId = droppedGiftId;
            CooldownEndsAtSeconds = cooldownEndsAtSeconds;
            StunnedUntilSeconds = stunnedUntilSeconds;
        }

        public int AttackerSlot { get; }
        public int VictimSlot { get; }
        public GiftGrabPushStatus Status { get; }
        public int DroppedGiftId { get; }
        public double CooldownEndsAtSeconds { get; }
        public double StunnedUntilSeconds { get; }
        public bool WasStarted =>
            Status == GiftGrabPushStatus.Hit ||
            Status == GiftGrabPushStatus.Missed;
        public bool WasHit => Status == GiftGrabPushStatus.Hit;
    }

    public readonly struct GiftGrabOutOfBoundsResolution
    {
        internal GiftGrabOutOfBoundsResolution(
            int giftId,
            GiftGrabOutOfBoundsStatus status,
            int previousCarrierSlot,
            int previousStoredOwnerSlot)
        {
            GiftId = giftId;
            Status = status;
            PreviousCarrierSlot = previousCarrierSlot;
            PreviousStoredOwnerSlot = previousStoredOwnerSlot;
        }

        public int GiftId { get; }
        public GiftGrabOutOfBoundsStatus Status { get; }
        public int PreviousCarrierSlot { get; }
        public int PreviousStoredOwnerSlot { get; }
        public bool WasReturned =>
            Status == GiftGrabOutOfBoundsStatus.Returned;
    }

    public sealed class GiftGrabPlayerRoundState
    {
        private const double TimeEpsilon = 0.000000001d;
        private double _observedAtSeconds;

        internal GiftGrabPlayerRoundState(int playerSlot)
        {
            PlayerSlot = playerSlot;
            HeldGiftId = GiftGrabRules.NoGiftId;
        }

        public int PlayerSlot { get; }
        public int HeldGiftId { get; private set; }
        public int StoredGiftCount { get; private set; }
        public double CumulativeStoredGiftSeconds { get; private set; }
        public double StunnedUntilSeconds { get; private set; }
        public double PushCooldownEndsAtSeconds { get; private set; }
        public bool IsCarryingGift => HeldGiftId != GiftGrabRules.NoGiftId;
        public bool IsStunned =>
            _observedAtSeconds + TimeEpsilon < StunnedUntilSeconds;
        public bool CanMove => !IsStunned;
        public bool CanAct => !IsStunned;
        public bool IsPushReady =>
            !IsStunned &&
            !IsCarryingGift &&
            _observedAtSeconds + TimeEpsilon >=
            PushCooldownEndsAtSeconds;
        public float CurrentMoveSpeed => IsStunned
            ? 0f
            : IsCarryingGift
                ? GiftGrabRules.CarryMoveSpeed
                : GiftGrabRules.NormalMoveSpeed;

        internal void ObserveAt(double activeElapsedSeconds)
        {
            _observedAtSeconds = activeElapsedSeconds;
        }

        internal void AddStoredGiftSeconds(double durationSeconds)
        {
            CumulativeStoredGiftSeconds +=
                StoredGiftCount * durationSeconds;
        }

        internal void TakeGift(int giftId)
        {
            HeldGiftId = giftId;
        }

        internal int ReleaseHeldGift()
        {
            var giftId = HeldGiftId;
            HeldGiftId = GiftGrabRules.NoGiftId;
            return giftId;
        }

        internal void AddStoredGift()
        {
            StoredGiftCount++;
        }

        internal void RemoveStoredGift()
        {
            if (StoredGiftCount <= 0)
            {
                throw new InvalidOperationException(
                    "Cannot remove a gift from an empty base.");
            }

            StoredGiftCount--;
        }

        internal void ApplyStun(double stunnedUntilSeconds)
        {
            StunnedUntilSeconds = Math.Max(
                StunnedUntilSeconds,
                stunnedUntilSeconds);
        }

        internal void BeginPush(double cooldownEndsAtSeconds)
        {
            PushCooldownEndsAtSeconds = cooldownEndsAtSeconds;
        }

        internal GiftGrabRoundOutcome CaptureOutcome()
        {
            return GiftGrabRoundOutcome.Create(
                PlayerSlot,
                StoredGiftCount,
                CumulativeStoredGiftSeconds);
        }
    }

    public sealed class GiftGrabGiftRoundState
    {
        internal GiftGrabGiftRoundState(int giftId, bool isInitiallySpawned)
        {
            GiftId = giftId;
            State = isInitiallySpawned
                ? GiftGrabGiftState.Loose
                : GiftGrabGiftState.Unspawned;
            CarrierSlot = GiftGrabRules.NoPlayerSlot;
            StoredOwnerSlot = GiftGrabRules.NoPlayerSlot;
            LastThrowerSlot = GiftGrabRules.NoPlayerSlot;
            PickupLockedPlayerSlot = GiftGrabRules.NoPlayerSlot;
        }

        public int GiftId { get; }
        public GiftGrabGiftState State { get; private set; }
        public int CarrierSlot { get; private set; }
        public int StoredOwnerSlot { get; private set; }
        public int LastThrowerSlot { get; private set; }
        public double ThrowerImmunityEndsAtSeconds { get; private set; }
        public int PickupLockedPlayerSlot { get; private set; }
        public double PickupLockEndsAtSeconds { get; private set; }

        internal void Spawn()
        {
            ResetToNeutralLoose();
        }

        internal void Carry(int playerSlot)
        {
            State = GiftGrabGiftState.Carried;
            CarrierSlot = playerSlot;
            StoredOwnerSlot = GiftGrabRules.NoPlayerSlot;
            LastThrowerSlot = GiftGrabRules.NoPlayerSlot;
            ThrowerImmunityEndsAtSeconds = 0d;
            PickupLockedPlayerSlot = GiftGrabRules.NoPlayerSlot;
            PickupLockEndsAtSeconds = 0d;
        }

        internal void Store(int ownerSlot)
        {
            State = GiftGrabGiftState.Stored;
            CarrierSlot = GiftGrabRules.NoPlayerSlot;
            StoredOwnerSlot = ownerSlot;
            LastThrowerSlot = GiftGrabRules.NoPlayerSlot;
            ThrowerImmunityEndsAtSeconds = 0d;
            PickupLockedPlayerSlot = GiftGrabRules.NoPlayerSlot;
            PickupLockEndsAtSeconds = 0d;
        }

        internal void Throw(int throwerSlot, double activeElapsedSeconds)
        {
            State = GiftGrabGiftState.Thrown;
            CarrierSlot = GiftGrabRules.NoPlayerSlot;
            StoredOwnerSlot = GiftGrabRules.NoPlayerSlot;
            LastThrowerSlot = throwerSlot;
            ThrowerImmunityEndsAtSeconds = activeElapsedSeconds +
                GiftGrabRules.ThrowSelfHitImmunitySeconds;
            PickupLockedPlayerSlot = GiftGrabRules.NoPlayerSlot;
            PickupLockEndsAtSeconds = 0d;
        }

        internal void LandFromThrow(double activeElapsedSeconds)
        {
            var throwerSlot = LastThrowerSlot;
            SetLooseWithRegrabLock(
                throwerSlot,
                activeElapsedSeconds);
        }

        internal void SetLooseWithRegrabLock(
            int playerSlot,
            double activeElapsedSeconds)
        {
            State = GiftGrabGiftState.Loose;
            CarrierSlot = GiftGrabRules.NoPlayerSlot;
            StoredOwnerSlot = GiftGrabRules.NoPlayerSlot;
            PickupLockedPlayerSlot = playerSlot;
            PickupLockEndsAtSeconds = activeElapsedSeconds +
                GiftGrabRules.RegrabLockSeconds;
            ThrowerImmunityEndsAtSeconds = 0d;
        }

        internal void ResetToNeutralLoose()
        {
            State = GiftGrabGiftState.Loose;
            CarrierSlot = GiftGrabRules.NoPlayerSlot;
            StoredOwnerSlot = GiftGrabRules.NoPlayerSlot;
            LastThrowerSlot = GiftGrabRules.NoPlayerSlot;
            ThrowerImmunityEndsAtSeconds = 0d;
            PickupLockedPlayerSlot = GiftGrabRules.NoPlayerSlot;
            PickupLockEndsAtSeconds = 0d;
        }
    }

    /// <summary>
    /// Pure authoritative gift ownership, action timing and scoring state for
    /// one round. Physics supplies collision results; this class accepts only
    /// monotonically increasing active-round timestamps.
    /// </summary>
    public sealed class GiftGrabRoundState
    {
        private const double TimeEpsilon = 0.000000001d;

        private readonly GiftGrabPlayerRoundState[] _players;
        private readonly GiftGrabGiftRoundState[] _gifts;

        public GiftGrabRoundState(int roundNumber)
        {
            GiftGrabRules.ValidateRoundNumber(roundNumber);
            RoundNumber = roundNumber;

            _players = new GiftGrabPlayerRoundState[
                GiftGrabRules.PlayerCount];
            for (var slot = 0; slot < _players.Length; slot++)
            {
                _players[slot] = new GiftGrabPlayerRoundState(slot);
            }

            _gifts = new GiftGrabGiftRoundState[
                GiftGrabRules.TotalGiftCount];
            for (var giftId = 0; giftId < _gifts.Length; giftId++)
            {
                _gifts[giftId] = new GiftGrabGiftRoundState(
                    giftId,
                    giftId < GiftGrabRules.InitialGiftCount);
            }

            SpawnedGiftCount = GiftGrabRules.InitialGiftCount;
        }

        public int RoundNumber { get; }
        public double ElapsedSeconds { get; private set; }
        public int SpawnedGiftCount { get; private set; }
        public bool IsComplete { get; private set; }
        public GiftGrabRoundEndReason EndReason { get; private set; }
        public GiftGrabRoundResult Result { get; private set; }

        public GiftGrabPlayerRoundState GetPlayer(int playerSlot)
        {
            if (!GiftGrabRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playerSlot),
                    playerSlot,
                    "Player slot must be between 0 and 3.");
            }

            return _players[playerSlot];
        }

        public GiftGrabGiftRoundState GetGift(int giftId)
        {
            if (!GiftGrabRules.IsValidGiftId(giftId))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(giftId),
                    giftId,
                    "Gift id must be between 0 and 18.");
            }

            return _gifts[giftId];
        }

        public void AdvanceTo(double activeElapsedSeconds)
        {
            GiftGrabRules.ValidateActiveElapsedSeconds(
                activeElapsedSeconds);
            if (activeElapsedSeconds + TimeEpsilon < ElapsedSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(activeElapsedSeconds),
                    activeElapsedSeconds,
                    "Active elapsed time cannot move backwards.");
            }

            if (IsComplete)
            {
                return;
            }

            var targetSeconds = Math.Min(
                activeElapsedSeconds,
                GiftGrabRules.RoundSeconds);
            if (targetSeconds < ElapsedSeconds)
            {
                targetSeconds = ElapsedSeconds;
            }

            var durationSeconds = targetSeconds - ElapsedSeconds;
            for (var slot = 0; slot < _players.Length; slot++)
            {
                _players[slot].AddStoredGiftSeconds(durationSeconds);
                _players[slot].ObserveAt(targetSeconds);
            }

            var targetGiftCount = GiftGrabRules.GetScheduledGiftCount(
                targetSeconds);
            for (var giftId = SpawnedGiftCount;
                 giftId < targetGiftCount;
                 giftId++)
            {
                _gifts[giftId].Spawn();
            }

            SpawnedGiftCount = targetGiftCount;
            ElapsedSeconds = targetSeconds;
            if (ElapsedSeconds >= GiftGrabRules.RoundSeconds)
            {
                Complete(GiftGrabRoundEndReason.TimeLimit);
            }
        }

        /// <summary>
        /// Synchronizes state when the server pauses. Because all deadlines and
        /// spawns use active elapsed time, keeping this clock frozen naturally
        /// prevents the pause from consuming stun, cooldown or spawn time.
        /// </summary>
        public void InterruptForPause(double activeElapsedSeconds)
        {
            AdvanceTo(activeElapsedSeconds);
        }

        public bool TryEndForTimeout(double activeElapsedSeconds)
        {
            var wasComplete = IsComplete;
            AdvanceTo(activeElapsedSeconds);
            return !wasComplete &&
                IsComplete &&
                EndReason == GiftGrabRoundEndReason.TimeLimit;
        }

        public GiftGrabPickupResolution ResolvePickup(
            int playerSlot,
            int giftId,
            double activeElapsedSeconds)
        {
            var player = GetPlayer(playerSlot);
            var gift = GetGift(giftId);
            AdvanceTo(activeElapsedSeconds);
            if (IsComplete)
            {
                return PickupResolution(
                    playerSlot,
                    giftId,
                    GiftGrabPickupStatus.IgnoredRoundComplete);
            }

            if (player.IsStunned)
            {
                return PickupResolution(
                    playerSlot,
                    giftId,
                    GiftGrabPickupStatus.IgnoredStunned);
            }

            if (player.IsCarryingGift)
            {
                return PickupResolution(
                    playerSlot,
                    giftId,
                    GiftGrabPickupStatus.IgnoredAlreadyCarrying);
            }

            if (gift.State != GiftGrabGiftState.Loose &&
                gift.State != GiftGrabGiftState.Stored)
            {
                return PickupResolution(
                    playerSlot,
                    giftId,
                    GiftGrabPickupStatus.IgnoredUnavailable);
            }

            if (gift.State == GiftGrabGiftState.Stored &&
                gift.StoredOwnerSlot == playerSlot)
            {
                return PickupResolution(
                    playerSlot,
                    giftId,
                    GiftGrabPickupStatus.IgnoredOwnStoredGift);
            }

            if (gift.PickupLockedPlayerSlot == playerSlot &&
                ElapsedSeconds + TimeEpsilon <
                gift.PickupLockEndsAtSeconds)
            {
                return PickupResolution(
                    playerSlot,
                    giftId,
                    GiftGrabPickupStatus.IgnoredRegrabLock);
            }

            var previousOwnerSlot = gift.StoredOwnerSlot;
            if (previousOwnerSlot != GiftGrabRules.NoPlayerSlot)
            {
                _players[previousOwnerSlot].RemoveStoredGift();
            }

            gift.Carry(playerSlot);
            player.TakeGift(giftId);
            return new GiftGrabPickupResolution(
                playerSlot,
                giftId,
                GiftGrabPickupStatus.PickedUp,
                previousOwnerSlot);
        }

        public GiftGrabDepositResolution ResolveDeposit(
            int playerSlot,
            double activeElapsedSeconds)
        {
            var player = GetPlayer(playerSlot);
            AdvanceTo(activeElapsedSeconds);
            if (IsComplete)
            {
                return DepositResolution(
                    playerSlot,
                    GiftGrabDepositStatus.IgnoredRoundComplete);
            }

            if (player.IsStunned)
            {
                return DepositResolution(
                    playerSlot,
                    GiftGrabDepositStatus.IgnoredStunned);
            }

            if (!player.IsCarryingGift)
            {
                return DepositResolution(
                    playerSlot,
                    GiftGrabDepositStatus.IgnoredNotCarrying);
            }

            var giftId = player.ReleaseHeldGift();
            _gifts[giftId].Store(playerSlot);
            player.AddStoredGift();
            return new GiftGrabDepositResolution(
                playerSlot,
                giftId,
                GiftGrabDepositStatus.Deposited);
        }

        public GiftGrabThrowResolution ResolveThrow(
            int playerSlot,
            double activeElapsedSeconds)
        {
            var player = GetPlayer(playerSlot);
            AdvanceTo(activeElapsedSeconds);
            if (IsComplete)
            {
                return ThrowResolution(
                    playerSlot,
                    GiftGrabThrowStatus.IgnoredRoundComplete);
            }

            if (player.IsStunned)
            {
                return ThrowResolution(
                    playerSlot,
                    GiftGrabThrowStatus.IgnoredStunned);
            }

            if (!player.IsCarryingGift)
            {
                return ThrowResolution(
                    playerSlot,
                    GiftGrabThrowStatus.IgnoredNotCarrying);
            }

            var giftId = player.ReleaseHeldGift();
            var gift = _gifts[giftId];
            gift.Throw(playerSlot, ElapsedSeconds);
            return new GiftGrabThrowResolution(
                playerSlot,
                giftId,
                GiftGrabThrowStatus.Thrown,
                gift.ThrowerImmunityEndsAtSeconds);
        }

        public GiftGrabGiftLandingResolution ResolveGiftLanded(
            int giftId,
            double activeElapsedSeconds)
        {
            var gift = GetGift(giftId);
            AdvanceTo(activeElapsedSeconds);
            if (IsComplete)
            {
                return LandingResolution(
                    gift,
                    GiftGrabGiftLandingStatus.IgnoredRoundComplete);
            }

            if (gift.State != GiftGrabGiftState.Thrown)
            {
                return LandingResolution(
                    gift,
                    GiftGrabGiftLandingStatus.IgnoredNotThrown);
            }

            gift.LandFromThrow(ElapsedSeconds);
            return LandingResolution(
                gift,
                GiftGrabGiftLandingStatus.Landed);
        }

        public GiftGrabThrownHitResolution ResolveThrownHit(
            int giftId,
            int victimSlot,
            double activeElapsedSeconds)
        {
            var gift = GetGift(giftId);
            var victim = GetPlayer(victimSlot);
            AdvanceTo(activeElapsedSeconds);
            if (IsComplete)
            {
                return ThrownHitResolution(
                    gift,
                    victim,
                    GiftGrabThrownHitStatus.IgnoredRoundComplete,
                    GiftGrabRules.NoGiftId);
            }

            if (gift.State != GiftGrabGiftState.Thrown)
            {
                return ThrownHitResolution(
                    gift,
                    victim,
                    GiftGrabThrownHitStatus.IgnoredNotThrown,
                    GiftGrabRules.NoGiftId);
            }

            if (gift.LastThrowerSlot == victimSlot &&
                ElapsedSeconds + TimeEpsilon <
                gift.ThrowerImmunityEndsAtSeconds)
            {
                return ThrownHitResolution(
                    gift,
                    victim,
                    GiftGrabThrownHitStatus.IgnoredThrowerImmunity,
                    GiftGrabRules.NoGiftId);
            }

            gift.LandFromThrow(ElapsedSeconds);
            var droppedGiftId = DropHeldGift(victim, ElapsedSeconds);
            victim.ApplyStun(
                ElapsedSeconds + GiftGrabRules.ThrowStunSeconds);
            return ThrownHitResolution(
                gift,
                victim,
                GiftGrabThrownHitStatus.Hit,
                droppedGiftId);
        }

        /// <summary>
        /// Resolves one empty-handed primary action. Pass NoPlayerSlot when the
        /// server's short forward cast misses; a valid attempt always consumes
        /// the 0.65-second push cooldown.
        /// </summary>
        public GiftGrabPushResolution ResolvePush(
            int attackerSlot,
            int victimSlot,
            double activeElapsedSeconds)
        {
            var attacker = GetPlayer(attackerSlot);
            if (victimSlot != GiftGrabRules.NoPlayerSlot &&
                !GiftGrabRules.IsValidPlayerSlot(victimSlot))
            {
                throw new ArgumentOutOfRangeException(nameof(victimSlot));
            }

            AdvanceTo(activeElapsedSeconds);
            if (IsComplete)
            {
                return PushResolution(
                    attacker,
                    victimSlot,
                    GiftGrabPushStatus.IgnoredRoundComplete,
                    GiftGrabRules.NoGiftId,
                    0d);
            }

            if (attacker.IsStunned)
            {
                return PushResolution(
                    attacker,
                    victimSlot,
                    GiftGrabPushStatus.IgnoredStunned,
                    GiftGrabRules.NoGiftId,
                    0d);
            }

            if (attacker.IsCarryingGift)
            {
                return PushResolution(
                    attacker,
                    victimSlot,
                    GiftGrabPushStatus.IgnoredCarryingGift,
                    GiftGrabRules.NoGiftId,
                    0d);
            }

            if (ElapsedSeconds + TimeEpsilon <
                attacker.PushCooldownEndsAtSeconds)
            {
                return PushResolution(
                    attacker,
                    victimSlot,
                    GiftGrabPushStatus.IgnoredCooldown,
                    GiftGrabRules.NoGiftId,
                    0d);
            }

            attacker.BeginPush(
                ElapsedSeconds + GiftGrabRules.PushCooldownSeconds);
            if (victimSlot == GiftGrabRules.NoPlayerSlot ||
                victimSlot == attackerSlot)
            {
                return PushResolution(
                    attacker,
                    victimSlot,
                    GiftGrabPushStatus.Missed,
                    GiftGrabRules.NoGiftId,
                    0d);
            }

            var victim = _players[victimSlot];
            var droppedGiftId = DropHeldGift(victim, ElapsedSeconds);
            victim.ApplyStun(
                ElapsedSeconds + GiftGrabRules.PushStunSeconds);
            return PushResolution(
                attacker,
                victimSlot,
                GiftGrabPushStatus.Hit,
                droppedGiftId,
                victim.StunnedUntilSeconds);
        }

        public GiftGrabOutOfBoundsResolution ResolveOutOfBounds(
            int giftId,
            double activeElapsedSeconds)
        {
            var gift = GetGift(giftId);
            AdvanceTo(activeElapsedSeconds);
            if (IsComplete)
            {
                return OutOfBoundsResolution(
                    gift,
                    GiftGrabOutOfBoundsStatus.IgnoredRoundComplete,
                    GiftGrabRules.NoPlayerSlot,
                    GiftGrabRules.NoPlayerSlot);
            }

            if (gift.State == GiftGrabGiftState.Unspawned)
            {
                return OutOfBoundsResolution(
                    gift,
                    GiftGrabOutOfBoundsStatus.IgnoredUnspawned,
                    GiftGrabRules.NoPlayerSlot,
                    GiftGrabRules.NoPlayerSlot);
            }

            var previousCarrierSlot = gift.CarrierSlot;
            var previousStoredOwnerSlot = gift.StoredOwnerSlot;
            if (previousCarrierSlot != GiftGrabRules.NoPlayerSlot)
            {
                _players[previousCarrierSlot].ReleaseHeldGift();
            }

            if (previousStoredOwnerSlot != GiftGrabRules.NoPlayerSlot)
            {
                _players[previousStoredOwnerSlot].RemoveStoredGift();
            }

            gift.ResetToNeutralLoose();
            return OutOfBoundsResolution(
                gift,
                GiftGrabOutOfBoundsStatus.Returned,
                previousCarrierSlot,
                previousStoredOwnerSlot);
        }

        private static GiftGrabPickupResolution PickupResolution(
            int playerSlot,
            int giftId,
            GiftGrabPickupStatus status)
        {
            return new GiftGrabPickupResolution(
                playerSlot,
                giftId,
                status,
                GiftGrabRules.NoPlayerSlot);
        }

        private GiftGrabDepositResolution DepositResolution(
            int playerSlot,
            GiftGrabDepositStatus status)
        {
            return new GiftGrabDepositResolution(
                playerSlot,
                _players[playerSlot].HeldGiftId,
                status);
        }

        private GiftGrabThrowResolution ThrowResolution(
            int playerSlot,
            GiftGrabThrowStatus status)
        {
            return new GiftGrabThrowResolution(
                playerSlot,
                _players[playerSlot].HeldGiftId,
                status,
                0d);
        }

        private static GiftGrabGiftLandingResolution LandingResolution(
            GiftGrabGiftRoundState gift,
            GiftGrabGiftLandingStatus status)
        {
            return new GiftGrabGiftLandingResolution(
                gift.GiftId,
                status,
                gift.PickupLockedPlayerSlot,
                gift.PickupLockEndsAtSeconds);
        }

        private static GiftGrabThrownHitResolution ThrownHitResolution(
            GiftGrabGiftRoundState gift,
            GiftGrabPlayerRoundState victim,
            GiftGrabThrownHitStatus status,
            int droppedGiftId)
        {
            return new GiftGrabThrownHitResolution(
                gift.GiftId,
                victim.PlayerSlot,
                status,
                droppedGiftId,
                victim.StunnedUntilSeconds,
                gift.PickupLockedPlayerSlot,
                gift.PickupLockEndsAtSeconds);
        }

        private static GiftGrabPushResolution PushResolution(
            GiftGrabPlayerRoundState attacker,
            int victimSlot,
            GiftGrabPushStatus status,
            int droppedGiftId,
            double stunnedUntilSeconds)
        {
            return new GiftGrabPushResolution(
                attacker.PlayerSlot,
                victimSlot,
                status,
                droppedGiftId,
                attacker.PushCooldownEndsAtSeconds,
                stunnedUntilSeconds);
        }

        private static GiftGrabOutOfBoundsResolution
            OutOfBoundsResolution(
                GiftGrabGiftRoundState gift,
                GiftGrabOutOfBoundsStatus status,
                int previousCarrierSlot,
                int previousStoredOwnerSlot)
        {
            return new GiftGrabOutOfBoundsResolution(
                gift.GiftId,
                status,
                previousCarrierSlot,
                previousStoredOwnerSlot);
        }

        private int DropHeldGift(
            GiftGrabPlayerRoundState player,
            double activeElapsedSeconds)
        {
            if (!player.IsCarryingGift)
            {
                return GiftGrabRules.NoGiftId;
            }

            var giftId = player.ReleaseHeldGift();
            _gifts[giftId].SetLooseWithRegrabLock(
                player.PlayerSlot,
                activeElapsedSeconds);
            return giftId;
        }

        private void Complete(GiftGrabRoundEndReason reason)
        {
            var outcomes = new GiftGrabRoundOutcome[
                GiftGrabRules.PlayerCount];
            for (var slot = 0; slot < _players.Length; slot++)
            {
                outcomes[slot] = _players[slot].CaptureOutcome();
            }

            Result = GiftGrabRoundScoring.Score(outcomes);
            EndReason = reason;
            IsComplete = true;
        }
    }
}
