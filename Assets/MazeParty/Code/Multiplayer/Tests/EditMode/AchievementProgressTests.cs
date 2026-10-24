using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class AchievementProgressTests
    {
        private string _directory, _path;
        [SetUp] public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "MazeParty-Achievements-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory); _path = Path.Combine(_directory, "account.json");
        }
        [TearDown] public void TearDown() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
        private static AchievementReceipt Receipt(int turns = 15) => new AchievementReceipt
        {
            matchId = Guid.NewGuid().ToString("N"), playerKey = "player", mapId = "forest-graybox",
            turns = turns, rank = 1, finalKeys = 4, progress = new AchievementMatchProgress()
        };
        private static bool Has(long flags, PartyAchievement id) => (flags & PartyAchievements.Bit(id)) != 0;

        [Test] public void CompletedReceipts_AcceptAllThreeLengths_RejectInvalidAndOtherPlayers_DeduplicateAfterReload()
        {
            var journal = new AchievementJournal(_path, "stove-account-a");
            foreach (var turns in new[] { 10, 15, 20 })
            {
                var receipt = Receipt(turns);
                Assert.That(journal.Commit(receipt, "someone-else"), Is.False);
                Assert.That(journal.Commit(receipt, "player"), Is.True);
                journal = new AchievementJournal(_path, "stove-account-a");
                Assert.That(journal.Commit(receipt, "player"), Is.False);
            }
            foreach (var turns in new[] { 0, 5, 9, 11, 14, 16, 21 }) Assert.That(journal.Commit(Receipt(turns), "player"), Is.False);
            var invalid = Receipt(); invalid.rank = 0;
            Assert.That(journal.Commit(invalid, "player"), Is.False);
            Assert.That(journal.Snapshot.matches, Is.EqualTo(3));
        }
        [Test] public void Thresholds_UseOnlyCommittedPurchasesAndFinalResults_AcrossMapsAndGames()
        {
            var journal = new AchievementJournal(_path, "account");
            for (var match = 0; match < 5; match++)
            {
                var receipt = Receipt(new[] { 10, 15, 20 }[match % 3]);
                receipt.mapId = match == 0 ? "forest-graybox" : "maze-graybox";
                receipt.bonusWinner = match == 4;
                receipt.progress.PurchaseKey(1, false); receipt.progress.PurchaseKey(1, true);
                receipt.progress.itemsBought = 2; receipt.progress.stolenGifts = 2;
                for (var turn = 1; turn <= 5; turn++)
                {
                    var game = (ScheduledMinigameId)((match * 5 + turn - 1) % 20 + 1);
                    receipt.progress.CompleteMinigame(turn, game, 1, true);
                }
                Assert.That(journal.Commit(receipt, "player"), Is.True);
                if (match == 3)
                    foreach (var id in new[] { PartyAchievement.RegularGuest, PartyAchievement.FiveCrowns,
                        PartyAchievement.KeyRegular, PartyAchievement.ItemRegular, PartyAchievement.TenStolenGifts,
                        PartyAchievement.BonusWinner }) Assert.That(Has(journal.Snapshot.earned, id), Is.False, id.ToString());
            }
            var state = journal.Snapshot;
            foreach (var id in new[] { PartyAchievement.FirstCeremony, PartyAchievement.RegularGuest,
                PartyAchievement.Champion, PartyAchievement.FiveCrowns, PartyAchievement.ForestExplorer,
                PartyAchievement.MazeExplorer, PartyAchievement.FirstKey, PartyAchievement.KeyRegular,
                PartyAchievement.MovingShop, PartyAchievement.FourKeys, PartyAchievement.BonusWinner,
                PartyAchievement.FirstItem, PartyAchievement.ItemRegular, PartyAchievement.InvisibleCustomer,
                PartyAchievement.FirstMinigameWin, PartyAchievement.FiveMinigameWins, PartyAchievement.WinStreak,
                PartyAchievement.AllMinigames, PartyAchievement.PerfectMemory, PartyAchievement.TenStolenGifts })
                Assert.That(Has(state.earned, id), Is.True, id.ToString());
            Assert.That(state.matches, Is.EqualTo(5));
            var onlyBonusKeys = Receipt(); onlyBonusKeys.progress = new AchievementMatchProgress();
            var fresh = new AchievementJournal(Path.Combine(_directory, "fresh.json"), "fresh");
            fresh.Commit(onlyBonusKeys, "player");
            Assert.That(Has(fresh.Snapshot.earned, PartyAchievement.FirstKey), Is.False);
        }
        [Test] public void MinigameStreak_IgnoresSkipAndDuplicateResults_ResetsOnLoss_MemoryRequiresPerfectWin()
        {
            var p = new AchievementMatchProgress();
            p.CompleteMinigame(1, ScheduledMinigameId.SequenceMemory, 1, false);
            p.CompleteMinigame(1, ScheduledMinigameId.SequenceMemory, 1, true);
            Assert.That(p.minigameWins, Is.EqualTo(1));
            Assert.That(Has(p.flags, PartyAchievement.PerfectMemory), Is.False);
            p.CompleteMinigame(2, ScheduledMinigameId.Skip, 1, true);
            p.CompleteMinigame(3, ScheduledMinigameId.WrongWay, 1, false);
            Assert.That(p.winStreak, Is.EqualTo(2));
            p.CompleteMinigame(4, ScheduledMinigameId.Minefield, 2, true);
            Assert.That(p.winStreak, Is.Zero);
            for (var turn = 5; turn <= 7; turn++) p.CompleteMinigame(turn, ScheduledMinigameId.SequenceMemory, 1, true);
            Assert.That(Has(p.flags, PartyAchievement.WinStreak), Is.True);
            Assert.That(Has(p.flags, PartyAchievement.PerfectMemory), Is.True);
            Assert.That(Has(p.flags, PartyAchievement.FiveMinigameWins), Is.True);
            var loser = new AchievementMatchProgress(); loser.CompleteMinigame(1, ScheduledMinigameId.SequenceMemory, 2, true);
            Assert.That(loser.flags, Is.Zero);
        }
        [Test] public void PurchaseAndItemConditions_SeparateTurnsAndSourceOwnership()
        {
            var p = new AchievementMatchProgress();
            p.PurchaseKey(1, false); p.PurchaseKey(2, false);
            Assert.That(Has(p.flags, PartyAchievement.MovingShop), Is.False);
            p.PurchaseKey(2, true);
            Assert.That(Has(p.flags, PartyAchievement.MovingShop), Is.True);
            Assert.That(Has(p.flags, PartyAchievement.InvisibleCustomer), Is.True);
            var items = new[] { PrototypeItemId.Sniper, PrototypeItemId.Pistol, PrototypeItemId.Mine, PrototypeItemId.Grenade };
            var other = new[] { PartyAchievement.Sniper, PartyAchievement.Pistol, PartyAchievement.MineKill, PartyAchievement.GrenadeKill };
            for (var index = 0; index < items.Length; index++)
                foreach (var self in new[] { false, true })
                {
                    var progress = new AchievementMatchProgress(); progress.ItemDeath(items[index], self);
                    var expected = !self ? PartyAchievements.Bit(other[index]) : index < 2 ? 0L :
                        PartyAchievements.Bit(index == 2 ? PartyAchievement.OwnMine : PartyAchievement.OwnGrenade);
                    Assert.That(progress.flags, Is.EqualTo(expected));
                }
        }
        [Test] public void Journal_PreservesOutboxAndAccountIsolation_FailsClosedOnCorruptionAndWriteFailure()
        {
            var journal = new AchievementJournal(_path, "account-a");
            var receipt = Receipt(); journal.Commit(receipt, "player");
            journal.Confirm(1, 1);
            var restored = new AchievementJournal(_path, "account-a");
            Assert.That(restored.Pending & 1L, Is.Zero);
            Assert.That(restored.Pending, Is.Not.Zero);
            Assert.Throws<InvalidDataException>(() => new AchievementJournal(_path, "account-b"));
            var second = Receipt();
            Directory.CreateDirectory(_path + ".tmp");
            Assert.That(() => restored.Commit(second, "player"), Throws.InstanceOf<UnauthorizedAccessException>().Or.InstanceOf<IOException>());
            Assert.That(restored.Snapshot.matches, Is.EqualTo(1));
            Directory.Delete(_path + ".tmp");
            Assert.That(restored.Commit(second, "player"), Is.True);
            Assert.That(restored.Commit(second, "player"), Is.False);
            File.WriteAllText(_path, "{\"payload\":\"damaged\",\"checksum\":\"invalid\"}");
            Assert.Throws<InvalidDataException>(() => new AchievementJournal(_path, "account-a"));
            Assert.That(File.ReadAllText(_path), Does.Contain("damaged"));
        }
        [Test] public void UploadQueue_ThrottlesRetriesAndRejectsStaleCallbacks_ConfirmsAlreadyStoredOne()
        {
            var journal = new AchievementJournal(_path, "account");
            var receipt = Receipt(); receipt.rank = 2; receipt.finalKeys = 0;
            journal.Commit(receipt, "player"); // Only completion and Forest remain pending.
            var queue = new AchievementUploadQueue();
            Assert.That(queue.TryBegin(journal, 0, out var number, out var value, out var first), Is.True);
            Assert.That(value, Is.EqualTo(1));
            Assert.That(number, Is.EqualTo(1));
            Assert.That(queue.TryBegin(journal, 19.999, out _, out _, out _), Is.False);
            Assert.That(queue.TryBegin(journal, 20, out _, out _, out _), Is.False);
            Assert.That(queue.TryBegin(journal, 60, out number, out _, out var second), Is.True);
            Assert.That(number, Is.EqualTo(5));
            Assert.That(queue.Complete(first, true, 1), Is.Zero);
            Assert.That(queue.Complete(second, false, 0), Is.Zero);
            Assert.That(queue.TryBegin(journal, 119.999, out _, out _, out _), Is.False);
            Assert.That(queue.TryBegin(journal, 120, out number, out _, out var retry), Is.True);
            Assert.That(number, Is.EqualTo(1));
            Assert.That(queue.Complete(retry, true, 1), Is.EqualTo(1));
            Assert.That(queue.Complete(retry, true, 1), Is.Zero);
        }

        [Test] public void SharedStats_PersistCountsBeyondThresholds_AndUseSingleMatchHighScore()
        {
            var journal = new AchievementJournal(_path, "account");
            var scores = new[] { 3, 2, 6, 1, 5, 4 };
            for (var index = 0; index < scores.Length; index++)
            {
                var receipt = Receipt(new[] { 10, 15, 20 }[index % 3]);
                receipt.progress.keysBought = 4; receipt.progress.itemsBought = 3;
                for (var turn = 1; turn <= scores[index]; turn++)
                    receipt.progress.CompleteMinigame(turn, ScheduledMinigameId.SequenceMemory, 1, false);
                journal.Commit(receipt, "player");
                journal = new AchievementJournal(_path, "account");
                Assert.That(journal.Commit(receipt, "player"), Is.False);
                var count = index + 1;
                foreach (var number in new[] { 1, 2, 3, 4 }) Assert.That(journal.GetStatValue(number), Is.EqualTo(count));
                foreach (var number in new[] { 7, 8 }) Assert.That(journal.GetStatValue(number), Is.EqualTo(count * 4));
                foreach (var number in new[] { 12, 13 }) Assert.That(journal.GetStatValue(number), Is.EqualTo(count * 3));
                var best = index < 2 ? 3 : 6;
                foreach (var number in new[] { 25, 26 }) Assert.That(journal.GetStatValue(number), Is.EqualTo(best));
                Assert.That(Has(journal.Snapshot.earned, PartyAchievement.FiveMinigameWins), Is.EqualTo(index >= 2));
            }
            Assert.That(journal.Snapshot.bestMinigameWins, Is.EqualTo(6));
        }

        [Test] public void UploadAcknowledgement_CapturesSentValue_RetriesLowReplies_AndRequeuesGrowth()
        {
            var journal = new AchievementJournal(_path, "account");
            var receipt = Receipt(); receipt.rank = 2; receipt.finalKeys = 0; receipt.mapId = "";
            journal.Commit(receipt, "player");
            var queue = new AchievementUploadQueue();
            Assert.That(queue.TryBegin(journal, 0, out var number, out var value, out var ticket), Is.True);
            Assert.That(value, Is.EqualTo(1));
            receipt.matchId = Guid.NewGuid().ToString("N"); journal.Commit(receipt, "player");
            Assert.That(queue.Complete(ticket, true, 9), Is.EqualTo(number));
            journal.Confirm(number, value); // Even a remote high value cannot acknowledge unsent local progress.
            journal = new AchievementJournal(_path, "account");
            Assert.That(journal.Pending, Is.EqualTo(1L));
            Assert.That(journal.Snapshot.confirmedValues[0], Is.EqualTo(1));
            Assert.That(queue.TryBegin(journal, 59.999, out _, out _, out _), Is.False);
            Assert.That(queue.TryBegin(journal, 60, out number, out value, out ticket), Is.True);
            Assert.That(value, Is.EqualTo(2));
            Assert.That(queue.Complete(ticket, true, 1), Is.Zero);
            Assert.That(journal.Pending, Is.EqualTo(1L));
            Assert.That(queue.TryBegin(journal, 120, out number, out value, out ticket), Is.True);
            Assert.That(queue.Complete(ticket, true, 2), Is.EqualTo(number));
            Directory.CreateDirectory(_path + ".tmp");
            Assert.That(() => journal.Confirm(number, value), Throws.InstanceOf<UnauthorizedAccessException>().Or.InstanceOf<IOException>());
            Assert.That(journal.Pending, Is.EqualTo(1L));
            Directory.Delete(_path + ".tmp");
            journal.Confirm(number, value);
            journal = new AchievementJournal(_path, "account");
            Assert.That(journal.Pending, Is.Zero);
            receipt.matchId = Guid.NewGuid().ToString("N"); journal.Commit(receipt, "player");
            Assert.That(journal.Pending, Is.EqualTo(1L));
        }

        [Test] public void Outbox_SendsOnly29CanonicalStats_WithRealValues_AndSurvivesReload()
        {
            var journal = new AchievementJournal(_path, "account");
            var receipt = Receipt(); receipt.progress.flags = PartyAchievements.All;
            receipt.progress.keysBought = 12; receipt.progress.itemsBought = 14; receipt.progress.minigameWins = 7;
            journal.Commit(receipt, "player");
            var queue = new AchievementUploadQueue();
            var sent = new Dictionary<string, int>();
            for (var now = 0d; journal.Pending != 0 && now < 60 * 34; now += 60)
            {
                Assert.That(queue.TryBegin(journal, now, out var number, out var value, out var ticket), Is.True);
                sent.Add(PartyAchievements.StatId(number), value);
                Assert.That(queue.Complete(ticket, true, value), Is.EqualTo(number));
                journal.Confirm(number, value);
                journal = new AchievementJournal(_path, "account");
            }
            Assert.That(journal.Pending, Is.Zero);
            Assert.That(sent.Count, Is.EqualTo(29));
            foreach (var retired in new[] { "MP_ACH_02", "MP_ACH_04", "MP_ACH_08", "MP_ACH_13", "MP_ACH_26" })
                Assert.That(sent.ContainsKey(retired), Is.False);
            Assert.That(sent["MP_ACH_07"], Is.EqualTo(12));
            Assert.That(sent["MP_ACH_12"], Is.EqualTo(14));
            Assert.That(sent["MP_ACH_25"], Is.EqualTo(7));
            Assert.That(sent["MP_ACH_27"], Is.EqualTo(1));
        }

        [Serializable] private sealed class LegacyProgress
        {
            public int version = 1;
            public string accountScope = "account";
            public long earned, confirmed;
            public int matches = 5, wins = 5, keysBought = 10, itemsBought = 10, stolenGifts, gamesPlayed;
            public List<string> committedMatches = new List<string>();
        }
        [Serializable] private sealed class TestEnvelope { public string payload, checksum; }
        private void WriteJournal(object state)
        {
            var payload = JsonUtility.ToJson(state);
            using (var sha = SHA256.Create())
                File.WriteAllText(_path, JsonUtility.ToJson(new TestEnvelope
                { payload = payload, checksum = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(payload))) }));
        }

        [Test] public void Version1Migration_PreservesEarnedAndDedupe_ResyncsSharedValues_RecoversProvenHighScore()
        {
            foreach (var best in new[] { 0, 1, 3, 5 })
            {
                var legacy = new LegacyProgress();
                foreach (var id in new[] { PartyAchievement.FirstCeremony, PartyAchievement.RegularGuest,
                    PartyAchievement.Champion, PartyAchievement.FiveCrowns, PartyAchievement.FirstKey,
                    PartyAchievement.KeyRegular, PartyAchievement.FirstItem, PartyAchievement.ItemRegular, PartyAchievement.ForestExplorer })
                    legacy.earned |= PartyAchievements.Bit(id);
                if (best > 0) legacy.earned |= PartyAchievements.Bit(PartyAchievement.FirstMinigameWin);
                if (best >= 3) legacy.earned |= PartyAchievements.Bit(PartyAchievement.WinStreak);
                if (best >= 5) legacy.earned |= PartyAchievements.Bit(PartyAchievement.FiveMinigameWins);
                legacy.confirmed = legacy.earned;
                for (var match = 0; match < 5; match++) legacy.committedMatches.Add(Guid.NewGuid().ToString("N"));
                WriteJournal(legacy);
                var journal = new AchievementJournal(_path, "account");
                Assert.That(journal.Snapshot.version, Is.EqualTo(2));
                Assert.That(journal.Snapshot.earned, Is.EqualTo(legacy.earned));
                Assert.That(journal.GetStatValue(25), Is.EqualTo(best));
                Assert.That(Has(journal.Pending, PartyAchievement.ForestExplorer), Is.False);
                foreach (var number in new[] { 1, 3, 7, 12 })
                    Assert.That((journal.Pending & (1L << (number - 1))) != 0, Is.True);
                Assert.That(Has(journal.Pending, PartyAchievement.FirstMinigameWin), Is.EqualTo(best > 0));
                var duplicate = Receipt(); duplicate.matchId = legacy.committedMatches[0];
                Assert.That(journal.Commit(duplicate, "player"), Is.False);
                journal.Confirm(1, 5);
                journal = new AchievementJournal(_path, "account");
                Assert.That(Has(journal.Pending, PartyAchievement.FirstCeremony), Is.False);
                Assert.That(journal.GetStatValue(7), Is.EqualTo(10));
                var next = Receipt(); next.progress.keysBought = 2; next.progress.itemsBought = 3;
                journal.Commit(next, "player");
                Assert.That(journal.GetStatValue(7), Is.EqualTo(12));
                Assert.That(journal.GetStatValue(12), Is.EqualTo(13));
                Assert.That(journal.GetStatValue(25), Is.EqualTo(best));
            }
        }

        [Test] public void Journal_RejectsInvalidStatAcknowledgements_AndSaturatesSdkIntegerBoundary()
        {
            var journal = new AchievementJournal(_path, "account");
            var receipt = Receipt(); receipt.progress.keysBought = int.MaxValue; receipt.progress.itemsBought = int.MaxValue;
            journal.Commit(receipt, "player");
            receipt.matchId = Guid.NewGuid().ToString("N"); journal.Commit(receipt, "player");
            Assert.That(journal.GetStatValue(7), Is.EqualTo(int.MaxValue));
            Assert.That(journal.GetStatValue(12), Is.EqualTo(int.MaxValue));
            foreach (var corruption in new[] { "negative", "future", "alias", "short", "score", "version" })
            {
                var state = journal.Snapshot;
                switch (corruption)
                {
                    case "negative": state.confirmedValues[0] = -1; break;
                    case "future": state.confirmedValues[0] = state.matches + 1; break;
                    case "alias": state.confirmedValues[1] = 1; break;
                    case "short": state.confirmedValues = new int[1]; break;
                    case "score": state.bestMinigameWins = 21; break;
                    case "version": state.version = 3; break;
                }
                WriteJournal(state);
                Assert.Throws<InvalidDataException>(() => new AchievementJournal(_path, "account"), corruption);
            }
        }
    }
}
