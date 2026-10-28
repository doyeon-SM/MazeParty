using Unity.Netcode;
using MazeParty.Gameplay;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        private readonly NetworkVariable<int> _ghostPassTurn = new();
        private readonly NetworkVariable<int> _ghostPassOwner = new(-1);
        private readonly NetworkVariable<byte> _ghostPassUsers = new();

        private int GhostPassBeneficiary => HasForestGhost && _ghostPassTurn.Value == Turn
            ? _ghostPassOwner.Value : -1;

        public bool HasUsedGhostPass(int slot) => slot >= 0 && slot < MultiplayerConstants.MaxPlayers &&
            GameplayEnabled && HasForestGhost && _ghostPassTurn.Value == Turn &&
            FlowState <= BoardFlowState.LandingEffectResolve && (_ghostPassUsers.Value & (1 << slot)) != 0;

        // Called only after the authoritative action validation and charge consumption.
        private void RegisterGhostPassOnServer(int slot)
        {
            if (!IsServer || !HasForestGhost || slot < 0 || slot >= MultiplayerConstants.MaxPlayers) return;
            if (_ghostPassTurn.Value != Turn)
            {
                _ghostPassTurn.Value = Turn;
                _ghostPassOwner.Value = -1;
                _ghostPassUsers.Value = 0;
            }
            if (_ghostPassOwner.Value < 0) _ghostPassOwner.Value = slot;
            _ghostPassUsers.Value |= (byte)(1 << slot);
        }

        private void FinishGhostPassOnServer() { if (IsServer) _ghostPassUsers.Value = 0; }

        private void ResetGhostPassOnServer()
        {
            if (!IsServer) return;
            _ghostPassTurn.Value = 0;
            _ghostPassOwner.Value = -1;
            _ghostPassUsers.Value = 0;
        }
    }
}
