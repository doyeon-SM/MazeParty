using System;
using System.Threading.Tasks;
using MazeParty.Gameplay;
using Unity.Services.Authentication;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class OnlineSessionController
    {
        private PrivacyAccountDeletion _privacyDeletion;
        private bool PrivacyDeletionBusy => _privacyDeletion != null && _privacyDeletion.IsBusy;
        public bool PrivacyOnlinePaused => PrivacyDeletionHoldStore.CurrentState != 0;
        public bool PrivacyAccountDeleted => (_privacyDeletion != null && _privacyDeletion.Deleted) || PrivacyDeletionHoldStore.CurrentState == 2;
        public string PrivacyPlayerId => _identity != null && _identity.IsSignedIn ? _identity.PlayerId : _privacyDeletion?.LastPlayerId ?? "";
        public string PrivacyProfile => PrivacyDeletionHoldStore.IsInitialized ? AuthenticationService.Instance.Profile : "";
        private bool PrivacyIsOffline => !_destroyed && !_applicationQuitting && !_sessionOperations.IsBusy &&
            !IsInSession && (_networkManager == null || !_networkManager.IsListening);
        public bool CanDeletePrivacyAccount => PrivacyIsOffline && !PrivacyDeletionBusy && !PrivacyAccountDeleted &&
            _identity != null && _identity.IsSignedIn && PrivacyDeletionHoldStore.IsInitialized && AuthenticationService.Instance.IsAuthorized;
        public bool CanResumePrivacyOnline => PrivacyIsOffline && !PrivacyDeletionBusy && PrivacyOnlinePaused;

        public string PrivacySupportDetails => "MazeParty\nPlayer ID: " + PrivacyPlayerId +
            "\nPlatform: " + PlatformContext.Channel + "\nEnvironment: " +
            PrivacyEnvironmentName +
            "\nVersion: " + Application.version;

        private static string PrivacyEnvironmentName
        {
            get { try { return PlatformContext.EnvironmentName; } catch (Exception) { return "unavailable"; } }
        }

        public async Task DeletePrivacyAccountAsync(string expectedId, string expectedProfile)
        {
            if (_privacyDeletion == null)
                _privacyDeletion = new PrivacyAccountDeletion(new PrivacyBackend(this), PrivacyDeletionHoldStore.Instance);
            var task = _privacyDeletion.DeleteAsync(expectedId, expectedProfile, () => PrivacyIsOffline);
            RenderLobby();
            try { await task; }
            finally
            {
                if (!_destroyed)
                {
                    if (_privacyDeletion.Deleted)
                    {
                        _savedEntry = null;
                        // Only the current identity's reconnect pointer; keep unrelated saves.
                        if (!string.IsNullOrEmpty(_playingReconnectTicketKey)) PlayerPrefs.DeleteKey(_playingReconnectTicketKey);
                        PlayerPrefs.Save();
                    }
                    if (PrivacyOnlinePaused)
                        SetLocalizedStatus(GameText.N("Online access is paused after a deletion request. Open Settings > Privacy & Support to continue."));
                    RenderLobby();
                }
            }
        }

        public void ResumePrivacyOnline(string expectedProfile)
        {
            if (!CanResumePrivacyOnline || expectedProfile != PrivacyProfile) return;
            PrivacyDeletionHoldStore.Instance.Clear(expectedProfile);
            _privacyDeletion = null;
            RunAsync(InitializeRecoveryEntryAsync, GameText.N("Checking the saved game..."), SessionLifecycleState.Connecting);
        }

        private sealed class PrivacyBackend : IPrivacyAccountBackend
        {
            private readonly OnlineSessionController _owner;
            public PrivacyBackend(OnlineSessionController owner) { _owner = owner; }
            public bool IsAuthorized => _owner._identity != null && _owner._identity.IsSignedIn &&
                PrivacyDeletionHoldStore.IsInitialized && AuthenticationService.Instance.IsAuthorized &&
                _owner._identity.PlayerId == AuthenticationService.Instance.PlayerId;
            public string PlayerId => _owner._identity?.PlayerId ?? "";
            public string Profile => _owner.PrivacyProfile;
            public Task DeleteAsync() => AuthenticationService.Instance.DeleteAccountAsync();
        }
    }
}
