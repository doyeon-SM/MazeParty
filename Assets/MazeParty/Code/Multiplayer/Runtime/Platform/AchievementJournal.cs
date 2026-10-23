using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    [Serializable]
    public sealed class AchievementAccountProgress
    {
        public int version = 2;
        public string accountScope;
        public long earned;
        public long confirmed; // Version 1 migration only; version 2 acknowledges stat values.
        public int matches, wins, keysBought, itemsBought, stolenGifts, gamesPlayed;
        public int bestMinigameWins;
        // Indexed by stable stat number - 1. The five merged-away slots stay zero.
        public int[] confirmedValues = new int[PartyAchievements.Count];
        public List<string> committedMatches = new List<string>();
    }

    // Commit receipt + cumulative counters + outbox in ONE durable replacement.
    // A damaged journal is never reset silently: that could count a receipt twice.
    public sealed class AchievementJournal
    {
        [Serializable] private sealed class Envelope { public string payload, checksum; }
        private readonly string _path;
        private AchievementAccountProgress _state;
        public long Pending
        {
            get
            {
                long pending = 0;
                for (var number = 1; number <= PartyAchievements.Count; number++)
                    if (PartyAchievements.StatNumber(number) == number &&
                        StatValue(_state, number) > _state.confirmedValues[number - 1])
                        pending |= PartyAchievements.Bit((PartyAchievement)number);
                return pending;
            }
        }
        public int GetStatValue(int number) => StatValue(_state, PartyAchievements.StatNumber(number));
        private static int StatValue(AchievementAccountProgress state, int number)
        {
            switch ((PartyAchievement)number)
            {
                case PartyAchievement.FirstCeremony: return state.matches;
                case PartyAchievement.Champion: return state.wins;
                case PartyAchievement.FirstKey: return state.keysBought;
                case PartyAchievement.FirstItem: return state.itemsBought;
                case PartyAchievement.FirstMinigameWin: return state.bestMinigameWins;
                default: return (state.earned & PartyAchievements.Bit((PartyAchievement)number)) != 0 ? 1 : 0;
            }
        }
        public AchievementAccountProgress Snapshot => JsonUtility.FromJson<AchievementAccountProgress>(JsonUtility.ToJson(_state));

        public AchievementJournal(string path, string scope)
        {
            _path = path;
            if (!File.Exists(path)) { _state = new AchievementAccountProgress { accountScope = scope }; return; }
            var envelope = JsonUtility.FromJson<Envelope>(File.ReadAllText(path));
            if (envelope == null || string.IsNullOrEmpty(envelope.payload) || Hash(envelope.payload) != envelope.checksum)
                throw new InvalidDataException("Achievement journal checksum mismatch.");
            _state = JsonUtility.FromJson<AchievementAccountProgress>(envelope.payload);
            if (_state == null || (_state.version != 1 && _state.version != 2) || _state.accountScope != scope ||
                _state.committedMatches == null || _state.matches < 0 || _state.wins < 0 ||
                _state.keysBought < 0 || _state.itemsBought < 0 ||
                (_state.version == 1 && (_state.keysBought > 10 || _state.itemsBought > 10)) ||
                _state.stolenGifts < 0 || _state.stolenGifts > 10 || _state.wins > _state.matches ||
                _state.committedMatches.Count != _state.matches ||
                new HashSet<string>(_state.committedMatches, StringComparer.Ordinal).Count != _state.matches ||
                (_state.gamesPlayed & ~PartyAchievements.RegisteredGamesMask) != 0 ||
                (_state.earned & ~PartyAchievements.All) != 0 || (_state.confirmed & ~_state.earned) != 0)
                throw new InvalidDataException("Invalid achievement account journal.");
            if (_state.version == 1) MigrateLegacy();
            if (_state.bestMinigameWins < 0 || _state.bestMinigameWins > 20 ||
                _state.confirmedValues == null || _state.confirmedValues.Length != PartyAchievements.Count)
                throw new InvalidDataException("Invalid achievement stat values.");
            for (var number = 1; number <= PartyAchievements.Count; number++)
            {
                var confirmedValue = _state.confirmedValues[number - 1];
                if (confirmedValue < 0 || confirmedValue > StatValue(_state, number) ||
                    (PartyAchievements.StatNumber(number) != number && confirmedValue != 0))
                    throw new InvalidDataException("Invalid achievement stat acknowledgement.");
            }
        }

        private void MigrateLegacy()
        {
            var next = Snapshot;
            next.version = 2;
            next.confirmedValues = new int[PartyAchievements.Count];
            // Version 1 never stored this high score. Recover only proven lower bounds.
            next.bestMinigameWins = (next.earned & PartyAchievements.Bit(PartyAchievement.FiveMinigameWins)) != 0 ? 5 :
                (next.earned & PartyAchievements.Bit(PartyAchievement.WinStreak)) != 0 ? 3 :
                (next.earned & PartyAchievements.Bit(PartyAchievement.FirstMinigameWin)) != 0 ? 1 : 0;
            for (var number = 1; number <= PartyAchievements.Count; number++)
                if (!PartyAchievements.IsSharedStat(number) &&
                    (next.confirmed & PartyAchievements.Bit((PartyAchievement)number)) != 0)
                    next.confirmedValues[number - 1] = 1;
            // Boolean acknowledgements cannot acknowledge cumulative values: resync shared stats.
            next.confirmed = 0;
            Save(next);
        }

        public bool Commit(AchievementReceipt receipt, string expectedPlayerKey)
        {
            if (receipt == null || !receipt.IsValid || receipt.playerKey != expectedPlayerKey) return false;
            if (_state.committedMatches.Contains(receipt.matchId)) return false;
            var next = Snapshot;
            next.committedMatches.Add(receipt.matchId);
            next.matches = checked(next.matches + 1);
            if (receipt.rank == 1) next.wins = checked(next.wins + 1);
            // Preserve real counts beyond the unlock threshold, up to the SDK's Int32 limit.
            next.keysBought = AddSaturated(next.keysBought, receipt.progress.keysBought);
            next.itemsBought = AddSaturated(next.itemsBought, receipt.progress.itemsBought);
            next.stolenGifts += Math.Min(10 - next.stolenGifts, receipt.progress.stolenGifts);
            next.bestMinigameWins = Math.Max(next.bestMinigameWins, receipt.progress.minigameWins);
            next.gamesPlayed |= receipt.progress.gamesPlayed;
            next.earned |= receipt.progress.flags;
            Earn(next, PartyAchievement.FirstCeremony, true);
            Earn(next, PartyAchievement.RegularGuest, next.matches >= 5);
            Earn(next, PartyAchievement.Champion, next.wins >= 1);
            Earn(next, PartyAchievement.FiveCrowns, next.wins >= 5);
            Earn(next, PartyAchievement.ForestExplorer, receipt.mapId == "forest-graybox");
            Earn(next, PartyAchievement.MazeExplorer, receipt.mapId == "maze-graybox");
            Earn(next, PartyAchievement.FirstKey, next.keysBought >= 1);
            Earn(next, PartyAchievement.KeyRegular, next.keysBought >= 10);
            Earn(next, PartyAchievement.FourKeys, receipt.finalKeys >= 4);
            Earn(next, PartyAchievement.BonusWinner, receipt.bonusWinner);
            Earn(next, PartyAchievement.FirstItem, next.itemsBought >= 1);
            Earn(next, PartyAchievement.ItemRegular, next.itemsBought >= 10);
            Earn(next, PartyAchievement.FirstMinigameWin, next.bestMinigameWins >= 1);
            Earn(next, PartyAchievement.FiveMinigameWins, next.bestMinigameWins >= 5);
            Earn(next, PartyAchievement.AllMinigames,
                (next.gamesPlayed & PartyAchievements.RegisteredGamesMask) == PartyAchievements.RegisteredGamesMask);
            Earn(next, PartyAchievement.TenStolenGifts, next.stolenGifts >= 10);
            Save(next);
            return true;
        }
        private static int AddSaturated(int a, int b) => a + Math.Min(int.MaxValue - a, b);
        private static void Earn(AchievementAccountProgress state, PartyAchievement id, bool condition)
        { if (condition) state.earned |= PartyAchievements.Bit(id); }
        public void Confirm(int number, int sentValue)
        {
            number = PartyAchievements.StatNumber(number);
            // A callback acknowledges the value sent, never a newer receipt's progress.
            if (sentValue <= _state.confirmedValues[number - 1] || sentValue > StatValue(_state, number)) return;
            var next = Snapshot;
            next.confirmedValues[number - 1] = sentValue;
            Save(next);
        }
        private void Save(AchievementAccountProgress next)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var payload = JsonUtility.ToJson(next);
            var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Envelope { payload = payload, checksum = Hash(payload) }));
            var temp = _path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            if (File.Exists(_path)) File.Replace(temp, _path, null);
            else File.Move(temp, _path);
            _state = next;
        }
        private static string Hash(string value)
        {
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
    }

    // One request at a time; a late callback cannot acknowledge a newer request.
    // All stat writes share the recommended one-minute interval, including retries.
    public sealed class AchievementUploadQueue
    {
        private double _nextRequestAt;
        private int _cursor, _ticket, _activeNumber, _activeValue;
        private double _deadline;
        public bool TryBegin(AchievementJournal journal, double now, out int number, out int value, out int ticket)
        {
            number = value = ticket = 0;
            if (_activeNumber != 0 && now < _deadline) return false;
            _activeNumber = 0;
            if (now < _nextRequestAt) return false;
            var pending = journal.Pending;
            for (var offset = 0; offset < PartyAchievements.Count; offset++)
            {
                var index = (_cursor + offset) % PartyAchievements.Count;
                if ((pending & (1L << index)) == 0) continue;
                _nextRequestAt = now + 60d;
                _cursor = (index + 1) % PartyAchievements.Count;
                number = _activeNumber = index + 1;
                value = _activeValue = journal.GetStatValue(number);
                ticket = ++_ticket;
                _deadline = now + 20d;
                return true;
            }
            return false;
        }
        public int Complete(int ticket, bool successful, int currentValue)
        {
            if (_activeNumber == 0 || ticket != _ticket) return 0;
            var number = _activeNumber; _activeNumber = 0;
            return successful && currentValue >= _activeValue ? number : 0;
        }
    }
}
