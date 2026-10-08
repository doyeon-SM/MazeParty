using System;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardPlayerMapKnowledgeTests
    {
        [Test]
        public void ObservationFrames_UpdateHistoryAndClearOnlyCurrentVisibility()
        {
            var knowledge = new BoardPlayerMapKnowledge();
            var initial = new Vector2Int(2, 3);
            var observed = new Vector2Int(5, 7);

            Assert.That(knowledge.SeedIfUnknown(0, initial), Is.True);
            Assert.That(knowledge.SeedIfUnknown(0, observed), Is.False);
            Assert.That(knowledge.KnownMask, Is.EqualTo(0b0001));
            Assert.That(knowledge.CurrentVisibleMask, Is.Zero);
            Assert.That(knowledge.TryGetLastKnown(0, out var coordinate), Is.True);
            Assert.That(coordinate, Is.EqualTo(initial));
            Assert.That(knowledge.Revision, Is.EqualTo(1));

            knowledge.BeginObservationFrame();
            knowledge.Observe(0, observed);
            knowledge.Observe(2, new Vector2Int(-4, 9));
            Assert.That(knowledge.EndObservationFrame(), Is.True);
            Assert.That(knowledge.KnownMask, Is.EqualTo(0b0101));
            Assert.That(knowledge.CurrentVisibleMask, Is.EqualTo(0b0101));
            Assert.That(knowledge.IsCurrentlyVisible(0), Is.True);
            Assert.That(knowledge.IsCurrentlyVisible(1), Is.False);
            Assert.That(knowledge.TryGetLastKnown(0, out coordinate), Is.True);
            Assert.That(coordinate, Is.EqualTo(observed));
            Assert.That(knowledge.Revision, Is.EqualTo(2),
                "One completed frame advances the revision only once.");

            knowledge.BeginObservationFrame();
            knowledge.Observe(0, observed);
            knowledge.Observe(2, new Vector2Int(-4, 9));
            Assert.That(knowledge.EndObservationFrame(), Is.False);
            Assert.That(knowledge.Revision, Is.EqualTo(2),
                "An identical frame must not churn map refresh revisions.");

            knowledge.BeginObservationFrame();
            Assert.That(knowledge.EndObservationFrame(), Is.True);
            Assert.That(knowledge.CurrentVisibleMask, Is.Zero);
            Assert.That(knowledge.TryGetLastKnown(0, out coordinate), Is.True);
            Assert.That(coordinate, Is.EqualTo(observed),
                "Losing sight hides the live marker without erasing map history.");
            Assert.That(knowledge.TryGetLastKnown(2, out _), Is.True);
            Assert.That(knowledge.Revision, Is.EqualTo(3));
        }

        [Test]
        public void ObservationFrame_IsAtomicAndKeepsLatestCoordinatePerSlot()
        {
            var knowledge = new BoardPlayerMapKnowledge();
            var original = new Vector2Int(1, 1);
            var latest = new Vector2Int(8, -3);
            knowledge.SeedIfUnknown(1, original);
            var revisionBeforeFrame = knowledge.Revision;

            knowledge.BeginObservationFrame();
            knowledge.Observe(1, new Vector2Int(4, 4));
            knowledge.Observe(1, latest);

            Assert.That(knowledge.IsObservationFrameOpen, Is.True);
            Assert.That(knowledge.CurrentVisibleMask, Is.Zero);
            Assert.That(knowledge.TryGetLastKnown(1, out var beforeCommit), Is.True);
            Assert.That(beforeCommit, Is.EqualTo(original));
            Assert.That(knowledge.Revision, Is.EqualTo(revisionBeforeFrame));

            Assert.That(knowledge.EndObservationFrame(), Is.True);
            Assert.That(knowledge.IsObservationFrameOpen, Is.False);
            Assert.That(knowledge.CurrentVisibleMask, Is.EqualTo(0b0010));
            Assert.That(knowledge.TryGetLastKnown(1, out var afterCommit), Is.True);
            Assert.That(afterCommit, Is.EqualTo(latest));
            Assert.That(knowledge.Revision, Is.EqualTo(revisionBeforeFrame + 1));
            Assert.That(knowledge.EndObservationFrame(), Is.False,
                "Completing an already closed frame is a no-op.");
        }

        [Test]
        public void Reset_ClearsCommittedAndPendingKnowledge()
        {
            var knowledge = new BoardPlayerMapKnowledge();
            knowledge.SeedIfUnknown(3, new Vector2Int(3, 3));
            knowledge.BeginObservationFrame();
            knowledge.Observe(1, new Vector2Int(1, 1));
            var revisionBeforeReset = knowledge.Revision;

            knowledge.Reset();

            Assert.That(knowledge.KnownMask, Is.Zero);
            Assert.That(knowledge.CurrentVisibleMask, Is.Zero);
            Assert.That(knowledge.IsObservationFrameOpen, Is.False);
            Assert.That(knowledge.TryGetLastKnown(1, out _), Is.False);
            Assert.That(knowledge.TryGetLastKnown(3, out _), Is.False);
            Assert.That(knowledge.Revision, Is.EqualTo(revisionBeforeReset + 1));

            knowledge.Reset();
            Assert.That(knowledge.Revision, Is.EqualTo(revisionBeforeReset + 1),
                "Resetting an empty cache is idempotent.");
        }

        [Test]
        public void SlotApis_RejectValuesOutsideFourPlayerRange()
        {
            foreach (var slot in new[] { -1, PlayerSlotRules.Count })
            {
                var knowledge = new BoardPlayerMapKnowledge();
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    knowledge.SeedIfUnknown(slot, Vector2Int.zero));
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    knowledge.TryGetLastKnown(slot, out _));
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    knowledge.IsCurrentlyVisible(slot));

                knowledge.BeginObservationFrame();
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    knowledge.Observe(slot, Vector2Int.zero));
            }

            Assert.Throws<InvalidOperationException>(() =>
                new BoardPlayerMapKnowledge().Observe(0, Vector2Int.zero));
        }
    }
}
