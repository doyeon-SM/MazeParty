using System;
using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        public MatchRecoveryPlayerSnapshot CaptureMatchRecoverySnapshotOnServer(
            string playerKey)
        {
            if (!IsServer || AssignedSlot < 0 ||
                string.IsNullOrWhiteSpace(playerKey))
            {
                return null;
            }

            var logicalTile = _traversal.IsInitialized
                ? _traversal.CurrentTile
                : null;
            return new MatchRecoveryPlayerSnapshot
            {
                playerKey = playerKey,
                hasLogicalTile = logicalTile != null,
                logicalTile = logicalTile != null
                    ? logicalTile.Coordinate
                    : default,
                traversalHistory = _traversal.CaptureHistoryCoordinates(),
                yaw = transform.rotation.eulerAngles.y,
                occupiedItemMask = (byte)(_occupiedItemMask.Value & 0b0000_0111),
                itemSlot0 = _itemSlot0.Value,
                itemSlot1 = _itemSlot1.Value,
                itemSlot2 = _itemSlot2.Value,
                maxHealth = _maxHealth.Value,
                currentHealth = _currentHealth.Value,
                keyCount = _keyCount.Value,
                gold = _gold.Value,
                minigameWins = _minigameWins.Value,
                peakGoldHeld = _matchAwardProgress.PeakGoldHeld,
                totalGoldEarned = _matchAwardProgress.TotalGoldEarned,
                minigameLastPlaces = _matchAwardProgress.MinigameLastPlaces,
                itemUses = _matchAwardProgress.ItemUses,
                damageTaken = _matchAwardProgress.DamageTaken,
                playerDamageDealt = _matchAwardProgress.PlayerDamageDealt,
                pendingCombatProtection = _pendingCombatProtection.Value,
                personalProtectionRemaining =
                    PersonalItemProtectionRemaining,
                appearance = _appearance.Value
            };
        }

        public bool RestoreMatchRecoverySnapshotOnServer(
            MatchRecoveryPlayerSnapshot snapshot)
        {
            if (!IsServer || snapshot == null ||
                string.IsNullOrWhiteSpace(snapshot.playerKey) ||
                !snapshot.hasLogicalTile ||
                snapshot.maxHealth < 1 ||
                snapshot.currentHealth < 1 ||
                snapshot.currentHealth > snapshot.maxHealth ||
                snapshot.keyCount < 0 || snapshot.gold < 0 ||
                snapshot.minigameWins < 0 ||
                float.IsNaN(snapshot.yaw) || float.IsInfinity(snapshot.yaw) ||
                double.IsNaN(snapshot.personalProtectionRemaining) ||
                double.IsInfinity(snapshot.personalProtectionRemaining) ||
                snapshot.personalProtectionRemaining < 0d ||
                snapshot.traversalHistory == null ||
                snapshot.traversalHistory.Length == 0 ||
                snapshot.traversalHistory[
                    snapshot.traversalHistory.Length - 1] != snapshot.logicalTile ||
                !HasValidRecoveryInventory(snapshot))
            {
                return false;
            }

            ResolveTopology();
            if (_topology == null ||
                !_topology.TryGetTile(snapshot.logicalTile, out var logicalTile) ||
                logicalTile == null)
            {
                return false;
            }

            StopServerInputOnServer();
            ClearUtilityEffectsOnServer();
            CancelHandGestureOnServer();
            _swapChannel.Clear();
            _swapSeconds.Value = 0f;
            _itemCooldownRemaining = 0d;
            _privateRoll.Value = 0;
            ResetBoardTravelPreviewOnServer();
            _remainingMoves.Value = 0;
            _choiceResolution.Value = (byte)ItemChoiceResolution.NotStarted;
            _selectedItemSlot.Value = -1;
            _equippedItem.Value = 0;
            _itemCharges.Value = 0;
            _rangeDieItem.Value = 0;
            _doubleDice.Value = false;
            _firstDieResult.Value = 0;
            _secondDieResult.Value = 0;
            _actionState.Value = (byte)PlayerBoardActionState.Hidden;
            _combatState.Value = (byte)NetworkCombatState.None;
            _combatHealth.Value = 0;
            _pendingCombatProtection.Value = snapshot.pendingCombatProtection;
            var match = NetworkMatchState.Instance;
            var now = match != null
                ? match.SynchronizedNow
                : Time.unscaledTimeAsDouble;
            var paused = match != null && match.IsGlobalSimulationPaused;
            _personalProtectionEndsAt.Value = !paused &&
                snapshot.personalProtectionRemaining > 0d
                    ? now + snapshot.personalProtectionRemaining
                    : 0d;
            _pausedPersonalProtectionRemaining.Value = paused
                ? snapshot.personalProtectionRemaining
                : 0d;
            _boardDeathEndsAt = 0d;
            _pausedBoardDeathRemaining = 0d;
            _combatKnockbackVelocity = Vector3.zero;

            _occupiedItemMask.Value = (byte)(snapshot.occupiedItemMask & 0b0000_0111);
            _itemSlot0.Value = snapshot.itemSlot0;
            _itemSlot1.Value = snapshot.itemSlot1;
            _itemSlot2.Value = snapshot.itemSlot2;
            _maxHealth.Value = snapshot.maxHealth;
            _currentHealth.Value = snapshot.currentHealth;
            _keyCount.Value = snapshot.keyCount;
            _gold.Value = snapshot.gold;
            _minigameWins.Value = snapshot.minigameWins;
            _matchAwardProgress = MatchAwardProgress.Restore(
                snapshot.peakGoldHeld,
                snapshot.totalGoldEarned,
                snapshot.minigameLastPlaces,
                snapshot.itemUses,
                snapshot.damageTaken,
                snapshot.playerDamageDealt);
            _appearance.Value = snapshot.appearance.Sanitized();

            var rotation = Quaternion.Euler(0f, snapshot.yaw, 0f);
            TeleportController(logicalTile.GetRecoveryCenter(1f), rotation);
            _serverYaw = snapshot.yaw;
            _serverPitch = 0f;
            _restoredFromSnapshot = true;
            _boardPositionInitialized = true;
            if (snapshot.traversalHistory != null &&
                snapshot.traversalHistory.Length > 0 &&
                _traversal.RestoreHistory(
                    _topology,
                    snapshot.traversalHistory,
                    0))
            {
                SyncLogicalTileOnServer();
            }
            else
            {
                _traversal.Begin(logicalTile, 0);
                SyncLogicalTileOnServer();
            }

            RefreshBoundaryWallsOnServer();
            ApplyCombatColliderState(NetworkCombatState.None);
            return _traversal.IsInitialized;
        }

        private static bool HasValidRecoveryInventory(
            MatchRecoveryPlayerSnapshot snapshot)
        {
            if ((snapshot.occupiedItemMask & ~0b0000_0111) != 0)
            {
                return false;
            }

            return IsValidRecoveryItem(snapshot.itemSlot0, snapshot.occupiedItemMask, 0) &&
                   IsValidRecoveryItem(snapshot.itemSlot1, snapshot.occupiedItemMask, 1) &&
                   IsValidRecoveryItem(snapshot.itemSlot2, snapshot.occupiedItemMask, 2);
        }

        private static bool IsValidRecoveryItem(byte raw, byte mask, int slot)
        {
            var occupied = (mask & (1 << slot)) != 0;
            var item = (PrototypeItemId)raw;
            return occupied
                ? PrototypeItemCatalog.IsValid(item)
                : item == PrototypeItemId.None;
        }
    }
}
