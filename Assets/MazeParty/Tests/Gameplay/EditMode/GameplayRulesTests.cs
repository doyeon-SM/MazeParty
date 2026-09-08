using MazeParty.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class GameplayPhaseClockTests
    {
        [Test]
        public void Start_UsesOneTimestampForAllThreeWindows()
        {
            var clock = new GameplayPhaseClock();

            clock.Start(100d);

            Assert.That(clock.GetActionRemaining(100d), Is.EqualTo(180d));
            Assert.That(clock.GetChoiceRemaining(100d), Is.EqualTo(30d));
            Assert.That(clock.GetOpeningProtectionRemaining(100d), Is.EqualTo(5d));
        }

        [Test]
        public void OpeningProtection_UsesHalfOpenFiveSecondInterval()
        {
            var clock = new GameplayPhaseClock();
            clock.Start(25d);

            Assert.That(clock.IsOpeningProtectionActive(25d), Is.True);
            Assert.That(clock.IsOpeningProtectionActive(29.999d), Is.True);
            Assert.That(clock.IsOpeningProtectionActive(30d), Is.False);
        }

        [Test]
        public void ChoiceTimeout_ResolvesNoItemAtExactlyThirtySeconds_WithoutStoppingSharedClock()
        {
            var clock = new GameplayPhaseClock();
            clock.Start(10d);

            clock.Tick(39.999d);
            Assert.That(clock.ChoiceResolution, Is.EqualTo(ItemChoiceResolution.Pending));

            clock.Tick(40d);

            Assert.That(clock.ChoiceResolution, Is.EqualTo(ItemChoiceResolution.TimedOut));
            Assert.That(clock.SelectedSlotIndex, Is.EqualTo(-1));
            Assert.That(clock.IsRunning, Is.True);
            Assert.That(clock.GetActionRemaining(40d), Is.EqualTo(150d));
        }

        [Test]
        public void ExplicitChoice_CannotOverrideTimeoutAtBoundary()
        {
            var clock = new GameplayPhaseClock();
            clock.Start(0d);

            var selected = clock.TrySelectItem(0, 30d);

            Assert.That(selected, Is.False);
            Assert.That(clock.ChoiceResolution, Is.EqualTo(ItemChoiceResolution.TimedOut));
        }

        [Test]
        public void PlayerChoices_AreIndependentAndDoNotGateActionClock()
        {
            var playerOne = new GameplayPhaseClock();
            var playerTwo = new GameplayPhaseClock();
            playerOne.Start(50d);
            playerTwo.Start(50d);

            Assert.That(playerOne.TrySelectItem(1, 52d), Is.True);

            Assert.That(playerOne.ChoiceResolution, Is.EqualTo(ItemChoiceResolution.ItemSelected));
            Assert.That(playerTwo.ChoiceResolution, Is.EqualTo(ItemChoiceResolution.Pending));
            Assert.That(playerOne.GetActionRemaining(60d), Is.EqualTo(170d));
            Assert.That(playerTwo.GetActionRemaining(60d), Is.EqualTo(170d));
        }

        [Test]
        public void ActionExpiresAtExactlyOneHundredEightySeconds()
        {
            var clock = new GameplayPhaseClock();
            clock.Start(5d);

            Assert.That(clock.IsActionExpired(184.999d), Is.False);
            Assert.That(clock.IsActionExpired(185d), Is.True);
        }
    }

    public sealed class GameplayInventoryTests
    {
        [Test]
        public void FullInventory_RejectsRewardWithoutDiscardingExistingItems()
        {
            var inventory = CreateFullInventory();

            var accepted = inventory.TryAdd(new GameplayItemDefinition("reward", "Reward", "Bonus"));

            Assert.That(accepted, Is.False);
            Assert.That(inventory.IsFull, Is.True);
            Assert.That(inventory.Slots[0].Definition.Id, Is.EqualTo("gun"));
            Assert.That(inventory.Slots[1].Definition.Id, Is.EqualTo("mine"));
            Assert.That(inventory.Slots[2].Definition.Id, Is.EqualTo("kit"));
        }

        [Test]
        public void MultiChargeItem_RemainsActiveUntilLastCharge()
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
        }

        [Test]
        public void EndAction_RemovesActiveItemAndHighlight()
        {
            var inventory = CreateFullInventory();
            inventory.TrySelect(0);

            var removed = inventory.EndSelectedItemUse();

            Assert.That(removed, Is.True);
            Assert.That(inventory.Slots[0], Is.Null);
            Assert.That(inventory.SelectedSlotIndex, Is.EqualTo(-1));
        }

        private static GameplayInventory CreateFullInventory()
        {
            var inventory = new GameplayInventory();
            inventory.TryAdd(new GameplayItemDefinition("gun", "Gun", "Ammo item", 7));
            inventory.TryAdd(new GameplayItemDefinition("mine", "Mine", "Single item"));
            inventory.TryAdd(new GameplayItemDefinition("kit", "Kit", "Single item"));
            return inventory;
        }
    }

    public sealed class GameplayHitFlowTests
    {
        private GameObject _target;
        private GameplayHealth _health;
        private PushProbe _pushProbe;
        private GameplayPhaseClock _clock;
        private double _now;

        [SetUp]
        public void SetUp()
        {
            _target = new GameObject("Hit Flow Target");
            _health = _target.AddComponent<GameplayHealth>();
            _pushProbe = _target.AddComponent<PushProbe>();
            _clock = new GameplayPhaseClock();
            _clock.Start(10d);
            _now = 10d;
            _health.ConfigureProtection(_clock, () => _now);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_target);
        }

        [Test]
        public void ChoicePending_DuringProtection_BlocksItemHpDamageButAppliesPush()
        {
            _now = 14.999d;

            var report = GameplayHitResolver.Resolve(
                _target,
                new DamageRequest(20, DamageKind.Item, null),
                Vector3.right * 4f);

            Assert.That(_clock.IsChoicePending, Is.True);
            Assert.That(report.DamageResult, Is.EqualTo(DamageResult.Blocked));
            Assert.That(report.PushApplied, Is.True);
            Assert.That(_health.CurrentHealth, Is.EqualTo(100));
            Assert.That(_pushProbe.CallCount, Is.EqualTo(1));
        }

        [Test]
        public void ChoicePending_AtFiveSeconds_AppliesBothDamageAndPush()
        {
            _now = 15d;

            var report = GameplayHitResolver.Resolve(
                _target,
                new DamageRequest(20, DamageKind.Item, null),
                Vector3.right * 4f);

            Assert.That(_clock.IsChoicePending, Is.True);
            Assert.That(report.DamageResult, Is.EqualTo(DamageResult.Applied));
            Assert.That(report.PushApplied, Is.True);
            Assert.That(_health.CurrentHealth, Is.EqualTo(80));
            Assert.That(_pushProbe.CallCount, Is.EqualTo(1));
        }

        [Test]
        public void OpeningShield_BlocksOnlyItemDamage()
        {
            _now = 12d;

            var result = _health.ApplyDamage(new DamageRequest(15, DamageKind.Environment, null));

            Assert.That(result, Is.EqualTo(DamageResult.Applied));
            Assert.That(_health.CurrentHealth, Is.EqualTo(85));
        }
    }

    public sealed class PushProbe : MonoBehaviour, IPushReceiver
    {
        public int CallCount { get; private set; }
        public Vector3 LastImpulse { get; private set; }

        public void ApplyPush(Vector3 impulse)
        {
            CallCount++;
            LastImpulse = impulse;
        }
    }
}
