using System;
using MazeParty.Gameplay.Minigames;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Owns the host's active logical match id. It deliberately survives a
    /// process restart and is cleared only when the 15-turn match completes.
    /// </summary>
    internal sealed class HostMinigameScheduleSession
    {
        private const string ActiveMatchKeyPrefix =
            "MazeParty.Host.ActiveMinigameSchedule.";

        private readonly HostMinigameScheduleRepository _repository;
        private readonly string _preferenceKey;

        public HostMinigameScheduleSession(
            HostMinigameScheduleRepository repository = null)
        {
            _repository = repository ??
                          HostMinigameScheduleRepository.CreateDefault();
            _preferenceKey =
                ActiveMatchKeyPrefix + Hash128.Compute(ResolveAuthProfile());
        }

        public string ActiveMatchKey { get; private set; }

        public HostMinigameSchedule LoadOrCreateActive(int totalTurns)
        {
            var matchKey =
                PlayerPrefs.GetString(_preferenceKey, string.Empty).Trim();
            if (matchKey.Length == 0)
            {
                matchKey =
                    "local-" + Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(_preferenceKey, matchKey);
                PlayerPrefs.Save();
            }

            var schedule = _repository.LoadOrCreate(
                matchKey,
                CreateSeed,
                totalTurns);
            if (schedule.TurnCount != totalTurns)
            {
                throw new InvalidOperationException(
                    "The persisted minigame schedule turn count does not " +
                    "match the current game rules.");
            }

            ActiveMatchKey = matchKey;
            return schedule;
        }

        public void CompleteActive()
        {
            if (string.IsNullOrWhiteSpace(ActiveMatchKey))
            {
                ActiveMatchKey =
                    PlayerPrefs.GetString(_preferenceKey, string.Empty).Trim();
            }

            if (!string.IsNullOrWhiteSpace(ActiveMatchKey))
            {
                _repository.Delete(ActiveMatchKey);
            }

            ActiveMatchKey = string.Empty;
            if (PlayerPrefs.HasKey(_preferenceKey))
            {
                PlayerPrefs.DeleteKey(_preferenceKey);
                PlayerPrefs.Save();
            }
        }

        private static int CreateSeed()
        {
            return unchecked(
                (int)(DateTime.UtcNow.Ticks ^ Environment.TickCount));
        }

        private static string ResolveAuthProfile()
        {
            var profile = "default";
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (!string.Equals(
                        arguments[index],
                        "-auth-profile",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidate = arguments[index + 1].Trim();
                if (candidate.Length > 0)
                {
                    profile = candidate;
                }

                break;
            }

            return profile;
        }
    }
}