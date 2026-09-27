using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public enum BoardSpecialEventFamily : byte
    {
        Direct = 0,
        Transfer = 1
    }

    public enum BoardSpecialEventAudience : byte
    {
        Self = 0,
        OneOpponent = 1,
        Everyone = 2,
        EveryoneExceptSelf = 3
    }

    public enum BoardSpecialEventOperation : byte
    {
        Gain = 0,
        Lose = 1,
        OpponentGivesToActor = 2,
        OpponentStealsFromActor = 3
    }

    public enum BoardSpecialEventResource : byte
    {
        Gold = 0,
        Key = 1
    }

    /// <summary>
    /// Pure deterministic rules for a special-event landing. Random choices are
    /// made in the same order as the roulette reveal: target, resource, action.
    /// </summary>
    public static class BoardSpecialEventRules
    {
        public const int ResourceRollExclusiveMaximum = 100;
        public const int Gold30UpperExclusive = 30;
        public const int Gold20UpperExclusive = 60;
        public const int Gold10UpperExclusive = 90;

        public readonly struct Resolution
        {
            public Resolution(
                BoardSpecialEventFamily family,
                BoardSpecialEventAudience audience,
                BoardSpecialEventOperation operation,
                BoardSpecialEventResource resource,
                int opponentSlot,
                int amount)
            {
                Family = family;
                Audience = audience;
                Operation = operation;
                Resource = resource;
                OpponentSlot = opponentSlot;
                Amount = amount;
            }

            public BoardSpecialEventFamily Family { get; }
            public BoardSpecialEventAudience Audience { get; }
            public BoardSpecialEventOperation Operation { get; }
            public BoardSpecialEventResource Resource { get; }
            public int OpponentSlot { get; }
            public int Amount { get; }
        }

        public static Resolution Resolve(
            int seed,
            int eventRevision,
            int actorSlot,
            Vector2Int tileCoordinate,
            int playerCount)
        {
            ValidatePlayerContext(actorSlot, playerCount);

            var random = new StableRandom(DeriveSeed(
                seed,
                eventRevision,
                actorSlot,
                tileCoordinate));
            var family = (BoardSpecialEventFamily)random.NextInt(2);
            BoardSpecialEventAudience audience;
            var opponentSlot = -1;
            if (family == BoardSpecialEventFamily.Direct)
            {
                audience = (BoardSpecialEventAudience)random.NextInt(4);
                if (audience == BoardSpecialEventAudience.OneOpponent)
                {
                    opponentSlot = ResolveOpponentSlot(
                        ref random,
                        actorSlot,
                        playerCount);
                }
            }
            else
            {
                audience = BoardSpecialEventAudience.OneOpponent;
                opponentSlot = ResolveOpponentSlot(
                    ref random,
                    actorSlot,
                    playerCount);
            }

            var resource = ResolveResourceRoll(
                random.NextInt(ResourceRollExclusiveMaximum),
                out var amount);
            BoardSpecialEventOperation operation;
            if (family == BoardSpecialEventFamily.Direct)
            {
                operation = random.NextInt(2) == 0
                    ? BoardSpecialEventOperation.Gain
                    : BoardSpecialEventOperation.Lose;
            }
            else
            {
                operation = random.NextInt(2) == 0
                    ? BoardSpecialEventOperation.OpponentGivesToActor
                    : BoardSpecialEventOperation.OpponentStealsFromActor;
            }

            return new Resolution(
                family,
                audience,
                operation,
                resource,
                opponentSlot,
                amount);
        }

        /// <summary>
        /// Maps the canonical 0..99 weighted roll to 30/20/10 gold or one key.
        /// </summary>
        public static BoardSpecialEventResource ResolveResourceRoll(
            int roll,
            out int amount)
        {
            if (roll < 0 || roll >= ResourceRollExclusiveMaximum)
            {
                throw new ArgumentOutOfRangeException(nameof(roll));
            }

            if (roll < Gold30UpperExclusive)
            {
                amount = 30;
                return BoardSpecialEventResource.Gold;
            }

            if (roll < Gold20UpperExclusive)
            {
                amount = 20;
                return BoardSpecialEventResource.Gold;
            }

            if (roll < Gold10UpperExclusive)
            {
                amount = 10;
                return BoardSpecialEventResource.Gold;
            }

            amount = 1;
            return BoardSpecialEventResource.Key;
        }

        /// <summary>
        /// Returns directly targeted players. Transfer events include both the
        /// actor and opponent because both balances are affected.
        /// </summary>
        public static int GetTargetMask(
            Resolution resolution,
            int actorSlot,
            int playerCount)
        {
            ValidatePlayerContext(actorSlot, playerCount);
            var allPlayersMask = (1 << playerCount) - 1;
            if (resolution.Family == BoardSpecialEventFamily.Transfer)
            {
                ValidateOpponentSlot(
                    resolution.OpponentSlot,
                    actorSlot,
                    playerCount);
                return (1 << actorSlot) | (1 << resolution.OpponentSlot);
            }

            switch (resolution.Audience)
            {
                case BoardSpecialEventAudience.Self:
                    return 1 << actorSlot;
                case BoardSpecialEventAudience.OneOpponent:
                    ValidateOpponentSlot(
                        resolution.OpponentSlot,
                        actorSlot,
                        playerCount);
                    return 1 << resolution.OpponentSlot;
                case BoardSpecialEventAudience.Everyone:
                    return allPlayersMask;
                case BoardSpecialEventAudience.EveryoneExceptSelf:
                    return allPlayersMask & ~(1 << actorSlot);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(resolution),
                        resolution.Audience,
                        "Unknown special-event audience.");
            }
        }

        /// <summary>
        /// Clamps a transfer to the source balance and destination integer room.
        /// </summary>
        public static int GetTransferAmount(
            int requested,
            int sourceBalance,
            int destinationBalance)
        {
            var safeRequested = Math.Max(0, requested);
            var safeSource = Math.Max(0, sourceBalance);
            var safeDestination = Math.Max(0, destinationBalance);
            var destinationRoom = int.MaxValue - safeDestination;
            return Math.Min(safeRequested, Math.Min(safeSource, destinationRoom));
        }

        private static int ResolveOpponentSlot(
            ref StableRandom random,
            int actorSlot,
            int playerCount)
        {
            var opponentSlot = random.NextInt(playerCount - 1);
            return opponentSlot >= actorSlot ? opponentSlot + 1 : opponentSlot;
        }

        private static void ValidatePlayerContext(int actorSlot, int playerCount)
        {
            if (playerCount < 2 || playerCount > 30)
            {
                throw new ArgumentOutOfRangeException(nameof(playerCount));
            }

            if (actorSlot < 0 || actorSlot >= playerCount)
            {
                throw new ArgumentOutOfRangeException(nameof(actorSlot));
            }
        }

        private static void ValidateOpponentSlot(
            int opponentSlot,
            int actorSlot,
            int playerCount)
        {
            if (opponentSlot < 0 ||
                opponentSlot >= playerCount ||
                opponentSlot == actorSlot)
            {
                throw new ArgumentOutOfRangeException(nameof(opponentSlot));
            }
        }

        private static ulong DeriveSeed(
            int seed,
            int eventRevision,
            int actorSlot,
            Vector2Int tileCoordinate)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var value = offset;
            value = unchecked((value ^ (uint)seed) * prime);
            value = unchecked((value ^ (uint)eventRevision) * prime);
            value = unchecked((value ^ (uint)actorSlot) * prime);
            value = unchecked((value ^ (uint)tileCoordinate.x) * prime);
            value = unchecked((value ^ (uint)tileCoordinate.y) * prime);
            return value;
        }

        private struct StableRandom
        {
            private ulong _state;

            public StableRandom(ulong seed)
            {
                _state = seed;
            }

            public int NextInt(int exclusiveMaximum)
            {
                if (exclusiveMaximum <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(exclusiveMaximum));
                }

                var range = (ulong)exclusiveMaximum;
                var threshold = unchecked(0UL - range) % range;
                ulong value;
                do
                {
                    value = NextUInt64();
                }
                while (value < threshold);

                return (int)(value % range);
            }

            private ulong NextUInt64()
            {
                _state = unchecked(_state + 0x9E3779B97F4A7C15UL);
                var value = _state;
                value = unchecked(
                    (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
                value = unchecked(
                    (value ^ (value >> 27)) * 0x94D049BB133111EBUL);
                return value ^ (value >> 31);
            }
        }
    }
}
