using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        private readonly NetworkVariable<BoardChestSnapshot> _chest0 = new();
        private readonly NetworkVariable<BoardChestSnapshot> _chest1 = new();
        private readonly BoardChestVisual[] _chestVisuals = new BoardChestVisual[BoardChestRules.Count];
        private readonly BoardChestMapKnowledge _localChestKnowledge = new();
        public BoardChestSnapshot GetChestSnapshot(int index) => index switch
        {
            0 => _chest0.Value, 1 => _chest1.Value, _ => default
        };
        private void SetChestSnapshot(int index, BoardChestSnapshot state)
        {
            if (index == 0) _chest0.Value = state;
            else if (index == 1) _chest1.Value = state;
        }
        private void RefreshChestsForTurnOnServer(int turn)
        {
            if (!IsServer || !BoardChestRules.ShouldRefresh(turn, _chest0.Value.AppearedTurn)) return;
            EnsureKeyShopRuntime();
            if (_boardTopology == null) return;
            var previous = _chest0.Value.Active
                ? new[] { _chest0.Value.Location, _chest1.Value.Location } : null;
            var shops = new List<Vector2Int>();
            if (_keyShopRuntime != null && _keyShopRuntime.HasLocation) shops.Add(_keyShopRuntime.Location);
            for (var i = 0; i < ItemShopRules.ShopCount; i++)
                if (GetItemShopSnapshot(i).Active) shops.Add(GetItemShopSnapshot(i).Location);
            var locations = BoardChestRules.ChooseLocations(_boardTopology.Tiles,
                shops, previous, unchecked(_boardEffectSeed.Value * 397 ^ turn));
            for (var index = 0; index < BoardChestRules.Count; index++)
                SetChestSnapshot(index, new BoardChestSnapshot
                {
                    Active = true, Location = locations[index], AppearedTurn = turn,
                    Revision = GetChestSnapshot(index).Revision + 1
                });
        }
        public bool TryOpenChestOnServer(NetworkPlayerAvatar avatar, int index, int expectedRevision)
        {
            if (!IsServer || index < 0 || index >= BoardChestRules.Count ||
                !CanProcessAvatarRequest(avatar) || FlowState != BoardFlowState.Action ||
                ActionRemaining <= 0d || avatar.IsBoardDeathInProgressOnServer || avatar.IsSettingBombardment ||
                !avatar.HasResolvedItemChoice || avatar.CurrentHealth <= 0) return false;
            var state = GetChestSnapshot(index);
            if (!state.Active || state.Consumed || state.Revision != expectedRevision ||
                !CanAccessCoordinateOnServer(avatar, state.Location) ||
                !avatar.IsAimingAtChestOnServer(index)) return false;
            var id = BoardChestRules.DrawItem(PrototypeItemCatalog.All,
                unchecked(_boardEffectSeed.Value * 397 ^ state.AppearedTurn * 31 ^ index), BoardMapId);
            if (!avatar.EquipChestItemOnServer(id)) return false;
            state.Consumed = true;
            state.Revision++;
            SetChestSnapshot(index, state);
            var rewardPosition = _boardTopology != null &&
                _boardTopology.TryGetTile(state.Location, out var tile)
                    ? BoardTileWorldPlacement.PeripheralPoint(tile, .55f) + Vector3.up * .9f
                    : avatar.transform.position + Vector3.up;
            avatar.PresentChestAcquiredOnServer(id, rewardPosition);
            return true;
        }
        private void TickChestPresentation()
        {
            var topology = _boardTopology != null ? _boardTopology : FindAnyObjectByType<BoardTopology>();
            for (var index = 0; index < BoardChestRules.Count; index++)
            {
                var state = GetChestSnapshot(index);
                _localChestKnowledge.ShouldShow(index, state, false);
                if (!GameplayEnabled || !state.Active || topology == null ||
                    !topology.TryGetTile(state.Location, out var tile))
                {
                    if (_chestVisuals[index] != null) _chestVisuals[index].gameObject.SetActive(false);
                    continue;
                }
                if (_chestVisuals[index] == null)
                    _chestVisuals[index] = Instantiate(BoardWorldPrefabs.LoadRequired().Chest, transform);
                var visual = _chestVisuals[index];
                visual.PlaceOnTile(tile);
                visual.gameObject.SetActive(true);
                visual.Present(index, state.Consumed);
            }
        }
        public bool ShouldShowChestOnLocalMap(int index) =>
            _localChestKnowledge.ShouldShow(index, GetChestSnapshot(index), false);
        public void ObserveChestsOnLocalMap(Camera camera, RaycastHit[] hits)
        {
            for (var index = 0; index < BoardChestRules.Count; index++)
            {
                var visual = _chestVisuals[index];
                var visible = visual != null && visual.gameObject.activeInHierarchy &&
                    BoardMapView.IsPointDirectlyVisible(camera, visual.ObservationPoint, hits);
                _localChestKnowledge.ShouldShow(index, GetChestSnapshot(index), visible);
            }
        }
        private void AddChestReservations(List<Vector2Int> reserved)
        {
            for (var i = 0; i < BoardChestRules.Count; i++)
            {
                var chest = GetChestSnapshot(i);
                AddUnique(reserved, chest.Location, chest.Active);
            }
        }
        private BoardChestSnapshot[] CaptureChests() => new[] { _chest0.Value, _chest1.Value };
        private bool ValidateRecoveryChests(MatchRecoverySnapshot snapshot)
        {
            if (!AreChestSnapshotsValid(snapshot.chests, snapshot.turn)) return false;
            foreach (var chest in snapshot.chests)
                if (chest.Active && (!_boardTopology.TryGetTile(chest.Location, out var tile) ||
                    tile.TileType == BoardTileType.Respawn ||
                    snapshot.keyShop.hasLocation && chest.Location == snapshot.keyShop.location ||
                    Array.Exists(snapshot.itemShops, s => s.active && s.location == chest.Location)))
                    return false;
            return true;
        }
        public static bool AreChestSnapshotsValid(BoardChestSnapshot[] chests, int turn)
        {
            if (chests == null || chests.Length != BoardChestRules.Count) return false;
            for (var i = 0; i < chests.Length; i++)
            {
                var c = chests[i];
                if (c.Revision < 0 || c.AppearedTurn < 0 || c.AppearedTurn > turn ||
                    c.Active && (c.AppearedTurn < 2 || c.AppearedTurn % 2 != 0 || c.Revision < 1) ||
                    !c.Active && (c.Consumed || c.AppearedTurn != 0 || c.Revision != 0)) return false;
            }
            return chests[0].Active == chests[1].Active &&
                chests[0].AppearedTurn == chests[1].AppearedTurn &&
                (!chests[0].Active || chests[0].Location != chests[1].Location);
        }
    }
}
