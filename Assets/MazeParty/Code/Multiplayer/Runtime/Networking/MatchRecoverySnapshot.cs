using System;
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
        public const int CurrentRecoveryVersion = 1;

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
        public MatchRecoveryKeyShopSnapshot keyShop;
        public MatchRecoveryItemShopSnapshot[] itemShops =
            Array.Empty<MatchRecoveryItemShopSnapshot>();
        public MatchRecoveryTombstoneSnapshot[] tombstones =
            Array.Empty<MatchRecoveryTombstoneSnapshot>();
        public int nextTombstoneId;
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
}
