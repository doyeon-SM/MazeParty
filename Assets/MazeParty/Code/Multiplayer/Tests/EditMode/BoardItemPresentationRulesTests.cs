using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using NUnit.Framework;
using UnityEngine;

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

        [Test]
        public void GrenadePresentationTracks_KeepIdentityWhileSmoothingSnapshots()
        {
            var tracks = new GrenadePresentationTrackSet(.05d, .1d);
            var added = new List<uint>();
            var removed = new List<uint>();

            Assert.That(
                tracks.ApplySnapshot(
                    new uint[] { 11u, 22u },
                    new[] { Vector3.zero, new Vector3(100f, 0f, 0f) },
                    new[] { new Vector3(10f, 0f, 0f), new Vector3(20f, 0f, 0f) },
                    1d,
                    added,
                    removed),
                Is.True);
            Assert.That(added, Is.EqualTo(new uint[] { 11u, 22u }));
            Assert.That(removed, Is.Empty);

            Assert.That(
                tracks.ApplySnapshot(
                    new uint[] { 11u, 22u },
                    new[] { new Vector3(.5f, 0f, 0f), new Vector3(101f, 0f, 0f) },
                    new[] { new Vector3(10f, 0f, 0f), new Vector3(20f, 0f, 0f) },
                    1.05d,
                    added,
                    removed),
                Is.True);

            (uint Id, double Now, float ExpectedX, string Contract)[] samples =
            {
                (11u, 1.075d, .25f, "interpolates the first grenade"),
                (22u, 1.075d, 100.5f, "interpolates the second grenade"),
                (22u, 1.125d, 101.5f, "briefly extrapolates after the newest snapshot"),
                (22u, 2d, 103f, "caps extrapolation during a long snapshot gap")
            };

            foreach (var sample in samples)
            {
                Assert.That(
                    tracks.TrySample(sample.Id, sample.Now, out var position),
                    Is.True,
                    sample.Contract);
                Assert.That(
                    position.x,
                    Is.EqualTo(sample.ExpectedX).Within(.0001f),
                    sample.Contract);
            }

            Assert.That(
                tracks.ApplySnapshot(
                    new uint[] { 22u },
                    new[] { new Vector3(102f, 0f, 0f) },
                    new[] { new Vector3(20f, 0f, 0f) },
                    1.1d,
                    added,
                    removed),
                Is.True);
            Assert.That(added, Is.Empty);
            Assert.That(removed, Is.Empty,
                "A removal snapshot stays delayed with the positions it describes.");
            Assert.That(
                tracks.RemoveExpired(1.149999d, removed),
                Is.True);
            Assert.That(removed, Is.Empty);
            Assert.That(
                tracks.TrySample(11u, 1.149999d, out var finalFlight),
                Is.True);
            Assert.That(finalFlight.x, Is.EqualTo(.99999f).Within(.0001f),
                "The removed grenade remains sampled until presentation time reaches the removal snapshot.");

            Assert.That(tracks.RemoveExpired(1.15d, removed), Is.True);
            Assert.That(removed, Is.EqualTo(new uint[] { 11u }),
                "The grenade expires exactly at removal time plus interpolation delay.");
            Assert.That(tracks.TrySample(11u, 1.15d, out _), Is.False);
            Assert.That(tracks.TrySample(22u, 1.125d, out var retained), Is.True);
            Assert.That(
                retained.x,
                Is.EqualTo(101.5f).Within(.0001f),
                "Removing the first grenade must not rebind the second track to its view.");
        }

        [Test]
        public void GrenadePresentationTracks_ReappearanceInvalidSnapshotsAndClearAreSafe()
        {
            var tracks = new GrenadePresentationTrackSet(.05d, .1d);
            var added = new List<uint>();
            var removed = new List<uint>();

            Assert.That(
                tracks.ApplySnapshot(
                    new uint[] { 7u },
                    new[] { Vector3.zero },
                    new[] { Vector3.right },
                    1d,
                    added,
                    removed),
                Is.True);
            Assert.That(added, Is.EqualTo(new uint[] { 7u }));

            Assert.That(
                tracks.ApplySnapshot(
                    Array.Empty<uint>(),
                    Array.Empty<Vector3>(),
                    Array.Empty<Vector3>(),
                    1.05d,
                    added,
                    removed),
                Is.True);
            Assert.That(removed, Is.Empty);

            Assert.That(
                tracks.ApplySnapshot(
                    new uint[] { 7u },
                    Array.Empty<Vector3>(),
                    new[] { Vector3.right },
                    1.06d,
                    added,
                    removed),
                Is.False);
            Assert.That(added, Is.Empty);
            Assert.That(removed, Is.Empty);
            Assert.That(tracks.TrySample(7u, 1.099999d, out _), Is.True,
                "An invalid snapshot must not mutate a pending removal.");

            Assert.That(
                tracks.ApplySnapshot(
                    new uint[] { 7u },
                    new[] { new Vector3(.075f, 0f, 0f) },
                    new[] { Vector3.right },
                    1.075d,
                    added,
                    removed),
                Is.True);
            Assert.That(added, Is.Empty,
                "A reappearing ID keeps its existing presentation view.");
            Assert.That(tracks.RemoveExpired(2d, removed), Is.True);
            Assert.That(removed, Is.Empty,
                "A newer authoritative sample cancels pending removal.");
            Assert.That(tracks.TrySample(7u, 2d, out _), Is.True);

            Assert.That(
                tracks.ApplySnapshot(
                    Array.Empty<uint>(),
                    Array.Empty<Vector3>(),
                    Array.Empty<Vector3>(),
                    2.1d,
                    added,
                    removed),
                Is.True);
            tracks.Clear();
            Assert.That(tracks.RemoveExpired(3d, removed), Is.True);
            Assert.That(removed, Is.Empty);
            Assert.That(tracks.TrySample(7u, 3d, out _), Is.False,
                "Clear removes active and pending-removal tracks together.");
        }
    }
}
