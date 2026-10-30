using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        private sealed class BoardSalvo
        {
            public int OwnerSlot;
            public BoardBombardmentSequence Sequence;
        }
        private readonly List<BoardSalvo> _boardSalvos = new List<BoardSalvo>();
        private Vector3[] _bombardmentWarnings = Array.Empty<Vector3>();
        private readonly List<GameObject> _bombardmentWarningViews = new List<GameObject>();
        private readonly NetworkPlayerAvatar[] _bombardmentRecipients = new NetworkPlayerAvatar[4];
        public IReadOnlyList<Vector3> BombardmentWarnings => _bombardmentWarnings;

        public bool TryBeginBombardmentOnServer(NetworkPlayerAvatar avatar)
        {
            return CanProcessActionRequest(avatar) && avatar.CurrentHealth > 0 &&
                avatar.HasResolvedItemChoice && avatar.HasLogicalBoardTile &&
                avatar.BeginBombardmentSelectionOnServer();
        }

        public bool TryChooseBombardmentTargetOnServer(NetworkPlayerAvatar avatar, Vector3 requested,
            byte expectedCount, uint revision)
        {
            // This endpoint intentionally allows an existing selection through ordinary
            // damage/knockdown, while all other actions stay locked.
            if (!CanProcessAvatarRequest(avatar) || FlowState != BoardFlowState.Action ||
                ActionRemaining <= 0d || avatar.CurrentHealth <= 0 ||
                avatar.IsBoardDeathInProgressOnServer || !avatar.IsSettingBombardment ||
                !BoardBombardmentRules.TryGetGroundPosition(_boardTopology, requested, out var point) ||
                !avatar.AcceptBombardmentTargetOnServer(point, expectedCount, revision, out var committed))
                return false;
            if (committed != null)
            {
                _boardSalvos.Add(new BoardSalvo { OwnerSlot = avatar.AssignedSlot,
                    Sequence = new BoardBombardmentSequence(committed, ServerNow) });
                SyncBombardmentWarnings();
            }
            return true;
        }

        private void TickBoardBombardments()
        {
            if (!BoardItemLifecycleRules.CanSimulateProjectile(FlowState))
            {
                if (_boardSalvos.Count > 0)
                {
                    _boardSalvos.Clear();
                    SyncBombardmentWarnings();
                }
                return;
            }
            bool dirty = false;
            for (int slot = 0; slot < 4; slot++)
            {
                var avatar = GetAvatarForSlot(slot);
                if (avatar != null && avatar.IsBoardReady && _bombardmentRecipients[slot] != avatar)
                    dirty = true;
            }
            for (var i = _boardSalvos.Count - 1; i >= 0; i--)
            {
                var salvo = _boardSalvos[i];
                salvo.Sequence.Tick(ServerNow, IsGlobalSimulationPaused);
                if (IsGlobalSimulationPaused) continue;
                while (salvo.Sequence.TryTakeDue(out var point))
                {
                    ExplodeBoardItem(point, salvo.OwnerSlot, PrototypeItemCatalog.Get(PrototypeItemId.Bombardment));
                    dirty = true;
                }
                if (salvo.Sequence.Complete) _boardSalvos.RemoveAt(i);
            }
            if (dirty) SyncBombardmentWarnings();
        }

        private void SyncBombardmentWarnings()
        {
            var positions = new List<Vector3>();
            foreach (var salvo in _boardSalvos)
                for (var i = salvo.Sequence.FiredCount; i < salvo.Sequence.Targets.Count; i++)
                    positions.Add(salvo.Sequence.Targets[i]);
            for (int slot = 0; slot < 4; slot++) _bombardmentRecipients[slot] = GetAvatarForSlot(slot);
            SyncBombardmentWarningsRpc(positions.ToArray());
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SyncBombardmentWarningsRpc(Vector3[] positions)
        {
            _bombardmentWarnings = positions ?? Array.Empty<Vector3>();
            var prefab = PrototypeItemCatalog.Get(PrototypeItemId.Mine).WorldPrefab;
            while (_bombardmentWarningViews.Count < _bombardmentWarnings.Length)
                _bombardmentWarningViews.Add(Instantiate(prefab));
            for (var i = _bombardmentWarningViews.Count - 1; i >= _bombardmentWarnings.Length; i--)
            {
                Destroy(_bombardmentWarningViews[i]);
                _bombardmentWarningViews.RemoveAt(i);
            }
            for (var i = 0; i < _bombardmentWarningViews.Count; i++)
                _bombardmentWarningViews[i].transform.position = _bombardmentWarnings[i];
        }

        private void ClearBoardBombardments()
        {
            _boardSalvos.Clear();
            _bombardmentWarnings = Array.Empty<Vector3>();
            Array.Clear(_bombardmentRecipients, 0, _bombardmentRecipients.Length);
            foreach (var view in _bombardmentWarningViews) if (view != null) Destroy(view);
            _bombardmentWarningViews.Clear();
        }
    }
}
