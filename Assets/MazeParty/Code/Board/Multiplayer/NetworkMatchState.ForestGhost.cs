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
        public bool IsForestGhostActive => FlowState == BoardFlowState.LandingEffectResolve && ForestGhost.Active;
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
                var choice = BoardForestGhostRules.Resolve(_boardEffectSeed.Value, Turn, eligible, empty.Length);
                if (choice.Slot < 0 && choice.EmptyIndex < 0)
                { _forestGhostComplete = true; return; }
                var target = choice.Slot >= 0 ? GetAvatarForSlot(choice.Slot) : null;
                ClearLandingEffectPresentationOnServer();
                ClearLandingEffectFeedbackOnServer();
                ClearResourceTransferPresentationOnServer();
                _forestGhost.Value = new BoardForestGhostSnapshot
                {
                    Active = true, Revision = ForestGhost.Revision + 1, Slot = choice.Slot,
                    Strong = choice.Strong, Resource = (byte)choice.Resource, Amount = choice.Amount,
                    Position = target != null ? target.transform.position : empty[choice.EmptyIndex].WorldCenter,
                    Facing = target != null ? target.transform.rotation : Quaternion.identity, StartedAt = now
                };
                return;
            }
            var snapshot = ForestGhost;
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
            snapshot.Active = false;
            _forestGhost.Value = snapshot;
            _forestGhostComplete = true;
        }

        private bool ApplyForestGhostOnServer()
        {
            var snapshot = ForestGhost;
            if (!IsServer || !snapshot.Active || snapshot.Applied) return false;
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
                        if (snapshot.Amount >= 0) snapshot.ActualAmount = avatar.HealOnServer(snapshot.Amount);
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
