using System;
using System.Collections.Generic;

namespace MazeParty.Multiplayer
{
    // One owner for the return attempt's transient data. NGO/service calls stay
    // in the controller; acknowledgements and next-frame scheduling live here.
    internal sealed class CompletedMatchReturnState
    {
        internal readonly Queue<string> SceneUnloadQueue =
            new Queue<string>();
        internal readonly HashSet<ulong> PendingUnloadClients =
            new HashSet<ulong>();
        internal readonly HashSet<ulong> DisconnectedClients =
            new HashSet<ulong>();
        internal bool LobbyReturnInProgress;
        internal bool VoidedMatchLobbyReturnPending;
        internal string VoidedMatchLobbyReturnReason = string.Empty;
        internal bool AvatarsPrepared;
        internal bool UnloadNextFrame;
        internal int UnloadEarliestFrame;
        internal string SceneBeingUnloaded = string.Empty;
        internal string PendingUnloadScene = string.Empty;
        internal double PendingUnloadDeadline;
        internal int PhaseSaveFailureCount;
        internal double SceneOperationDeadline;
        internal bool FailClosedTerminationPending;
        internal string FailClosedReason = string.Empty;
        internal double FailClosedRetryAt;
        internal int FailClosedTerminationAttemptCount;
        internal bool FailClosedCallTimedOut;
        internal bool FailClosedExhaustedStatusShown;
        internal bool PreserveRecoveryOnLobbyReturn;

        internal void Reset()
        {
            PreserveRecoveryOnLobbyReturn = false;
            SceneUnloadQueue.Clear();
            PendingUnloadClients.Clear();
            DisconnectedClients.Clear();
            SceneBeingUnloaded = string.Empty;
            PendingUnloadScene = string.Empty;
            PendingUnloadDeadline = 0d;
            PhaseSaveFailureCount = 0;
            SceneOperationDeadline = 0d;
            FailClosedTerminationPending = false;
            FailClosedReason = string.Empty;
            FailClosedRetryAt = 0d;
            FailClosedTerminationAttemptCount = 0;
            FailClosedCallTimedOut = false;
            FailClosedExhaustedStatusShown = false;
            AvatarsPrepared = false;
            UnloadNextFrame = false;
            UnloadEarliestFrame = 0;
            LobbyReturnInProgress = false;
            VoidedMatchLobbyReturnPending = false;
            VoidedMatchLobbyReturnReason = string.Empty;
        }

        internal void ScheduleUnload(int currentFrame)
        {
            UnloadNextFrame = true;
            UnloadEarliestFrame = Math.Max(
                UnloadEarliestFrame,
                currentFrame + 1);
        }

        internal bool TryConsumeScheduledUnload(int currentFrame)
        {
            if (!LobbyReturnInProgress || !UnloadNextFrame || currentFrame < UnloadEarliestFrame)
                return false;
            UnloadNextFrame = false;
            return true;
        }

        internal bool TryAcknowledgeUnload(ulong clientId, string sceneName, int currentFrame)
        {
            if (!string.Equals(sceneName, PendingUnloadScene, StringComparison.Ordinal)) return false;
            return TryRemovePendingClient(clientId, currentFrame);
        }

        internal bool TryRemovePendingClient(ulong clientId, int currentFrame)
        {
            if (!LobbyReturnInProgress || !PendingUnloadClients.Remove(clientId)) return false;
            if (PendingUnloadClients.Count == 0)
            {
                PendingUnloadScene = string.Empty;
                PendingUnloadDeadline = 0d;
                ScheduleUnload(currentFrame);
            }
            return true;
        }
    }
}
