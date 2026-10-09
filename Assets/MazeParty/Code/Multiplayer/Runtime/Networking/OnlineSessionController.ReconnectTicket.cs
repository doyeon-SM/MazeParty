using System;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class OnlineSessionController
    {
        private void OnApplicationQuitting()
        {
            // Menu and window-close paths normally finish project-side Leave before
            // reaching this non-cancellable phase. Keep the flag for external quit
            // paths and arm the Windows native-shutdown fallback idempotently.
            _applicationQuitting = true;
            ClearPlayingReconnectTicket();
            ApplicationExitWatchdog.Arm(
                _allowApplicationQuit
                    ? ApplicationQuitNativeWatchdogMilliseconds
                    : ApplicationQuitUnpreparedWatchdogMilliseconds,
                0);
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
