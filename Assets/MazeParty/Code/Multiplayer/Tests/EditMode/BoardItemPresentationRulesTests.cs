using MazeParty.Gameplay;
using NUnit.Framework;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardItemPresentationRulesTests
    {
        [TestCase(0b0000_0000, ItemChoiceResolution.DoNotUse)]
        [TestCase(0b0000_0001, ItemChoiceResolution.Pending)]
        [TestCase(0b0000_0010, ItemChoiceResolution.Pending)]
        [TestCase(0b0000_0100, ItemChoiceResolution.Pending)]
        [TestCase(0b1111_1000, ItemChoiceResolution.DoNotUse)]
        [TestCase(0b1111_1111, ItemChoiceResolution.Pending)]
        public void InitialItemChoice_SkipsOnlyWhenNoValidInventorySlotIsOccupied(
            int occupiedItemMask,
            ItemChoiceResolution expected)
        {
            Assert.That(
                NetworkPlayerAvatar.GetInitialItemChoiceResolution(
                    (byte)occupiedItemMask),
                Is.EqualTo(expected));
        }

        [TestCase(true, 100, false, false, 0d, true)]
        [TestCase(false, 100, false, false, 0d, false)]
        [TestCase(true, 0, false, false, 0d, false)]
        [TestCase(true, 100, true, false, 0d, false)]
        [TestCase(true, 100, false, true, 0d, false)]
        [TestCase(true, 100, false, false, 0.001d, false)]
        public void FirearmTargetHighlight_RequiresImmediatelyDamageablePlayer(
            bool hitPlayer,
            int targetHealth,
            bool isCloaked,
            bool openingProtected,
            double personalProtectionRemaining,
            bool expected)
        {
            Assert.That(
                BoardItemPresentationRules.ShouldHighlightFirearmTarget(
                    hitPlayer,
                    targetHealth,
                    isCloaked,
                    openingProtected,
                    personalProtectionRemaining),
                Is.EqualTo(expected));
        }

        [TestCase(true, true, 100, ItemChoiceResolution.ItemSelected,
            PrototypeItemId.Grenade, 1, false, true)]
        [TestCase(false, true, 100, ItemChoiceResolution.ItemSelected,
            PrototypeItemId.Grenade, 1, false, false)]
        [TestCase(true, false, 100, ItemChoiceResolution.ItemSelected,
            PrototypeItemId.Grenade, 1, false, false)]
        [TestCase(true, true, 0, ItemChoiceResolution.ItemSelected,
            PrototypeItemId.Grenade, 1, false, false)]
        [TestCase(true, true, 100, ItemChoiceResolution.Pending,
            PrototypeItemId.Grenade, 1, false, false)]
        [TestCase(true, true, 100, ItemChoiceResolution.ItemSelected,
            PrototypeItemId.Pistol, 1, false, false)]
        [TestCase(true, true, 100, ItemChoiceResolution.ItemSelected,
            PrototypeItemId.Grenade, 0, false, false)]
        [TestCase(true, true, 100, ItemChoiceResolution.ItemSelected,
            PrototypeItemId.Grenade, 1, true, false)]
        public void GrenadeRange_RequiresUsableOwnerSelection(
            bool isOwner,
            bool canAcceptActionInput,
            int currentHealth,
            ItemChoiceResolution choice,
            PrototypeItemId equippedItem,
            int charges,
            bool localUsePending,
            bool expected)
        {
            Assert.That(
                BoardItemPresentationRules.ShouldShowGrenadeRange(
                    isOwner,
                    canAcceptActionInput,
                    currentHealth,
                    choice,
                    equippedItem,
                    charges,
                    localUsePending),
                Is.EqualTo(expected));
        }

        [Test]
        public void GrenadeRangeUseState_HoldsAcceptedUseUntilSelectionEnds()
        {
            var state = new GrenadeRangeUseState();

            Assert.That(state.TryBegin(out var acceptedRequest), Is.True);
            Assert.That(acceptedRequest, Is.Not.Zero);
            Assert.That(state.IsPending, Is.True);
            Assert.That(state.TryBegin(out _), Is.False,
                "A hidden pending grenade must not submit a second request.");

            state.Resolve(acceptedRequest + 1u, false);
            Assert.That(state.IsPending, Is.True,
                "An unrelated response must not change the active request.");
            state.Resolve(acceptedRequest, true);
            Assert.That(state.IsPending, Is.True,
                "An accepted use stays hidden until replicated selection state changes.");

            state.ClearWhenUnavailable(false);
            Assert.That(state.IsPending, Is.False);

            Assert.That(state.TryBegin(out var rejectedRequest), Is.True);
            Assert.That(rejectedRequest, Is.Not.EqualTo(acceptedRequest));
            state.Resolve(rejectedRequest, false);
            Assert.That(state.IsPending, Is.False,
                "A rejected use restores the selectable grenade preview.");
        }
    }
}
