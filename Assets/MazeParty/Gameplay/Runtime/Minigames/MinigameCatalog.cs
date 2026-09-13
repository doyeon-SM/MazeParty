using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.BalloonBlow;
using MazeParty.Gameplay.Minigames.GiftGrab;
using MazeParty.Gameplay.Minigames.Minefield;
using MazeParty.Gameplay.Minigames.RedLightGreenLight;
using MazeParty.Gameplay.Minigames.StableFooting;
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
            Color towerColor)
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

            Id = id;
            DisplayName = displayName;
            RoundCount = roundCount;
            PhaseDurationSeconds = phaseDurationSeconds;
            SceneName = sceneName;
            TowerColor = towerColor;
        }

        public ScheduledMinigameId Id { get; }
        public string DisplayName { get; }
        public int RoundCount { get; }
        public float PhaseDurationSeconds { get; }
        public string SceneName { get; }
        public Color TowerColor { get; }
    }

    public static class MinigameCatalog
    {
        public static IReadOnlyList<MinigameDefinition> RegisteredMinigames =>
            RegisteredMinigameDefinitions;

        private static readonly MinigameDefinition[] RegisteredMinigameDefinitions =
        {
            new MinigameDefinition(
                ScheduledMinigameId.Minefield,
                "MINEFIELD",
                MinefieldRules.RoundCount,
                40f,
                "Minefield",
                new Color(0.12f, 0.58f, 0.42f, 1f)),
            new MinigameDefinition(
                ScheduledMinigameId.WrongWay,
                "WRONG WAY",
                WrongWayRules.RoundCount,
                60f,
                "WrongWay",
                new Color(0.95f, 0.42f, 0.12f, 1f)),
            new MinigameDefinition(
                ScheduledMinigameId.RedLightGreenLight,
                "RED LIGHT / GREEN LIGHT",
                RedLightGreenLightRules.RoundCount,
                60f,
                "RedLightGreenLight",
                new Color(0.84f, 0.16f, 0.2f, 1f)),
            new MinigameDefinition(
                ScheduledMinigameId.StableFooting,
                "STABLE FOOTING",
                StableFootingRules.RoundCount,
                60f,
                "StableFooting",
                new Color(0.2f, 0.7f, 0.86f, 1f)),
            new MinigameDefinition(
                ScheduledMinigameId.BalloonBlow,
                "BALLOON BLOW",
                BalloonBlowRules.RoundCount,
                30f,
                "BalloonBlow",
                new Color(0.94f, 0.28f, 0.62f, 1f)),
            new MinigameDefinition(
                ScheduledMinigameId.GiftGrab,
                "GIFT GRAB",
                GiftGrabRules.RoundCount,
                60f,
                "GiftGrab",
                new Color(0.72f, 0.36f, 0.92f, 1f))
        };

        public static int RegisteredCount => RegisteredMinigameDefinitions.Length;

        public static string GetDisplayName(
            ScheduledMinigameId minigame)
        {
            return TryGetDefinition(minigame, out var definition)
                ? definition.DisplayName
                : minigame == ScheduledMinigameId.Skip
                    ? "SKIP"
                    : "UNKNOWN";
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
