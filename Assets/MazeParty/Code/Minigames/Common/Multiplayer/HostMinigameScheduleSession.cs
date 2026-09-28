using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Owns the host's active logical match id. It deliberately survives a
    /// process restart, a crash or a network failure so the same players can
    /// restart with the same minigame order. It is discarded when a match
    /// starts with different players (see <see cref="MinigameScheduleRoster"/>),
    /// after the completed match has returned to the lobby, or when the host
    /// explicitly discards or leaves it.
    /// </summary>
    internal sealed class HostMinigameScheduleSession
    {
        private const string ActiveMatchKeyPrefix =
            "MazeParty.Host.ActiveMinigameSchedule.";
        private const string RosterKeySuffix = ".Roster";

        private readonly HostMinigameScheduleRepository _repository;
        private readonly MatchRecoveryJournal _recoveryJournal;
        private readonly MatchRecoverySnapshotCodec _recoveryCodec;
        private readonly string _preferenceKey;
        private readonly string _rosterPreferenceKey;

        public HostMinigameScheduleSession(
            HostMinigameScheduleRepository repository = null,
            MatchRecoveryJournal recoveryJournal = null,
            MatchRecoverySnapshotCodec recoveryCodec = null)
        {
            _repository = repository ??
                          HostMinigameScheduleRepository.CreateDefault();
            _recoveryJournal = recoveryJournal ?? new MatchRecoveryJournal();
            _recoveryCodec = recoveryCodec ?? new MatchRecoverySnapshotCodec();
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
                TryDeleteSavedMatch(matchKey);
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

        public bool TryGetSavedMatch(
            out string matchKey,
            out string rosterKey)
        {
            matchKey = PlayerPrefs.GetString(
                _preferenceKey,
                string.Empty).Trim();
            rosterKey = PlayerPrefs.GetString(
                _rosterPreferenceKey,
                string.Empty).Trim();
            return matchKey.Length > 0 && rosterKey.Length > 0;
        }

        public MatchRecoveryLoadStatus TryPeekRecovery(
            out MatchRecoveryJournalRecord record)
        {
            if (!TryGetSavedMatch(out var matchKey, out _))
            {
                record = default;
                return MatchRecoveryLoadStatus.None;
            }

            return _recoveryJournal.TryPeekLatest(
                matchKey,
                DateTime.UtcNow,
                out record);
        }

        public MatchRecoveryLoadStatus TryPeekRecoverySnapshot(
            out MatchRecoverySnapshot snapshot)
        {
            snapshot = null;
            var status = TryPeekRecovery(out var record);
            if (status != MatchRecoveryLoadStatus.Loaded)
            {
                return status;
            }

            return _recoveryCodec.TryDecode(record.Payload, out snapshot)
                ? MatchRecoveryLoadStatus.Loaded
                : MatchRecoveryLoadStatus.Corrupt;
        }

        public MatchRecoveryLoadStatus TryLoadRecovery(
            string rosterFingerprint,
            IReadOnlyList<string> compatibleContentFingerprints,
            out MatchRecoverySnapshot snapshot)
        {
            snapshot = null;
            var matchKey = ResolveActiveMatchKey();
            if (matchKey.Length == 0)
            {
                return MatchRecoveryLoadStatus.None;
            }

            var status = _recoveryJournal.TryLoadLatest(
                matchKey,
                DateTime.UtcNow,
                rosterFingerprint,
                null,
                out var record);
            if (status != MatchRecoveryLoadStatus.Loaded)
            {
                return status;
            }

            var contentMatches = false;
            if (compatibleContentFingerprints != null)
            {
                for (var index = 0;
                     index < compatibleContentFingerprints.Count;
                     index++)
                {
                    if (string.Equals(
                            record.ContentFingerprint,
                            compatibleContentFingerprints[index],
                            StringComparison.Ordinal))
                    {
                        contentMatches = true;
                        break;
                    }
                }
            }

            if (!contentMatches)
            {
                return MatchRecoveryLoadStatus.ContentMismatch;
            }

            return _recoveryCodec.TryDecode(record.Payload, out snapshot)
                ? MatchRecoveryLoadStatus.Loaded
                : MatchRecoveryLoadStatus.Corrupt;
        }

        public MatchRecoveryJournalRecord SaveRecovery(
            string rosterFingerprint,
            string contentFingerprint,
            MatchRecoverySnapshot snapshot)
        {
            var matchKey = ResolveActiveMatchKey();
            if (matchKey.Length == 0)
            {
                throw new InvalidOperationException(
                    "An active host match is required before saving recovery state.");
            }

            return _recoveryJournal.Save(
                matchKey,
                rosterFingerprint,
                contentFingerprint,
                _recoveryCodec.Encode(snapshot),
                DateTime.UtcNow);
        }

        public void DeleteRecovery()
        {
            var matchKey = ResolveActiveMatchKey();
            if (matchKey.Length > 0)
            {
                _recoveryJournal.Delete(matchKey);
            }
        }

        public void CompleteActive()
        {
            ActiveMatchKey = ResolveActiveMatchKey();

            if (!string.IsNullOrWhiteSpace(ActiveMatchKey))
            {
                TryDeleteSavedMatch(ActiveMatchKey);
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

        private string ResolveActiveMatchKey()
        {
            if (string.IsNullOrWhiteSpace(ActiveMatchKey))
            {
                ActiveMatchKey = PlayerPrefs.GetString(
                    _preferenceKey,
                    string.Empty).Trim();
            }

            return ActiveMatchKey ?? string.Empty;
        }

        private void TryDeleteSavedMatch(string matchKey)
        {
            try
            {
                _recoveryJournal.Delete(matchKey);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not delete the previous match recovery journal: " +
                    exception.Message);
            }

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
