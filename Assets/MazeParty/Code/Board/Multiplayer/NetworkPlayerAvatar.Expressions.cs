using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        private readonly NetworkVariable<byte> _handGesture = new NetworkVariable<byte>();
        private readonly NetworkVariable<byte> _handGestureFace = new NetworkVariable<byte>();
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
        public void RequestHandGesture(byte id)
        {
            if (!IsOwner || !IsSpawned)
            {
                return;
            }

            var fallback = _appearance.Value.ExpressionId;
            var controller = OnlineSessionController.Instance;
            var requestedFace = controller != null
                ? controller.LocalEmoteFaces.Get(id, fallback)
                : fallback;
            HandGestureRpc(id, requestedFace);
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void HandGestureRpc(
            byte id,
            byte requestedFace,
            RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId == OwnerClientId)
            {
                TryStartHandGestureOnServer(id, requestedFace);
            }
        }
        public bool TryStartHandGestureOnServer(
            byte id,
            byte requestedFace = byte.MaxValue)
        {
            if (!IsServer || !HandEmoteRules.CanStart(GestureNow, _handGestureEndsAt.Value, CanUseHandGestures,
                PlayerExpressionCatalog.HasGesture(id))) return false;
            var fallback = _appearance.Value.ExpressionId;
            var catalog = PlayerExpressionCatalog.Instance;
            _handGestureFace.Value = HandEmoteRules.ResolveExpression(
                id,
                requestedFace,
                fallback,
                catalog != null && catalog.Faces != null
                    ? catalog.Faces.Length
                    : 0);
            _handGestureEndsAt.Value = GestureNow + HandEmoteRules.Duration;
            _handGesture.Value = id;
            StopServerInputOnServer();
            return true;
        }
        public void CancelHandGestureOnServer() { if (IsServer) _handGesture.Value = 0; }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void CancelHandGestureRpc(RpcParams rpc = default)
        { if (rpc.Receive.SenderClientId == OwnerClientId) CancelHandGestureOnServer(); }
        private void TickHandGestures()
        {
            if (IsServer && (!CanUseHandGestures || GestureNow >= _handGestureEndsAt.Value)) CancelHandGestureOnServer();
            if (IsOwner && ActiveHandGesture != 0 && !HandEmoteWheelView.BlocksPointerInput && LocalMouse != null &&
                (LocalMouse.leftButton.wasPressedThisFrame || LocalMouse.rightButton.wasPressedThisFrame)) CancelHandGestureRpc();
            var gesture = ActiveHandGesture;
            _avatarVisual?.SetHandGesture(
                gesture,
                gesture == 0
                    ? _appearance.Value.ExpressionId
                    : _handGestureFace.Value);
        }
    }
}
