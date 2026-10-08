using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.ArenaCombat;
using MazeParty.Gameplay.Minigames.Race;
using MazeParty.Gameplay.Minigames.SequenceMemory;
using MazeParty.Gameplay.Minigames.WrongWay;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        public ReconnectSnapshot CreateReconnectSnapshotOnServer()
        {
            var logicalTile = _traversal.IsInitialized ? _traversal.CurrentTile : null;
            return new ReconnectSnapshot
            {
                IsValid = IsServer && _slot.Value >= 0,
                Position = transform.position,
                Rotation = transform.rotation,
                Roll = _privateRoll.Value,
                RemainingMoves = _remainingMoves.Value,
                ChoiceResolution = (ItemChoiceResolution)_choiceResolution.Value,
                SelectedItemSlot = _selectedItemSlot.Value,
                EquippedItem = _equippedItem.Value, ItemCharges = _itemCharges.Value,
                Cloaked = _cloaked.Value, RangeDieItem = _rangeDieItem.Value,
                SwapTargetSlot = _swapChannel.TargetSlot, SwapRemaining = _swapChannel.Remaining,
                ItemCooldownRemaining = _itemCooldownRemaining, DoubleDice = _doubleDice.Value,
                FirstDieResult = _firstDieResult.Value, SecondDieResult = _secondDieResult.Value,
                OccupiedItemMask = _occupiedItemMask.Value,
                ItemSlot0 = _itemSlot0.Value,
                ItemSlot1 = _itemSlot1.Value,
                ItemSlot2 = _itemSlot2.Value,
                MaxHealth = _maxHealth.Value,
                CurrentHealth = _currentHealth.Value,
                KeyCount = _keyCount.Value,
                Gold = _gold.Value,
                MinigameWins = _minigameWins.Value,
                MatchAwardProgress = _matchAwardProgress,
                ActionState = (PlayerBoardActionState)_actionState.Value,
                CombatState = CombatState,
                CombatHealth = _combatHealth.Value,
                PendingCombatProtection = _pendingCombatProtection.Value,
                PersonalProtectionRemaining = PersonalItemProtectionRemaining,
                DeathPresentationRemaining = _pausedBoardDeathRemaining > 0d
                    ? _pausedBoardDeathRemaining
                    : Math.Max(0d, _boardDeathEndsAt -
                        (NetworkMatchState.Instance != null
                            ? NetworkMatchState.Instance.SynchronizedNow
                            : Time.unscaledTimeAsDouble)),
                Appearance = _appearance.Value,
                DisplayName = _displayName.Value.ToString(),
                HasLogicalCurrentTile = logicalTile != null,
                LogicalCurrentTileCoordinate = logicalTile != null
                    ? logicalTile.Coordinate
                    : default,
                TraversalHistory = _traversal.CaptureHistoryCoordinates(),
                HasTurnRouteOrigin = _privateRoll.Value > 0 &&
                                     _hasTurnRouteOrigin.Value,
                TurnRouteOrigin = _turnRouteOrigin.Value,
                TurnRouteStepOffset = _turnRouteStepOffset.Value,
                BoardRouteChoices = CaptureBoardRouteChoicesOnServer()
            };
        }

        public bool RestoreReconnectSnapshotOnServer(ReconnectSnapshot snapshot)
        {
            if (!IsServer || !snapshot.IsValid)
            {
                return false;
            }

            ResolveTopology();
            if (_topology == null)
            {
                return false;
            }

            BoardTile logicalTile = null;
            if (snapshot.HasLogicalCurrentTile &&
                (!_topology.TryGetTile(snapshot.LogicalCurrentTileCoordinate, out logicalTile) ||
                 logicalTile == null))
            {
                return false;
            }

            var restoredRoll = Mathf.Clamp(
                snapshot.Roll,
                0,
                WorldDieAuthorityModel.MaximumFace * 2);
            _privateRoll.Value = 0;
            var restoredTurnRoute = BoardTravelRouteStatePolicy.Restore(
                _topology,
                restoredRoll,
                snapshot.RemainingMoves,
                snapshot.HasTurnRouteOrigin,
                snapshot.TurnRouteOrigin,
                snapshot.TurnRouteStepOffset,
                snapshot.BoardRouteChoices);
            _turnRouteOrigin.Value = restoredTurnRoute.Active
                ? restoredTurnRoute.Origin
                : default;
            _turnRouteStepOffset.Value = restoredTurnRoute.Active
                ? restoredTurnRoute.StartingStep
                : 0;
            _hasTurnRouteOrigin.Value = restoredTurnRoute.Active;
            _remainingMoves.Value = Mathf.Max(0, snapshot.RemainingMoves);
            _choiceResolution.Value = (byte)snapshot.ChoiceResolution;
            _selectedItemSlot.Value = snapshot.SelectedItemSlot;
            _equippedItem.Value = snapshot.EquippedItem; _itemCharges.Value = snapshot.ItemCharges;
            _cloaked.Value = snapshot.Cloaked; _rangeDieItem.Value = snapshot.RangeDieItem;
            _swapChannel.Clear();
            if (snapshot.SwapRemaining > 0) _swapChannel.Begin(AssignedSlot, snapshot.SwapTargetSlot, snapshot.SwapRemaining);
            _swapSeconds.Value = (float)_swapChannel.Remaining;
            _itemCooldownRemaining = snapshot.ItemCooldownRemaining; _doubleDice.Value = snapshot.DoubleDice;
            _firstDieResult.Value = snapshot.FirstDieResult; _secondDieResult.Value = snapshot.SecondDieResult;
            _occupiedItemMask.Value = snapshot.OccupiedItemMask;
            _itemSlot0.Value = snapshot.ItemSlot0;
            _itemSlot1.Value = snapshot.ItemSlot1;
            _itemSlot2.Value = snapshot.ItemSlot2;
            _maxHealth.Value = Mathf.Max(1, snapshot.MaxHealth);
            _currentHealth.Value = PlayerStatRules.ClampHealth(
                snapshot.CurrentHealth,
                _maxHealth.Value);
            _keyCount.Value = Mathf.Max(0, snapshot.KeyCount);
            _gold.Value = Mathf.Max(0, snapshot.Gold);
            _minigameWins.Value = Mathf.Max(0, snapshot.MinigameWins);
            _matchAwardProgress = snapshot.MatchAwardProgress.Sanitized();
            _matchAwardProgress.RecordGoldBalance(_gold.Value, _gold.Value);
            _actionState.Value = (byte)snapshot.ActionState;
            _combatState.Value = (byte)snapshot.CombatState;
            _combatHealth.Value = Mathf.Clamp(
                snapshot.CombatHealth,
                0,
                BoardCombatRules.TemporaryHealth);
            _pendingCombatProtection.Value = snapshot.PendingCombatProtection;
            var match = NetworkMatchState.Instance;
            var now = match != null ? match.SynchronizedNow : Time.unscaledTimeAsDouble;
            var paused = match != null && match.IsGlobalSimulationPaused;
            _pausedPersonalProtectionRemaining.Value = paused
                ? snapshot.PersonalProtectionRemaining
                : 0d;
            _personalProtectionEndsAt.Value = !paused &&
                snapshot.PersonalProtectionRemaining > 0d
                    ? now + snapshot.PersonalProtectionRemaining
                    : 0d;
            _pausedBoardDeathRemaining = paused
                ? snapshot.DeathPresentationRemaining
                : 0d;
            _boardDeathEndsAt = !paused && snapshot.DeathPresentationRemaining > 0d
                ? now + snapshot.DeathPresentationRemaining
                : 0d;
            _appearance.Value = snapshot.Appearance.Sanitized();
            _displayName.Value = new FixedString64Bytes(
                PlayerProfilePreferences.SanitizeDisplayName(snapshot.DisplayName));
            _serverYaw = snapshot.Rotation.eulerAngles.y;
            TeleportController(snapshot.Position, snapshot.Rotation);
            var networkTransform = GetComponent<NetworkTransform>();
            if (networkTransform != null)
            {
                networkTransform.Teleport(
                    snapshot.Position,
                    snapshot.Rotation,
                    transform.localScale);
            }
            _restoredFromSnapshot = true;
            _boardPositionInitialized = true;
            if (snapshot.TraversalHistory != null &&
                _traversal.RestoreHistory(
                    _topology,
                    snapshot.TraversalHistory,
                    _remainingMoves.Value))
            {
                SyncLogicalTileOnServer();
            }
            else if (logicalTile != null)
            {
                _traversal.Begin(logicalTile, _remainingMoves.Value);
                SyncLogicalTileOnServer();
            }
            else
            {
                EnsureTraversalInitialized();
            }

            ApplyRestoredBoardRouteChoicesOnServer(
                restoredTurnRoute.Choices);
            // Publish the roll only after its validated origin and ordered fork
            // choices have been restored for the reconnecting owner.
            _privateRoll.Value = restoredRoll;

            RefreshBoundaryWallsOnServer();
            ApplyCombatColliderState(CombatState);
            return _traversal.IsInitialized;
        }

        private BoardRouteChoice[] CaptureBoardRouteChoicesOnServer()
        {
            var result = new BoardRouteChoice[_boardRouteChoices.Count];
            for (var index = 0; index < result.Length; index++)
                result[index] = _boardRouteChoices[index];
            return result;
        }

        private void ApplyRestoredBoardRouteChoicesOnServer(
            BoardRouteChoice[] choices)
        {
            _boardRouteChoices.Clear();
            if (!IsServer || choices == null)
                return;

            for (var choiceIndex = 0; choiceIndex < choices.Length; choiceIndex++)
                _boardRouteChoices.Add(choices[choiceIndex]);
        }
    }
}
