using System;
using System.Collections.Generic;
using System.IO;
using MazeParty.Gameplay.Minigames;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class MinigameScheduleTests
    {
        [Test]
        public void DefaultSchedule_UsesTurnCountAndEveryRegisteredGameExactlyOnce()
        {
            var schedule = HostMinigameSchedule.Create(12345);
            var counts = CountEntries(schedule);

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
                counts[ScheduledMinigameId.Skip],
                Is.EqualTo(
                    schedule.TurnCount -
                    MinigameScheduleRules.RegisteredGameCount));
        }

        [Test]
        public void Create_SameSeedProducesSameDeterministicShuffle()
        {
            var first = HostMinigameSchedule.Create(0x12345678);
            var second = HostMinigameSchedule.Create(0x12345678);

            AssertSchedulesEqual(first, second);
        }

        [Test]
        public void ScheduleShorterThanCatalog_SelectsDistinctGamesWithoutSkip()
        {
            var schedule = HostMinigameSchedule.Create(42, 1);

            Assert.That(schedule.TurnCount, Is.EqualTo(1));
            Assert.That(
                schedule.GetMinigameForTurn(1),
                Is.Not.EqualTo(ScheduledMinigameId.Skip));
        }

        [Test]
        public void Lookup_UsesOneBasedTurnsAndRejectsOutsideSchedule()
        {
            var schedule = HostMinigameSchedule.Create(77);

            Assert.That(
                Enum.IsDefined(
                    typeof(ScheduledMinigameId),
                    schedule.GetMinigameForTurn(1)),
                Is.True);
            Assert.That(
                Enum.IsDefined(
                    typeof(ScheduledMinigameId),
                    schedule.GetMinigameForTurn(schedule.TurnCount)),
                Is.True);
            Assert.That(
                schedule.TryGetMinigameForTurn(0, out _),
                Is.False);
            Assert.That(
                schedule.TryGetMinigameForTurn(
                    schedule.TurnCount + 1,
                    out _),
                Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => schedule.GetMinigameForTurn(0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => schedule.GetMinigameForTurn(
                    schedule.TurnCount + 1));
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
                out var restored);

            Assert.That(decoded, Is.True);
            Assert.That(matchKey, Is.EqualTo("session:room-42"));
            AssertSchedulesEqual(original, restored);
        }

        [TestCase(
            "{\"schemaVersion\":1,\"matchKey\":\"match\",\"seed\":1," +
            "\"turnCount\":2,\"entries\":[1,1]}")]
        [TestCase(
            "{\"schemaVersion\":1,\"matchKey\":\"match\",\"seed\":1," +
            "\"turnCount\":2,\"entries\":[1,99]}")]
        [TestCase(
            "{\"schemaVersion\":99,\"matchKey\":\"match\",\"seed\":1," +
            "\"turnCount\":2,\"entries\":[1,2]}")]
        public void JsonCodec_RejectsInvalidOrUnknownSchedules(string payload)
        {
            var codec = new HostMinigameScheduleJsonCodec();

            Assert.That(
                codec.TryDecode(payload, out _, out _),
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
        public void Repository_NewMatchKeyCreatesAndStoresANewSchedule()
        {
            var payloadStore = new MemoryPayloadStore();
            var repository = new HostMinigameScheduleRepository(
                payloadStore,
                new HostMinigameScheduleJsonCodec());

            var first = repository.LoadOrCreate("match-a", () => 10);
            var second = repository.LoadOrCreate("match-b", () => 20);

            Assert.That(first.Seed, Is.EqualTo(10));
            Assert.That(second.Seed, Is.EqualTo(20));
            Assert.That(payloadStore.Count, Is.EqualTo(2));
        }

        [Test]
        public void Repository_RefusesToReplaceScheduleForExistingMatch()
        {
            var payloadStore = new MemoryPayloadStore();
            var repository = new HostMinigameScheduleRepository(
                payloadStore,
                new HostMinigameScheduleJsonCodec());
            repository.Save(
                "immutable-match",
                HostMinigameSchedule.Create(1));

            Assert.Throws<InvalidOperationException>(
                () => repository.Save(
                    "immutable-match",
                    HostMinigameSchedule.Create(2)));
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
        public void FileStore_HashesMatchKeyAndKeepsPayloadInsideConfiguredDirectory()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "MazeParty.MinGameSchedule." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                var store = new FileMinigameSchedulePayloadStore(root);
                const string matchKey = "../../outside/unsafe:match";
                store.Write(matchKey, "{\"value\":1}");

                var files = Directory.GetFiles(
                    root,
                    "*.json",
                    SearchOption.TopDirectoryOnly);
                Assert.That(files, Has.Length.EqualTo(1));
                Assert.That(
                    Path.GetDirectoryName(Path.GetFullPath(files[0])),
                    Is.EqualTo(Path.GetFullPath(root)));
                Assert.That(
                    Path.GetFileNameWithoutExtension(files[0]).Length,
                    Is.EqualTo(64));
                Assert.That(
                    store.TryRead(matchKey, out var payload),
                    Is.True);
                Assert.That(payload, Is.EqualTo("{\"value\":1}"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void DefaultFileStore_IsUnderApplicationPersistentDataPath()
        {
            var store = new FileMinigameSchedulePayloadStore();
            var persistentRoot =
                Path.GetFullPath(Application.persistentDataPath)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            var actual =
                store.DirectoryPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            Assert.That(
                actual.StartsWith(
                    persistentRoot,
                    StringComparison.OrdinalIgnoreCase),
                Is.True);
        }

        private static Dictionary<ScheduledMinigameId, int> CountEntries(
            HostMinigameSchedule schedule)
        {
            var counts = new Dictionary<ScheduledMinigameId, int>
            {
                [ScheduledMinigameId.Skip] = 0,
                [ScheduledMinigameId.Minefield] = 0,
                [ScheduledMinigameId.WrongWay] = 0
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

            public int Count => _payloads.Count;

            public bool TryRead(string matchKey, out string payload)
            {
                return _payloads.TryGetValue(matchKey, out payload);
            }

            public void Write(string matchKey, string payload)
            {
                _payloads[matchKey] = payload;
            }

            public void Delete(string matchKey)
            {
                _payloads.Remove(matchKey);
            }
        }
    }
}
