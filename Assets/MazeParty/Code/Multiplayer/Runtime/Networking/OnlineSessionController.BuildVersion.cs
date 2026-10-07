using Unity.Netcode;

namespace MazeParty.Multiplayer
{
    public sealed partial class OnlineSessionController
    {
        private readonly BuildVersionRejectionState _buildVersionRejections =
            new BuildVersionRejectionState();

        private void ConfigureBuildVersionApproval()
        {
            _networkManager.NetworkConfig.ConnectionApproval = true;
            _networkManager.NetworkConfig.ConnectionData =
                BuildVersionCompatibility.CreateConnectionPayload();
            _networkManager.ConnectionApprovalCallback = ApproveBuildVersion;
        }

        private void ReleaseBuildVersionApproval()
        {
            if (_networkManager != null &&
                _networkManager.ConnectionApprovalCallback == ApproveBuildVersion)
            {
                _networkManager.ConnectionApprovalCallback = null;
            }
        }

        private bool WasRejectedForBuildVersion()
        {
            return _buildVersionRejections.ConsumeLocalAttemptRejection();
        }

        private void ResetLocalBuildVersionRejection()
        {
            _buildVersionRejections.BeginLocalAttempt();
        }

        private bool IgnoreBuildVersionRejectionDisconnect(ulong clientId)
        {
            if (_buildVersionRejections.ConsumeServerRejectedClient(clientId))
            {
                return true;
            }

            if (_networkManager != null &&
                !_networkManager.IsServer &&
                clientId == _networkManager.LocalClientId &&
                !string.IsNullOrEmpty(_networkManager.DisconnectReason))
            {
                _buildVersionRejections.ObserveLocalDisconnectReason(
                    _networkManager.DisconnectReason);
            }

            return false;
        }

        private void ApproveBuildVersion(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            var approved =
                BuildVersionCompatibility.IsCompatiblePayload(request.Payload);
            response.Approved = approved;
            response.CreatePlayerObject = approved;
            response.PlayerPrefabHash = null;
            response.Position = null;
            response.Rotation = null;
            response.Pending = false;
            response.Reason = approved
                ? string.Empty
                : BuildVersionCompatibility.RejectionReason;

            if (!approved &&
                _networkManager != null &&
                _networkManager.IsServer &&
                request.ClientNetworkId != NetworkManager.ServerClientId)
            {
                _buildVersionRejections.RecordServerRejectedClient(
                    request.ClientNetworkId);
            }
        }
    }
}
