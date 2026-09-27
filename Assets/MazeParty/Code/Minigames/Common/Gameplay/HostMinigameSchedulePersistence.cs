using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace MazeParty.Gameplay.Minigames
{
    /// <summary>
    /// Raw persistence boundary for a host-only schedule document. Implementations
    /// must key payloads by the stable match key, never by the current turn.
    /// </summary>
    public interface IMinigameSchedulePayloadStore
    {
        bool TryRead(string matchKey, out string payload);
        void Write(string matchKey, string payload);
        void Delete(string matchKey);
    }

    /// <summary>
    /// Serializes the complete host schedule, including unrevealed future turns.
    /// Encoded payloads are private host state and must not be sent to clients.
    /// </summary>
    public interface IHostMinigameScheduleCodec
    {
        string Encode(string matchKey, HostMinigameSchedule schedule);

        bool TryDecode(
            string payload,
            out string matchKey,
            out HostMinigameSchedule schedule);
    }

    /// <summary>
    /// Optional codec capability that reports whether a successfully decoded
    /// document should be rewritten in the current format. Keeping this
    /// separate preserves compatibility with custom codecs that implement the
    /// original persistence contract.
    /// </summary>
    public interface IMigratingHostMinigameScheduleCodec :
        IHostMinigameScheduleCodec
    {
        bool TryDecode(
            string payload,
            out string matchKey,
            out HostMinigameSchedule schedule,
            out bool requiresMigration);
    }

    public sealed class HostMinigameScheduleJsonCodec :
        IMigratingHostMinigameScheduleCodec
    {
        private const int CurrentFormatVersion = 1;
        private const int FirstLegacySchemaVersion = 1;
        private const int LastLegacySchemaVersion = 14;
        private const int FirstLegacyCatalogEntryCount = 2;

        public string Encode(
            string matchKey,
            HostMinigameSchedule schedule)
        {
            MatchKeyValidator.Validate(matchKey);
            if (schedule == null)
            {
                throw new ArgumentNullException(nameof(schedule));
            }

            var scheduleEntries = schedule.CopyEntriesForHostPersistence();
            var serializedEntries = new int[scheduleEntries.Length];
            for (var index = 0; index < scheduleEntries.Length; index++)
            {
                serializedEntries[index] = (int)scheduleEntries[index];
            }

            var catalogEntryCount = schedule.RegisteredGameCountAtCreation;
            var document = new ScheduleDocument
            {
                formatVersion = CurrentFormatVersion,
                catalogEntryCount = catalogEntryCount,
                catalogFingerprint =
                    MinigameCatalog.GetRecoveryFingerprint(catalogEntryCount),
                matchKey = matchKey,
                seed = schedule.Seed,
                turnCount = schedule.TurnCount,
                entries = serializedEntries
            };

            return JsonUtility.ToJson(document);
        }

        public bool TryDecode(
            string payload,
            out string matchKey,
            out HostMinigameSchedule schedule)
        {
            return TryDecode(
                payload,
                out matchKey,
                out schedule,
                out _);
        }

        public bool TryDecode(
            string payload,
            out string matchKey,
            out HostMinigameSchedule schedule,
            out bool requiresMigration)
        {
            matchKey = null;
            schedule = null;
            requiresMigration = false;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            try
            {
                var header = JsonUtility.FromJson<ScheduleHeader>(payload);
                if (header == null)
                {
                    return false;
                }

                if (header.formatVersion == CurrentFormatVersion)
                {
                    return TryDecodeCurrent(
                        payload,
                        out matchKey,
                        out schedule);
                }

                if (header.formatVersion == 0 &&
                    header.schemaVersion >= FirstLegacySchemaVersion &&
                    header.schemaVersion <= LastLegacySchemaVersion)
                {
                    var decoded = TryDecodeLegacy(
                        payload,
                        header.schemaVersion,
                        out matchKey,
                        out schedule);
                    requiresMigration = decoded;
                    return decoded;
                }

                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool TryDecodeCurrent(
            string payload,
            out string matchKey,
            out HostMinigameSchedule schedule)
        {
            matchKey = null;
            schedule = null;
            var document = JsonUtility.FromJson<ScheduleDocument>(payload);
            if (document == null ||
                document.formatVersion != CurrentFormatVersion ||
                !MinigameCatalog.IsRecoveryFingerprintCompatible(
                    document.catalogEntryCount,
                    document.catalogFingerprint))
            {
                return false;
            }

            return TryRestore(
                document.matchKey,
                document.seed,
                document.turnCount,
                document.entries,
                document.catalogEntryCount,
                out matchKey,
                out schedule);
        }

        private static bool TryDecodeLegacy(
            string payload,
            int schemaVersion,
            out string matchKey,
            out HostMinigameSchedule schedule)
        {
            matchKey = null;
            schedule = null;
            var document = JsonUtility.FromJson<LegacyScheduleDocument>(payload);
            if (document == null || document.schemaVersion != schemaVersion)
            {
                return false;
            }

            var catalogEntryCount =
                FirstLegacyCatalogEntryCount +
                schemaVersion - FirstLegacySchemaVersion;
            return TryRestore(
                document.matchKey,
                document.seed,
                document.turnCount,
                document.entries,
                catalogEntryCount,
                out matchKey,
                out schedule);
        }

        private static bool TryRestore(
            string storedMatchKey,
            int seed,
            int turnCount,
            int[] serializedEntries,
            int catalogEntryCount,
            out string matchKey,
            out HostMinigameSchedule schedule)
        {
            matchKey = null;
            schedule = null;
            if (string.IsNullOrWhiteSpace(storedMatchKey) ||
                serializedEntries == null ||
                turnCount != serializedEntries.Length)
            {
                return false;
            }

            MatchKeyValidator.Validate(storedMatchKey);
            var entries =
                new ScheduledMinigameId[serializedEntries.Length];
            for (var index = 0; index < serializedEntries.Length; index++)
            {
                var rawEntry = serializedEntries[index];
                if (rawEntry < byte.MinValue || rawEntry > byte.MaxValue)
                {
                    return false;
                }

                entries[index] = (ScheduledMinigameId)rawEntry;
            }

            schedule = HostMinigameSchedule.Restore(
                seed,
                entries,
                catalogEntryCount);
            matchKey = storedMatchKey;
            return true;
        }

        [Serializable]
        private sealed class ScheduleHeader
        {
            public int formatVersion;
            public int schemaVersion;
        }

        [Serializable]
        private sealed class ScheduleDocument
        {
            public int formatVersion;
            public int catalogEntryCount;
            public string catalogFingerprint;
            public string matchKey;
            public int seed;
            public int turnCount;
            public int[] entries;
        }

        [Serializable]
        private sealed class LegacyScheduleDocument
        {
            public int schemaVersion;
            public string matchKey;
            public int seed;
            public int turnCount;
            public int[] entries;
        }
    }

    /// <summary>
    /// Stores each match in a traversal-safe SHA-256 file beneath a dedicated
    /// directory. The default directory is inside Application.persistentDataPath.
    /// </summary>
    public sealed class FileMinigameSchedulePayloadStore :
        IMinigameSchedulePayloadStore
    {
        private const string ProductDirectoryName = "MazeParty";
        private const string HostStateDirectoryName = "HostMatchState";
        private const string ScheduleDirectoryName = "MinigameSchedules";
        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);

        private readonly string _directoryPath;

        public FileMinigameSchedulePayloadStore()
            : this(
                Path.Combine(
                    Application.persistentDataPath,
                    ProductDirectoryName,
                    HostStateDirectoryName,
                    ScheduleDirectoryName))
        {
        }

        /// <summary>
        /// Injectable directory constructor for isolated tests and tools. Runtime
        /// hosts should use the parameterless constructor.
        /// </summary>
        public FileMinigameSchedulePayloadStore(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException(
                    "A schedule storage directory is required.",
                    nameof(directoryPath));
            }

            _directoryPath = Path.GetFullPath(directoryPath);
        }

        public string DirectoryPath => _directoryPath;

        public bool TryRead(string matchKey, out string payload)
        {
            var path = GetPayloadPath(matchKey);
            if (!File.Exists(path))
            {
                payload = null;
                return false;
            }

            payload = File.ReadAllText(path, Utf8WithoutBom);
            return true;
        }

        public void Write(string matchKey, string payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            var path = GetPayloadPath(matchKey);
            Directory.CreateDirectory(_directoryPath);
            var temporaryPath =
                path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                File.WriteAllText(temporaryPath, payload, Utf8WithoutBom);
                if (!File.Exists(path))
                {
                    File.Move(temporaryPath, path);
                    return;
                }

                try
                {
                    File.Replace(temporaryPath, path, null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(temporaryPath, path, true);
                }
                catch (IOException)
                {
                    File.Copy(temporaryPath, path, true);
                }
            }
            finally
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }

        public void Delete(string matchKey)
        {
            var path = GetPayloadPath(matchKey);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private string GetPayloadPath(string matchKey)
        {
            MatchKeyValidator.Validate(matchKey);
            byte[] hash;
            using (var sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(matchKey));
            }

            var name = new StringBuilder(hash.Length * 2);
            for (var index = 0; index < hash.Length; index++)
            {
                name.Append(hash[index].ToString("x2"));
            }

            return Path.Combine(_directoryPath, name + ".json");
        }

        private static void TryDeleteTemporaryFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // The final payload already succeeded or raised its own error.
            }
            catch (UnauthorizedAccessException)
            {
                // Do not hide the result of the primary persistence operation.
            }
        }
    }

    /// <summary>
    /// Loads or creates exactly one immutable schedule for a stable match key.
    /// Reusing the key restores the same schedule across retries, reconnects and
    /// host process restarts. A new match must supply a new key.
    /// </summary>
    public sealed class HostMinigameScheduleRepository
    {
        private readonly IMinigameSchedulePayloadStore _payloadStore;
        private readonly IHostMinigameScheduleCodec _codec;

        public HostMinigameScheduleRepository(
            IMinigameSchedulePayloadStore payloadStore,
            IHostMinigameScheduleCodec codec)
        {
            _payloadStore = payloadStore ??
                throw new ArgumentNullException(nameof(payloadStore));
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        }

        public static HostMinigameScheduleRepository CreateDefault()
        {
            return new HostMinigameScheduleRepository(
                new FileMinigameSchedulePayloadStore(),
                new HostMinigameScheduleJsonCodec());
        }

        public HostMinigameSchedule LoadOrCreate(
            string matchKey,
            Func<int> seedFactory,
            int totalTurns = MinigameScheduleRules.DefaultTurnCount)
        {
            MatchKeyValidator.Validate(matchKey);
            if (seedFactory == null)
            {
                throw new ArgumentNullException(nameof(seedFactory));
            }

            if (TryLoad(matchKey, out var restored))
            {
                return restored;
            }

            var created = HostMinigameSchedule.Create(
                seedFactory(),
                totalTurns);
            Save(matchKey, created);
            return created;
        }

        public bool TryLoad(
            string matchKey,
            out HostMinigameSchedule schedule)
        {
            MatchKeyValidator.Validate(matchKey);
            if (!_payloadStore.TryRead(matchKey, out var payload))
            {
                schedule = null;
                return false;
            }

            if (!TryDecodePayload(
                    payload,
                    out var storedMatchKey,
                    out var restored,
                    out var requiresMigration))
            {
                throw new InvalidDataException(
                    "The persisted minigame schedule is invalid.");
            }

            if (!string.Equals(
                    matchKey,
                    storedMatchKey,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The persisted minigame schedule belongs to another match.");
            }

            if (requiresMigration)
            {
                TryRewriteMigratedPayload(matchKey, restored);
            }

            schedule = restored;
            return true;
        }

        public void Save(
            string matchKey,
            HostMinigameSchedule schedule)
        {
            MatchKeyValidator.Validate(matchKey);
            if (schedule == null)
            {
                throw new ArgumentNullException(nameof(schedule));
            }

            if (_payloadStore.TryRead(matchKey, out var existingPayload))
            {
                if (!TryDecodePayload(
                        existingPayload,
                        out var storedMatchKey,
                        out var existingSchedule,
                        out var requiresMigration) ||
                    !string.Equals(
                        matchKey,
                        storedMatchKey,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "The existing minigame schedule is invalid.");
                }

                if (!SchedulesMatch(existingSchedule, schedule))
                {
                    throw new InvalidOperationException(
                        "A different schedule is already stored for this match.");
                }

                if (requiresMigration)
                {
                    _payloadStore.Write(
                        matchKey,
                        _codec.Encode(matchKey, existingSchedule));
                }

                return;
            }

            _payloadStore.Write(
                matchKey,
                _codec.Encode(matchKey, schedule));
        }

        public void Delete(string matchKey)
        {
            MatchKeyValidator.Validate(matchKey);
            _payloadStore.Delete(matchKey);
        }

        private static bool SchedulesMatch(
            HostMinigameSchedule left,
            HostMinigameSchedule right)
        {
            if (left.Seed != right.Seed ||
                left.TurnCount != right.TurnCount ||
                left.RegisteredGameCountAtCreation !=
                right.RegisteredGameCountAtCreation)
            {
                return false;
            }

            for (var turn = 1; turn <= left.TurnCount; turn++)
            {
                if (left.GetMinigameForTurn(turn) !=
                    right.GetMinigameForTurn(turn))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryDecodePayload(
            string payload,
            out string matchKey,
            out HostMinigameSchedule schedule,
            out bool requiresMigration)
        {
            if (_codec is IMigratingHostMinigameScheduleCodec migratingCodec)
            {
                return migratingCodec.TryDecode(
                    payload,
                    out matchKey,
                    out schedule,
                    out requiresMigration);
            }

            requiresMigration = false;
            return _codec.TryDecode(payload, out matchKey, out schedule);
        }

        private void TryRewriteMigratedPayload(
            string matchKey,
            HostMinigameSchedule schedule)
        {
            try
            {
                _payloadStore.Write(matchKey, _codec.Encode(matchKey, schedule));
            }
            catch (IOException)
            {
                // Migration is opportunistic; a readable legacy schedule must
                // remain recoverable when the storage device is temporarily
                // unavailable or read-only.
            }
            catch (UnauthorizedAccessException)
            {
                // Preserve the successfully decoded schedule for this session.
            }
        }
    }

    internal static class MatchKeyValidator
    {
        private const int MaximumLength = 512;

        public static void Validate(string matchKey)
        {
            if (string.IsNullOrWhiteSpace(matchKey))
            {
                throw new ArgumentException(
                    "A stable match key is required.",
                    nameof(matchKey));
            }

            if (matchKey.Length > MaximumLength)
            {
                throw new ArgumentException(
                    "The match key is too long.",
                    nameof(matchKey));
            }
        }
    }
}
