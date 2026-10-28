using System;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        private readonly NetworkVariable<bool> _bombardmentSelecting =
            new NetworkVariable<bool>(false, NetworkVariableReadPermission.Owner);
        private readonly NetworkVariable<byte> _bombardmentTargetCount =
            new NetworkVariable<byte>(0, NetworkVariableReadPermission.Owner);
        private readonly NetworkVariable<uint> _bombardmentRevision =
            new NetworkVariable<uint>(0, NetworkVariableReadPermission.Owner);
        private readonly NetworkVariable<Vector3> _bombardmentTarget0 =
            new NetworkVariable<Vector3>(default, NetworkVariableReadPermission.Owner);
        private readonly NetworkVariable<Vector3> _bombardmentTarget1 =
            new NetworkVariable<Vector3>(default, NetworkVariableReadPermission.Owner);
        public bool IsSettingBombardment => (IsServer || IsOwner) && _bombardmentSelecting.Value;
        public int LocalBombardmentTargetCount => (IsServer || IsOwner) ? _bombardmentTargetCount.Value : 0;

        public Vector3[] CaptureBombardmentTargets()
        {
            if (!IsSettingBombardment) return Array.Empty<Vector3>();
            return _bombardmentTargetCount.Value == 0 ? Array.Empty<Vector3>() :
                _bombardmentTargetCount.Value == 1 ? new[] { _bombardmentTarget0.Value } :
                new[] { _bombardmentTarget0.Value, _bombardmentTarget1.Value };
        }

        public void BeginBombardment()
        {
            if (IsOwner && IsSpawned) BeginBombardmentRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void BeginBombardmentRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
                NetworkMatchState.Instance?.TryBeginBombardmentOnServer(this);
        }

        internal bool BeginBombardmentSelectionOnServer()
        {
            if (!IsServer || IsSettingBombardment ||
                GetSelectedItemOnServer() != PrototypeItemId.Bombardment ||
                !SpendItemChargeOnServer(PrototypeItemCatalog.Get(PrototypeItemId.Bombardment)))
                return false;
            _bombardmentRevision.Value++;
            _bombardmentTargetCount.Value = 0;
            _bombardmentSelecting.Value = true;
            _equippedItem.Value = (byte)PrototypeItemId.Bombardment;
            StopServerInputOnServer();
            PresentItemUseOnServer(PrototypeItemId.Bombardment, animate: false);
            return true;
        }

        public void ChooseBombardmentTarget(Vector3 point)
        {
            if (IsOwner && IsSpawned && IsSettingBombardment)
                ChooseBombardmentTargetRpc(point, _bombardmentTargetCount.Value, _bombardmentRevision.Value);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ChooseBombardmentTargetRpc(Vector3 point, byte expectedCount, uint revision,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            var match = NetworkMatchState.Instance;
            var accepted = match != null &&
                match.TryChooseBombardmentTargetOnServer(this, point, expectedCount, revision);
            ConfirmBombardmentTargetRpc(accepted);
        }

        [Rpc(SendTo.Owner)]
        private void ConfirmBombardmentTargetRpc(bool accepted)
        {
            // Private feedback only after the authority accepts a point.
            GameSound.Play(accepted ? SoundKeys.ItemUse(PrototypeItemId.Mine) : SoundKeys.UiError);
        }

        internal bool AcceptBombardmentTargetOnServer(Vector3 point, byte expectedCount,
            uint revision, out Vector3[] committed)
        {
            committed = null;
            if (!IsServer || !IsSettingBombardment || expectedCount != _bombardmentTargetCount.Value ||
                revision != _bombardmentRevision.Value || expectedCount >= BoardBombardmentRules.TargetCount)
                return false;
            if (expectedCount == 0) _bombardmentTarget0.Value = point;
            else if (expectedCount == 1) _bombardmentTarget1.Value = point;
            else committed = new[] { _bombardmentTarget0.Value, _bombardmentTarget1.Value, point };
            if (committed != null) ClearBombardmentSelectionOnServer();
            else _bombardmentTargetCount.Value++;
            return true;
        }

        internal void ClearBombardmentSelectionOnServer()
        {
            if (!IsServer) return;
            _bombardmentSelecting.Value = false;
            _bombardmentTargetCount.Value = 0;
            if ((PrototypeItemId)_equippedItem.Value == PrototypeItemId.Bombardment && _itemCharges.Value == 0)
                _equippedItem.Value = 0;
        }

        private void TickBombardmentSelection()
        {
            if (!IsServer || !IsSettingBombardment) return;
            var match = NetworkMatchState.Instance;
            if (match == null || !match.GameplayEnabled || !match.IsActionPhase ||
                (!match.IsGlobalSimulationPaused && match.ActionRemaining <= 0d) || CurrentHealth <= 0 || IsBoardDeathInProgressOnServer)
                ClearBombardmentSelectionOnServer();
        }

        private void RestoreBombardmentSelection(ReconnectSnapshot snapshot)
        {
            ClearBombardmentSelectionOnServer();
            if (!snapshot.BombardmentSelecting || snapshot.BombardmentTargets == null ||
                snapshot.BombardmentTargets.Length >= BoardBombardmentRules.TargetCount) return;
            foreach (var point in snapshot.BombardmentTargets)
                if (!BoardItemLifecycleRules.IsValidMinePosition(_topology, point)) return;
            _bombardmentRevision.Value = snapshot.BombardmentRevision;
            _bombardmentTargetCount.Value = (byte)snapshot.BombardmentTargets.Length;
            if (snapshot.BombardmentTargets.Length > 0) _bombardmentTarget0.Value = snapshot.BombardmentTargets[0];
            if (snapshot.BombardmentTargets.Length > 1) _bombardmentTarget1.Value = snapshot.BombardmentTargets[1];
            _bombardmentSelecting.Value = true;
            _equippedItem.Value = (byte)PrototypeItemId.Bombardment;
        }
    }
}
