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
        [SerializeField] private Text healthText;
        [SerializeField] private BoardHudStatFeedbackView feedback;
        [SerializeField] private Color healthy = new Color(0.2f, 0.82f, 0.38f);
        [SerializeField] private Color wounded = new Color(1f, 0.7f, 0.16f);
        [SerializeField] private Color critical = new Color(0.95f, 0.2f, 0.2f);

        private int _current;
        private int _maximum;
        private bool _initialized;

        public bool HasRequiredReferences =>
            worldCanvas != null && slider != null && fill != null &&
            healthText != null && feedback != null && feedback.HasRequiredReferences &&
            worldCanvas.renderMode == RenderMode.WorldSpace &&
            slider.transform.IsChildOf(worldCanvas.transform) &&
            fill.transform.IsChildOf(slider.transform) &&
            healthText.transform.IsChildOf(slider.transform);

        public void Configure(Canvas canvas, Slider healthSlider, Image healthFill,
            Text label, BoardHudStatFeedbackView statFeedback,
            Color healthyColor, Color woundedColor, Color criticalColor)
        {
            worldCanvas = canvas;
            slider = healthSlider;
            fill = healthFill;
            healthText = label;
            feedback = statFeedback;
            healthy = healthyColor;
            wounded = woundedColor;
            critical = criticalColor;
        }

        public void SetHealth(int current, int maximum)
        {
            if (!HasRequiredReferences) return;
            maximum = Mathf.Max(1, maximum);
            current = Mathf.Clamp(current, 0, maximum);
            var ratio = current / (float)maximum;
            var now = Time.unscaledTimeAsDouble;
            if (!_initialized || _maximum != maximum)
            {
                feedback.ResetHealth(ratio);
                feedback.HideDelta(0);
            }
            else if (_current != current)
            {
                feedback.PresentHealth(_current, current, maximum, now);
                // Arena HP retains the bar trail/pulse, without a delta number.
                feedback.HideDelta(0);
            }
            _initialized = true;
            _current = current;
            _maximum = maximum;
            slider.SetValueWithoutNotify(ratio);
            fill.color = ratio > 0.5f ? healthy : ratio > 0.25f ? wounded : critical;
            healthText.text = current + "/" + maximum;
            feedback.Tick(now);
        }

        private void LateUpdate()
        {
            if (_initialized) feedback.Tick(Time.unscaledTimeAsDouble);
        }

        private void OnDisable() => _initialized = false;
    }
}
