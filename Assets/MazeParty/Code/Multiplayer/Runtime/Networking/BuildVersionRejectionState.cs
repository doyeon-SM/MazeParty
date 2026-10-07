using System.Collections.Generic;

namespace MazeParty.Multiplayer
{
    internal sealed class BuildVersionRejectionState
    {
        private readonly HashSet<ulong> _serverRejectedClientIds =
            new HashSet<ulong>();
        private bool _localAttemptRejected;

        public void BeginLocalAttempt()
        {
            _localAttemptRejected = false;
        }

        public void ObserveLocalDisconnectReason(string reason)
        {
            if (BuildVersionCompatibility.IsMismatchDisconnectReason(reason))
            {
                _localAttemptRejected = true;
            }
        }

        public bool ConsumeLocalAttemptRejection()
        {
            var rejected = _localAttemptRejected;
            _localAttemptRejected = false;
            return rejected;
        }

        public void RecordServerRejectedClient(ulong clientId)
        {
            _serverRejectedClientIds.Add(clientId);
        }

        public bool ConsumeServerRejectedClient(ulong clientId)
        {
            return _serverRejectedClientIds.Remove(clientId);
        }
    }
}
