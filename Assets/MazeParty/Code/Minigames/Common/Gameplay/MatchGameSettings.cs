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
        private readonly MinigameSetting[] _games;
        public int Turns { get; }
        public int IncludedCount { get; }
        private MatchGameSettings(int turns, MinigameSetting[] games)
        {
            Turns = turns; _games = (MinigameSetting[])games.Clone();
            foreach (var game in _games) if (game.Included) IncludedCount++;
        }
        public static MatchGameSettings Default { get; } = CreateDefault();
        private static MatchGameSettings CreateDefault()
        {
            var games = new MinigameSetting[GameCount];
            foreach (var definition in MinigameCatalog.RegisteredMinigames)
                games[(int)definition.Id - 1] = definition.DefaultOnlineSetting;
            return new MatchGameSettings(15, games);
        }
        public MinigameSetting Get(ScheduledMinigameId id)
        {
            var index = (int)id - 1;
            if (index < 0 || index >= GameCount) throw new ArgumentOutOfRangeException(nameof(id));
            return _games[index];
        }
        public static bool HasTimeLimit(ScheduledMinigameId id) =>
            MinigameCatalog.TryGetDefinition(id, out var definition) && definition.HasRoundTimeLimit;
        public static int MaximumSeconds(ScheduledMinigameId id) =>
            MinigameCatalog.TryGetDefinition(id, out var definition) ? definition.MaximumOnlineSeconds : 0;
        private static bool IsSupportedTurnCount(int turns)
        {
            if (turns == 10 || turns == 15 || turns == 20) return true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return turns == 5;
#else
            return false;
#endif
        }
        public bool TryChangeTurns(int turns, out MatchGameSettings updated)
        {
            updated = this;
            if (!IsSupportedTurnCount(turns) || IncludedCount < turns) return false;
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
                    CultureInfo.InvariantCulture, out var turns) || !IsSupportedTurnCount(turns)) return false;
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

}
