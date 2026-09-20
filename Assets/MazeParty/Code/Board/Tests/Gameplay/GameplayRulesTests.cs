using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class GameplayRulesTests
    {
        [Test]
        public void PhaseClock_UsesSharedTimestampAndExactProtectionChoiceActionBoundaries()
        {
            var clock = new GameplayPhaseClock();
            clock.Start(10d);

            Assert.That(clock.GetOpeningProtectionRemaining(10d), Is.EqualTo(5d));
            Assert.That(clock.GetChoiceRemaining(10d), Is.EqualTo(30d));
            Assert.That(clock.GetActionRemaining(10d), Is.EqualTo(180d));
            Assert.That(clock.IsOpeningProtectionActive(14.999d), Is.True);
            Assert.That(clock.IsOpeningProtectionActive(15d), Is.False);
            Assert.That(clock.TrySelectItem(0, 40d), Is.False);
            Assert.That(clock.ChoiceResolution, Is.EqualTo(ItemChoiceResolution.TimedOut));
            Assert.That(clock.IsActionExpired(189.999d), Is.False);
            Assert.That(clock.IsActionExpired(190d), Is.True);

            var firstPlayer = new GameplayPhaseClock();
            var secondPlayer = new GameplayPhaseClock();
            firstPlayer.Start(50d);
            secondPlayer.Start(50d);
            Assert.That(firstPlayer.TrySelectItem(1, 52d), Is.True);
            Assert.That(firstPlayer.ChoiceResolution, Is.EqualTo(ItemChoiceResolution.ItemSelected));
            Assert.That(secondPlayer.ChoiceResolution, Is.EqualTo(ItemChoiceResolution.Pending));
            Assert.That(firstPlayer.GetActionRemaining(60d), Is.EqualTo(secondPlayer.GetActionRemaining(60d)));
        }

        [Test]
        public void FullInventory_RejectsRewardWithoutDiscardingExistingItems()
        {
            var inventory = new GameplayInventory();
            inventory.TryAdd(new GameplayItemDefinition("gun", "Gun", "Ammo item", 7));
            inventory.TryAdd(new GameplayItemDefinition("mine", "Mine", "Single item"));
            inventory.TryAdd(new GameplayItemDefinition("kit", "Kit", "Single item"));

            var accepted = inventory.TryAdd(new GameplayItemDefinition("reward", "Reward", "Bonus"));

            Assert.That(accepted, Is.False);
            Assert.That(inventory.IsFull, Is.True);
            Assert.That(
                new[]
                {
                    inventory.Slots[0].Definition.Id,
                    inventory.Slots[1].Definition.Id,
                    inventory.Slots[2].Definition.Id
                },
                Is.EqualTo(new[] { "gun", "mine", "kit" }));
        }

        [Test]
        public void ItemUse_ConsumesChargesAndClearsSelectionAtTerminalUseOrActionEnd()
        {
            var inventory = new GameplayInventory();
            inventory.TryAdd(new GameplayItemDefinition("gun", "Gun", "Ammo item", 2));
            inventory.TrySelect(0);

            Assert.That(inventory.TryConsumeSelectedCharge(), Is.True);
            Assert.That(inventory.SelectedSlot.Charges, Is.EqualTo(1));
            Assert.That(inventory.SelectedSlotIndex, Is.EqualTo(0));
            Assert.That(inventory.TryConsumeSelectedCharge(), Is.True);
            Assert.That(inventory.Slots[0], Is.Null);
            Assert.That(inventory.SelectedSlotIndex, Is.EqualTo(-1));

            inventory.TryAdd(new GameplayItemDefinition("mine", "Mine", "Single item"));
            inventory.TrySelect(0);
            Assert.That(inventory.EndSelectedItemUse(), Is.True);
            Assert.That(inventory.Slots[0], Is.Null);
            Assert.That(inventory.SelectedSlotIndex, Is.EqualTo(-1));
        }

        [Test]
        public void OpeningProtection_BlocksOnlyItemDamageButNeverPush()
        {
            var target = new GameObject("Hit Flow Target");
            try
            {
                var health = target.AddComponent<GameplayHealth>();
                var push = target.AddComponent<PushProbe>();
                var clock = new GameplayPhaseClock();
                clock.Start(10d);
                var now = 14.999d;
                health.ConfigureProtection(clock, () => now);

                var protectedHit = GameplayHitResolver.Resolve(
                    target,
                    new DamageRequest(20, DamageKind.Item, null),
                    Vector3.right * 4f);
                Assert.That(protectedHit.DamageResult, Is.EqualTo(DamageResult.Blocked));
                Assert.That(protectedHit.PushApplied, Is.True);
                Assert.That(health.CurrentHealth, Is.EqualTo(100));

                now = 15d;
                var boundaryHit = GameplayHitResolver.Resolve(
                    target,
                    new DamageRequest(20, DamageKind.Item, null),
                    Vector3.right * 4f);
                Assert.That(boundaryHit.DamageResult, Is.EqualTo(DamageResult.Applied));
                Assert.That(boundaryHit.PushApplied, Is.True);
                Assert.That(health.CurrentHealth, Is.EqualTo(80));

                now = 12d;
                Assert.That(
                    health.ApplyDamage(new DamageRequest(15, DamageKind.Environment, null)),
                    Is.EqualTo(DamageResult.Applied));
                Assert.That(health.CurrentHealth, Is.EqualTo(65));
                Assert.That(push.CallCount, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        private sealed class PushProbe : MonoBehaviour, IPushReceiver
        {
            public int CallCount { get; private set; }

            public void ApplyPush(Vector3 impulse)
            {
                CallCount++;
            }
        }
    }
}
