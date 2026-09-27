using System;
using MazeParty.Gameplay.Minigames;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Owns the host's active logical match id. It deliberately survives a
    /// process restart, a crash or a network failure so the same players can
    /// restart with the same minigame order. It is discarded when a match
    /// starts with different players (see <see cref="MinigameScheduleRoster"/>),
    /// when the 15-turn match completes, or when the host explicitly leaves.
    /// </summary>
    internal sealed class HostMinigameScheduleSession
    {
        private const string ActiveMatchKeyPrefix =
            "MazeParty.Host.ActiveMinigameSchedule.";
        private const string RosterKeySuffix = ".Roster";

        private readonly HostMinigameScheduleRepository _repository;
        private readonly string _preferenceKey;
        private readonly string _rosterPreferenceKey;

        public HostMinigameScheduleSession(
            HostMinigameScheduleRepository repository = null)
        {
            _repository = repository ??
                          HostMinigameScheduleRepository.CreateDefault();
            _preferenceKey =
                ActiveMatchKeyPrefix + Hash128.Compute(ResolveAuthProfile());
            _rosterPreferenceKey = _preferenceKey + RosterKeySuffix;
        }

        public string ActiveMatchKey { get; private set; }

        /// <param name="rosterKey">
        /// <see cref="MinigameScheduleRoster.CreateKey"/> of the players in
        /// this match. A saved schedule for other players is discarded.
        /// </param>
        public HostMinigameSchedule LoadOrCreateActive(
            int totalTurns,
            string rosterKey)
        {
            rosterKey ??= string.Empty;
            var matchKey =
                PlayerPrefs.GetString(_preferenceKey, string.Empty).Trim();
            if (matchKey.Length > 0 &&
                !MinigameScheduleRoster.CanReuse(
                    PlayerPrefs.GetString(_rosterPreferenceKey, string.Empty),
                    rosterKey))
            {
                // Saved for other players: a new game gets a new order. The
                // old file is only orphaned if it cannot be deleted.
                TryDeleteSavedSchedule(matchKey);
                matchKey = string.Empty;
            }

            if (matchKey.Length == 0)
            {
                matchKey =
                    "local-" + Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(_preferenceKey, matchKey);
                PlayerPrefs.SetString(_rosterPreferenceKey, rosterKey);
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
            if (PlayerPrefs.HasKey(_preferenceKey) ||
                PlayerPrefs.HasKey(_rosterPreferenceKey))
            {
                PlayerPrefs.DeleteKey(_preferenceKey);
                PlayerPrefs.DeleteKey(_rosterPreferenceKey);
                PlayerPrefs.Save();
            }
        }

        private void TryDeleteSavedSchedule(string matchKey)
        {
            try
            {
                _repository.Delete(matchKey);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not delete the previous minigame schedule: " +
                    exception.Message);
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