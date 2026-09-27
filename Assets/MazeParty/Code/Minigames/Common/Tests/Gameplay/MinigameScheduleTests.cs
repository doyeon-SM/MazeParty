using System;
using System.Collections.Generic;
using System.IO;
using MazeParty.Gameplay.Minigames;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class MinigameScheduleTests
    {
        [Test]
        public void Create_CoversCatalogAndRepeatsDeterministicShuffle()
        {
            var schedule = HostMinigameSchedule.Create(12345);
            var repeated = HostMinigameSchedule.Create(12345);
            var counts = CountEntries(schedule);

            AssertSchedulesEqual(schedule, repeated);
            Assert.That(
                (int)ScheduledMinigameId.RedLightGreenLight,
                Is.EqualTo(3),
                "Serialized production minigame ids must remain stable.");
            Assert.That(
                (int)ScheduledMinigameId.StableFooting,
                Is.EqualTo(4),
                "Serialized production minigame ids must remain stable.");
            Assert.That(
                (int)ScheduledMinigameId.BalloonBlow,
                Is.EqualTo(5),
                "Serialized production minigame ids must remain stable.");
            Assert.That(
                (int)ScheduledMinigameId.GiftGrab,
                Is.EqualTo(6),
                "Serialized production minigame ids must remain stable.");
            Assert.That(
                (int)ScheduledMinigameId.TerritoryPaint,
                Is.EqualTo(7),
                "Serialized production minigame ids must remain stable.");
            Assert.That(
                (int)ScheduledMinigameId.TagChase,
                Is.EqualTo(8),
                "Serialized production minigame ids must remain stable.");
            Assert.That(
                (int)ScheduledMinigameId.Race,
                Is.EqualTo(9),
                "Serialized production minigame ids must remain stable.");
            Assert.That(
                (int)ScheduledMinigameId.SequenceMemory,
                Is.EqualTo(10),
                "Serialized production minigame ids must remain stable.");
            Assert.That(
                (int)ScheduledMinigameId.BouncingBalls,
                Is.EqualTo(11),
                "Serialized production minigame ids must remain stable.");
            Assert.That(
                (int)ScheduledMinigameId.BombPassing,
                Is.EqualTo(12),
                "Serialized production minigame ids must remain stable.");
            Assert.That(
                (int)ScheduledMinigameId.SnowySpin,
                Is.EqualTo(13),
                "Serialized production minigame ids must remain stable.");
            Assert.That(
                (int)ScheduledMinigameId.ArenaCombat,
                Is.EqualTo(14),
                "Serialized production minigame ids must remain stable.");
            Assert.That(
                (int)ScheduledMinigameId.CliffBarrage,
                Is.EqualTo(15),
                "Serialized production minigame ids must remain stable.");
            Assert.That(
                MinigameScheduleRules.RegisteredGameCount,
                Is.EqualTo(15));
            Assert.That(
                schedule.TurnCount,
                Is.EqualTo(MinigameScheduleRules.DefaultTurnCount));
            Assert.That(
                counts[ScheduledMinigameId.Minefield],
                Is.EqualTo(1));
            Assert.That(
                counts[ScheduledMinigameId.WrongWay],
                Is.EqualTo(1));
            Assert.That(
                counts[ScheduledMinigameId.RedLightGreenLight],
                Is.EqualTo(1));
            Assert.That(
                counts[ScheduledMinigameId.StableFooting],
                Is.EqualTo(1));
            Assert.That(
                counts[ScheduledMinigameId.BalloonBlow],
                Is.EqualTo(1));
            Assert.That(
                counts[ScheduledMinigameId.GiftGrab],
                Is.EqualTo(1));
            Assert.That(
                counts[ScheduledMinigameId.TerritoryPaint],
                Is.EqualTo(1));
            Assert.That(
                counts[ScheduledMinigameId.TagChase],
                Is.EqualTo(1));
            Assert.That(
                counts[ScheduledMinigameId.Race],
                Is.EqualTo(1));
            Assert.That(
                counts[ScheduledMinigameId.SequenceMemory],
                Is.EqualTo(1));
            Assert.That(
                counts[ScheduledMinigameId.BouncingBalls],
                Is.EqualTo(1));
            Assert.That(
                counts[ScheduledMinigameId.BombPassing],
                Is.EqualTo(1));
            Assert.That(
                counts[ScheduledMinigameId.SnowySpin],
                Is.EqualTo(1));
            Assert.That(
                counts[ScheduledMinigameId.ArenaCombat],
                Is.EqualTo(1));
            Assert.That(
                counts[ScheduledMinigameId.CliffBarrage],
                Is.EqualTo(1));
            Assert.That(
                counts[ScheduledMinigameId.Skip],
                Is.EqualTo(
                    schedule.TurnCount -
                    MinigameScheduleRules.RegisteredGameCount));
        }

        [Test]
        public void JsonCodec_RoundTripsCompleteHostSchedule()
        {
            var codec = new HostMinigameScheduleJsonCodec();
            var original = HostMinigameSchedule.Create(-501, 19);

            var payload = codec.Encode("session:room-42", original);
            var decoded = codec.TryDecode(
                payload,
                out var matchKey,
                out var restored,
                out var requiresMigration);

            Assert.That(decoded, Is.True);
            Assert.That(requiresMigration, Is.False);
            Assert.That(payload, Does.Contain("\"formatVersion\":1"));
            Assert.That(
                payload,
                Does.Contain(
                    "\"catalogEntryCount\":" +
                    MinigameScheduleRules.RegisteredGameCount));
            Assert.That(payload, Does.Contain("\"catalogFingerprint\":"));
            Assert.That(payload, Does.Not.Contain("\"schemaVersion\":"));
            Assert.That(matchKey, Is.EqualTo("session:room-42"));
            AssertSchedulesEqual(original, restored);
        }

        [TestCase(1, 2)]
        [TestCase(2, 3)]
        [TestCase(3, 4)]
        [TestCase(4, 5)]
        [TestCase(5, 6)]
        [TestCase(6, 7)]
        [TestCase(7, 8)]
        [TestCase(8, 9)]
        [TestCase(9, 10)]
        [TestCase(10, 11)]
        [TestCase(11, 12)]
        [TestCase(12, 13)]
        [TestCase(13, 14)]
        [TestCase(14, 15)]
        public void JsonCodec_MigratesLegacyCatalogWithoutRerollingQueue(
            int schemaVersion,
            int catalogEntryCount)
        {
            var serializedEntries = new List<int>(catalogEntryCount);
            for (var id = 1; id <= catalogEntryCount; id++)
            {
                serializedEntries.Add(id);
            }

            var matchKey = "legacy-match-" + schemaVersion;
            var payload =
                "{\"schemaVersion\":" + schemaVersion +
                ",\"matchKey\":\"" + matchKey +
                "\",\"seed\":314,\"turnCount\":" + catalogEntryCount +
                ",\"entries\":[" +
                string.Join(",", serializedEntries) + "]}";
            var codec = new HostMinigameScheduleJsonCodec();

            var decoded = codec.TryDecode(
                payload,
                out var restoredMatchKey,
                out var restored,
                out var requiresMigration);

            Assert.That(decoded, Is.True);
            Assert.That(requiresMigration, Is.True);
            Assert.That(restoredMatchKey, Is.EqualTo(matchKey));
            Assert.That(restored.Seed, Is.EqualTo(314));
            Assert.That(restored.TurnCount, Is.EqualTo(catalogEntryCount));
            for (var turn = 1; turn <= catalogEntryCount; turn++)
            {
                Assert.That(
                    restored.GetMinigameForTurn(turn),
                    Is.EqualTo((ScheduledMinigameId)turn),
                    "Turn " + turn);
            }

            var migrated = codec.Encode(restoredMatchKey, restored);
            Assert.That(migrated, Does.Contain("\"formatVersion\":1"));
            Assert.That(
                migrated,
                Does.Contain(
                    "\"catalogEntryCount\":" + catalogEntryCount));
            Assert.That(migrated, Does.Contain("\"catalogFingerprint\":"));
            Assert.That(migrated, Does.Not.Contain("\"schemaVersion\":"));
            Assert.That(
                codec.TryDecode(
                    migrated,
                    out _,
                    out var roundTripped,
                    out var migratedAgain),
                Is.True);
            Assert.That(migratedAgain, Is.False);
            AssertSchedulesEqual(restored, roundTripped);
        }

        [Test]
        public void JsonCodec_MigratesLegacyQueueWithSkipTurnsIntact()
        {
            const string payload =
                "{\"schemaVersion\":1,\"matchKey\":\"legacy-with-skips\"," +
                "\"seed\":314,\"turnCount\":5," +
                "\"entries\":[0,2,0,1,0]}";
            var codec = new HostMinigameScheduleJsonCodec();

            Assert.That(
                codec.TryDecode(
                    payload,
                    out var matchKey,
                    out var restored,
                    out var requiresMigration),
                Is.True);
            Assert.That(requiresMigration, Is.True);
            Assert.That(matchKey, Is.EqualTo("legacy-with-skips"));
            var expected = new[]
            {
                ScheduledMinigameId.Skip,
                ScheduledMinigameId.WrongWay,
                ScheduledMinigameId.Skip,
                ScheduledMinigameId.Minefield,
                ScheduledMinigameId.Skip
            };
            for (var turn = 1; turn <= expected.Length; turn++)
            {
                Assert.That(
                    restored.GetMinigameForTurn(turn),
                    Is.EqualTo(expected[turn - 1]),
                    "Turn " + turn);
            }
        }

        [Test]
        public void JsonCodec_RejectsCatalogFingerprintMismatch()
        {
            var codec = new HostMinigameScheduleJsonCodec();
            var payload = codec.Encode(
                "fingerprint-match",
                HostMinigameSchedule.Create(2718));
            var tampered = payload.Replace(
                "\"catalogFingerprint\":\"",
                "\"catalogFingerprint\":\"tampered-");

            Assert.That(
                codec.TryDecode(
                    tampered,
                    out _,
                    out _,
                    out _),
                Is.False);
        }

        [Test]
        public void Repository_SameMatchRestoresWithoutCallingNewSeedFactory()
        {
            var payloadStore = new MemoryPayloadStore();
            var codec = new HostMinigameScheduleJsonCodec();
            var firstRepository =
                new HostMinigameScheduleRepository(payloadStore, codec);
            var original = firstRepository.LoadOrCreate(
                "stable-match-key",
                () => 111);

            var seedFactoryCalls = 0;
            var restartedRepository =
                new HostMinigameScheduleRepository(payloadStore, codec);
            var restored = restartedRepository.LoadOrCreate(
                "stable-match-key",
                () =>
                {
                    seedFactoryCalls++;
                    return 999;
                });

            Assert.That(seedFactoryCalls, Is.EqualTo(0));
            AssertSchedulesEqual(original, restored);
        }

        [Test]
        public void Repository_LoadMigratesLegacyPayloadInPlace()
        {
            const string matchKey = "legacy-repository-match";
            var payloadStore = new MemoryPayloadStore();
            payloadStore.Write(
                matchKey,
                "{\"schemaVersion\":1,\"matchKey\":\"" + matchKey +
                "\",\"seed\":111,\"turnCount\":2,\"entries\":[1,2]}");
            var repository = new HostMinigameScheduleRepository(
                payloadStore,
                new HostMinigameScheduleJsonCodec());

            Assert.That(repository.TryLoad(matchKey, out var restored), Is.True);
            Assert.That(restored.GetMinigameForTurn(1),
                Is.EqualTo(ScheduledMinigameId.Minefield));
            Assert.That(payloadStore.TryRead(matchKey, out var migrated), Is.True);
            Assert.That(migrated, Does.Contain("\"formatVersion\":1"));
            Assert.That(migrated, Does.Not.Contain("\"schemaVersion\":"));
        }

        [Test]
        public void Repository_LegacyLoadSurvivesMigrationWriteFailure()
        {
            const string matchKey = "read-only-legacy-match";
            var payloadStore = new MemoryPayloadStore();
            payloadStore.Write(
                matchKey,
                "{\"schemaVersion\":1,\"matchKey\":\"" + matchKey +
                "\",\"seed\":222,\"turnCount\":2,\"entries\":[1,2]}");
            payloadStore.FailWrites = true;
            var repository = new HostMinigameScheduleRepository(
                payloadStore,
                new HostMinigameScheduleJsonCodec());

            Assert.That(repository.TryLoad(matchKey, out var restored), Is.True);
            Assert.That(restored.Seed, Is.EqualTo(222));
        }

        [Test]
        public void Repository_CorruptPayloadFailsInsteadOfRerolling()
        {
            var payloadStore = new MemoryPayloadStore();
            payloadStore.Write("match", "{not-json");
            var repository = new HostMinigameScheduleRepository(
                payloadStore,
                new HostMinigameScheduleJsonCodec());
            var seedFactoryCalls = 0;

            Assert.Throws<InvalidDataException>(
                () => repository.LoadOrCreate(
                    "match",
                    () =>
                    {
                        seedFactoryCalls++;
                        return 5;
                    }));
            Assert.That(seedFactoryCalls, Is.EqualTo(0));
        }

        [Test]
        public void Roster_SamePlayersInAnySeatOrderReuseTheSavedSchedule()
        {
            var saved = MinigameScheduleRoster.CreateKey(
                new[] { "player-a", "player-b", "player-c", "player-d" });
            var restartedInNewRoom = MinigameScheduleRoster.CreateKey(
                new[] { "player-c", "player-a", "player-d", "player-b" });

            Assert.That(saved, Is.Not.Empty);
            Assert.That(restartedInNewRoom, Is.EqualTo(saved));
            Assert.That(
                MinigameScheduleRoster.CanReuse(saved, restartedInNewRoom),
                Is.True);
        }

        [Test]
        public void Roster_DifferentPlayersResetTheSavedSchedule()
        {
            var saved = MinigameScheduleRoster.CreateKey(
                new[] { "player-a", "player-b", "player-c", "player-d" });
            var oneReplaced = MinigameScheduleRoster.CreateKey(
                new[] { "player-a", "player-b", "player-c", "player-e" });

            Assert.That(oneReplaced, Is.Not.EqualTo(saved));
            Assert.That(
                MinigameScheduleRoster.CanReuse(saved, oneReplaced),
                Is.False);
            // Schedules saved before rosters were recorded have no roster.
            Assert.That(
                MinigameScheduleRoster.CanReuse(string.Empty, saved),
                Is.False);
            // An unknown current roster never reuses anything.
            Assert.That(
                MinigameScheduleRoster.CanReuse(string.Empty, string.Empty),
                Is.False);
            Assert.That(
                MinigameScheduleRoster.CreateKey(new[] { " ", null }),
                Is.Empty);
        }

        private static Dictionary<ScheduledMinigameId, int> CountEntries(
            HostMinigameSchedule schedule)
        {
            var counts = new Dictionary<ScheduledMinigameId, int>
            {
                [ScheduledMinigameId.Skip] = 0,
                [ScheduledMinigameId.Minefield] = 0,
                [ScheduledMinigameId.WrongWay] = 0,
                [ScheduledMinigameId.RedLightGreenLight] = 0,
                [ScheduledMinigameId.StableFooting] = 0,
                [ScheduledMinigameId.BalloonBlow] = 0,
                [ScheduledMinigameId.GiftGrab] = 0,
                [ScheduledMinigameId.TerritoryPaint] = 0,
                [ScheduledMinigameId.TagChase] = 0,
                [ScheduledMinigameId.Race] = 0,
                [ScheduledMinigameId.SequenceMemory] = 0,
                [ScheduledMinigameId.BouncingBalls] = 0,
                [ScheduledMinigameId.BombPassing] = 0,
                [ScheduledMinigameId.SnowySpin] = 0,
                [ScheduledMinigameId.ArenaCombat] = 0,
                [ScheduledMinigameId.CliffBarrage] = 0
            };

            for (var turn = 1; turn <= schedule.TurnCount; turn++)
            {
                counts[schedule.GetMinigameForTurn(turn)]++;
            }

            return counts;
        }

        private static void AssertSchedulesEqual(
            HostMinigameSchedule expected,
            HostMinigameSchedule actual)
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual.Seed, Is.EqualTo(expected.Seed));
            Assert.That(actual.TurnCount, Is.EqualTo(expected.TurnCount));
            for (var turn = 1; turn <= expected.TurnCount; turn++)
            {
                Assert.That(
                    actual.GetMinigameForTurn(turn),
                    Is.EqualTo(expected.GetMinigameForTurn(turn)),
                    "Turn " + turn);
            }
        }

        private sealed class MemoryPayloadStore :
            IMinigameSchedulePayloadStore
        {
            private readonly Dictionary<string, string> _payloads =
                new Dictionary<string, string>(StringComparer.Ordinal);

            public bool FailWrites { get; set; }

            public bool TryRead(string matchKey, out string payload)
            {
                return _payloads.TryGetValue(matchKey, out payload);
            }

            public void Write(string matchKey, string payload)
            {
                if (FailWrites)
                {
                    throw new IOException("Simulated read-only payload store.");
                }

                _payloads[matchKey] = payload;
            }

            public void Delete(string matchKey)
            {
                _payloads.Remove(matchKey);
            }
        }
    }
}
