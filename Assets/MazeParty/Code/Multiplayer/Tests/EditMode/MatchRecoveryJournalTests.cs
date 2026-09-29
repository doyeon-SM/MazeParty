using System;
using System.IO;
using NUnit.Framework;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MatchRecoveryJournalTests
    {
        private string _directory;
        private MatchRecoveryJournal _journal;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(
                Path.GetTempPath(),
                "MazeParty-MatchRecovery-" + Guid.NewGuid().ToString("N"));
            _journal = new MatchRecoveryJournal(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void TwoValidSlots_SelectHighestRevision()
        {
            var now = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            _journal.Save("match", "roster", "content", "first", now);
            var second = _journal.Save(
                "match",
                "roster",
                "content",
                "second",
                now.AddMinutes(1));

            var status = _journal.TryLoadLatest(
                "match",
                now.AddMinutes(2),
                "roster",
                "content",
                out var loaded);

            Assert.That(status, Is.EqualTo(MatchRecoveryLoadStatus.Loaded));
            Assert.That(loaded.Revision, Is.EqualTo(second.Revision));
            Assert.That(loaded.Payload, Is.EqualTo("second"));
        }

        [Test]
        public void LatestSlotDamaged_FallsBackToPreviousValidRevision()
        {
            var now = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var first = _journal.Save(
                "match",
                "roster",
                "content",
                "first",
                now);
            var second = _journal.Save(
                "match",
                "roster",
                "content",
                "second",
                now.AddMinutes(1));
            File.WriteAllText(
                _journal.GetSlotPath("match", second.Slot),
                "{partial");

            var status = _journal.TryLoadLatest(
                "match",
                now.AddMinutes(2),
                "roster",
                "content",
                out var loaded);

            Assert.That(status, Is.EqualTo(MatchRecoveryLoadStatus.Loaded));
            Assert.That(loaded.Revision, Is.EqualTo(first.Revision));
            Assert.That(loaded.Payload, Is.EqualTo("first"));
        }

        [Test]
        public void BothSlotsDamaged_ReturnsCorrupt()
        {
            var now = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            _journal.Save("match", "roster", "content", "first", now);
            _journal.Save("match", "roster", "content", "second", now);
            File.WriteAllText(
                _journal.GetSlotPath("match", MatchRecoveryJournalSlot.A),
                "broken-a");
            File.WriteAllText(
                _journal.GetSlotPath("match", MatchRecoveryJournalSlot.B),
                "broken-b");

            var status = _journal.TryPeekLatest(
                "match",
                now.AddMinutes(1),
                out _);

            Assert.That(status, Is.EqualTo(MatchRecoveryLoadStatus.Corrupt));
        }

        [Test]
        public void FingerprintMismatch_IsRejected()
        {
            var now = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var cases = new[]
            {
                (Roster: "other-roster", Content: "content",
                    Expected: MatchRecoveryLoadStatus.RosterMismatch),
                (Roster: "roster", Content: "other-content",
                    Expected: MatchRecoveryLoadStatus.ContentMismatch)
            };

            for (var index = 0; index < cases.Length; index++)
            {
                var testCase = cases[index];
                var matchKey = "match-" + index;
                _journal.Save(
                    matchKey,
                    "roster",
                    "content",
                    "payload",
                    now);

                var status = _journal.TryLoadLatest(
                    matchKey,
                    now.AddMinutes(1),
                    testCase.Roster,
                    testCase.Content,
                    out _);

                Assert.That(
                    status,
                    Is.EqualTo(testCase.Expected),
                    matchKey);
            }
        }

        [Test]
        public void ExpiredJournal_IsDeleted()
        {
            var now = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            _journal.Save(
                "match",
                "roster",
                "content",
                "payload",
                now,
                TimeSpan.FromHours(1));

            var status = _journal.TryPeekLatest(
                "match",
                now.AddHours(1),
                out _);

            Assert.That(status, Is.EqualTo(MatchRecoveryLoadStatus.Expired));
            Assert.That(Directory.GetFiles(_directory, "*.json"), Is.Empty);
        }

        [Test]
        public void PayloadMutation_InvalidatesChecksum()
        {
            var now = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var saved = _journal.Save(
                "match",
                "roster",
                "content",
                "original",
                now);
            var path = _journal.GetSlotPath("match", saved.Slot);
            File.WriteAllText(
                path,
                File.ReadAllText(path).Replace("original", "tampered"));

            var status = _journal.TryPeekLatest(
                "match",
                now.AddMinutes(1),
                out _);

            Assert.That(status, Is.EqualTo(MatchRecoveryLoadStatus.Corrupt));
        }
    }
}
