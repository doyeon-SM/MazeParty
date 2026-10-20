using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using MazeParty.Gameplay.Minigames.BalloonBlow;
using MazeParty.Gameplay.Minigames.BouncingBalls;
using MazeParty.Gameplay.Minigames.BombPassing;
using MazeParty.Gameplay.Minigames.CliffBarrage;
using MazeParty.Gameplay.Minigames.GiftGrab;
using MazeParty.Gameplay.Minigames.Minefield;
using MazeParty.Gameplay.Minigames.RedLightGreenLight;
using MazeParty.Gameplay.Minigames.Race;
using MazeParty.Gameplay.Minigames.SequenceMemory;
using MazeParty.Gameplay.Minigames.SnowySpin;
using MazeParty.Gameplay.Minigames.StableFooting;
using MazeParty.Gameplay.Minigames.TagChase;
using MazeParty.Gameplay.Minigames.TerritoryPaint;
using MazeParty.Gameplay.Minigames.WrongWay;
using UnityEngine;

namespace MazeParty.Gameplay.Minigames
{
    public readonly struct MinigameDefinition
    {
        public MinigameDefinition(
            ScheduledMinigameId id,
            string displayName,
            int roundCount,
            float phaseDurationSeconds,
            string sceneName,
            Color towerColor,
            int recoveryCompatibilityVersion = 1)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException(
                    "Display name is required.",
                    nameof(displayName));
            }

            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException(
                    "Scene name is required.",
                    nameof(sceneName));
            }

            if (roundCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(roundCount));
            }

            if (recoveryCompatibilityVersion < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(recoveryCompatibilityVersion));
            }

            Id = id;
            DisplayName = displayName;
            _authoredRoundCount = roundCount;
            _authoredPhaseDurationSeconds = phaseDurationSeconds;
            SceneName = sceneName;
            TowerColor = towerColor;
            RecoveryCompatibilityVersion = recoveryCompatibilityVersion;
        }

        public ScheduledMinigameId Id { get; }
        public string DisplayName { get; }
        private readonly int _authoredRoundCount;
        public int RoundCount => MinigameRuntimeSettings.Rounds(Id, _authoredRoundCount);
        private readonly float _authoredPhaseDurationSeconds;
        public float PhaseDurationSeconds => (float)MinigameRuntimeSettings.Seconds(Id, _authoredPhaseDurationSeconds);
        public string SceneName { get; }
        public Color TowerColor { get; }
        public int RecoveryCompatibilityVersion { get; }
    }

    public static class MinigameCatalog
    {
        public static IReadOnlyList<MinigameDefinition> RegisteredMinigames =>
            RegisteredMinigameDefinitions;

        private static readonly MinigameDefinition[] RegisteredMinigameDefinitions =
        {
            new MinigameDefinition(
                ScheduledMinigameId.Minefield,
                GameText.N("MINEFIELD"),
                3,
                40f,
                "Minefield",
                new Color(0.12f, 0.58f, 0.42f, 1f)),
            new MinigameDefinition(
                ScheduledMinigameId.WrongWay,
                GameText.N("WRONG WAY"),
                2,
                60f,
                "WrongWay",
                new Color(0.95f, 0.42f, 0.12f, 1f)),
            new MinigameDefinition(
                ScheduledMinigameId.RedLightGreenLight,
                GameText.N("RED LIGHT / GREEN LIGHT"),
                3,
                60f,
                "RedLightGreenLight",
                new Color(0.84f, 0.16f, 0.2f, 1f)),
            new MinigameDefinition(
                ScheduledMinigameId.StableFooting,
                GameText.N("STABLE FOOTING"),
                3,
                60f,
                "StableFooting",
                new Color(0.2f, 0.7f, 0.86f, 1f)),
            new MinigameDefinition(
                ScheduledMinigameId.BalloonBlow,
                GameText.N("BALLOON BLOW"),
                3,
                30f,
                "BalloonBlow",
                new Color(0.94f, 0.28f, 0.62f, 1f)),
            new MinigameDefinition(
                ScheduledMinigameId.GiftGrab,
                GameText.N("GIFT GRAB"),
                2,
                60f,
                "GiftGrab",
                new Color(0.72f, 0.36f, 0.92f, 1f)),
            new MinigameDefinition(
                ScheduledMinigameId.TerritoryPaint,
                GameText.N("TERRITORY PAINT"),
                1,
                60f,
                "TerritoryPaint",
                new Color(0.16f, 0.72f, 0.68f, 1f)),
            new MinigameDefinition(
                ScheduledMinigameId.TagChase,
                GameText.N("TAG CHASE"),
                4,
                60f,
                "TagChase",
                new Color(0.94f, 0.18f, 0.28f, 1f)),
            new MinigameDefinition(
                ScheduledMinigameId.Race,
                GameText.N("RACE"),
                3,
                60f,
                "Race",
                new Color(0.98f, 0.64f, 0.12f, 1f)),
            new MinigameDefinition(
                ScheduledMinigameId.SequenceMemory,
                GameText.N("SEQUENCE MEMORY"),
                10,
                (float)SequenceMemoryRules.InputWindowSeconds,
                "SequenceMemory",
                new Color(0.48f, 0.36f, 0.94f, 1f)),
            new MinigameDefinition(
                ScheduledMinigameId.BouncingBalls,
                GameText.N("BOUNCING BALLS"),
                2,
                60f,
                "BouncingBalls",
                new Color(0.26f, 0.66f, 0.92f, 1f)),
            new MinigameDefinition(
                ScheduledMinigameId.BombPassing,
                GameText.N("BOMB PASSING"),
                1,
                // Metadata only: one bomb's maximum fuse, not a match limit.
                (float)BombPassingRules.MaximumFuseSeconds,
                "BombPassing",
                new Color(0.95f, 0.55f, 0.11f, 1f)),
            new MinigameDefinition(
                ScheduledMinigameId.SnowySpin,
                GameText.N("SNOWY SPIN"),
                3,
                60f,
                "SnowySpin",
                new Color(0.62f, 0.83f, 0.98f, 1f)),
            new MinigameDefinition(
                ScheduledMinigameId.ArenaCombat,
                GameText.N("ARENA COMBAT"),
                1,
                60f,
                "ArenaCombat",
                new Color(0.87f, 0.22f, 0.2f, 1f)),
            new MinigameDefinition(
                ScheduledMinigameId.CliffBarrage,
                GameText.N("CLIFF BARRAGE"),
                3,
                60f,
                "CliffBarrage",
                new Color(0.98f, 0.48f, 0.14f, 1f)),
            new MinigameDefinition(
                ScheduledMinigameId.CoinCollect,
                GameText.N("COIN COLLECT"),
                2, 60f, "CoinCollect",
                new Color(1f, 0.78f, 0.16f, 1f)),
            new MinigameDefinition(ScheduledMinigameId.OddOneOut, GameText.N("ODD ONE OUT"),
                10, 3f, "OddOneOut", new Color(.65f, .38f, .95f, 1f)),
            new MinigameDefinition(ScheduledMinigameId.Race2, GameText.N("RACE 2"),
                3, 120f, "Race2", new Color(.25f, .7f, .95f, 1f)),
            new MinigameDefinition(ScheduledMinigameId.ShellGame, GameText.N("SHELL GAME"),
                5, 10f, "ShellGame", new Color(1f, .7f, .2f, 1f)),
            new MinigameDefinition(ScheduledMinigameId.Flight, GameText.N("FLIGHT"),
                1, 120f, "Flight", new Color(.3f, .8f, 1f, 1f))
        };

        public static int RegisteredCount => RegisteredMinigameDefinitions.Length;
        public static string CurrentRecoveryFingerprint =>
            GetRecoveryFingerprint(RegisteredMinigameDefinitions.Length);

        public static string GetDisplayName(
            ScheduledMinigameId minigame)
        {
            return TryGetDefinition(minigame, out var definition)
                ? definition.DisplayName
                : minigame == ScheduledMinigameId.Skip
                    ? GameText.N("SKIP")
                    : GameText.N("UNKNOWN");
        }

        public static string GetDescription(ScheduledMinigameId minigame)
        {
            var description = GameText.T(GetDescriptionSource(minigame));
            var settings = MinigameRuntimeSettings.Current;
            if (settings == null || !IsRegistered(minigame)) return description;
            var value = settings.Get(minigame);
            var summary = MatchGameSettings.HasTimeLimit(minigame)
                ? GameText.F("{0} rounds · {1}s per round", value.Rounds, value.Seconds)
                : GameText.F("{0} rounds", value.Rounds);
            if (value.Rounds > 1 && (minigame == ScheduledMinigameId.TerritoryPaint ||
                minigame == ScheduledMinigameId.BombPassing || minigame == ScheduledMinigameId.ArenaCombat ||
                minigame == ScheduledMinigameId.Flight))
                description += "\n" + GameText.T("Round placement points: 3 / 2 / 1 / 0.");
            return summary + "\n" + description;
        }

        public static string GetDescriptionSource(ScheduledMinigameId minigame)
        {
            switch (minigame)
            {
                case ScheduledMinigameId.Minefield:
                    return GameText.N("Find and avoid the mines to reach the goal.");
                case ScheduledMinigameId.WrongWay:
                    return GameText.N("Enter the direction shown on screen.");
                case ScheduledMinigameId.RedLightGreenLight:
                    return GameText.N("Move on green. Stop on red.");
                case ScheduledMinigameId.StableFooting:
                    return GameText.N("The safe platform has a green symbol.");
                case ScheduledMinigameId.BalloonBlow:
                    return GameText.N("Control your breath to pop the balloon. Push too hard and you may collapse.");
                case ScheduledMinigameId.GiftGrab:
                    return GameText.N("Bring more gold bars into your zone. You can steal or throw them.");
                case ScheduledMinigameId.TerritoryPaint:
                    return GameText.N("Paint as much territory as you can.");
                case ScheduledMinigameId.TagChase:
                    return GameText.N("Run from the tagger until time runs out. If you are the tagger, catch everyone.");
                case ScheduledMinigameId.Race:
                    return GameText.N("Press A and D alternately as fast as you can.");
                case ScheduledMinigameId.SequenceMemory:
                    return GameText.N("Memorize the notes you hear.");
                case ScheduledMinigameId.BouncingBalls:
                    return GameText.N("Keep the ball out of your goal and bounce it into another.");
                case ScheduledMinigameId.BombPassing:
                    return GameText.N("Touch another player to pass the bomb before it explodes. Each pass has a 1-second cooldown.");
                case ScheduledMinigameId.SnowySpin:
                    return GameText.N("Roll your snowball and knock others off.");
                case ScheduledMinigameId.ArenaCombat:
                    return GameText.N("Become the top fighter in a bare-knuckle brawl.");
                case ScheduledMinigameId.CliffBarrage:
                    return GameText.N("Dodge and survive.");
                case ScheduledMinigameId.Flight:
                    return GameText.N("Click to flap and glide through the gaps. Obstacles speed up. Hitting an obstacle or falling below the screen eliminates you.");
                case ScheduledMinigameId.ShellGame:
                    return GameText.N("Follow the coin and click the correct box.");
                case ScheduledMinigameId.Race2:
                    return GameText.N("Choose left with A or right with D to dodge boxes. Boxes speed up; one collision eliminates you.");
                case ScheduledMinigameId.OddOneOut:
                    return GameText.N("Choose sit or stand secretly during the countdown. Only one sitting or standing scores 1 point. Click again to switch.");
                case ScheduledMinigameId.CoinCollect:
                    return GameText.N("Collect falling coins and money. Coin +1, Money +5. Bombs stun for 1 second and drop half your score. Click to push.");
                default:
                    return string.Empty;
            }
        }

        public static bool IsRegistered(ScheduledMinigameId minigame)
        {
            return TryGetDefinition(minigame, out _);
        }

        public static bool TryGetDefinition(
            ScheduledMinigameId minigame,
            out MinigameDefinition definition)
        {
            for (var index = 0; index < RegisteredMinigameDefinitions.Length; index++)
            {
                definition = RegisteredMinigameDefinitions[index];
                if (definition.Id == minigame)
                {
                    return true;
                }
            }

            definition = default;
            return false;
        }

        public static ScheduledMinigameId GetRegisteredGame(int index)
        {
            if (index < 0 || index >= RegisteredMinigameDefinitions.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return RegisteredMinigameDefinitions[index].Id;
        }

        internal static bool IsKnownValue(
            ScheduledMinigameId minigame,
            int registeredGameCount)
        {
            if (minigame == ScheduledMinigameId.Skip)
            {
                return true;
            }

            if (registeredGameCount <= 0)
            {
                return false;
            }

            var max = Math.Min(
                registeredGameCount,
                RegisteredMinigameDefinitions.Length);
            for (var index = 0; index < max; index++)
            {
                if (RegisteredMinigameDefinitions[index].Id == minigame)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Stable fingerprint for the append-only schedule catalog prefix.
        /// Presentation metadata is intentionally excluded; increment a
        /// definition's recovery compatibility version only when replaying a
        /// saved match with that minigame would no longer be safe.
        /// </summary>
        public static string GetRecoveryFingerprint(int catalogEntryCount)
        {
            if (catalogEntryCount < 1 ||
                catalogEntryCount > RegisteredMinigameDefinitions.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(catalogEntryCount));
            }

            var fingerprintInput = new byte[catalogEntryCount * 5];
            for (var index = 0; index < catalogEntryCount; index++)
            {
                var definition = RegisteredMinigameDefinitions[index];
                var offset = index * 5;
                fingerprintInput[offset] = (byte)definition.Id;
                var version = definition.RecoveryCompatibilityVersion;
                fingerprintInput[offset + 1] = (byte)version;
                fingerprintInput[offset + 2] = (byte)(version >> 8);
                fingerprintInput[offset + 3] = (byte)(version >> 16);
                fingerprintInput[offset + 4] = (byte)(version >> 24);
            }

            byte[] hash;
            using (var sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(fingerprintInput);
            }

            var text = new StringBuilder(hash.Length * 2);
            for (var index = 0; index < hash.Length; index++)
            {
                text.Append(hash[index].ToString("x2"));
            }

            return text.ToString();
        }

        public static bool IsRecoveryFingerprintCompatible(
            int catalogEntryCount,
            string fingerprint)
        {
            if (catalogEntryCount < 1 ||
                catalogEntryCount > RegisteredMinigameDefinitions.Length ||
                string.IsNullOrEmpty(fingerprint))
            {
                return false;
            }

            return string.Equals(
                fingerprint,
                GetRecoveryFingerprint(catalogEntryCount),
                StringComparison.Ordinal);
        }

        public static int GetRoundCount(ScheduledMinigameId minigame)
        {
            return TryGetDefinition(minigame, out var definition)
                ? definition.RoundCount
                : 0;
        }

        internal static float GetPhaseDurationSeconds(ScheduledMinigameId minigame)
        {
            return TryGetDefinition(minigame, out var definition)
                ? definition.PhaseDurationSeconds
                : 0f;
        }

        public static string GetSceneName(ScheduledMinigameId minigame)
        {
            return TryGetDefinition(minigame, out var definition)
                ? definition.SceneName
                : string.Empty;
        }

        public static Color GetTowerColor(ScheduledMinigameId minigame)
        {
            return TryGetDefinition(minigame, out var definition)
                ? definition.TowerColor
                : Color.gray;
        }
    }
}
