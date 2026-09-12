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
                MinigameScheduleRules.RegisteredGameCount,
                Is.EqualTo(6));
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
                out var restored);

            Assert.That(decoded, Is.True);
            Assert.That(payload, Does.Contain("\"schemaVersion\":5"));
            Assert.That(matchKey, Is.EqualTo("session:room-42"));
            AssertSchedulesEqual(original, restored);
        }

        [Test]
        public void JsonCodec_RestoresLegacyCatalogWithoutRerollingQueue()
        {
            const string payload =
                "{\"schemaVersion\":1,\"matchKey\":\"legacy-match\"," +
                "\"seed\":314,\"turnCount\":5," +
                "\"entries\":[0,2,0,1,0]}";
            var codec = new HostMinigameScheduleJsonCodec();

            var decoded = codec.TryDecode(
                payload,
                out var matchKey,
                out var restored);

            Assert.That(decoded, Is.True);
            Assert.That(matchKey, Is.EqualTo("legacy-match"));
            Assert.That(restored.Seed, Is.EqualTo(314));
            Assert.That(restored.TurnCount, Is.EqualTo(5));
            Assert.That(
                restored.GetMinigameForTurn(2),
                Is.EqualTo(ScheduledMinigameId.WrongWay));
            Assert.That(
                restored.GetMinigameForTurn(4),
                Is.EqualTo(ScheduledMinigameId.Minefield));

            var reencoded = codec.Encode(matchKey, restored);
            StringAssert.Contains("\"schemaVersion\":1", reencoded);
            Assert.That(
                codec.TryDecode(reencoded, out _, out var roundTripped),
                Is.True);
            AssertSchedulesEqual(restored, roundTripped);

            const string threeGamePayload =
                "{\"schemaVersion\":2,\"matchKey\":\"three-game-match\"," +
                "\"seed\":2718,\"turnCount\":5," +
                "\"entries\":[3,0,2,0,1]}";
            Assert.That(
                codec.TryDecode(
                    threeGamePayload,
                    out var threeGameMatchKey,
                    out var threeGameSchedule),
                Is.True);
            Assert.That(threeGameMatchKey, Is.EqualTo("three-game-match"));
            Assert.That(
                threeGameSchedule.GetMinigameForTurn(1),
                Is.EqualTo(ScheduledMinigameId.RedLightGreenLight));
            Assert.That(
                codec.Encode(threeGameMatchKey, threeGameSchedule),
                Does.Contain("\"schemaVersion\":2"));

            const string fourGamePayload =
                "{\"schemaVersion\":3,\"matchKey\":\"four-game-match\"," +
                "\"seed\":1618,\"turnCount\":6," +
                "\"entries\":[4,0,3,2,0,1]}";
            Assert.That(
                codec.TryDecode(
                    fourGamePayload,
                    out var fourGameMatchKey,
                    out var fourGameSchedule),
                Is.True);
            Assert.That(fourGameMatchKey, Is.EqualTo("four-game-match"));
            Assert.That(
                fourGameSchedule.GetMinigameForTurn(1),
                Is.EqualTo(ScheduledMinigameId.StableFooting));
            Assert.That(
                codec.Encode(fourGameMatchKey, fourGameSchedule),
                Does.Contain("\"schemaVersion\":3"));

            const string fiveGamePayload =
                "{\"schemaVersion\":4,\"matchKey\":\"five-game-match\"," +
                "\"seed\":1414,\"turnCount\":7," +
                "\"entries\":[5,0,4,3,2,0,1]}";
            Assert.That(
                codec.TryDecode(
                    fiveGamePayload,
                    out var fiveGameMatchKey,
                    out var fiveGameSchedule),
                Is.True);
            Assert.That(fiveGameMatchKey, Is.EqualTo("five-game-match"));
            Assert.That(
                fiveGameSchedule.GetMinigameForTurn(1),
                Is.EqualTo(ScheduledMinigameId.BalloonBlow));
            Assert.That(
                codec.Encode(fiveGameMatchKey, fiveGameSchedule),
                Does.Contain("\"schemaVersion\":4"));
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
                [ScheduledMinigameId.GiftGrab] = 0
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
