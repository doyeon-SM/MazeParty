using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        private readonly NetworkVariable<byte> _handGesture = new NetworkVariable<byte>();
        private readonly NetworkVariable<double> _handGestureEndsAt = new NetworkVariable<double>();
        private double GestureNow => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.unscaledTimeAsDouble;
        public byte ActiveHandGesture => HandEmoteRules.IsActive(_handGesture.Value, _handGestureEndsAt.Value, GestureNow) ? _handGesture.Value : (byte)0;
        public bool IsInLobbyForExpressions => CanUseLobbyInput();
        public bool CanUseHandGestures
        {
            get
            {
                if (!IsSpawned || CurrentHealth <= 0 || IsSwapping || _equippedItem.Value != 0 || IsBoardDeathInProgressOnServer) return false;
                if (CanUseLobbyInput()) return true;
                var match = NetworkMatchState.Instance;
                return match != null && match.CanAcceptActionInput && HasResolvedItemChoice && IsBoardReady;
            }
        }
        public void RequestHandGesture(byte id) { if (IsOwner && IsSpawned) HandGestureRpc(id); }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void HandGestureRpc(byte id, RpcParams rpc = default)
        { if (rpc.Receive.SenderClientId == OwnerClientId) TryStartHandGestureOnServer(id); }
        public bool TryStartHandGestureOnServer(byte id)
        {
            if (!IsServer || !HandEmoteRules.CanStart(GestureNow, _handGestureEndsAt.Value, CanUseHandGestures,
                PlayerExpressionCatalog.HasGesture(id))) return false;
            _handGestureEndsAt.Value = GestureNow + HandEmoteRules.Duration;
            _handGesture.Value = id; return true;
        }
        public void CancelHandGestureOnServer() { if (IsServer) _handGesture.Value = 0; }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void CancelHandGestureRpc(RpcParams rpc = default)
        { if (rpc.Receive.SenderClientId == OwnerClientId) CancelHandGestureOnServer(); }
        private void TickHandGestures()
        {
            if (IsServer && (!CanUseHandGestures || GestureNow >= _handGestureEndsAt.Value)) CancelHandGestureOnServer();
            if (IsOwner && ActiveHandGesture != 0 && !HandEmoteWheelView.BlocksPointerInput && Mouse.current != null &&
                (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)) CancelHandGestureRpc();
            _avatarVisual?.SetHandGesture(ActiveHandGesture);
        }
    }
}
