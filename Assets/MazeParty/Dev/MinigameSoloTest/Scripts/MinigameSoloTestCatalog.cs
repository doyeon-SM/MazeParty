using System;
using System.Collections.Generic;

namespace MazeParty.Dev.MinigameSoloTest
{
    public enum MinigameSoloTestId : byte
    {
        Minefield = 0,
        WrongWay = 1,
        RedLightGreenLight = 3,
        StableFooting = 4,
        BalloonBlow = 5,
        GiftGrab = 6,
        TerritoryPaint = 7,
        TagChase = 8,
        Race = 9,
        SequenceMemory = 10,
        BouncingBalls = 11,
        BombPassing = 12,
        SnowySpin = 13,
        ArenaCombat = 14,
        CliffBarrage = 15
    }

    public readonly struct MinigameSoloTestDescriptor
    {
        public MinigameSoloTestDescriptor(
            MinigameSoloTestId id,
            string displayName,
            string scenePath,
            string controlsLabel)
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
            if (string.IsNullOrWhiteSpace(controlsLabel))
            {
                throw new ArgumentException(
                    "A controls label is required.",
                    nameof(controlsLabel));
            }

            Id = id;
            DisplayName = displayName;
            ScenePath = scenePath;
            ControlsLabel = controlsLabel;
        }

        public MinigameSoloTestId Id { get; }
        public string DisplayName { get; }
        public string ScenePath { get; }
        public string ControlsLabel { get; }
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
        public const string StableFootingScenePath =
            "Assets/MazeParty/Scenes/StableFooting.unity";
        public const string BalloonBlowScenePath =
            "Assets/MazeParty/Scenes/BalloonBlow.unity";
        public const string GiftGrabScenePath =
            "Assets/MazeParty/Scenes/GiftGrab.unity";
        public const string TerritoryPaintScenePath =
            "Assets/MazeParty/Scenes/TerritoryPaint.unity";
        public const string TagChaseScenePath =
            "Assets/MazeParty/Scenes/TagChase.unity";
        public const string RaceScenePath =
            "Assets/MazeParty/Scenes/Race.unity";
        public const string SequenceMemoryScenePath =
            "Assets/MazeParty/Scenes/Minigames/SequenceMemory.unity";
        public const string BouncingBallsScenePath =
            "Assets/MazeParty/Scenes/Minigames/BouncingBalls.unity";
        public const string BombPassingScenePath =
            "Assets/MazeParty/Scenes/Minigames/BombPassing.unity";
        public const string SnowySpinScenePath =
            "Assets/MazeParty/Scenes/Minigames/SnowySpin.unity";
        public const string ArenaCombatScenePath =
            "Assets/MazeParty/Scenes/Minigames/ArenaCombat.unity";
        public const string CliffBarrageScenePath =
            "Assets/MazeParty/Scenes/Minigames/CliffBarrage.unity";

        private static readonly MinigameSoloTestDescriptor[] Descriptors =
        {
            new MinigameSoloTestDescriptor(
                MinigameSoloTestId.Minefield,
                "Minefield",
                MinefieldScenePath,
                "WASD move · stop + RMB sonar · R restart · " +
                "N next seed · Esc stop"),
            new MinigameSoloTestDescriptor(
                MinigameSoloTestId.WrongWay,
                "WrongWay",
                WrongWayScenePath,
                "WASD match prompt · R restart · " +
                "N next seed · Esc stop"),
            new MinigameSoloTestDescriptor(
                MinigameSoloTestId.RedLightGreenLight,
                "Red Light, Green Light",
                RedLightGreenLightScenePath,
                "WASD move on green · freeze on red · " +
                "R restart · N next seed · Esc stop"),
            new MinigameSoloTestDescriptor(
                MinigameSoloTestId.StableFooting,
                "Stable Footing",
                StableFootingScenePath,
                "WASD move · LMB push · R restart · " +
                "N next seed · Esc stop"),
            new MinigameSoloTestDescriptor(
                MinigameSoloTestId.BalloonBlow,
                "Balloon Blow",
                BalloonBlowScenePath,
                "Hold LMB inflate · release to rest · " +
                "R restart · N next seed · Esc stop"),
            new MinigameSoloTestDescriptor(
                MinigameSoloTestId.GiftGrab,
                "Gift Grab",
                GiftGrabScenePath,
                "WASD move + auto pickup · LMB throw / push · " +
                "R restart · N next seed · Esc stop"),
            new MinigameSoloTestDescriptor(
                MinigameSoloTestId.TerritoryPaint,
                "Territory Paint",
                TerritoryPaintScenePath,
                "WASD move + paint · R restart · " +
                "N next seed · Esc stop"),
            new MinigameSoloTestDescriptor(
                MinigameSoloTestId.TagChase,
                "Tag Chase",
                TagChaseScenePath,
                "WASD move · mouse look + LMB catch as tagger · " +
                "R restart · N next seed · Esc stop"),
            new MinigameSoloTestDescriptor(
                MinigameSoloTestId.Race,
                "Race",
                RaceScenePath,
                "Alternate A / D · first to 500 · " +
                "R restart · N next seed · Esc stop"),
            new MinigameSoloTestDescriptor(
                MinigameSoloTestId.SequenceMemory,
                "Sequence Memory",
                SequenceMemoryScenePath,
                "Repeat with A / S / D · one mistake loses torso · " +
                "two mistakes eliminate · R restart · " +
                "N next seed · Esc stop"),
            new MinigameSoloTestDescriptor(
                MinigameSoloTestId.BouncingBalls,
                "Bouncing Balls",
                BouncingBallsScenePath,
                "Hold A / D to move shield · claim and score with balls · " +
                "R restart · N next seed · Esc stop"),
            new MinigameSoloTestDescriptor(
                MinigameSoloTestId.BombPassing,
                "Bomb Passing",
                BombPassingScenePath,
                "WASD move · LMB pass bomb / stun nearby player · " +
                "R restart · N next seed · Esc stop"),
            new MinigameSoloTestDescriptor(
                MinigameSoloTestId.SnowySpin,
                "Snowy Spin",
                SnowySpinScenePath,
                "WASD roll · accelerate and push balls off the ice · " +
                "R restart · N next seed · Esc stop"),
            new MinigameSoloTestDescriptor(
                MinigameSoloTestId.ArenaCombat,
                "Arena Combat",
                ArenaCombatScenePath,
                "WASD move · mouse look · LMB punch · " +
                "R restart · N next seed · Esc stop"),
            new MinigameSoloTestDescriptor(
                MinigameSoloTestId.CliffBarrage,
                "Cliff Barrage",
                CliffBarrageScenePath,
                "WASD move · LMB push · dodge shells and lasers · " +
                "R restart · N next seed · Esc stop")
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
