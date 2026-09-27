using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.ArenaCombat;
using MazeParty.Gameplay.Minigames.Race;
using MazeParty.Gameplay.Minigames.SequenceMemory;
using MazeParty.Gameplay.Minigames.WrongWay;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        public void InitializeBoardStateOnServer()
        {
            if (!IsServer || _slot.Value < 0 || !IsBoardLoaded())
            {
                return;
            }

            ResolveTopology();
            if (_topology == null)
            {
                return;
            }

            if (!_restoredFromSnapshot && !_boardPositionInitialized)
            {
                var start = FindStartTileForSlot(_slot.Value);
                if (start != null)
                {
                    TeleportController(start.GetRecoveryCenter(1f), Quaternion.identity);
                }
            }

            _boardPositionInitialized = true;
            EnsureTraversalInitialized();
            RefreshBoundaryWallsOnServer();
        }

        public void PrepareForOverviewOnServer()
        {
            if (!IsServer)
            {
                return;
            }

            StopServerInputOnServer();
            _privateRoll.Value = 0;
            ClearUtilityEffectsOnServer();
            _rangeDieItem.Value = 0;
            _doubleDice.Value = false;
            _firstDieResult.Value = 0;
            _secondDieResult.Value = 0;
            _remainingMoves.Value = 0;
            _choiceResolution.Value = (byte)ItemChoiceResolution.NotStarted;
            _selectedItemSlot.Value = -1;
            _equippedItem.Value = 0;
            _itemCharges.Value = 0;
            _actionState.Value = (byte)PlayerBoardActionState.Hidden;
            if (_traversal.IsInitialized)
            {
                _traversal.ResetMoves(0);
            }
            HideBoundaryWalls();
        }

        public void BeginActionOnServer(int _)
        {
            if (!IsServer)
            {
                return;
            }

            StopServerInputOnServer();
            _privateRoll.Value = 0;
            ClearUtilityEffectsOnServer();
            _rangeDieItem.Value = 0;
            _doubleDice.Value = false;
            _firstDieResult.Value = 0;
            _secondDieResult.Value = 0;
            _remainingMoves.Value = 0;
            _choiceResolution.Value = (byte)ItemChoiceResolution.Pending;
            _selectedItemSlot.Value = -1;
            _equippedItem.Value = 0;
            _itemCharges.Value = 0;
            _actionState.Value = (byte)PlayerBoardActionState.Dice;
            if (_pendingCombatProtection.Value)
            {
                var match = NetworkMatchState.Instance;
                var now = match != null ? match.SynchronizedNow : Time.unscaledTimeAsDouble;
                _personalProtectionEndsAt.Value = Math.Max(
                    _personalProtectionEndsAt.Value,
                    now + BoardCombatRules.NextActionItemProtectionSeconds);
                _pendingCombatProtection.Value = false;
            }
            EnsureTraversalInitialized();
            if (_traversal.IsInitialized)
            {
                _traversal.ResetMoves(0);
            }
            RefreshBoundaryWallsOnServer();
        }

        public void EndActionOnServer()
        {
            if (!IsServer)
            {
                return;
            }

            StopServerInputOnServer();
            if (_selectedItemSlot.Value >= 0)
            {
                ConsumeSelectedItemOnServer();
            }

            _choiceResolution.Value = (byte)ItemChoiceResolution.NotStarted;
            _privateRoll.Value = 0;
            ClearUtilityEffectsOnServer();
            _rangeDieItem.Value = 0;
            _doubleDice.Value = false;
            _firstDieResult.Value = 0;
            _secondDieResult.Value = 0;
            _remainingMoves.Value = 0;
            _actionState.Value = (byte)PlayerBoardActionState.Hidden;
            if (_traversal.IsInitialized)
            {
                _traversal.ResetMoves(0);
            }
            HideBoundaryWalls();
        }

        public bool ResolveItemChoiceOnServer(int slotIndex)
        {
            if (!IsServer || (ItemChoiceResolution)_choiceResolution.Value != ItemChoiceResolution.Pending ||
                slotIndex < 0 || slotIndex >= GameplayInventory.Capacity ||
                (_occupiedItemMask.Value & (1 << slotIndex)) == 0)
            {
                return false;
            }

            _selectedItemSlot.Value = slotIndex;
            InitializeSelectedItem();
            _choiceResolution.Value = (byte)ItemChoiceResolution.ItemSelected;
            return true;
        }

        public bool ResolveNoItemChoiceOnServer(bool timedOut)
        {
            if (!IsServer || (ItemChoiceResolution)_choiceResolution.Value != ItemChoiceResolution.Pending)
            {
                return false;
            }

            _selectedItemSlot.Value = -1;
            _equippedItem.Value = 0;
            _itemCharges.Value = 0;
            _choiceResolution.Value = (byte)(timedOut
                ? ItemChoiceResolution.TimedOut
                : ItemChoiceResolution.DoNotUse);
            return true;
        }

        public bool ConsumeSelectedItemOnServer()
        {
            if (!IsServer)
            {
                return false;
            }

            var selected = _selectedItemSlot.Value;
            if (selected < 0 || selected >= GameplayInventory.Capacity ||
                (_occupiedItemMask.Value & (1 << selected)) == 0)
            {
                return false;
            }

            _occupiedItemMask.Value = (byte)(_occupiedItemMask.Value & ~(1 << selected));
            SetItemSlotValue(selected, PrototypeItemId.None);
            _selectedItemSlot.Value = -1;
            _equippedItem.Value = 0;
            _itemCharges.Value = 0;
            return true;
        }

        public void SetRollOnServer(int roll)
        {
            if (!IsServer)
            {
                return;
            }

            var safeRoll = Mathf.Clamp(
                roll,
                0,
                WorldDieAuthorityModel.MaximumFace * 2);
            _privateRoll.Value = safeRoll;
            _remainingMoves.Value = safeRoll;
            _actionState.Value = safeRoll > 0
                ? (byte)PlayerBoardActionState.Moving
                : (byte)PlayerBoardActionState.Dice;
            EnsureTraversalInitialized();
            if (_traversal.IsInitialized)
            {
                _traversal.ResetMoves(safeRoll);
            }
            RefreshBoundaryWallsOnServer();
        }

        public void MarkArrivedOnServer()
        {
            SetActionStateOnServer(PlayerBoardActionState.Arrived);
        }

        public bool ForceSettleRemainingMovesOnServer(BoardTile keyShopTile)
        {
            if (!IsServer)
            {
                return false;
            }

            ResolveTopology();
            EnsureTraversalInitialized();
            if (_topology == null || !_traversal.IsInitialized)
            {
                return false;
            }

            var source = _traversal.CurrentTile;
            var history = _traversal.History;
            var previousTile = history.Count > 1 ? history[history.Count - 2] : null;
            var path = _topology.PlanForcedAdvancePath(
                source,
                previousTile,
                transform.forward,
                keyShopTile,
                _remainingMoves.Value);

            var destination = source;
            var beforeDestination = source;
            for (var index = 0; index < path.Count; index++)
            {
                var gate = path[index];
                if (!_traversal.TryForceCommit(gate))
                {
                    break;
                }

                beforeDestination = destination;
                destination = gate.Destination;
            }

            _remainingMoves.Value = 0;
            if (destination == source)
            {
                _traversal.ResetMoves(0);
                RefreshBoundaryWallsOnServer();
                return false;
            }

            var direction = destination.WorldCenter - beforeDestination.WorldCenter;
            direction.y = 0f;
            var rotation = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : transform.rotation;
            TeleportController(destination.GetRecoveryCenter(1f), rotation);
            _serverYaw = rotation.eulerAngles.y;
            _serverPitch = 0f;
            SyncLogicalTileOnServer();
            RefreshBoundaryWallsOnServer();
            return true;
        }

        public void StopServerInputOnServer()
        {
            if (IsServer)
            {
                _serverInput = Vector2.zero;
                _serverQuietWalkHeld = false;
                _isQuietWalking.Value = false;
                UpdateCrouchStateOnServer(false);
            }
        }
    }
}
