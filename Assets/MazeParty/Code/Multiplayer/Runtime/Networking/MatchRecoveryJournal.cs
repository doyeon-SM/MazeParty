using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum MatchRecoveryLoadStatus
    {
        None,
        Loaded,
        Expired,
        Corrupt,
        RosterMismatch,
        ContentMismatch
    }

    public enum MatchRecoveryJournalSlot
    {
        A,
        B
    }

    public readonly struct MatchRecoveryJournalRecord
    {
        internal MatchRecoveryJournalRecord(
            MatchRecoveryJournalSlot slot,
            long revision,
            DateTime savedUtc,
            DateTime expiresUtc,
            string matchKey,
            string rosterFingerprint,
            string contentFingerprint,
            string payload)
        {
            Slot = slot;
            Revision = revision;
            SavedUtc = savedUtc;
            ExpiresUtc = expiresUtc;
            MatchKey = matchKey;
            RosterFingerprint = rosterFingerprint;
            ContentFingerprint = contentFingerprint;
            Payload = payload;
        }

        public MatchRecoveryJournalSlot Slot { get; }
        public long Revision { get; }
        public DateTime SavedUtc { get; }
        public DateTime ExpiresUtc { get; }
        public string MatchKey { get; }
        public string RosterFingerprint { get; }
        public string ContentFingerprint { get; }
        public string Payload { get; }
    }

    /// <summary>
    /// Host-only A/B journal. Each write goes to the inactive slot and is
    /// flushed before replacement, leaving the previous valid revision intact.
    /// SHA-256 detects damage and partial writes; it is not an anti-tamper proof.
    /// </summary>
    public sealed class MatchRecoveryJournal
    {
        public const int CurrentFormatVersion = 1;
        public static readonly TimeSpan DefaultRetention = TimeSpan.FromHours(72d);

        private const string ProductDirectoryName = "MazeParty";
        private const string HostStateDirectoryName = "HostMatchState";
        private const string RecoveryDirectoryName = "MatchRecovery";
        private const int MaximumMatchKeyLength = 512;
        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);

        private readonly string _directoryPath;

        public MatchRecoveryJournal()
            : this(Path.Combine(
                Application.persistentDataPath,
                ProductDirectoryName,
                HostStateDirectoryName,
                RecoveryDirectoryName))
        {
        }

        public MatchRecoveryJournal(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException(
                    "A match recovery directory is required.",
                    nameof(directoryPath));
            }

            _directoryPath = Path.GetFullPath(directoryPath);
        }

        public string DirectoryPath => _directoryPath;

        public MatchRecoveryJournalRecord Save(
            string matchKey,
            string rosterFingerprint,
            string contentFingerprint,
            string payload,
            DateTime savedUtc,
            TimeSpan? retention = null)
        {
            ValidateMatchKey(matchKey);
            ValidateFingerprint(rosterFingerprint, nameof(rosterFingerprint));
            ValidateFingerprint(contentFingerprint, nameof(contentFingerprint));
            if (string.IsNullOrWhiteSpace(payload))
            {
                throw new ArgumentException(
                    "A non-empty recovery payload is required.",
                    nameof(payload));
            }
            if (savedUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException(
                    "Recovery timestamps must use UTC.",
                    nameof(savedUtc));
            }

            var lifetime = retention ?? DefaultRetention;
            if (lifetime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(retention));
            }

            Directory.CreateDirectory(_directoryPath);
            var a = TryReadSlot(matchKey, MatchRecoveryJournalSlot.A);
            var b = TryReadSlot(matchKey, MatchRecoveryJournalSlot.B);
            var highestRevision = Math.Max(
                a.IsIntegrityValid ? a.Envelope.revision : 0L,
                b.IsIntegrityValid ? b.Envelope.revision : 0L);
            var target = SelectInactiveSlot(a, b);
            var envelope = new JournalEnvelope
            {
                formatVersion = CurrentFormatVersion,
                revision = checked(highestRevision + 1L),
                savedUtcTicks = savedUtc.Ticks,
                expiresUtcTicks = savedUtc.Add(lifetime).Ticks,
                matchKey = matchKey,
                rosterFingerprint = rosterFingerprint,
                contentFingerprint = contentFingerprint,
                payload = payload
            };
            envelope.checksum = ComputeChecksum(envelope);

            var targetPath = GetSlotPath(matchKey, target);
            WriteAtomically(targetPath, JsonUtility.ToJson(envelope));
            var verified = TryReadSlot(matchKey, target);
            if (!verified.IsIntegrityValid ||
                verified.Envelope.revision != envelope.revision)
            {
                throw new IOException(
                    "The recovery journal write could not be verified.");
            }

            return ToRecord(target, verified.Envelope);
        }

        public MatchRecoveryLoadStatus TryPeekLatest(
            string matchKey,
            DateTime utcNow,
            out MatchRecoveryJournalRecord record)
        {
            return TryLoadLatest(
                matchKey,
                utcNow,
                null,
                null,
                out record);
        }

        public MatchRecoveryLoadStatus TryLoadLatest(
            string matchKey,
            DateTime utcNow,
            string expectedRosterFingerprint,
            string expectedContentFingerprint,
            out MatchRecoveryJournalRecord record)
        {
            ValidateMatchKey(matchKey);
            if (utcNow.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException(
                    "Recovery timestamps must use UTC.",
                    nameof(utcNow));
            }

            record = default;
            var a = TryReadSlot(matchKey, MatchRecoveryJournalSlot.A);
            var b = TryReadSlot(matchKey, MatchRecoveryJournalSlot.B);
            if (!a.Exists && !b.Exists)
            {
                return MatchRecoveryLoadStatus.None;
            }

            var selected = SelectLatestValid(a, b);
            if (!selected.IsIntegrityValid)
            {
                return MatchRecoveryLoadStatus.Corrupt;
            }

            var envelope = selected.Envelope;
            if (envelope.expiresUtcTicks <= utcNow.Ticks)
            {
                Delete(matchKey);
                return MatchRecoveryLoadStatus.Expired;
            }
            if (expectedRosterFingerprint != null &&
                !string.Equals(
                    envelope.rosterFingerprint,
                    expectedRosterFingerprint,
                    StringComparison.Ordinal))
            {
                return MatchRecoveryLoadStatus.RosterMismatch;
            }
            if (expectedContentFingerprint != null &&
                !string.Equals(
                    envelope.contentFingerprint,
                    expectedContentFingerprint,
                    StringComparison.Ordinal))
            {
                return MatchRecoveryLoadStatus.ContentMismatch;
            }

            record = ToRecord(selected.Slot, envelope);
            return MatchRecoveryLoadStatus.Loaded;
        }

        public void Delete(string matchKey)
        {
            ValidateMatchKey(matchKey);
            DeleteIfPresent(GetSlotPath(matchKey, MatchRecoveryJournalSlot.A));
            DeleteIfPresent(GetSlotPath(matchKey, MatchRecoveryJournalSlot.B));
        }

        public string GetSlotPath(
            string matchKey,
            MatchRecoveryJournalSlot slot)
        {
            ValidateMatchKey(matchKey);
            var suffix = slot == MatchRecoveryJournalSlot.A ? ".a.json" : ".b.json";
            return Path.Combine(
                _directoryPath,
                ComputeSha256Hex(matchKey) + suffix);
        }

        private static MatchRecoveryJournalSlot SelectInactiveSlot(
            SlotReadResult a,
            SlotReadResult b)
        {
            if (!a.IsIntegrityValid)
            {
                return MatchRecoveryJournalSlot.A;
            }
            if (!b.IsIntegrityValid)
            {
                return MatchRecoveryJournalSlot.B;
            }

            return a.Envelope.revision <= b.Envelope.revision
                ? MatchRecoveryJournalSlot.A
                : MatchRecoveryJournalSlot.B;
        }

        private static SlotReadResult SelectLatestValid(
            SlotReadResult a,
            SlotReadResult b)
        {
            if (!a.IsIntegrityValid)
            {
                return b;
            }
            if (!b.IsIntegrityValid)
            {
                return a;
            }

            return a.Envelope.revision >= b.Envelope.revision ? a : b;
        }

        private SlotReadResult TryReadSlot(
            string matchKey,
            MatchRecoveryJournalSlot slot)
        {
            var path = GetSlotPath(matchKey, slot);
            if (!File.Exists(path))
            {
                return new SlotReadResult(slot, false, false, null);
            }

            try
            {
                var json = File.ReadAllText(path, Utf8WithoutBom);
                var envelope = JsonUtility.FromJson<JournalEnvelope>(json);
                var valid = envelope != null &&
                            envelope.formatVersion == CurrentFormatVersion &&
                            envelope.revision > 0L &&
                            envelope.savedUtcTicks > 0L &&
                            envelope.savedUtcTicks <= DateTime.MaxValue.Ticks &&
                            envelope.expiresUtcTicks > envelope.savedUtcTicks &&
                            envelope.expiresUtcTicks <= DateTime.MaxValue.Ticks &&
                            string.Equals(
                                envelope.matchKey,
                                matchKey,
                                StringComparison.Ordinal) &&
                            !string.IsNullOrWhiteSpace(envelope.rosterFingerprint) &&
                            !string.IsNullOrWhiteSpace(envelope.contentFingerprint) &&
                            !string.IsNullOrWhiteSpace(envelope.payload) &&
                            string.Equals(
                                envelope.checksum,
                                ComputeChecksum(envelope),
                                StringComparison.OrdinalIgnoreCase);
                return new SlotReadResult(slot, true, valid, envelope);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException)
            {
                return new SlotReadResult(slot, true, false, null);
            }
        }

        private static MatchRecoveryJournalRecord ToRecord(
            MatchRecoveryJournalSlot slot,
            JournalEnvelope envelope)
        {
            return new MatchRecoveryJournalRecord(
                slot,
                envelope.revision,
                new DateTime(envelope.savedUtcTicks, DateTimeKind.Utc),
                new DateTime(envelope.expiresUtcTicks, DateTimeKind.Utc),
                envelope.matchKey,
                envelope.rosterFingerprint,
                envelope.contentFingerprint,
                envelope.payload);
        }

        private static void WriteAtomically(string targetPath, string json)
        {
            var temporaryPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var bytes = Utf8WithoutBom.GetBytes(json);
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (!File.Exists(targetPath))
                {
                    File.Move(temporaryPath, targetPath);
                    return;
                }

                try
                {
                    File.Replace(temporaryPath, targetPath, null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(temporaryPath, targetPath, true);
                }
                catch (IOException)
                {
                    File.Copy(temporaryPath, targetPath, true);
                }
            }
            finally
            {
                DeleteIfPresent(temporaryPath);
            }
        }

        private static string ComputeChecksum(JournalEnvelope envelope)
        {
            var canonical = string.Join(
                "\n",
                envelope.formatVersion.ToString(CultureInfo.InvariantCulture),
                envelope.revision.ToString(CultureInfo.InvariantCulture),
                envelope.savedUtcTicks.ToString(CultureInfo.InvariantCulture),
                envelope.expiresUtcTicks.ToString(CultureInfo.InvariantCulture),
                EncodeCanonical(envelope.matchKey),
                EncodeCanonical(envelope.rosterFingerprint),
                EncodeCanonical(envelope.contentFingerprint),
                EncodeCanonical(envelope.payload));
            return ComputeSha256Hex(canonical);
        }

        private static string EncodeCanonical(string value)
        {
            value ??= string.Empty;
            return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
        }

        private static string ComputeSha256Hex(string value)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
            var builder = new StringBuilder(hash.Length * 2);
            for (var index = 0; index < hash.Length; index++)
            {
                builder.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static void ValidateMatchKey(string matchKey)
        {
            if (string.IsNullOrWhiteSpace(matchKey) ||
                matchKey.Length > MaximumMatchKeyLength)
            {
                throw new ArgumentException(
                    "A valid match key is required.",
                    nameof(matchKey));
            }
        }

        private static void ValidateFingerprint(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty fingerprint is required.",
                    parameterName);
            }
        }

        private static void DeleteIfPresent(string path)
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
                // Cleanup must not hide the primary persistence result.
            }
            catch (UnauthorizedAccessException)
            {
                // Cleanup must not hide the primary persistence result.
            }
        }

        [Serializable]
        private sealed class JournalEnvelope
        {
            public int formatVersion;
            public long revision;
            public long savedUtcTicks;
            public long expiresUtcTicks;
            public string matchKey;
            public string rosterFingerprint;
            public string contentFingerprint;
            public string payload;
            public string checksum;
        }

        private readonly struct SlotReadResult
        {
            public SlotReadResult(
                MatchRecoveryJournalSlot slot,
                bool exists,
                bool isIntegrityValid,
                JournalEnvelope envelope)
            {
                Slot = slot;
                Exists = exists;
                IsIntegrityValid = isIntegrityValid;
                Envelope = envelope;
            }

            public MatchRecoveryJournalSlot Slot { get; }
            public bool Exists { get; }
            public bool IsIntegrityValid { get; }
            public JournalEnvelope Envelope { get; }
        }
    }
}
