using System;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class PlayerSlotCollectionsTests
    {
        [Test]
        public void PlayerMask4_ComposesMembershipAndNormalizesBoundaryBits()
        {
            var mask = PlayerMask4.FromBits(0b1111_0000)
                .With(0)
                .With(2)
                .Union(PlayerMask4.FromBits(0b0000_1000));

            Assert.That(mask.Bits, Is.EqualTo(0b0000_1101));
            Assert.That(mask.Count, Is.EqualTo(3));
            Assert.That(mask.Contains(0), Is.True);
            Assert.That(mask.Contains(1), Is.False);
            Assert.That(mask.Contains(4), Is.False);
            Assert.That(
                mask.Intersect(PlayerMask4.FromBits(0b0000_0110)).Bits,
                Is.EqualTo(0b0000_0100));
            Assert.That(mask.Without(2).Bits, Is.EqualTo(0b0000_1001));
        }

        [TestCase(-1)]
        [TestCase(PlayerSlotRules.Count)]
        public void PlayerMask4_MutationRejectsInvalidSlots(int slot)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlayerMask4.None.With(slot));
        }

        [Test]
        public void PlayerSlots_RequiresFourValuesAndUpdatesByValue()
        {
            Assert.Throws<ArgumentException>(
                () => PlayerSlots<int>.From(new[] { 1, 2, 3 }));

            var original = PlayerSlots<int>.From(new[] { 10, 20, 30, 40 });
            var updated = original.With(2, 99);

            Assert.That(original.ToArray(), Is.EqualTo(new[] { 10, 20, 30, 40 }));
            Assert.That(updated.ToArray(), Is.EqualTo(new[] { 10, 20, 99, 40 }));
        }
    }
}
