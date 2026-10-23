using System;
using System.Text.RegularExpressions;

namespace MazeParty.Multiplayer
{
    // Connection data is input, never a command to execute. Only our exact protocol is accepted.
    public static class SteamInvite
    {
        public const string Argument = "+mp_join";
        public static string Create(string environment, string version, string code)
        {
            var value = Argument + " " + environment + "/" + version + "/" + code;
            return TryRead(value, environment, version, out _) ? value : null;
        }
        public static bool TryRead(string value, string environment, string version, out string code)
        {
            code = null;
            if (string.IsNullOrEmpty(value) || value.Length > 200) return false;
            var match = Regex.Match(value, @"\A\+mp_join ([a-z0-9][a-z0-9-]{0,39})/([A-Za-z0-9._-]{1,40})/([A-Z0-9]{4,16})\z");
            if (!match.Success || match.Groups[1].Value != environment || match.Groups[2].Value != version) return false;
            code = match.Groups[3].Value;
            return true;
        }
        public static string FromArguments(string[] arguments)
        {
            string found = null;
            if (arguments == null) return null;
            for (var index = 1; index < arguments.Length; index++)
            {
                if (arguments[index] != Argument) continue;
                if (found != null || index + 1 >= arguments.Length) return null;
                found = Argument + " " + arguments[++index];
            }
            return found;
        }
        public static bool CanInvite(SessionSnapshot session, bool inSession, bool busy) =>
            inSession && !busy && session != null && session.Phase == MultiplayerConstants.LobbyPhase &&
            session.Players.Count > 0 && session.Players.Count < MultiplayerConstants.MaxPlayers &&
            session.WaitingPlayerNames.Count == 0 && !string.IsNullOrEmpty(session.Code);
    }

    // One accepted invite survives startup/sign-in; it cannot displace an existing room.
    public sealed class SteamInviteInbox
    {
        private string _code;
        private double _expires;
        public bool Accept(string value, string environment, string version, bool inSession, double now)
        {
            if (inSession || !SteamInvite.TryRead(value, environment, version, out var code)) return false;
            _code = code; _expires = now + 90d;
            return true;
        }
        public bool TryTake(bool signedIn, bool busy, bool inSession, double now, out string code)
        {
            code = null;
            if (inSession || now >= _expires) { _code = null; return false; }
            if (_code == null || !signedIn || busy) return false;
            code = _code; _code = null;
            return true;
        }
    }
}
