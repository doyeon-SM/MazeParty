using System;
using System.Linq;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        private readonly NetworkVariable<BoardForestGhostSnapshot> _forestGhost = new();
        private bool _forestGhostComplete;
        public BoardForestGhostSnapshot ForestGhost => _forestGhost.Value;
        public bool IsForestGhostActive => FlowState == BoardFlowState.LandingEffectResolve && ForestGhost.Active &&
            (!ForestGhost.IsIntro || !ForestGhost.HasReached(SynchronizedNow, BoardForestGhostRules.Duration));
        public bool IsForestGhostReturningToTopView => FlowState == BoardFlowState.LandingEffectResolve &&
            ForestGhost.Active && ForestGhost.IsIntro && ForestGhost.HasReached(SynchronizedNow, BoardForestGhostRules.Duration);
        private bool HasForestGhost => BoardMapId == BoardForestGhostRules.MapId;

        private void ResetForestGhostOnServer()
        {
            _forestGhostComplete = !HasForestGhost;
            _forestGhost.Value = new BoardForestGhostSnapshot { Revision = ForestGhost.Revision + 1, Slot = -1 };
        }

        private void AdvanceForestGhostOnServer(double now)
        {
            if (!IsServer || !HasForestGhost || _forestGhostComplete || IsGlobalSimulationPaused ||
                _flow == null || _flow.State != BoardFlowState.LandingEffectResolve) return;
            if (!ForestGhost.Active)
            {
                // Landing deaths (and their coin flights) must finish before selecting a target.
                if (HasBoardDeathInProgressOnServer() || _boardTopology == null) return;
                if (Turn == 1)
                {
                    var tiles = _boardTopology.Tiles.Where(tile => tile != null).ToArray();
                    if (tiles.Length == 0) return;
                    var bounds = new Bounds(tiles[0].WorldCenter, Vector3.zero);
                    foreach (var tile in tiles) bounds.Encapsulate(tile.WorldCenter);
                    ClearLandingEffectPresentationOnServer();
                    ClearLandingEffectFeedbackOnServer();
                    ClearResourceTransferPresentationOnServer();
                    _forestGhost.Value = new BoardForestGhostSnapshot
                    {
                        Active = true, IsIntro = true, Revision = ForestGhost.Revision + 1,
                        Slot = -1, Position = bounds.center, Facing = Quaternion.identity, StartedAt = now
                    };
                    return;
                }
                byte eligible = 0;
                var occupied = new System.Collections.Generic.HashSet<BoardTile>();
                for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                {
                    var avatar = GetAvatarForSlot(slot);
                    if (avatar == null) return; // A reconnect must not silently consume a seat's roll.
                    var tile = avatar.CurrentBoardTileOnServer;
                    if (tile != null) occupied.Add(tile);
                    if (tile != null && tile.TileType != BoardTileType.Respawn && avatar.CurrentHealth > 0)
                        eligible |= (byte)(1 << slot);
                }
                var empty = _boardTopology.Tiles.Where(t => t != null &&
                    t.TileType != BoardTileType.Respawn && !occupied.Contains(t))
                    .OrderBy(t => t.Coordinate.x).ThenBy(t => t.Coordinate.y).ToArray();
                var beneficiary = GhostPassBeneficiary;
                var choice = beneficiary >= 0
                    ? BoardForestGhostRules.ResolveSteal(_boardEffectSeed.Value, Turn, eligible, beneficiary)
                    : BoardForestGhostRules.Resolve(_boardEffectSeed.Value, Turn, eligible, empty.Length);
                if (choice.Slot < 0 && choice.EmptyIndex < 0)
                { _forestGhostComplete = true; FinishGhostPassOnServer(); return; }
                var target = choice.Slot >= 0 ? GetAvatarForSlot(choice.Slot) : null;
                ClearLandingEffectPresentationOnServer();
                ClearLandingEffectFeedbackOnServer();
                ClearResourceTransferPresentationOnServer();
                _forestGhost.Value = new BoardForestGhostSnapshot
                {
                    Active = true, Revision = ForestGhost.Revision + 1, Slot = choice.Slot,
                    Beneficiary = (byte)(beneficiary + 1),
                    Strong = choice.Strong, Resource = (byte)choice.Resource, Amount = choice.Amount,
                    Position = target != null ? target.transform.position : empty[choice.EmptyIndex].WorldCenter,
                    Facing = target != null ? target.transform.rotation : Quaternion.identity, StartedAt = now
                };
                return;
            }
            var snapshot = ForestGhost;
            if (snapshot.IsIntro)
            {
                // Three seconds on camera, then the shared top-view transition.
                // Keep landing resolution pending until that transition completes.
                if (!snapshot.HasReached(now, BoardForestGhostRules.Duration + BoardCameraCloudTiming.Duration)) return;
                snapshot.Active = false;
                _forestGhost.Value = snapshot;
                _forestGhostComplete = true;
                FinishGhostPassOnServer();
                return;
            }
            if (!snapshot.Applied && snapshot.HasReached(now, BoardForestGhostRules.ApplyAt))
            {
                if (!ApplyForestGhostOnServer()) return;
                // Preserve the effect portion of the shot after a stalled server frame.
                snapshot = ForestGhost;
                snapshot.StartedAt = now - BoardForestGhostRules.ApplyAt;
                _forestGhost.Value = snapshot;
                return;
            }
            if (!snapshot.HasReached(now, BoardForestGhostRules.Duration)) return;
            if (snapshot.Beneficiary > 0 && !snapshot.IsDelivery && snapshot.ActualAmount < 0)
            {
                if (HasBoardDeathInProgressOnServer()) return;
                var recipient = GetAvatarForSlot(snapshot.Beneficiary - 1);
                if (recipient == null) return; // Reconnection must not drop the pending payment.
                _forestGhost.Value = new BoardForestGhostSnapshot
                {
                    Active = true, IsDelivery = true, Beneficiary = snapshot.Beneficiary,
                    Revision = snapshot.Revision + 1, Slot = snapshot.Beneficiary - 1,
                    Strong = snapshot.Strong, Resource = snapshot.Resource, Amount = -snapshot.ActualAmount,
                    Position = recipient.transform.position, Facing = recipient.transform.rotation, StartedAt = now
                };
                return;
            }
            snapshot.Active = false;
            _forestGhost.Value = snapshot;
            _forestGhostComplete = true;
            FinishGhostPassOnServer();
        }

        private bool ApplyForestGhostOnServer()
        {
            var snapshot = ForestGhost;
            if (!IsServer || !snapshot.Active || snapshot.IsIntro || snapshot.Applied) return false;
            var avatar = snapshot.Slot >= 0 ? GetAvatarForSlot(snapshot.Slot) : null;
            if (snapshot.Slot >= 0 && avatar == null) return false;
            snapshot.Applied = true;
            _forestGhost.Value = snapshot; // Exactly once, before resource/death callbacks.
            if (avatar != null)
            {
                switch ((ForestGhostResource)snapshot.Resource)
                {
                    case ForestGhostResource.Gold:
                        snapshot.ActualAmount = avatar.ApplyGoldDeltaOnServer(snapshot.Amount); break;
                    case ForestGhostResource.Key:
                        snapshot.ActualAmount = avatar.ApplyKeyDeltaOnServer(snapshot.Amount); break;
                    case ForestGhostResource.Health:
                        if (snapshot.Amount >= 0)
                            snapshot.ActualAmount = !snapshot.IsDelivery || avatar.CurrentHealth > 0
                                ? avatar.HealOnServer(snapshot.Amount) : 0;
                        else
                        {
                            var before = avatar.CurrentHealth;
                            avatar.ApplyDamage(new DamageRequest(-snapshot.Amount, DamageKind.Environment, null));
                            snapshot.ActualAmount = avatar.CurrentHealth - before;
                        }
                        break;
                }
            }
            _forestGhost.Value = snapshot;
            return true;
        }

        private void PauseForestGhostOnServer(double now)
        { var snapshot = ForestGhost; snapshot.Pause(now); _forestGhost.Value = snapshot; }
        private void ResumeForestGhostOnServer(double now)
        { var snapshot = ForestGhost; snapshot.Resume(now); _forestGhost.Value = snapshot; }
    }
}
