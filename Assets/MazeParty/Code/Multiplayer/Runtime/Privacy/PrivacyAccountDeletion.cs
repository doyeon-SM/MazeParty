using System;
using System.Threading.Tasks;
using MazeParty.Gameplay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public interface IPrivacyAccountBackend
    {
        bool IsAuthorized { get; }
        string PlayerId { get; }
        string Profile { get; }
        Task DeleteAsync();
    }

    public interface IPrivacyDeletionHoldStore
    {
        int Read(string profile);
        void Write(string profile, int state);
        void Clear(string profile);
    }

    /// <summary>Serializes deletion and rechecks the identity shown in the confirmation.</summary>
    public sealed class PrivacyAccountDeletion
    {
        private readonly IPrivacyAccountBackend _account;
        private readonly IPrivacyDeletionHoldStore _holds;
        public bool IsBusy { get; private set; }
        public bool Deleted { get; private set; }
        public string LastPlayerId { get; private set; } = "";

        public PrivacyAccountDeletion(IPrivacyAccountBackend account, IPrivacyDeletionHoldStore holds)
        { _account = account; _holds = holds; }

        public async Task DeleteAsync(string expectedId, string expectedProfile, Func<bool> isOffline)
        {
            if (IsBusy || Deleted || !isOffline() || !_account.IsAuthorized ||
                string.IsNullOrWhiteSpace(expectedId) || string.IsNullOrWhiteSpace(expectedProfile) ||
                !string.Equals(expectedId, _account.PlayerId, StringComparison.Ordinal) ||
                !string.Equals(expectedProfile, _account.Profile, StringComparison.Ordinal))
                throw new InvalidOperationException(GameText.T("Return to the main menu and check the current account before deleting."));

            IsBusy = true;
            LastPlayerId = expectedId;
            try
            {
                // Save before the network request: even a crash or unknown network result
                // must not cause a fresh anonymous account to be silently created at startup.
                _holds.Write(expectedProfile, 1);
                await _account.DeleteAsync(); // SDK clears this account's credentials on success.
                Deleted = true;
                _holds.Write(expectedProfile, 2);
            }
            finally { IsBusy = false; }
        }
    }

    public sealed class PrivacyDeletionHoldStore : IPrivacyDeletionHoldStore
    {
        public static readonly PrivacyDeletionHoldStore Instance = new PrivacyDeletionHoldStore();
        private static string Key(string profile) => "MazeParty.Privacy.OnlineHold." + Application.cloudProjectId + "." + profile;
        public int Read(string profile) => PlayerPrefs.GetInt(Key(profile), 0);
        public void Write(string profile, int state)
        { PlayerPrefs.SetInt(Key(profile), state); PlayerPrefs.Save(); }
        public void Clear(string profile)
        { PlayerPrefs.DeleteKey(Key(profile)); PlayerPrefs.Save(); }

        public static bool IsInitialized => UnityServices.State == ServicesInitializationState.Initialized;
        public static int CurrentState => IsInitialized ? Instance.Read(AuthenticationService.Instance.Profile) : 0;
        public static void EnsureSignInAllowed()
        {
            if (CurrentState != 0)
                throw new InvalidOperationException(GameText.T("Online access is paused after a deletion request. Open Settings > Privacy & Support to continue."));
        }
    }
}
