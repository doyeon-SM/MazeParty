using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class ArenaCombatHealthBarView : MonoBehaviour
    {
        [SerializeField] private Canvas worldCanvas;
        [SerializeField] private Slider slider;
        [SerializeField] private Image fill;

        public bool HasRequiredReferences =>
            worldCanvas != null && slider != null && fill != null &&
            worldCanvas.renderMode == RenderMode.WorldSpace &&
            slider.transform.IsChildOf(worldCanvas.transform) &&
            fill.transform.IsChildOf(slider.transform);

        public void Configure(Canvas canvas, Slider healthSlider,
            Image healthFill)
        {
            worldCanvas = canvas;
            slider = healthSlider;
            fill = healthFill;
        }

        public void SetHealth(int current, int maximum)
        {
            if (!HasRequiredReferences)
            {
                return;
            }

            var ratio = maximum > 0
                ? Mathf.Clamp01(current / (float)maximum)
                : 0f;
            slider.SetValueWithoutNotify(ratio);
            fill.color = ratio > 0.5f
                ? new Color(0.2f, 0.82f, 0.38f, 1f)
                : ratio > 0.25f
                    ? new Color(1f, 0.7f, 0.16f, 1f)
                    : new Color(0.95f, 0.2f, 0.2f, 1f);
        }
    }
}
