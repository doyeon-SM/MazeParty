using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    public enum RedLightGreenLightHudSignalStyle
    {
        Neutral,
        Green,
        TurnWarning,
        Red
    }

    /// <summary>
    /// Serialized design contract for the Red Light / Green Light HUD prefab.
    /// Runtime presentation code only writes values through these references;
    /// layout, typography and colors remain owned by the prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RedLightGreenLightHudBindings : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private Text signalText;

        [Header("Signal Palette")]
        [SerializeField] private Color neutralSignalColor = Color.white;
        [SerializeField] private Color greenSignalColor =
            new Color(0.22f, 1f, 0.35f, 1f);
        [SerializeField] private Color turnWarningSignalColor =
            new Color(1f, 0.74f, 0.12f, 1f);
        [SerializeField] private Color redSignalColor =
            new Color(1f, 0.16f, 0.1f, 1f);

        public Canvas RootCanvas => rootCanvas;
        public Text SignalText => signalText;
        public Color NeutralSignalColor => neutralSignalColor;
        public Color GreenSignalColor => greenSignalColor;
        public Color TurnWarningSignalColor => turnWarningSignalColor;
        public Color RedSignalColor => redSignalColor;

        public bool HasRequiredReferences =>
            rootCanvas != null &&
            signalText != null;

        public void Configure(
            Canvas canvas,
            Text signal,
            Color neutral,
            Color green,
            Color turnWarning,
            Color red)
        {
            rootCanvas = canvas;
            signalText = signal;
            neutralSignalColor = neutral;
            greenSignalColor = green;
            turnWarningSignalColor = turnWarning;
            redSignalColor = red;
        }

        public void SetSignal(
            string label,
            RedLightGreenLightHudSignalStyle style)
        {
            if (signalText == null)
            {
                return;
            }

            signalText.text = label ?? string.Empty;
            signalText.color = GetSignalColor(style);
        }

        public Color GetSignalColor(RedLightGreenLightHudSignalStyle style)
        {
            switch (style)
            {
                case RedLightGreenLightHudSignalStyle.Green:
                    return greenSignalColor;
                case RedLightGreenLightHudSignalStyle.TurnWarning:
                    return turnWarningSignalColor;
                case RedLightGreenLightHudSignalStyle.Red:
                    return redSignalColor;
                default:
                    return neutralSignalColor;
            }
        }
    }
}
