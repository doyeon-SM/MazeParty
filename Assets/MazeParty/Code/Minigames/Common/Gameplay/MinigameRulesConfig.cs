using System;

namespace MazeParty.Gameplay.Minigames
{
    /// <summary>Immutable rules for one simulation. Never reads a process-wide active match.</summary>
    public readonly struct MinigameRulesConfig
    {
        private MinigameRulesConfig(ScheduledMinigameId id, int rounds, double seconds)
        { Id = id; Rounds = rounds; Seconds = seconds; }
        public ScheduledMinigameId Id { get; }
        public int Rounds { get; }
        public double Seconds { get; }

        public static MinigameRulesConfig FromMatch(ScheduledMinigameId id, MatchGameSettings settings)
        {
            if (!MinigameCatalog.TryGetDefinition(id, out var definition))
                throw new ArgumentOutOfRangeException(nameof(id));
            return new MinigameRulesConfig(id,
                settings?.Get(id).Rounds ?? definition.AuthoredRoundCount,
                settings != null && definition.HasRoundTimeLimit
                    ? settings.Get(id).Seconds : definition.AuthoredPhaseDurationSeconds);
        }

        public static MinigameRulesConfig Resolve(ScheduledMinigameId id, MinigameRulesConfig? rules)
        {
            if (!rules.HasValue) return FromMatch(id, null);
            if (rules.Value.Id != id || rules.Value.Rounds < 1 || rules.Value.Seconds <= 0d)
                throw new ArgumentException("Rules must belong to this minigame.", nameof(rules));
            return rules.Value;
        }
    }
}
