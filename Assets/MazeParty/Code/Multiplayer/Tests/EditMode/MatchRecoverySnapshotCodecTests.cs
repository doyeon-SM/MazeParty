using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MatchRecoverySnapshotCodecTests
    {

        [Test]
        public void CustomSettingsRoundTrip_RejectsMismatchedScheduleAndMissingPayload()
        {
            var codec = new MatchRecoverySnapshotCodec();
            foreach (var turns in new[] { 5, 10 })
            {
                var snapshot = CreateValidSnapshot();
                var settings = MatchGameSettings.Default;
                Assert.That(settings.TryChangeTurns(turns, out settings), Is.True);
                settings.TryChangeGame(ScheduledMinigameId.Minefield, new MinigameSetting(false, 1, 30), out settings);
                settings.TryChangeGame(ScheduledMinigameId.Flight, new MinigameSetting(true, 5, 180), out settings);
                snapshot.gameSettings = settings.Encode();
                snapshot.scheduleTurnCount = turns;
                snapshot.remainingMinigameSlots = turns - 1;
                var schedule = HostMinigameSchedule.Create(123, turns, settings.IncludedGames());
                snapshot.scheduleEntries = new int[turns];
                for (var i = 0; i < turns; i++) snapshot.scheduleEntries[i] = (int)schedule.GetMinigameForTurn(i + 1);
                Assert.That(codec.TryDecode(codec.Encode(snapshot), out var restored), Is.True);
                Assert.That(restored.gameSettings, Is.EqualTo(settings.Encode()));
                var entries = (int[])snapshot.scheduleEntries.Clone();
                snapshot.scheduleEntries[0] = (int)ScheduledMinigameId.Minefield;
                Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);
                snapshot.scheduleEntries = entries;
                snapshot.gameSettings = MatchGameSettings.Default.Encode();
                Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);
                snapshot.gameSettings = string.Empty;
                Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);
            }
            var legacySnapshot = CreateValidSnapshot();
            legacySnapshot.recoveryVersion = MatchRecoverySnapshot.LegacyRecoveryVersionWithoutGameSettings;
            legacySnapshot.gameSettings = string.Empty;
            Assert.That(codec.TryDecode(JsonUtility.ToJson(legacySnapshot), out var legacyRestored), Is.True);
            Assert.That(legacyRestored.gameSettings, Is.EqualTo(MatchGameSettings.Default.Encode()));
        }

        [Test]
        public void RoundTrip_PreservesSafeCheckpointPayload()
        {
            var codec = new MatchRecoverySnapshotCodec();
            var snapshot = CreateValidSnapshot();
            snapshot.boardMapId = "forest-graybox";
            snapshot.boardMapContentVersion = 7;
            snapshot.originalHostPlayerKey = snapshot.players[0].playerKey;
            snapshot.sessionId = "saved-session";
            snapshot.players[0].displayName = "Original Host";

            var payload = codec.Encode(snapshot);
            var decoded = codec.TryDecode(payload, out var restored);

            Assert.That(decoded, Is.True);
            Assert.That(restored.checkpoint, Is.EqualTo(snapshot.checkpoint));
            Assert.That(restored.turn, Is.EqualTo(snapshot.turn));
            Assert.That(restored.originalHostPlayerKey, Is.EqualTo(snapshot.originalHostPlayerKey));
            Assert.That(restored.sessionId, Is.EqualTo(snapshot.sessionId));
            Assert.That(restored.players[0].displayName, Is.EqualTo(snapshot.players[0].displayName));
            Assert.That(restored.scheduleEntries, Is.EqualTo(snapshot.scheduleEntries));
            Assert.That(restored.players[0].playerKey, Is.EqualTo("player-0"));
            Assert.That(restored.players[0].pendingCombatProtection, Is.True);
            Assert.That(
                restored.players[0].personalProtectionRemaining,
                Is.EqualTo(12.5d));
            Assert.That(restored.mines, Has.Length.EqualTo(1));
            Assert.That(restored.mines[0].ownerSlot, Is.EqualTo(2));
            Assert.That(
                restored.mines[0].position,
                Is.EqualTo(new Vector3(2f, 0.06f, 3f)));
            Assert.That(restored.mines[0].armRemaining, Is.EqualTo(0.5f));
            Assert.That(restored.boardMapId, Is.EqualTo("forest-graybox"));
            Assert.That(restored.boardMapContentVersion, Is.EqualTo(7));
        }

        [Test]
        public void Decode_RejectsUnknownScheduledGameAndDuplicatePlayer()
        {
            var codec = new MatchRecoverySnapshotCodec();
            var snapshot = CreateValidSnapshot();
            snapshot.scheduleEntries[0] = 250;
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);

            snapshot = CreateValidSnapshot();
            snapshot.players[3].playerKey = snapshot.players[0].playerKey;
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);
        }

        [Test]
        public void Decode_UsesSavedCatalogPrefixAndExactScheduleComposition()
        {
            var codec = new MatchRecoverySnapshotCodec();
            var snapshot = CreateValidSnapshot();
            snapshot.catalogEntryCount = 2;
            snapshot.catalogFingerprint = MinigameCatalog.GetRecoveryFingerprint(2);
            snapshot.recoveryVersion = MatchRecoverySnapshot.LegacyRecoveryVersionWithoutGameSettings;
            snapshot.gameSettings = string.Empty;
            snapshot.scheduleTurnCount = 15;
            snapshot.scheduleEntries = new int[15];
            snapshot.scheduleEntries[0] = (int)MinigameCatalog.GetRegisteredGame(0);
            snapshot.scheduleEntries[1] = (int)MinigameCatalog.GetRegisteredGame(1);
            snapshot.remainingMinigameSlots = 14;

            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.True);

            snapshot.scheduleEntries[1] =
                (int)MinigameCatalog.GetRegisteredGame(2);
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);

            snapshot.scheduleEntries[1] = (int)ScheduledMinigameId.Skip;
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);
        }

        [Test]
        public void Decode_RejectsImpossibleStableCheckpointState()
        {
            var codec = new MatchRecoverySnapshotCodec();
            var snapshot = CreateValidSnapshot();
            snapshot.checkpoint = MatchRecoveryCheckpoint.MatchComplete;
            snapshot.turn = snapshot.scheduleTurnCount - 1;
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);

            snapshot = CreateValidSnapshot();
            snapshot.players[0].currentHealth = 0;
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);

            snapshot = CreateValidSnapshot();
            snapshot.players[0].traversalHistory = new[] { new Vector2Int(99, 99) };
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);

            snapshot = CreateValidSnapshot();
            snapshot.players[0].personalProtectionRemaining = -0.1d;
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);
        }

        [Test]
        public void Decode_RejectsInvalidKeyShopCheckpoint()
        {
            var codec = new MatchRecoverySnapshotCodec();
            var snapshot = CreateValidSnapshot();
            snapshot.keyShop.lifecycle = (byte)KeyShopLifecycleState.Preparing;
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);

            snapshot = CreateValidSnapshot();
            snapshot.keyShop = new MatchRecoveryKeyShopSnapshot
            {
                lifecycle = (byte)KeyShopLifecycleState.Active,
                hasLocation = false,
                revision = 1
            };
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);

            snapshot = CreateValidSnapshot();
            snapshot.keyShop.location = Vector2Int.right;
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);
        }

        [Test]
        public void Decode_IgnoresRetiredGravesWithoutChangingSettledGold()
        {
            var codec = new MatchRecoverySnapshotCodec();
            var original = CreateValidSnapshot();
            var payload = JsonUtility.ToJson(original);
            payload = payload.Insert(payload.Length - 1,
                ",\"tombstones\":[{\"id\":1,\"gold\":30}],\"nextTombstoneId\":1");
            Assert.That(codec.TryDecode(payload, out var restored), Is.True);
            for (var slot = 0; slot < original.players.Length; slot++)
                Assert.That(restored.players[slot].gold, Is.EqualTo(original.players[slot].gold));
            Assert.That(codec.Encode(restored), Does.Not.Contain("tombstones"));
        }

        [Test]
        public void Decode_RejectsUnsafeMineStateAndCompletedMatchResidue()
        {
            var codec = new MatchRecoverySnapshotCodec();
            var snapshot = CreateValidSnapshot();
            snapshot.mines[0].ownerSlot = MultiplayerConstants.MaxPlayers;
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);

            snapshot = CreateValidSnapshot();
            snapshot.mines[0].position = new Vector3(float.NaN, 0.06f, 3f);
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);

            snapshot = CreateValidSnapshot();
            snapshot.mines[0].armRemaining = -0.001f;
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);

            snapshot = CreateValidSnapshot();
            snapshot.mines[0].armRemaining =
                PrototypeItemCatalog.Get(PrototypeItemId.Mine).ArmingDelay + 0.001f;
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);

            snapshot = CreateValidSnapshot();
            snapshot.checkpoint = MatchRecoveryCheckpoint.MatchComplete;
            snapshot.turn = snapshot.scheduleTurnCount;
            snapshot.remainingMinigameSlots = 0;
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);

            snapshot.mines = System.Array.Empty<MatchRecoveryMineSnapshot>();
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.True);
        }

        [Test]
        public void Decode_MigratesVersionOneOnlyWhenItContainsNoMineState()
        {
            var codec = new MatchRecoverySnapshotCodec();
            var snapshot = CreateValidSnapshot();
            snapshot.recoveryVersion =
                MatchRecoverySnapshot.LegacyRecoveryVersionWithoutMines;

            Assert.That(
                codec.TryDecode(JsonUtility.ToJson(snapshot), out _),
                Is.False,
                "The v1 schema never contained mine state.");

            snapshot.mines = System.Array.Empty<MatchRecoveryMineSnapshot>();
            var legacyPayload = JsonUtility.ToJson(snapshot).Replace(
                ",\"mines\":[]",
                string.Empty);
            Assert.That(legacyPayload, Does.Not.Contain("\"mines\""));
            Assert.That(
                codec.TryDecode(legacyPayload, out var migrated),
                Is.True);
            Assert.That(
                migrated.recoveryVersion,
                Is.EqualTo(MatchRecoverySnapshot.CurrentRecoveryVersion));
            Assert.That(migrated.mines, Is.Empty);

            var currentPlayers = CreatePlayers(
                "player-3",
                "player-2",
                "player-1",
                "player-0");
            Assert.That(
                MatchRecoveryMineOwnership.TryMapToCurrentSlots(
                    migrated.mines,
                    migrated.players,
                    currentPlayers,
                    out var currentOwnerSlots),
                Is.True);
            Assert.That(currentOwnerSlots, Is.Empty);
        }

        [Test]
        public void Decode_MigratesVersionTwoToLegacyMapIdentity()
        {
            var codec = new MatchRecoverySnapshotCodec();
            var snapshot = CreateValidSnapshot();
            snapshot.recoveryVersion =
                MatchRecoverySnapshot.LegacyRecoveryVersionWithoutMapIdentity;
            var legacyPayload = JsonUtility.ToJson(snapshot)
                .Replace(",\"boardMapId\":\"\"", string.Empty)
                .Replace(",\"boardMapContentVersion\":0", string.Empty);

            Assert.That(legacyPayload, Does.Not.Contain("boardMapId"));
            Assert.That(
                codec.TryDecode(legacyPayload, out var migrated),
                Is.True);
            Assert.That(
                migrated.recoveryVersion,
                Is.EqualTo(MatchRecoverySnapshot.CurrentRecoveryVersion));
            Assert.That(migrated.boardMapId, Is.Empty);
            Assert.That(migrated.boardMapContentVersion, Is.Zero);
        }

        [Test]
        public void MineOwnership_MapsSavedSeatsThroughPlayerIdentity()
        {
            var savedPlayers = CreatePlayers(
                "player-a",
                "player-b",
                "player-c",
                "player-d");
            var currentPlayers = CreatePlayers(
                "player-c",
                "player-a",
                "player-d",
                "player-b");
            var mines = new[]
            {
                new MatchRecoveryMineSnapshot { ownerSlot = 0 },
                new MatchRecoveryMineSnapshot { ownerSlot = 3 }
            };

            Assert.That(
                MatchRecoveryMineOwnership.TryMapToCurrentSlots(
                    mines,
                    savedPlayers,
                    currentPlayers,
                    out var currentOwnerSlots),
                Is.True);
            Assert.That(currentOwnerSlots, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(
                mines[0].ownerSlot,
                Is.Zero,
                "The persisted saved-seat value remains immutable.");
        }

        [TestCase(MatchRecoveryCheckpoint.TurnOverview, 1, 0)]
        [TestCase(MatchRecoveryCheckpoint.TurnOverview, 8, 7)]
        [TestCase(MatchRecoveryCheckpoint.MinigameIntroReady, 8, 8)]
        [TestCase(MatchRecoveryCheckpoint.MatchComplete, 15, 15)]
        [TestCase(MatchRecoveryCheckpoint.MinigameSettled, 8, 8)]
        public void RevealedMinigameHistory_RestoresOnlyPublicTurns(
            MatchRecoveryCheckpoint checkpoint,
            int turn,
            int expectedCount)
        {
            Assert.That(
                NetworkMatchState.GetRevealedMinigameCountForRecovery(
                    checkpoint,
                    turn),
                Is.EqualTo(expectedCount));
        }

        [Test]
        public void SettledCheckpoint_RequiresMatchingScheduleAndCompletedRewards()
        {
            var snapshot = CreateValidSnapshot();
            snapshot.checkpoint = MatchRecoveryCheckpoint.MinigameSettled;
            snapshot.currentMinigame = snapshot.scheduleEntries[snapshot.turn - 1];
            snapshot.settledMinigameTurn = snapshot.turn;
            snapshot.players[0].gold = 27;
            snapshot.players[0].minigameWins = 1;
            var codec = new MatchRecoverySnapshotCodec();
            Assert.That(codec.TryDecode(codec.Encode(snapshot), out var restored), Is.True);
            Assert.That(restored.players[0].gold, Is.EqualTo(27));
            Assert.That(restored.players[0].minigameWins, Is.EqualTo(1));
            Assert.That(MatchRecoveryResumeRules.Resolve(restored).Turn, Is.EqualTo(3));
            snapshot.settledMinigameTurn--;
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);
            snapshot.settledMinigameTurn = snapshot.turn;
            snapshot.currentMinigame = (int)ScheduledMinigameId.Skip;
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);
        }


        [Test]
        public void Chests_RoundTripConsumedState_RejectCorruption_AndMigratePreChestSaves()
        {
            var codec = new MatchRecoverySnapshotCodec();
            var snapshot = CreateValidSnapshot();
            snapshot.chests = new[]
            {
                new BoardChestSnapshot { Active = true, Consumed = true, Location = Vector2Int.zero, AppearedTurn = 2, Revision = 2 },
                new BoardChestSnapshot { Active = true, Location = Vector2Int.right, AppearedTurn = 2, Revision = 1 }
            };
            var payload = codec.Encode(snapshot);
            Assert.That(codec.TryDecode(payload, out var restored), Is.True);
            Assert.That(restored.chests, Is.EqualTo(snapshot.chests));
            for (var invalid = 0; invalid < 5; invalid++)
            {
                Assert.That(codec.TryDecode(payload, out restored), Is.True);
                if (invalid == 0) restored.chests = null;
                if (invalid == 1) restored.chests[0].Location = restored.chests[1].Location;
                if (invalid == 2) restored.chests[0].AppearedTurn = 3;
                if (invalid == 3) restored.chests[0].Revision = -1;
                if (invalid == 4) restored.chests[0].Active = false;
                Assert.That(codec.TryDecode(JsonUtility.ToJson(restored), out _), Is.False);
            }
            snapshot.recoveryVersion = MatchRecoverySnapshot.LegacyRecoveryVersionWithoutChests;
            snapshot.chests = null;
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out restored), Is.True);
            Assert.That(restored.recoveryVersion, Is.EqualTo(MatchRecoverySnapshot.CurrentRecoveryVersion));
            Assert.That(restored.chests, Has.Length.EqualTo(BoardChestRules.Count));
            Assert.That(restored.chests[0].Active, Is.False);
        }

        [Test]
        public void AchievementCheckpoint_PreservesMatchIdentityAndRollsBackUncommittedProgress_RejectsMalformedHistory()
        {
            var codec = new MatchRecoverySnapshotCodec();
            var snapshot = CreateValidSnapshot();
            snapshot.achievementMatchId = System.Guid.NewGuid().ToString("N");
            foreach (var player in snapshot.players) player.achievementProgress = JsonUtility.ToJson(new AchievementMatchProgress());
            var progress = new AchievementMatchProgress();
            progress.PurchaseKey(1, true);
            progress.CompleteMinigame(1, ScheduledMinigameId.Minefield, 1, false);
            snapshot.players[2].achievementProgress = JsonUtility.ToJson(progress);
            var payload = codec.Encode(snapshot);
            progress.PurchaseKey(2, false);
            snapshot.players[2].achievementProgress = JsonUtility.ToJson(progress);
            Assert.That(codec.TryDecode(payload, out var restored), Is.True);
            Assert.That(restored.achievementMatchId, Is.EqualTo(snapshot.achievementMatchId));
            Assert.That(AchievementMatchProgress.TryDecode(restored.players[2].achievementProgress, out var saved), Is.True);
            Assert.That(saved.keysBought, Is.EqualTo(1));
            Assert.That(saved.minigameWins, Is.EqualTo(1));
            saved.keysBought = -1;
            restored.players[2].achievementProgress = JsonUtility.ToJson(saved);
            Assert.That(codec.TryDecode(JsonUtility.ToJson(restored), out _), Is.False);
            restored.players[2].achievementProgress = null;
            Assert.That(codec.TryDecode(JsonUtility.ToJson(restored), out _), Is.False);
            restored.achievementMatchId = null;
            Assert.That(codec.TryDecode(JsonUtility.ToJson(restored), out _), Is.True, "Legacy recovery stays playable without achievement credit.");
        }

        private static MatchRecoveryPlayerSnapshot[] CreatePlayers(
            params string[] playerKeys)
        {
            var players = new MatchRecoveryPlayerSnapshot[playerKeys.Length];
            for (var index = 0; index < playerKeys.Length; index++)
            {
                players[index] = new MatchRecoveryPlayerSnapshot
                {
                    playerKey = playerKeys[index]
                };
            }

            return players;
        }

        private static MatchRecoverySnapshot CreateValidSnapshot()
        {
            var entries = new int[MinigameScheduleRules.DefaultTurnCount];
            for (var index = 0;
                 index < MinigameScheduleRules.RegisteredGameCount &&
                 index < entries.Length;
                 index++)
            {
                entries[index] = (int)MinigameScheduleRules.GetRegisteredGame(index);
            }

            var players = new MatchRecoveryPlayerSnapshot[MultiplayerConstants.MaxPlayers];
            for (var slot = 0; slot < players.Length; slot++)
            {
                players[slot] = new MatchRecoveryPlayerSnapshot
                {
                    playerKey = "player-" + slot,
                    hasLogicalTile = true,
                    logicalTile = new Vector2Int(slot, 0),
                    traversalHistory = new[] { new Vector2Int(slot, 0) },
                    maxHealth = 5,
                    currentHealth = 5,
                    gold = 10,
                    pendingCombatProtection = true,
                    personalProtectionRemaining = 12.5d,
                    appearance = PlayerAppearanceState.Default
                };
            }

            return new MatchRecoverySnapshot
            {
                gameSettings = MatchGameSettings.Default.Encode(),
                checkpoint = MatchRecoveryCheckpoint.TurnOverview,
                turn = 2,
                settledMinigameTurn = 1,
                scheduleSeed = 123,
                scheduleTurnCount = entries.Length,
                catalogEntryCount = MinigameCatalog.RegisteredCount,
                catalogFingerprint = MinigameCatalog.CurrentRecoveryFingerprint,
                scheduleEntries = entries,
                currentMinigame = (int)ScheduledMinigameId.Skip,
                currentMinigameSeed = MatchRecoverySnapshotCodec.EncodeMinigameSeed(0UL),
                remainingMinigameSlots = entries.Length - 1,
                boardEffectSeed = 456,
                boardEffectRevision = 1,
                keyShop = new MatchRecoveryKeyShopSnapshot
                {
                    lifecycle = (byte)KeyShopLifecycleState.Inactive
                },
                itemShops = new[]
                {
                    new MatchRecoveryItemShopSnapshot(),
                    new MatchRecoveryItemShopSnapshot()
                },
                mines = new[]
                {
                    new MatchRecoveryMineSnapshot
                    {
                        ownerSlot = 2,
                        position = new Vector3(2f, 0.06f, 3f),
                        armRemaining = 0.5f
                    }
                },
                players = players
            };
        }
    }
}
