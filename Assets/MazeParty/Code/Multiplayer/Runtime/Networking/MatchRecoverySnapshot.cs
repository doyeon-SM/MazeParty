using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Stable points from which a host can rebuild authoritative match state.
    /// Mid-action and mid-minigame state is deliberately never persisted.
    /// </summary>
    public enum MatchRecoveryCheckpoint : byte
    {
        TurnOverview = 1,
        MinigameIntroReady = 2,
        MatchComplete = 3
    }

    [Serializable]
    public sealed class MatchRecoverySnapshot
    {
        public const int LegacyRecoveryVersionWithoutMines = 1;
        public const int LegacyRecoveryVersionWithoutMapIdentity = 2;
        public const int CurrentRecoveryVersion = 3;

        public int recoveryVersion = CurrentRecoveryVersion;
        public MatchRecoveryCheckpoint checkpoint;
        public int turn;
        public int settledMinigameTurn;
        public int scheduleSeed;
        public int scheduleTurnCount;
        public int catalogEntryCount;
        public string catalogFingerprint = string.Empty;
        public int[] scheduleEntries = Array.Empty<int>();
        public int currentMinigame;
        public string currentMinigameSeed = "0";
        public int remainingMinigameSlots;
        public int boardEffectSeed;
        public int boardEffectRevision;
        public string boardMapId = string.Empty;
        public int boardMapContentVersion;
        public MatchRecoveryKeyShopSnapshot keyShop;
        public MatchRecoveryItemShopSnapshot[] itemShops =
            Array.Empty<MatchRecoveryItemShopSnapshot>();
        public MatchRecoveryTombstoneSnapshot[] tombstones =
            Array.Empty<MatchRecoveryTombstoneSnapshot>();
        public int nextTombstoneId;
        public MatchRecoveryMineSnapshot[] mines =
            Array.Empty<MatchRecoveryMineSnapshot>();
        public MatchRecoveryPlayerSnapshot[] players =
            Array.Empty<MatchRecoveryPlayerSnapshot>();
    }

    [Serializable]
    public sealed class MatchRecoveryPlayerSnapshot
    {
        public string playerKey = string.Empty;
        public bool hasLogicalTile;
        public Vector2Int logicalTile;
        public Vector2Int[] traversalHistory = Array.Empty<Vector2Int>();
        public float yaw;
        public byte occupiedItemMask;
        public byte itemSlot0;
        public byte itemSlot1;
        public byte itemSlot2;
        public int maxHealth;
        public int currentHealth;
        public int keyCount;
        public int gold;
        public int minigameWins;
        public int peakGoldHeld;
        public int totalGoldEarned;
        public int minigameLastPlaces;
        public int itemUses;
        public int damageTaken;
        public int playerDamageDealt;
        public bool pendingCombatProtection;
        public double personalProtectionRemaining;
        public PlayerAppearanceState appearance;
    }

    [Serializable]
    public struct MatchRecoveryKeyShopSnapshot
    {
        public byte lifecycle;
        public bool hasLocation;
        public Vector2Int location;
        public int revision;
    }

    [Serializable]
    public struct MatchRecoveryItemShopSnapshot
    {
        public bool active;
        public Vector2Int location;
        public int revision;
        public int appearedTurn;
        public byte soldMask;
        public int[] offers;
    }

    [Serializable]
    public struct MatchRecoveryTombstoneSnapshot
    {
        public int id;
        public Vector3 position;
        public int gold;
    }

    [Serializable]
    public struct MatchRecoveryMineSnapshot
    {
        // Index into MatchRecoverySnapshot.players at capture time. Recovery
        // resolves that player's key to the current authoritative seat.
        public int ownerSlot;
        public Vector3 position;
        public float armRemaining;
    }

    /// <summary>
    /// Resolves persisted mine ownership through the stable player identity.
    /// Recovery rosters are order-independent, so a saved seat is never a
    /// durable owner identifier by itself.
    /// </summary>
    public static class MatchRecoveryMineOwnership
    {
        public static bool TryMapToCurrentSlots(
            MatchRecoveryMineSnapshot[] mines,
            MatchRecoveryPlayerSnapshot[] savedPlayersBySlot,
            MatchRecoveryPlayerSnapshot[] currentPlayersBySlot,
            out int[] currentOwnerSlots)
        {
            currentOwnerSlots = Array.Empty<int>();
            if (mines == null || savedPlayersBySlot == null ||
                currentPlayersBySlot == null ||
                savedPlayersBySlot.Length != MultiplayerConstants.MaxPlayers ||
                currentPlayersBySlot.Length != MultiplayerConstants.MaxPlayers)
            {
                return false;
            }

            var currentSlotByPlayerKey = new Dictionary<string, int>(
                StringComparer.Ordinal);
            for (var slot = 0; slot < currentPlayersBySlot.Length; slot++)
            {
                var playerKey = currentPlayersBySlot[slot]?.playerKey;
                if (string.IsNullOrWhiteSpace(playerKey) ||
                    !currentSlotByPlayerKey.TryAdd(playerKey, slot))
                {
                    return false;
                }
            }

            var savedPlayerKeys = new HashSet<string>(StringComparer.Ordinal);
            for (var slot = 0; slot < savedPlayersBySlot.Length; slot++)
            {
                var playerKey = savedPlayersBySlot[slot]?.playerKey;
                if (string.IsNullOrWhiteSpace(playerKey) ||
                    !savedPlayerKeys.Add(playerKey) ||
                    !currentSlotByPlayerKey.ContainsKey(playerKey))
                {
                    return false;
                }
            }

            var mappedSlots = new int[mines.Length];
            for (var index = 0; index < mines.Length; index++)
            {
                var savedOwnerSlot = mines[index].ownerSlot;
                if (savedOwnerSlot < 0 ||
                    savedOwnerSlot >= savedPlayersBySlot.Length)
                {
                    return false;
                }

                var savedOwnerKey =
                    savedPlayersBySlot[savedOwnerSlot].playerKey;
                if (!currentSlotByPlayerKey.TryGetValue(
                        savedOwnerKey,
                        out mappedSlots[index]))
                {
                    return false;
                }
            }

            currentOwnerSlots = mappedSlots;
            return true;
        }
    }
}
