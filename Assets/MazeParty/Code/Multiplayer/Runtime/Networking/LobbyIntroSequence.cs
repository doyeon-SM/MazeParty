namespace MazeParty.Multiplayer
{
    public enum LobbyIntroStage { Hidden, Intro, Logo, Ready }

    // Kept for this application run, including destruction/recreation of the lobby.
    public sealed class LobbyIntroSequence
    {
        public LobbyIntroStage Stage { get; private set; }
        public bool HasPlayed { get; private set; }
        public double LogoStartedAt { get; private set; }

        public void Begin()
        {
            Stage = HasPlayed ? LobbyIntroStage.Ready : LobbyIntroStage.Intro;
            HasPlayed = true;
        }

        public void FinishIntro(double now)
        {
            if (Stage != LobbyIntroStage.Intro) return;
            Stage = LobbyIntroStage.Logo;
            LogoStartedAt = now;
        }

        public void Advance(double now, double logoDuration)
        {
            if (Stage == LobbyIntroStage.Logo && now >= LogoStartedAt + logoDuration)
                Stage = LobbyIntroStage.Ready;
        }

        public void Hide() => Stage = LobbyIntroStage.Hidden;
    }
}
