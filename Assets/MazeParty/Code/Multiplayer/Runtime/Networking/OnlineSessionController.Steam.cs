using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class OnlineSessionController
    {
#if MAZEPARTY_STEAM
        private readonly SteamInviteInbox _steamInvites = new SteamInviteInbox();
        private SteamPlayerIdentityProvider SteamIdentity => _identity as SteamPlayerIdentityProvider;
#endif
        private void InitializeSteamInvites()
        {
#if MAZEPARTY_STEAM
            if (SteamIdentity != null) SteamIdentity.InviteAccepted += OnSteamInviteAccepted;
#endif
        }
        private void RefreshSteamDisplayName()
        {
#if MAZEPARTY_STEAM
            var steam = SteamIdentity;
            if (steam == null || !steam.IsSignedIn) return;
            if (_localProfile.DisplayName != steam.DisplayName)
                SaveLocalProfile(steam.DisplayName, _localProfile.Appearance);
            lobbyView?.SetDisplayName(steam.DisplayName, true);
#endif
        }
        private void TickSteamInvites()
        {
#if MAZEPARTY_STEAM
            var steam = SteamIdentity;
            if (steam == null || _applicationQuitting || _destroyed) return;
            var canInvite = steam.IsSignedIn && SteamInvite.CanInvite(CurrentSession, IsInSession, IsBusy);
            var connection = canInvite ? SteamInvite.Create(PlatformContext.EnvironmentName,
                BuildVersionCompatibility.CurrentVersion, CurrentSession.Code) : null;
            steam.SetJoinPresence(connection);
            lobbyView?.SetSteamInviteState(IsInSession && CurrentSession.Phase == MultiplayerConstants.LobbyPhase,
                canInvite && connection != null && steam.OverlayAvailable);
            if (_steamInvites.TryTake(steam.IsSignedIn, IsBusy, IsInSession, Time.realtimeSinceStartupAsDouble, out var code))
                OnJoinRequested(code, _localProfile.DisplayName);
#endif
        }
        private void OnSteamInviteRequested()
        {
#if MAZEPARTY_STEAM
            var steam = SteamIdentity;
            if (steam == null || !SteamInvite.CanInvite(CurrentSession, IsInSession, IsBusy)) return;
            var value = SteamInvite.Create(PlatformContext.EnvironmentName, BuildVersionCompatibility.CurrentVersion, CurrentSession.Code);
            if (!steam.OpenInvite(value)) SetLocalizedStatus("Steam Overlay is unavailable. Enable it in Steam or share the invite code.");
#endif
        }
#if MAZEPARTY_STEAM
        private void OnSteamInviteAccepted(string value)
        {
            if (_applicationQuitting || _destroyed) return;
            if (!_steamInvites.Accept(value, PlatformContext.EnvironmentName,
                    BuildVersionCompatibility.CurrentVersion, IsInSession, Time.realtimeSinceStartupAsDouble))
            {
                _pendingNotices.Enqueue(IsInSession
                    ? GameText.T("Leave your current room before accepting another Steam invite.")
                    : GameText.T("This Steam invite is invalid or uses another game version or environment."));
                NoticeQueued?.Invoke();
            }
        }
#endif
    }
}
