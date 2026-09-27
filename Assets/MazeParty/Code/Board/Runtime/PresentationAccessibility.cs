namespace MazeParty.Gameplay
{
    /// <summary>
    /// Central presentation-only accessibility policy. Gameplay simulation and
    /// networking never depend on these values.
    /// </summary>
    public static class PresentationAccessibility
    {
        public const float ReducedScreenShakeScale = 0.25f;
        public const float ReducedFlashIntensityScale = 0.25f;

        public static bool ReduceScreenShake { get; private set; }
        public static bool ReduceFlashes { get; private set; }
        public static float ScreenShakeScale =>
            ReduceScreenShake ? ReducedScreenShakeScale : 1f;
        public static float FlashIntensityScale =>
            ReduceFlashes ? ReducedFlashIntensityScale : 1f;

        public static void Apply(bool reduceScreenShake, bool reduceFlashes)
        {
            ReduceScreenShake = reduceScreenShake;
            ReduceFlashes = reduceFlashes;
        }
    }
}
