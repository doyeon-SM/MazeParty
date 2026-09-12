using System;
using System.Collections.Generic;

namespace MazeParty.Dev.MinigameSoloTest
{
    public enum MinigameSoloTestId : byte
    {
        Minefield = 0,
        WrongWay = 1,
        RedLightGreenLight = 3
    }

    public readonly struct MinigameSoloTestDescriptor
    {
        public MinigameSoloTestDescriptor(
            MinigameSoloTestId id,
            string displayName,
            string scenePath)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException(
                    "A display name is required.",
                    nameof(displayName));
            }
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                throw new ArgumentException(
                    "A scene path is required.",
                    nameof(scenePath));
            }

            Id = id;
            DisplayName = displayName;
            ScenePath = scenePath;
        }

        public MinigameSoloTestId Id { get; }
        public string DisplayName { get; }
        public string ScenePath { get; }
    }

    /// <summary>
    /// Editor-only catalog used by the launcher. Add one descriptor and one
    /// bootstrap branch when a new minigame gains a local solo harness.
    /// </summary>
    public static class MinigameSoloTestCatalog
    {
        public const string MinefieldScenePath =
            "Assets/MazeParty/Scenes/Minefield.unity";
        public const string WrongWayScenePath =
            "Assets/MazeParty/Scenes/WrongWay.unity";
        public const string RedLightGreenLightScenePath =
            "Assets/MazeParty/Scenes/RedLightGreenLight.unity";

        private static readonly MinigameSoloTestDescriptor[] Descriptors =
        {
            new MinigameSoloTestDescriptor(
                MinigameSoloTestId.Minefield,
                "Minefield",
                MinefieldScenePath),
            new MinigameSoloTestDescriptor(
                MinigameSoloTestId.WrongWay,
                "WrongWay",
                WrongWayScenePath),
            new MinigameSoloTestDescriptor(
                MinigameSoloTestId.RedLightGreenLight,
                "Red Light, Green Light",
                RedLightGreenLightScenePath)
        };

        public static IReadOnlyList<MinigameSoloTestDescriptor> All =>
            Descriptors;

        public static bool TryGet(
            MinigameSoloTestId id,
            out MinigameSoloTestDescriptor descriptor)
        {
            for (var index = 0; index < Descriptors.Length; index++)
            {
                if (Descriptors[index].Id != id)
                {
                    continue;
                }

                descriptor = Descriptors[index];
                return true;
            }

            descriptor = default;
            return false;
        }
    }
}
