using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        public ItemShopSnapshot GetItemShopSnapshot(int shopIndex)
        {
            switch (shopIndex)
            {
                case 0: return _itemShop0.Value;
                case 1: return _itemShop1.Value;
                default: return default;
            }
        }

        public bool TryPurchaseKeyOnServer(
            NetworkPlayerAvatar avatar,
            int expectedRevision)
        {
            if (!CanProcessActionRequest(avatar) || _keyShopRuntime == null ||
                !_keyShopRuntime.IsActive || expectedRevision != _keyShopRevision.Value ||
                !PlayerStatRules.CanPurchaseKey(avatar.Gold) ||
                !CanAccessCoordinateOnServer(avatar, _keyShopRuntime.Location))
            {
                return false;
            }

            var blocked = GetOccupiedAndItemShopCoordinates();
            if (!_keyShopRuntime.TryBeginPurchaseRelocation(
                    _boardTopology.Tiles,
                    blocked,
                    out _))
            {
                return false;
            }

            if (!avatar.TryPurchaseKeyOnServer())
            {
                throw new InvalidOperationException(
                    "A validated key purchase failed after reserving its relocation.");
            }

            SyncKeyShopSnapshot();
            var now = ServerNow;
            _keyShopAppearanceEndsAt = now + 0.75d;
            BeginKeyShopRevealOnServer(now);
            return true;
        }

        public bool TryPurchaseItemOnServer(
            NetworkPlayerAvatar avatar,
            int shopIndex,
            int offerIndex,
            int expectedRevision)
        {
            if (!CanProcessActionRequest(avatar) ||
                shopIndex < 0 || shopIndex >= ItemShopRules.ShopCount ||
                offerIndex < 0 || offerIndex >= ItemShopRules.OfferCount)
            {
                return false;
            }

            var snapshot = GetItemShopSnapshot(shopIndex);
            var stock = _itemShopStocks[shopIndex];
            if (!snapshot.Active || stock == null || snapshot.Revision != expectedRevision ||
                snapshot.IsSold(offerIndex) || stock.IsSold(offerIndex) ||
                !CanAccessCoordinateOnServer(avatar, snapshot.Location) ||
                !avatar.HasFreeItemSlotOnServer)
            {
                return false;
            }

            var itemId = snapshot.GetOffer(offerIndex);
            if (!PrototypeItemCatalog.IsValid(itemId))
            {
                return false;
            }

            var price = PrototypeItemCatalog.Get(itemId).Price;
            if (!avatar.CanAfford(price) || !stock.TrySell(offerIndex))
            {
                return false;
            }

            if (!avatar.TryAddItemOnServer(itemId) || !avatar.TrySpendGoldOnServer(price))
            {
                throw new InvalidOperationException(
                    "A validated item-shop transaction failed after reserving its stock.");
            }

            SetItemShopSnapshot(shopIndex, snapshot.WithSoldMask(stock.SoldMask));
            return true;
        }

        public bool CanLocalAvatarAccessItemShop(NetworkPlayerAvatar avatar, int shopIndex)
        {
            if (avatar == null || !avatar.IsOwner || !CanAcceptActionInput ||
                shopIndex < 0 || shopIndex >= ItemShopRules.ShopCount)
            {
                return false;
            }

            var snapshot = GetItemShopSnapshot(shopIndex);
            if (!snapshot.Active || !avatar.HasLogicalBoardTile ||
                avatar.LogicalBoardTileCoordinate != snapshot.Location)
            {
                return false;
            }

            if (_boardTopology == null)
            {
                _boardTopology = FindAnyObjectByType<BoardTopology>();
            }

            return _boardTopology != null &&
                   _boardTopology.TryGetTile(snapshot.Location, out var tile) && tile != null &&
                   HorizontalDistance(avatar.transform.position, tile.WorldCenter) <=
                   ItemShopRules.InteractionDistance;
        }

        private void EnsureKeyShopRuntime()
        {
            if (_keyShopRuntime == null)
            {
                _keyShopRuntime = GetComponent<KeyShopRuntimeState>();
            }

            if (_keyShopRuntime == null)
            {
                _keyShopRuntime = gameObject.AddComponent<KeyShopRuntimeState>();
            }

            if (_boardTopology == null)
            {
                _boardTopology = FindAnyObjectByType<BoardTopology>();
            }
        }

        private void RefreshItemShopsForTurnOnServer(int turn)
        {
            if (!IsServer)
            {
                return;
            }

            EnsureKeyShopRuntime();
            if (_boardTopology == null)
            {
                return;
            }

            for (var shopIndex = 0; shopIndex < ItemShopRules.ShopCount; shopIndex++)
            {
                var snapshot = GetItemShopSnapshot(shopIndex);
                var requiresPlacement = !snapshot.Active || snapshot.IsSoldOut ||
                                        turn - snapshot.AppearedTurn >=
                                        ItemShopRules.TurnsBeforeRefresh;
                if (requiresPlacement)
                {
                    TryPlaceItemShopOnServer(shopIndex, turn, snapshot);
                }
            }
        }

        private bool TryPlaceItemShopOnServer(
            int shopIndex,
            int turn,
            ItemShopSnapshot previous)
        {
            var occupied = GetOccupiedPlayerCoordinates();
            var reserved = GetReservedShopCoordinates(shopIndex);
            if (!ItemShopPlacementPolicy.TryChoose(
                    _boardTopology.Tiles,
                    occupied,
                    reserved,
                    UnityKeyShopRandomSource.Shared,
                    out var selectedTile,
                    previous.Active,
                    previous.Location))
            {
                return false;
            }

            var stock = new ItemShopStock(UnityEngine.Random.Range(1, int.MaxValue));
            _itemShopStocks[shopIndex] = stock;
            SetItemShopSnapshot(
                shopIndex,
                ItemShopSnapshot.Create(
                    selectedTile.Coordinate,
                    previous.Revision + 1,
                    turn,
                    stock));
            return true;
        }

        private void SetItemShopSnapshot(int shopIndex, ItemShopSnapshot snapshot)
        {
            if (shopIndex == 0)
            {
                _itemShop0.Value = snapshot;
            }
            else if (shopIndex == 1)
            {
                _itemShop1.Value = snapshot;
            }
        }

        private List<Vector2Int> GetOccupiedPlayerCoordinates()
        {
            var occupied = new List<Vector2Int>(MultiplayerConstants.MaxPlayers);
            ForEachAvatar(avatar =>
            {
                var currentTile = avatar.CurrentBoardTileOnServer;
                AddUnique(occupied, currentTile != null ? currentTile.Coordinate : default,
                    currentTile != null);
            });
            return occupied;
        }

        private List<Vector2Int> GetReservedShopCoordinates(int excludedItemShop = -1)
        {
            var reserved = new List<Vector2Int>(ItemShopRules.ShopCount + 1);
            if (_keyShopRuntime != null && _keyShopRuntime.HasLocation)
            {
                AddUnique(reserved, _keyShopRuntime.Location, true);
            }

            for (var shopIndex = 0; shopIndex < ItemShopRules.ShopCount; shopIndex++)
            {
                if (shopIndex == excludedItemShop)
                {
                    continue;
                }

                var snapshot = GetItemShopSnapshot(shopIndex);
                AddUnique(reserved, snapshot.Location, snapshot.Active);
            }

            return reserved;
        }

        private List<Vector2Int> GetOccupiedAndItemShopCoordinates()
        {
            var blocked = GetOccupiedPlayerCoordinates();
            for (var shopIndex = 0; shopIndex < ItemShopRules.ShopCount; shopIndex++)
            {
                var snapshot = GetItemShopSnapshot(shopIndex);
                AddUnique(blocked, snapshot.Location, snapshot.Active);
            }
            return blocked;
        }

        private bool CanAccessCoordinateOnServer(
            NetworkPlayerAvatar avatar,
            Vector2Int coordinate)
        {
            if (avatar == null || !avatar.HasLogicalBoardTile ||
                avatar.LogicalBoardTileCoordinate != coordinate)
            {
                return false;
            }

            EnsureKeyShopRuntime();
            return _boardTopology != null &&
                   _boardTopology.TryGetTile(coordinate, out var tile) && tile != null &&
                   HorizontalDistance(avatar.transform.position, tile.WorldCenter) <=
                   ItemShopRules.InteractionDistance;
        }

        private static float HorizontalDistance(Vector3 left, Vector3 right)
        {
            left.y = 0f;
            right.y = 0f;
            return Vector3.Distance(left, right);
        }

        private static void AddUnique(
            List<Vector2Int> coordinates,
            Vector2Int coordinate,
            bool shouldAdd)
        {
            if (shouldAdd && !coordinates.Contains(coordinate))
            {
                coordinates.Add(coordinate);
            }
        }

        private void TryBeginInitialKeyShopPlacementOnServer(int turn)
        {
            if (!IsServer || turn != KeyShopRuntimeState.InitialPlacementTurn)
            {
                return;
            }

            EnsureKeyShopRuntime();
            if (_keyShopRuntime == null || _boardTopology == null)
            {
                return;
            }

            var occupied = GetOccupiedAndItemShopCoordinates();

            if (_keyShopRuntime.TryBeginInitialPlacement(
                    turn,
                    _boardTopology.Tiles,
                    occupied,
                    out _))
            {
                SyncKeyShopSnapshot();
                _keyShopAppearanceEndsAt = ServerNow + 0.75d;
            }
        }

        private void AdvanceKeyShopLifecycleOnServer(double now)
        {
            if (!IsServer || _keyShopRuntime == null ||
                _keyShopRuntime.State != KeyShopLifecycleState.Appearing ||
                _keyShopAppearanceEndsAt <= 0d || now < _keyShopAppearanceEndsAt)
            {
                return;
            }

            if (_keyShopRuntime.TryCompleteAppearance())
            {
                _keyShopAppearanceEndsAt = 0d;
                SyncKeyShopSnapshot();
            }
        }

        private void BeginKeyShopRevealOnServer(double now)
        {
            EnsureFlowModel();
            ResolveWorldDiceCoordinator();
            if (_keyShopRevealActive.Value ||
                !_flow.Pause(
                    now,
                    ShouldDeferActionTimeoutForWorldDie(),
                    HasBoardDeathInProgressOnServer(),
                    ShouldDeferLandingEffectResolutionOnServer(
                        HasBoardDeathInProgressOnServer())))
            {
                return;
            }

            _pausedStateRemaining.Value = _flow.GetStateRemaining(now);
            _pausedActionRemaining.Value = _flow.GetActionRemaining(now);
            _pausedChoiceRemaining.Value = GetPersonalChoiceRemainingOnServer(now);
            _pausedShieldRemaining.Value = _flow.GetOpeningProtectionRemaining(now);
            PauseCombatAndPersonalProtectionOnServer(now);
            _keyShopRevealActive.Value = true;
            _keyShopRevealEndsAt.Value = now + KeyShopRevealSeconds;
            _keyShopRevealRevision.Value++;
            StopAllAvatarInputOnServer();
            SyncFlowSnapshot(now);
        }

        private void CompleteKeyShopRevealOnServer(double now)
        {
            if (!IsServer || !_keyShopRevealActive.Value || IsSimulationSuspended)
            {
                return;
            }

            _keyShopRevealActive.Value = false;
            _keyShopRevealEndsAt.Value = 0d;
            _keyShopRevealRemainingDuringReconnect = 0d;
            _flow.Resume(now);
            ResumeCombatAndPersonalProtectionOnServer(now);
            SyncFlowSnapshot(now);
            StopAllAvatarInputOnServer();
        }

        private void SyncKeyShopSnapshot()
        {
            if (!IsServer || _keyShopRuntime == null)
            {
                return;
            }

            _keyShopLifecycle.Value = (byte)_keyShopRuntime.State;
            _keyShopHasLocation.Value = _keyShopRuntime.HasLocation;
            _keyShopLocation.Value = _keyShopRuntime.Location;
            _keyShopRevision.Value = _keyShopRuntime.PlacementRevision;
        }
    }
}
