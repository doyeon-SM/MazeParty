using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardSpecialEventRulesTests
    {
        [Test]
        public void Resolve_IsDeterministicAndUsesCanonicalRouletteOrder()
        {
            var direct = BoardSpecialEventRules.Resolve(
                12345,
                7,
                2,
                new Vector2Int(4, -3),
                4);
            var repeated = BoardSpecialEventRules.Resolve(
                12345,
                7,
                2,
                new Vector2Int(4, -3),
                4);
            AssertResolution(
                direct,
                BoardSpecialEventFamily.Direct,
                BoardSpecialEventAudience.Everyone,
                BoardSpecialEventOperation.Lose,
                BoardSpecialEventResource.Gold,
                -1,
                30);
            AssertResolutionEquals(direct, repeated);

            var transfer = BoardSpecialEventRules.Resolve(
                12345,
                8,
                2,
                new Vector2Int(4, -3),
                4);
            AssertResolution(
                transfer,
                BoardSpecialEventFamily.Transfer,
                BoardSpecialEventAudience.OneOpponent,
                BoardSpecialEventOperation.OpponentGivesToActor,
                BoardSpecialEventResource.Gold,
                0,
                20);
        }

        [Test]
        public void ResourceRoll_UsesExactThirtyThirtyThirtyTenBoundaries()
        {
            var rolls = new[] { 0, 29, 30, 59, 60, 89, 90, 99 };
            var expectedResources = new[]
            {
                BoardSpecialEventResource.Gold,
                BoardSpecialEventResource.Gold,
                BoardSpecialEventResource.Gold,
                BoardSpecialEventResource.Gold,
                BoardSpecialEventResource.Gold,
                BoardSpecialEventResource.Gold,
                BoardSpecialEventResource.Key,
                BoardSpecialEventResource.Key
            };
            var expectedAmounts = new[] { 30, 30, 20, 20, 10, 10, 1, 1 };

            for (var index = 0; index < rolls.Length; index++)
            {
                var resource = BoardSpecialEventRules.ResolveResourceRoll(
                    rolls[index],
                    out var amount);
                Assert.That(resource, Is.EqualTo(expectedResources[index]));
                Assert.That(amount, Is.EqualTo(expectedAmounts[index]));
            }
        }

        [Test]
        public void TargetMasksAndTransfers_RespectParticipantsAndBalanceLimits()
        {
            var directCases = new[]
            {
                CreateDirect(BoardSpecialEventAudience.Self, -1),
                CreateDirect(BoardSpecialEventAudience.OneOpponent, 3),
                CreateDirect(BoardSpecialEventAudience.Everyone, -1),
                CreateDirect(BoardSpecialEventAudience.EveryoneExceptSelf, -1)
            };
            var expectedMasks = new[] { 0b0010, 0b1000, 0b1111, 0b1101 };
            for (var index = 0; index < directCases.Length; index++)
            {
                Assert.That(
                    BoardSpecialEventRules.GetTargetMask(
                        directCases[index],
                        1,
                        4),
                    Is.EqualTo(expectedMasks[index]));
            }

            var transfer = new BoardSpecialEventRules.Resolution(
                BoardSpecialEventFamily.Transfer,
                BoardSpecialEventAudience.OneOpponent,
                BoardSpecialEventOperation.OpponentStealsFromActor,
                BoardSpecialEventResource.Gold,
                3,
                30);
            Assert.That(
                BoardSpecialEventRules.GetTargetMask(transfer, 1, 4),
                Is.EqualTo(0b1010));

            var transferCases = new[,]
            {
                { 30, 100, 0, 30 },
                { 30, 12, 0, 12 },
                { 30, 100, int.MaxValue - 5, 5 },
                { -1, 100, 0, 0 },
                { 30, -1, 0, 0 }
            };
            for (var index = 0; index < transferCases.GetLength(0); index++)
            {
                Assert.That(
                    BoardSpecialEventRules.GetTransferAmount(
                        transferCases[index, 0],
                        transferCases[index, 1],
                        transferCases[index, 2]),
                    Is.EqualTo(transferCases[index, 3]));
            }
        }

        private static BoardSpecialEventRules.Resolution CreateDirect(
            BoardSpecialEventAudience audience,
            int opponentSlot)
        {
            return new BoardSpecialEventRules.Resolution(
                BoardSpecialEventFamily.Direct,
                audience,
                BoardSpecialEventOperation.Gain,
                BoardSpecialEventResource.Gold,
                opponentSlot,
                10);
        }

        private static void AssertResolution(
            BoardSpecialEventRules.Resolution actual,
            BoardSpecialEventFamily family,
            BoardSpecialEventAudience audience,
            BoardSpecialEventOperation operation,
            BoardSpecialEventResource resource,
            int opponentSlot,
            int amount)
        {
            Assert.That(actual.Family, Is.EqualTo(family));
            Assert.That(actual.Audience, Is.EqualTo(audience));
            Assert.That(actual.Operation, Is.EqualTo(operation));
            Assert.That(actual.Resource, Is.EqualTo(resource));
            Assert.That(actual.OpponentSlot, Is.EqualTo(opponentSlot));
            Assert.That(actual.Amount, Is.EqualTo(amount));
        }

        private static void AssertResolutionEquals(
            BoardSpecialEventRules.Resolution expected,
            BoardSpecialEventRules.Resolution actual)
        {
            AssertResolution(
                actual,
                expected.Family,
                expected.Audience,
                expected.Operation,
                expected.Resource,
                expected.OpponentSlot,
                expected.Amount);
        }
    }
}
