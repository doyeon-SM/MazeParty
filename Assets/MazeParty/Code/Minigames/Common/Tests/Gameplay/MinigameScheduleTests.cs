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
                out var restored);

            Assert.That(decoded, Is.True);
            Assert.That(payload, Does.Contain("\"schemaVersion\":14"));
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

            const string sixGamePayload =
                "{\"schemaVersion\":5,\"matchKey\":\"six-game-match\"," +
                "\"seed\":1732,\"turnCount\":7," +
                "\"entries\":[6,0,5,4,3,2,1]}";
            Assert.That(
                codec.TryDecode(
                    sixGamePayload,
                    out var sixGameMatchKey,
                    out var sixGameSchedule),
                Is.True);
            Assert.That(sixGameMatchKey, Is.EqualTo("six-game-match"));
            Assert.That(
                sixGameSchedule.GetMinigameForTurn(1),
                Is.EqualTo(ScheduledMinigameId.GiftGrab));
            Assert.That(
                codec.Encode(sixGameMatchKey, sixGameSchedule),
                Does.Contain("\"schemaVersion\":5"));

            const string sevenGamePayload =
                "{\"schemaVersion\":6,\"matchKey\":\"seven-game-match\"," +
                "\"seed\":2048,\"turnCount\":8," +
                "\"entries\":[7,6,5,4,3,2,1,0]}";
            Assert.That(
                codec.TryDecode(
                    sevenGamePayload,
                    out var sevenGameMatchKey,
                    out var sevenGameSchedule),
                Is.True);
            Assert.That(
                sevenGameMatchKey,
                Is.EqualTo("seven-game-match"));
            Assert.That(
                sevenGameSchedule.GetMinigameForTurn(1),
                Is.EqualTo(ScheduledMinigameId.TerritoryPaint));
            Assert.That(
                codec.Encode(sevenGameMatchKey, sevenGameSchedule),
                Does.Contain("\"schemaVersion\":6"));

            const string eightGamePayload =
                "{\"schemaVersion\":7,\"matchKey\":\"eight-game-match\"," +
                "\"seed\":4096,\"turnCount\":9," +
                "\"entries\":[8,7,6,5,4,3,2,1,0]}";
            Assert.That(
                codec.TryDecode(
                    eightGamePayload,
                    out var eightGameMatchKey,
                    out var eightGameSchedule),
                Is.True);
            Assert.That(
                eightGameSchedule.GetMinigameForTurn(1),
                Is.EqualTo(ScheduledMinigameId.TagChase));
            Assert.That(
                codec.Encode(eightGameMatchKey, eightGameSchedule),
                Does.Contain("\"schemaVersion\":7"));

            const string nineGamePayload =
                "{\"schemaVersion\":8,\"matchKey\":\"nine-game-match\"," +
                "\"seed\":8192,\"turnCount\":10," +
                "\"entries\":[9,8,7,6,5,4,3,2,1,0]}";
            Assert.That(
                codec.TryDecode(
                    nineGamePayload,
                    out var nineGameMatchKey,
                    out var nineGameSchedule),
                Is.True);
            Assert.That(
                nineGameSchedule.GetMinigameForTurn(1),
                Is.EqualTo(ScheduledMinigameId.Race));
            Assert.That(
                codec.Encode(nineGameMatchKey, nineGameSchedule),
                Does.Contain("\"schemaVersion\":8"));

            const string tenGamePayload =
                "{\"schemaVersion\":9,\"matchKey\":\"ten-game-match\"," +
                "\"seed\":8193,\"turnCount\":11," +
                "\"entries\":[10,9,8,7,6,5,4,3,2,1,0]}";
            Assert.That(
                codec.TryDecode(
                    tenGamePayload,
                    out var tenGameMatchKey,
                    out var tenGameSchedule),
                Is.True);
            Assert.That(
                tenGameSchedule.GetMinigameForTurn(1),
                Is.EqualTo(ScheduledMinigameId.SequenceMemory));
            Assert.That(
                codec.Encode(tenGameMatchKey, tenGameSchedule),
                Does.Contain("\"schemaVersion\":9"));

            const string elevenGamePayload =
                "{\"schemaVersion\":10,\"matchKey\":\"eleven-game-match\"," +
                "\"seed\":8194,\"turnCount\":12," +
                "\"entries\":[11,10,9,8,7,6,5,4,3,2,1,0]}";
            Assert.That(
                codec.TryDecode(
                    elevenGamePayload,
                    out var elevenGameMatchKey,
                    out var elevenGameSchedule),
                Is.True);
            Assert.That(
                elevenGameSchedule.GetMinigameForTurn(1),
                Is.EqualTo(ScheduledMinigameId.BouncingBalls));
            Assert.That(
                codec.Encode(elevenGameMatchKey, elevenGameSchedule),
                Does.Contain("\"schemaVersion\":10"));

            const string twelveGamePayload =
                "{\"schemaVersion\":11,\"matchKey\":\"twelve-game-match\"," +
                "\"seed\":8195,\"turnCount\":13," +
                "\"entries\":[12,11,10,9,8,7,6,5,4,3,2,1,0]}";
            Assert.That(
                codec.TryDecode(
                    twelveGamePayload,
                    out var twelveGameMatchKey,
                    out var twelveGameSchedule),
                Is.True);
            Assert.That(
                twelveGameSchedule.GetMinigameForTurn(1),
                Is.EqualTo(ScheduledMinigameId.BombPassing));
            Assert.That(
                codec.Encode(twelveGameMatchKey, twelveGameSchedule),
                Does.Contain("\"schemaVersion\":11"));

            const string thirteenGamePayload =
                "{\"schemaVersion\":12,\"matchKey\":\"thirteen-game-match\"," +
                "\"seed\":8196,\"turnCount\":14," +
                "\"entries\":[13,12,11,10,9,8,7,6,5,4,3,2,1,0]}";
            Assert.That(
                codec.TryDecode(
                    thirteenGamePayload,
                    out var thirteenGameMatchKey,
                    out var thirteenGameSchedule),
                Is.True);
            Assert.That(
                thirteenGameSchedule.GetMinigameForTurn(1),
                Is.EqualTo(ScheduledMinigameId.SnowySpin));
            Assert.That(
                codec.Encode(thirteenGameMatchKey, thirteenGameSchedule),
                Does.Contain("\"schemaVersion\":12"));

            const string fourteenGamePayload =
                "{\"schemaVersion\":13,\"matchKey\":\"fourteen-game-match\"," +
                "\"seed\":8197,\"turnCount\":15," +
                "\"entries\":[14,13,12,11,10,9,8,7,6,5,4,3,2,1,0]}";
            Assert.That(
                codec.TryDecode(
                    fourteenGamePayload,
                    out var fourteenGameMatchKey,
                    out var fourteenGameSchedule),
                Is.True);
            Assert.That(
                fourteenGameSchedule.GetMinigameForTurn(1),
                Is.EqualTo(ScheduledMinigameId.ArenaCombat));
            Assert.That(
                codec.Encode(fourteenGameMatchKey, fourteenGameSchedule),
                Does.Contain("\"schemaVersion\":13"));
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
