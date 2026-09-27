using System;
using System.Collections.Generic;
using System.Globalization;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed class MatchRecoverySnapshotCodec
    {
        public string Encode(MatchRecoverySnapshot snapshot)
        {
            if (!IsValid(snapshot))
            {
                throw new ArgumentException(
                    "The match recovery snapshot is invalid.",
                    nameof(snapshot));
            }

            return JsonUtility.ToJson(snapshot);
        }

        public bool TryDecode(
            string payload,
            out MatchRecoverySnapshot snapshot)
        {
            snapshot = null;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            try
            {
                var decoded = JsonUtility.FromJson<MatchRecoverySnapshot>(payload);
                if (!IsValid(decoded))
                {
                    return false;
                }

                snapshot = decoded;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        public static string EncodeMinigameSeed(ulong seed)
        {
            return seed.ToString(CultureInfo.InvariantCulture);
        }

        public static bool TryDecodeMinigameSeed(string value, out ulong seed)
        {
            return ulong.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out seed);
        }

        private static bool IsValid(MatchRecoverySnapshot snapshot)
        {
            if (snapshot == null ||
                snapshot.recoveryVersion != MatchRecoverySnapshot.CurrentRecoveryVersion ||
                !IsCheckpoint(snapshot.checkpoint) ||
                snapshot.turn < 1 ||
                snapshot.scheduleTurnCount < 1 ||
                snapshot.turn > snapshot.scheduleTurnCount ||
                snapshot.catalogEntryCount < 1 ||
                snapshot.catalogEntryCount > MinigameCatalog.RegisteredCount ||
                !string.Equals(
                    snapshot.catalogFingerprint,
                    MinigameCatalog.GetRecoveryFingerprint(
                        snapshot.catalogEntryCount),
                    StringComparison.Ordinal) ||
                snapshot.scheduleEntries == null ||
                snapshot.scheduleEntries.Length != snapshot.scheduleTurnCount ||
                snapshot.remainingMinigameSlots < 0 ||
                snapshot.remainingMinigameSlots > snapshot.scheduleTurnCount ||
                snapshot.boardEffectRevision < 1 ||
                snapshot.settledMinigameTurn < -1 ||
                snapshot.players == null ||
                snapshot.players.Length != MultiplayerConstants.MaxPlayers ||
                snapshot.itemShops == null ||
                snapshot.itemShops.Length != ItemShopRules.ShopCount ||
                snapshot.tombstones == null ||
                snapshot.nextTombstoneId < 0 ||
                !TryDecodeMinigameSeed(snapshot.currentMinigameSeed, out _))
            {
                return false;
            }

            var catalogPrefix = new HashSet<int>();
            for (var index = 0; index < snapshot.catalogEntryCount; index++)
            {
                catalogPrefix.Add((int)MinigameCatalog.GetRegisteredGame(index));
            }
            if (!IsKnownMinigame(snapshot.currentMinigame, catalogPrefix))
            {
                return false;
            }

            var scheduled = new HashSet<int>();
            for (var index = 0; index < snapshot.scheduleEntries.Length; index++)
            {
                var raw = snapshot.scheduleEntries[index];
                if (!IsKnownMinigame(raw, catalogPrefix) ||
                    raw != (int)ScheduledMinigameId.Skip && !scheduled.Add(raw))
                {
                    return false;
                }
            }
            if (scheduled.Count != Math.Min(
                    snapshot.scheduleTurnCount,
                    snapshot.catalogEntryCount))
            {
                return false;
            }

            if (snapshot.checkpoint == MatchRecoveryCheckpoint.TurnOverview &&
                snapshot.currentMinigame != (int)ScheduledMinigameId.Skip)
            {
                return false;
            }
            if (snapshot.checkpoint == MatchRecoveryCheckpoint.MinigameIntroReady &&
                snapshot.currentMinigame != snapshot.scheduleEntries[snapshot.turn - 1])
            {
                return false;
            }
            if (snapshot.checkpoint == MatchRecoveryCheckpoint.MatchComplete &&
                snapshot.turn != snapshot.scheduleTurnCount)
            {
                return false;
            }

            if (!IsValidKeyShop(snapshot.keyShop))
            {
                return false;
            }

            var playerKeys = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < snapshot.players.Length; index++)
            {
                var player = snapshot.players[index];
                if (!IsValidPlayer(player) || !playerKeys.Add(player.playerKey))
                {
                    return false;
                }
            }

            for (var index = 0; index < snapshot.itemShops.Length; index++)
            {
                if (!IsValidItemShop(snapshot.itemShops[index]))
                {
                    return false;
                }
            }

            var tombstoneIds = new HashSet<int>();
            for (var index = 0; index < snapshot.tombstones.Length; index++)
            {
                var tombstone = snapshot.tombstones[index];
                if (tombstone.id < 0 || tombstone.gold < 0 ||
                    !IsFinite(tombstone.position) ||
                    !tombstoneIds.Add(tombstone.id) ||
                    tombstone.id > snapshot.nextTombstoneId)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidPlayer(MatchRecoveryPlayerSnapshot player)
        {
            if (player == null || string.IsNullOrWhiteSpace(player.playerKey) ||
                !player.hasLogicalTile ||
                player.traversalHistory == null ||
                player.traversalHistory.Length == 0 ||
                player.traversalHistory[player.traversalHistory.Length - 1] !=
                player.logicalTile ||
                float.IsNaN(player.yaw) || float.IsInfinity(player.yaw) ||
                player.maxHealth < 1 || player.currentHealth < 1 ||
                player.currentHealth > player.maxHealth ||
                player.keyCount < 0 || player.gold < 0 ||
                player.minigameWins < 0 || player.peakGoldHeld < 0 ||
                player.totalGoldEarned < 0 || player.minigameLastPlaces < 0 ||
                player.itemUses < 0 || player.damageTaken < 0 ||
                player.playerDamageDealt < 0 ||
                !IsFinite(player.personalProtectionRemaining) ||
                player.personalProtectionRemaining < 0d ||
                (player.occupiedItemMask & ~0b0000_0111) != 0)
            {
                return false;
            }

            return IsValidInventoryItem(player.itemSlot0, player.occupiedItemMask, 0) &&
                   IsValidInventoryItem(player.itemSlot1, player.occupiedItemMask, 1) &&
                   IsValidInventoryItem(player.itemSlot2, player.occupiedItemMask, 2);
        }

        private static bool IsValidKeyShop(MatchRecoveryKeyShopSnapshot shop)
        {
            var lifecycle = (KeyShopLifecycleState)shop.lifecycle;
            if (shop.revision < 0)
            {
                return false;
            }

            if (lifecycle == KeyShopLifecycleState.Inactive)
            {
                return !shop.hasLocation && shop.location == default;
            }

            return lifecycle == KeyShopLifecycleState.Active &&
                   shop.hasLocation && shop.revision > 0;
        }

        private static bool IsValidInventoryItem(byte raw, byte mask, int slot)
        {
            var occupied = (mask & (1 << slot)) != 0;
            var item = (PrototypeItemId)raw;
            return occupied
                ? PrototypeItemCatalog.IsValid(item)
                : item == PrototypeItemId.None;
        }

        private static bool IsValidItemShop(MatchRecoveryItemShopSnapshot shop)
        {
            if (!shop.active)
            {
                return shop.offers == null || shop.offers.Length == 0;
            }
            if (shop.revision < 0 || shop.appearedTurn < 1 ||
                (shop.soldMask & ~((1 << ItemShopRules.OfferCount) - 1)) != 0 ||
                shop.offers == null ||
                shop.offers.Length != ItemShopRules.OfferCount)
            {
                return false;
            }

            for (var index = 0; index < shop.offers.Length; index++)
            {
                if (!PrototypeItemCatalog.IsValid(
                        (PrototypeItemId)shop.offers[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsKnownMinigame(
            int raw,
            HashSet<int> catalogPrefix)
        {
            if (raw < byte.MinValue || raw > byte.MaxValue)
            {
                return false;
            }

            var id = (ScheduledMinigameId)raw;
            return id == ScheduledMinigameId.Skip ||
                   catalogPrefix.Contains(raw);
        }

        private static bool IsCheckpoint(MatchRecoveryCheckpoint checkpoint)
        {
            return checkpoint == MatchRecoveryCheckpoint.TurnOverview ||
                   checkpoint == MatchRecoveryCheckpoint.MinigameIntroReady ||
                   checkpoint == MatchRecoveryCheckpoint.MatchComplete;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
