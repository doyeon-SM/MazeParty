using System;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class OnlineSessionController
    {
        private void OnApplicationQuitting()
        {
            // MPS owns its Application.quitting LeaveAsync. Suppress controller callbacks
            // so a second project-side Leave cannot stop the same network session twice.
            _applicationQuitting = true;
            // MPS treats graceful application quit as a normal Leave. Keep the
            // reconnect ticket only for a process/network loss that skips this callback.
            ClearPlayingReconnectTicket();
        }

        private void UpdatePlayingReconnectTicket()
        {
            if (!_applicationQuitting &&
                LifecycleState != SessionLifecycleState.Leaving &&
                !_networkTerminationRequested &&
                _sessions != null &&
                _sessions.IsInSession &&
                !_sessions.Current.IsHost &&
                _sessions.Current.Phase == MultiplayerConstants.PlayingPhase &&
                !string.IsNullOrWhiteSpace(_sessions.CurrentSessionId))
            {
                PlayerPrefs.SetString(
                    _playingReconnectTicketKey,
                    _sessions.CurrentSessionId);
                PlayerPrefs.Save();
                return;
            }

            ClearPlayingReconnectTicket();
        }

        private bool TryGetPlayingReconnectTicket(out string sessionId)
        {
            sessionId = string.IsNullOrWhiteSpace(_playingReconnectTicketKey)
                ? string.Empty
                : PlayerPrefs.GetString(_playingReconnectTicketKey, string.Empty).Trim();
            return sessionId.Length > 0;
        }

        private void ClearPlayingReconnectTicket()
        {
            if (string.IsNullOrWhiteSpace(_playingReconnectTicketKey) ||
                !PlayerPrefs.HasKey(_playingReconnectTicketKey))
            {
                return;
            }

            PlayerPrefs.DeleteKey(_playingReconnectTicketKey);
            PlayerPrefs.Save();
        }

        private static string BuildPlayingReconnectTicketKey()
        {
            var profile = "default";
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (!string.Equals(
                        arguments[index],
                        "-auth-profile",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidate = arguments[index + 1].Trim();
                if (candidate.Length > 0)
                {
                    profile = candidate;
                }
                break;
            }

            return PlayingReconnectTicketPrefix + Hash128.Compute(profile);
        }
    }
}
