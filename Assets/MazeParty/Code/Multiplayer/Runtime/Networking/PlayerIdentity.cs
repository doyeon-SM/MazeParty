using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Core.Environments;

namespace MazeParty.Multiplayer
{
    public interface IPlayerIdentityProvider
    {
        bool IsSignedIn { get; }
        string PlayerId { get; }
        string DisplayName { get; }
        Task SignInAsync(string requestedDisplayName);
    }

    public sealed class UnityAnonymousIdentityProvider : IPlayerIdentityProvider
    {
        private const string EnvironmentName = "production";

        public bool IsSignedIn =>
            UnityServices.State == ServicesInitializationState.Initialized &&
            AuthenticationService.Instance.IsSignedIn;

        public string PlayerId => IsSignedIn ? AuthenticationService.Instance.PlayerId : string.Empty;
        public string DisplayName { get; private set; } = "Player";

        public async Task SignInAsync(string requestedDisplayName)
        {
            DisplayName = SanitizeDisplayName(requestedDisplayName);

            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                var options = new InitializationOptions().SetEnvironmentName(EnvironmentName);
                var profile = GetCommandLineProfile();
                if (!string.IsNullOrWhiteSpace(profile))
                {
                    options.SetProfile(profile);
                }

                await UnityServices.InitializeAsync(options);
            }

            PrivacyDeletionHoldStore.EnsureSignInAllowed();
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        private static string SanitizeDisplayName(string value)
        {
            return PlayerProfilePreferences.SanitizeDisplayName(value);
        }

        // TODO(STEAM-AUTH): Replace or decorate this provider when Steam is enabled.
        // Obtain a Web API session ticket via Steamworks GetAuthTicketForWebApi, convert it
        // to the format required by Unity Authentication, and call SignInWithSteamAsync.
        // Release accounts start fresh in a separate platform profile; do not link development
        // anonymous accounts. The ticket must match the Unity Dashboard Steam identity.

        private static string GetCommandLineProfile()
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(
                        arguments[index],
                        "-auth-profile",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return arguments[index + 1].Trim();
                }
            }

            return string.Empty;
        }
    }
}
