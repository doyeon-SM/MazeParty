using System;

namespace MazeParty.Multiplayer
{
    public interface ISteamAchievementApi
    {
        bool GetStat(string name, out int value);
        bool GetAchievement(string name, out bool achieved);
        bool SetStat(string name, int value);
        bool SetAchievement(string name);
        bool StoreStats();
    }

    // One StoreStats batch at a time. Callbacks have no request ID, so a timeout cannot
    // safely start another batch. Keep the durable outbox and retry on the next launch.
    public sealed class SteamAchievementSync
    {
        private readonly AchievementJournal _journal;
        private readonly ISteamAchievementApi _api;
        private int[] _sent;
        private double _nextAttempt, _deadline;
        private bool _initial = true, _uncertain;
        public bool HasPendingStore => _sent != null;
        public bool RequiresRestart => _uncertain;

        public SteamAchievementSync(AchievementJournal journal, ISteamAchievementApi api)
        { _journal = journal; _api = api; }

        public bool Tick(double now)
        {
            if (_uncertain) return false;
            if (_sent != null)
            {
                if (now >= _deadline) _uncertain = true;
                return !_uncertain;
            }
            if (now < _nextAttempt || (!_initial && _journal.Pending == 0)) return true;
            _nextAttempt = now + 60d;
            var local = new int[PartyAchievements.Count];
            var merged = new int[PartyAchievements.Count];
            var unlocked = new bool[PartyAchievements.Count];
            // Read the whole published schema before changing the Steam cache.
            for (var number = 1; number <= PartyAchievements.Count; number++)
            {
                if (PartyAchievements.StatNumber(number) == number)
                {
                    if (!_api.GetStat(PartyAchievements.StatId(number), out var remote) || remote < 0) return false;
                    local[number - 1] = _journal.GetStatValue(number);
                    merged[number - 1] = Math.Max(remote, local[number - 1]);
                }
                if (!_api.GetAchievement(PartyAchievements.ApiName(number), out unlocked[number - 1])) return false;
            }
            for (var number = 1; number <= PartyAchievements.Count; number++)
            {
                var stat = PartyAchievements.StatNumber(number);
                if (stat == number && !_api.SetStat(PartyAchievements.StatId(number), merged[number - 1])) return false;
                if (!unlocked[number - 1] && merged[stat - 1] >= PartyAchievements.Target(number) &&
                    !_api.SetAchievement(PartyAchievements.ApiName(number))) return false;
            }
            _sent = local;
            _deadline = now + 30d;
            if (_api.StoreStats()) return true;
            _sent = null;
            return false;
        }

        public void Complete(bool successful)
        {
            if (_sent == null || _uncertain) return;
            var sent = _sent;
            _sent = null;
            if (!successful) return;
            // A receipt can arrive while the SDK request is in flight. Only acknowledge
            // the local values captured by that request, never the newer journal state.
            for (var number = 1; number <= PartyAchievements.Count; number++)
                if (PartyAchievements.StatNumber(number) == number) _journal.Confirm(number, sent[number - 1]);
            _initial = false;
        }
    }
}
