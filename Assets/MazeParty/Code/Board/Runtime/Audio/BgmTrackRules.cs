using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames;

namespace MazeParty.Gameplay
{
    /// <summary>Where the local player is, for music selection.</summary>
    public enum BgmScene : byte
    {
        None,
        Lobby,
        WaitingRoom,
        Board,
        Minigame,
        Ceremony
    }

    /// <summary>
    /// Chooses the music for the current game flow. A missing or still empty
    /// track falls back to the next candidate, so per-minigame tracks can be
    /// added one by one.
    /// </summary>
    public static class BgmTrackRules
    {
        public static BgmScene Resolve(
            bool inSession,
            bool matchActive,
            bool backInWaitingRoom,
            BoardFlowState flowState,
            bool ceremonyActive,
            ScheduledMinigameId minigame)
        {
            if (!inSession)
            {
                return BgmScene.Lobby;
            }

            if (!matchActive || backInWaitingRoom)
            {
                return BgmScene.WaitingRoom;
            }

            if (flowState == BoardFlowState.MatchComplete)
            {
                return ceremonyActive ? BgmScene.Ceremony : BgmScene.Board;
            }

            // The rule card (MinigameIntroReady) keeps the board music so the
            // tower reveal is not spoiled by the minigame's own track.
            var minigameRunning =
                flowState == BoardFlowState.MinigameLoading ||
                flowState == BoardFlowState.MinigamePlaying ||
                flowState == BoardFlowState.MinigameResult;
            return minigameRunning && minigame != ScheduledMinigameId.Skip
                ? BgmScene.Minigame
                : BgmScene.Board;
        }

        /// <summary>Keys to try in order for a scene.</summary>
        public static IReadOnlyList<string> Candidates(
            BgmScene scene,
            ScheduledMinigameId minigame)
        {
            switch (scene)
            {
                case BgmScene.Lobby:
                    return new[] { SoundKeys.BgmLobby };
                case BgmScene.WaitingRoom:
                    return new[] { SoundKeys.BgmWaitingRoom, SoundKeys.BgmLobby };
                case BgmScene.Board:
                    return new[] { SoundKeys.BgmBoard };
                case BgmScene.Minigame:
                    return new[]
                    {
                        SoundKeys.MinigameBgm(minigame),
                        SoundKeys.BgmMinigame,
                        SoundKeys.BgmBoard
                    };
                case BgmScene.Ceremony:
                    return new[] { SoundKeys.BgmCeremony, SoundKeys.BgmBoard };
                default:
                    return Array.Empty<string>();
            }
        }

        /// <summary>First candidate that has audio, or null for silence.</summary>
        public static string Pick(
            IReadOnlyList<string> candidates,
            Func<string, bool> hasClips)
        {
            if (candidates == null || hasClips == null)
            {
                return null;
            }

            for (var index = 0; index < candidates.Count; index++)
            {
                if (hasClips(candidates[index]))
                {
                    return candidates[index];
                }
            }

            return null;
        }
    }
}
