#if MAZEPARTY_STOVE
using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Core.Environments;

namespace MazeParty.Multiplayer
{
    // STOVE verifies launch/ownership locally. UGS authenticates its own cached,
    // device-local anonymous account; this is not a server-verified STOVE link.
    public sealed class StovePlayerIdentityProvider : IPlatformIdentityProvider
    {
        private readonly StoveSdkClient _sdk = new StoveSdkClient();
        private Task _signIn;
        private ulong _signedInStoveUser;
        private string _profile;
        private bool _disposed;
        public AdmissionProof Proof { get; } = new AdmissionProof();
        public string PlayerId => IsSignedIn ? AuthenticationService.Instance.PlayerId : "";
        public string DisplayName { get; private set; } = "Player";
        public bool IsSignedIn => !_disposed && _signedInStoveUser != 0 &&
            UnityServices.State == ServicesInitializationState.Initialized &&
            AuthenticationService.Instance.Profile == _profile && AuthenticationService.Instance.IsSignedIn;

        public async Task SignInAsync(string requestedDisplayName)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(StovePlayerIdentityProvider));
            DisplayName = PlayerProfilePreferences.SanitizeDisplayName(requestedDisplayName);
            if (_signIn != null) { await _signIn; return; }
            _signIn = SignInCoreAsync();
            try { await _signIn; }
            finally { _signIn = null; }
        }

        private async Task SignInCoreAsync()
        {
            var settings = PlatformContext.Settings;
            // No anonymous fallback if the launcher, SDK or ownership check fails.
            await _sdk.InitializeAsync(settings);
            if (_disposed) throw new ObjectDisposedException(nameof(StovePlayerIdentityProvider));
            var stoveUser = _sdk.RefreshUser();
            if (_signedInStoveUser != 0 && stoveUser != _signedInStoveUser)
                throw new InvalidOperationException("The STOVE account changed. Restart the game before continuing.");
            var profile = PlatformAccountScope.AuthenticationProfile(PlatformContext.Channel, settings, stoveUser);
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
                await UnityServices.InitializeAsync(new InitializationOptions()
                    .SetEnvironmentName(settings.ugsEnvironment).SetProfile(profile));
            if (UnityServices.State != ServicesInitializationState.Initialized ||
                AuthenticationService.Instance.Profile != profile)
                throw new InvalidOperationException("UGS was initialized for another account or environment. Restart the game.");

            var auth = AuthenticationService.Instance;
            if (!auth.IsAuthorized)
            {
                // Retain the cached session token. Clearing it loses an anonymous account.
                if (auth.IsSignedIn) auth.SignOut();
                await auth.SignInAnonymouslyAsync();
            }
            if (_disposed) throw new ObjectDisposedException(nameof(StovePlayerIdentityProvider));
            if (_sdk.RefreshUser() != stoveUser)
                throw new InvalidOperationException("The STOVE account changed during sign-in. Restart the game.");
            _profile = profile;
            _signedInStoveUser = stoveUser;
        }

        public void Tick() => _sdk.Tick();
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sdk.Dispose();
            Proof.Dispose();
        }
    }
}
#endif
