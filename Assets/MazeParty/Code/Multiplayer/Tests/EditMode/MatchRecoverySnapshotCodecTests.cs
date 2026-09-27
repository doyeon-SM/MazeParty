using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MatchRecoverySnapshotCodecTests
    {
        [Test]
        public void RoundTrip_PreservesSafeCheckpointPayload()
        {
            var codec = new MatchRecoverySnapshotCodec();
            var snapshot = CreateValidSnapshot();

            var payload = codec.Encode(snapshot);
            var decoded = codec.TryDecode(payload, out var restored);

            Assert.That(decoded, Is.True);
            Assert.That(restored.checkpoint, Is.EqualTo(snapshot.checkpoint));
            Assert.That(restored.turn, Is.EqualTo(snapshot.turn));
            Assert.That(restored.scheduleEntries, Is.EqualTo(snapshot.scheduleEntries));
            Assert.That(restored.players[0].playerKey, Is.EqualTo("player-0"));
            Assert.That(restored.players[0].pendingCombatProtection, Is.True);
            Assert.That(
                restored.players[0].personalProtectionRemaining,
                Is.EqualTo(12.5d));
            Assert.That(restored.tombstones[0].id, Is.EqualTo(1));
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
            snapshot.scheduleTurnCount = 3;
            snapshot.scheduleEntries = new[]
            {
                (int)MinigameCatalog.GetRegisteredGame(0),
                (int)MinigameCatalog.GetRegisteredGame(1),
                (int)ScheduledMinigameId.Skip
            };
            snapshot.remainingMinigameSlots = 2;

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
        public void Decode_AllowsLastAllocatedTombstoneIdButRejectsFutureId()
        {
            var codec = new MatchRecoverySnapshotCodec();
            var snapshot = CreateValidSnapshot();
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.True);

            snapshot.nextTombstoneId = 0;
            Assert.That(codec.TryDecode(JsonUtility.ToJson(snapshot), out _), Is.False);
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
                tombstones = new[]
                {
                    new MatchRecoveryTombstoneSnapshot
                    {
                        id = 1,
                        position = new Vector3(1f, 0f, 2f),
                        gold = 3
                    }
                },
                nextTombstoneId = 1,
                players = players
            };
        }
    }
}
