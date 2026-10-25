#if MAZEPARTY_STEAM
using System;
using System.IO;
using System.Threading.Tasks;
using MazeParty.Gameplay;
using Steamworks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    // Steam verifies the local launch/account/ownership. UGS remains a device-local
    // anonymous identity scoped to that Steam account, not a server-verified Steam login.
    public sealed class SteamPlayerIdentityProvider : IPlatformIdentityProvider
    {
        private bool _initialized, _disposed, _accountLost, _reportedFailure;
        private ulong _user;
        private uint _appId;
        private string _profile;
        private Task _signIn;
        private AchievementJournal _journal;
        private SteamAchievementSync _achievements;
        private Callback<GameRichPresenceJoinRequested_t> _join;
        private Callback<NewUrlLaunchParameters_t> _launch;
        private Callback<UserStatsStored_t> _stored;
        public event Action<string> InviteAccepted;
        public AdmissionProof Proof { get; } = new AdmissionProof();
        public string DisplayName { get; private set; } = "Player";
        public string PlayerId => IsSignedIn ? AuthenticationService.Instance.PlayerId : "";
        public bool IsSignedIn => !_disposed && !_accountLost && _initialized && _user != 0 &&
            UnityServices.State == ServicesInitializationState.Initialized &&
            AuthenticationService.Instance.Profile == _profile && AuthenticationService.Instance.IsSignedIn;
        public bool OverlayAvailable => IsSignedIn && SteamUtils.IsOverlayEnabled();

        public async Task SignInAsync(string requestedDisplayName)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SteamPlayerIdentityProvider));
            DisplayName = PlayerProfilePreferences.SanitizeDisplayName(requestedDisplayName);
            if (_signIn != null) { await _signIn; return; }
            _signIn = SignInCoreAsync();
            try { await _signIn; } finally { _signIn = null; }
        }
        private async Task SignInCoreAsync()
        {
            var settings = PlatformContext.Settings;
            Initialize(settings);
            RequireCurrentUser();
            var profile = PlatformAccountScope.AuthenticationProfile("steam", settings, _user);
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
                await UnityServices.InitializeAsync(new InitializationOptions()
                    .SetEnvironmentName(settings.ugsEnvironment).SetProfile(profile));
            if (UnityServices.State != ServicesInitializationState.Initialized || AuthenticationService.Instance.Profile != profile)
                throw new InvalidOperationException(GameText.T("UGS is using another account. Restart the game."));
            PrivacyDeletionHoldStore.EnsureSignInAllowed();
            var auth = AuthenticationService.Instance;
            if (!auth.IsAuthorized)
            {
                if (auth.IsSignedIn) auth.SignOut();
                await auth.SignInAnonymouslyAsync();
            }
            if (_disposed) throw new ObjectDisposedException(nameof(SteamPlayerIdentityProvider));
            RequireCurrentUser();
            _profile = profile;
            if (_achievements == null && !Application.isEditor && !Debug.isDebugBuild)
            {
                try
                {
                    _journal = new AchievementJournal(Path.Combine(Application.persistentDataPath,
                        "Achievements", profile + ".json"), profile);
                    _achievements = new SteamAchievementSync(_journal, new Api());
                }
                catch (Exception) { ReportAchievementFailure(); }
            }
        }
        private void Initialize(PlatformLaunchSettings settings)
        {
            if (_initialized) return;
            _appId = settings.steamAppId;
            try
            {
                if (!Application.isEditor && SteamAPI.RestartAppIfNecessary(new AppId_t(_appId)))
                {
                    Application.Quit();
                    throw new InvalidOperationException(GameText.T("Restarting through Steam. Open the game from your Steam library."));
                }
                if (!Packsize.Test() || !DllCheck.Test() ||
                    SteamAPI.InitEx(out _) != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                    throw new InvalidOperationException(GameText.T("Steam could not initialize. Sign in to Steam and launch this game from your library."));
                _initialized = true;
                if (SteamUtils.GetAppID().m_AppId != _appId || !SteamUser.BLoggedOn() ||
                    !SteamApps.BIsSubscribed() || !SteamUser.GetSteamID().IsValid())
                    throw new InvalidOperationException(GameText.T("This Steam account cannot run this app. Check the App ID and game license."));
                _user = SteamUser.GetSteamID().m_SteamID;
                _join = Callback<GameRichPresenceJoinRequested_t>.Create(value => InviteAccepted?.Invoke(value.m_rgchConnect));
                _launch = Callback<NewUrlLaunchParameters_t>.Create(_ => ReadSteamLaunch());
                _stored = Callback<UserStatsStored_t>.Create(value =>
                {
                    if (!IsSignedIn || !CurrentUserMatches() || value.m_nGameID != _appId) return;
                    try { _achievements?.Complete(value.m_eResult == EResult.k_EResultOK); }
                    catch (Exception) { _achievements = null; ReportAchievementFailure(); }
                });
                var command = SteamInvite.FromArguments(Environment.GetCommandLineArgs());
                if (command != null) InviteAccepted?.Invoke(command);
                else ReadSteamLaunch();
            }
            catch (DllNotFoundException)
            { throw new InvalidOperationException(GameText.T("The Steam SDK is missing. Verify the game installation.")); }
            catch
            {
                if (_initialized && _user == 0) { SteamAPI.Shutdown(); _initialized = false; }
                throw;
            }
        }
        private void ReadSteamLaunch()
        {
            if (SteamApps.GetLaunchCommandLine(out var command, 1024) > 0 && !string.IsNullOrWhiteSpace(command))
                InviteAccepted?.Invoke(command);
        }
        private bool CurrentUserMatches() => _initialized && SteamUser.BLoggedOn() &&
            SteamUser.GetSteamID().m_SteamID == _user && SteamApps.BIsSubscribed();
        private void RequireCurrentUser()
        {
            if (_accountLost || !CurrentUserMatches())
                throw new InvalidOperationException(GameText.T("The Steam account changed or disconnected. Restart the game."));
        }
        public bool OpenInvite(string connection)
        {
            if (!OverlayAvailable || !CurrentUserMatches() || string.IsNullOrEmpty(connection)) return false;
            SteamFriends.ActivateGameOverlayInviteDialogConnectString(connection);
            return true;
        }
        private string _presence;
        public void SetJoinPresence(string connection)
        {
            if (!_initialized || _presence == connection) return;
            if (SteamFriends.SetRichPresence("connect", connection)) _presence = connection;
        }
        public void CommitAchievements(AchievementReceipt receipt)
        {
            if (!IsSignedIn || _achievements == null || !CurrentUserMatches()) return;
            try { _journal.Commit(receipt, MatchRecoveryFingerprint.CreatePlayerKey(PlayerId)); }
            catch (Exception) { ReportAchievementFailure(); }
        }
        public void Tick()
        {
            if (_disposed || !_initialized) return;
            SteamAPI.RunCallbacks();
            if (!_accountLost && !CurrentUserMatches())
            {
                _accountLost = true;
                SteamFriends.ClearRichPresence();
                Debug.LogWarning("Steam account unavailable. Restart the game before continuing online play.");
            }
            if (IsSignedIn && _achievements != null)
            {
                try { if (!_achievements.Tick(Time.realtimeSinceStartupAsDouble)) ReportAchievementFailure(); }
                catch (Exception) { _achievements = null; ReportAchievementFailure(); }
            }
        }
        private void ReportAchievementFailure()
        {
            if (_reportedFailure) return;
            _reportedFailure = true;
            Debug.LogWarning("Steam achievement sync is pending. Check the published 29 stats / 34 achievements. Saved results are retained for the next launch.");
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _join?.Dispose(); _launch?.Dispose(); _stored?.Dispose();
            if (_initialized) { SteamFriends.ClearRichPresence(); SteamAPI.Shutdown(); _initialized = false; }
            Proof.Dispose();
        }
        private sealed class Api : ISteamAchievementApi
        {
            public bool GetStat(string name, out int value) => SteamUserStats.GetStat(name, out value);
            public bool GetAchievement(string name, out bool achieved) => SteamUserStats.GetAchievement(name, out achieved);
            public bool SetStat(string name, int value) => SteamUserStats.SetStat(name, value);
            public bool SetAchievement(string name) => SteamUserStats.SetAchievement(name);
            public bool StoreStats() => SteamUserStats.StoreStats();
        }
    }
}
#endif
