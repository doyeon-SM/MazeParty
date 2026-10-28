using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    public sealed partial class BoardHudCanvasBindings
    {
        [SerializeField] private Outline[] ghostPassOutlines;
        [SerializeField, Min(0.1f)] private float ghostPassPulsePeriod = 1.6f;
        [SerializeField, Range(0f, 1f)] private float ghostPassPulseMinimumAlpha = 0.45f;
        [SerializeField, Range(0f, 1f)] private float ghostPassPulseMinimumBrightness = 0.75f;
        [SerializeField, Min(1f)] private float ghostPassPulseMaximumWidth = 1.2f;

        private struct GhostPassPulseState
        {
            public Color BaseColor;
            public Vector2 BaseDistance;
            public bool Cached, Visible;
            public double StartedAt;
        }

        private readonly GhostPassPulseState[] _ghostPassPulseStates =
            new GhostPassPulseState[MultiplayerConstants.MaxPlayers];

        public bool HasGhostPassReferences
        {
            get
            {
                if (ghostPassOutlines == null || ghostPassOutlines.Length != MultiplayerConstants.MaxPlayers)
                    return false;
                for (var slot = 0; slot < ghostPassOutlines.Length; slot++)
                    if (ghostPassOutlines[slot] == null ||
                        !ghostPassOutlines[slot].transform.IsChildOf(transform)) return false;
                return true;
            }
        }

        public void PresentGhostPass(int slot, bool visible, double now)
        {
            if (ghostPassOutlines == null || slot < 0 || slot >= ghostPassOutlines.Length ||
                slot >= _ghostPassPulseStates.Length || ghostPassOutlines[slot] == null) return;
            var outline = ghostPassOutlines[slot];
            ref var state = ref _ghostPassPulseStates[slot];
            if (!visible)
            {
                outline.enabled = false;
                if (state.Visible)
                {
                    outline.effectColor = state.BaseColor;
                    outline.effectDistance = state.BaseDistance;
                    state.Visible = false;
                }
                return;
            }

            // Preserve each outline's prefab-authored color and thickness.
            if (!state.Cached)
            {
                state.BaseColor = outline.effectColor;
                state.BaseDistance = outline.effectDistance;
                state.Cached = true;
            }
            if (!state.Visible)
            {
                state.StartedAt = now;
                state.Visible = true;
            }

            var elapsed = System.Math.Max(0d, now - state.StartedAt);
            var phase = (float)(elapsed / Mathf.Max(0.1f, ghostPassPulsePeriod) % 1d);
            var pulse = 0.5f + 0.5f * Mathf.Cos(phase * Mathf.PI * 2f);
            var brightness = Mathf.Lerp(ghostPassPulseMinimumBrightness, 1f, pulse);
            var color = state.BaseColor;
            color.r *= brightness;
            color.g *= brightness;
            color.b *= brightness;
            color.a *= Mathf.Lerp(ghostPassPulseMinimumAlpha, 1f, pulse);
            outline.effectColor = color;
            outline.effectDistance = state.BaseDistance *
                Mathf.Lerp(1f, ghostPassPulseMaximumWidth, pulse);
            outline.enabled = true;
        }

        public void ResetGhostPassPresentation()
        {
            for (var slot = 0; slot < _ghostPassPulseStates.Length; slot++)
                PresentGhostPass(slot, false, 0d);
        }
    }
}
