using System;
using System.Collections.Generic;
using System.Globalization;

namespace MazeParty.Gameplay.Minigames
{
    public readonly struct MinigameSetting
    {
        public MinigameSetting(bool included, int rounds, int seconds)
        { Included = included; Rounds = rounds; Seconds = seconds; }
        public bool Included { get; }
        public int Rounds { get; }
        // Zero denotes a game with its own phase timers, not a configurable round limit.
        public int Seconds { get; }
    }

    /// <summary>Immutable lobby contract. Compact encoding is shared by MPS, NGO and recovery.</summary>
    public sealed class MatchGameSettings
    {
        public const int GameCount = 20;
        private static readonly int[] DefaultRounds = { 3,2,3,3,3,2,1,4,3,5,2,1,3,1,3,2,5,3,5,1 };
        // Actual round limits, not catalog phase/fuse metadata. Minefield's authored 40s
        // becomes 45s on the 15s grid; its upper bound remains <= authored limit + 60.
        private static readonly int[] BaseSeconds = { 40,60,60,60,30,60,60,60,60,0,60,0,60,60,60,60,0,120,0,120 };
        private readonly MinigameSetting[] _games;
        public int Turns { get; }
        public int IncludedCount { get; }
        private MatchGameSettings(int turns, MinigameSetting[] games)
        {
            Turns = turns; _games = (MinigameSetting[])games.Clone();
            foreach (var game in _games) if (game.Included) IncludedCount++;
        }
        public static MatchGameSettings Default
        {
            get
            {
                var games = new MinigameSetting[GameCount];
                for (var i = 0; i < GameCount; i++)
                    games[i] = new MinigameSetting(true, DefaultRounds[i],
                        BaseSeconds[i] == 0 ? 0 : Math.Max(30, ((BaseSeconds[i] + 7) / 15) * 15));
                return new MatchGameSettings(15, games);
            }
        }
        public MinigameSetting Get(ScheduledMinigameId id)
        {
            var index = (int)id - 1;
            if (index < 0 || index >= GameCount) throw new ArgumentOutOfRangeException(nameof(id));
            return _games[index];
        }
        public static bool HasTimeLimit(ScheduledMinigameId id) =>
            (int)id >= 1 && (int)id <= GameCount && BaseSeconds[(int)id - 1] > 0;
        public static int MaximumSeconds(ScheduledMinigameId id) =>
            HasTimeLimit(id) ? ((BaseSeconds[(int)id - 1] + 60) / 15) * 15 : 0;
        public bool TryChangeTurns(int turns, out MatchGameSettings updated)
        {
            updated = this;
            if ((turns != 10 && turns != 15 && turns != 20) || IncludedCount < turns) return false;
            updated = new MatchGameSettings(turns, _games); return true;
        }
        public bool TryChangeGame(ScheduledMinigameId id, MinigameSetting value, out MatchGameSettings updated)
        {
            updated = this;
            if (!ValidGame(id, value)) return false;
            var old = Get(id);
            if (IncludedCount + (value.Included ? 1 : 0) - (old.Included ? 1 : 0) < Turns) return false;
            var games = (MinigameSetting[])_games.Clone(); games[(int)id - 1] = value;
            updated = new MatchGameSettings(Turns, games); return true;
        }
        private static bool ValidGame(ScheduledMinigameId id, MinigameSetting value) =>
            (int)id >= 1 && (int)id <= GameCount && value.Rounds >= 1 && value.Rounds <= 5 &&
            (HasTimeLimit(id) ? value.Seconds >= 30 && value.Seconds % 15 == 0 &&
                value.Seconds <= MaximumSeconds(id) : value.Seconds == 0);
        public IReadOnlyList<ScheduledMinigameId> IncludedGames()
        {
            var list = new List<ScheduledMinigameId>();
            for (var i = 0; i < GameCount; i++) if (_games[i].Included) list.Add((ScheduledMinigameId)(i + 1));
            return list.AsReadOnly();
        }
        public string Encode()
        {
            var bytes = new byte[GameCount * 3];
            for (var i = 0; i < GameCount; i++)
            { bytes[i * 3] = (byte)(_games[i].Included ? 1 : 0);
              bytes[i * 3 + 1] = (byte)_games[i].Rounds; bytes[i * 3 + 2] = (byte)(_games[i].Seconds / 15); }
            return "1:" + Turns.ToString(CultureInfo.InvariantCulture) + ":" + Convert.ToBase64String(bytes);
        }
        public static bool TryDecode(string text, out MatchGameSettings settings)
        {
            settings = null;
            if (string.IsNullOrEmpty(text) || text.Length > 128) return false;
            var parts = text.Split(':');
            if (parts.Length != 3 || parts[0] != "1" || !int.TryParse(parts[1], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var turns) || (turns != 10 && turns != 15 && turns != 20)) return false;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(parts[2]); } catch (FormatException) { return false; }
            if (bytes.Length != GameCount * 3) return false;
            var games = new MinigameSetting[GameCount];
            for (var i = 0; i < GameCount; i++)
            {
                if (bytes[i * 3] > 1) return false;
                games[i] = new MinigameSetting(bytes[i * 3] == 1, bytes[i * 3 + 1], bytes[i * 3 + 2] * 15);
                if (!ValidGame((ScheduledMinigameId)(i + 1), games[i])) return false;
            }
            var candidate = new MatchGameSettings(turns, games);
            if (candidate.IncludedCount < turns || candidate.Encode() != text) return false;
            settings = candidate; return true;
        }
    }

    /// <summary>
    /// One immutable rules snapshot for this Unity process's active match. Owned by
    /// NetworkMatchState; cleared on despawn. Pure simulations and Solo retain authored
    /// rules when no online match owns this context. Never populated from lobby drafts.
    /// </summary>
    public static class MinigameRuntimeSettings
    {
        private static object _owner;
        public static MatchGameSettings Current { get; private set; }
        public static void Bind(object owner, MatchGameSettings settings)
        {
            if (owner == null || settings == null) throw new ArgumentNullException();
            if (_owner != null && !ReferenceEquals(owner, _owner))
                throw new InvalidOperationException("Another match owns the minigame rules.");
            _owner = owner; Current = settings;
        }
        public static void Release(object owner)
        { if (ReferenceEquals(owner, _owner)) { _owner = null; Current = null; } }
        public static int Rounds(ScheduledMinigameId id, int authored) => Current?.Get(id).Rounds ?? authored;
        public static double Seconds(ScheduledMinigameId id, double authored) =>
            Current != null && MatchGameSettings.HasTimeLimit(id) ? Current.Get(id).Seconds : authored;
    }
}
