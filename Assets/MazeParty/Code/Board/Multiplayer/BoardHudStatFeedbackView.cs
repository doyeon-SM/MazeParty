using System;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>Only updates the authored HUD graphics; creates no UI at runtime.</summary>
    [DisallowMultipleComponent]
    public sealed class BoardHudStatFeedbackView : MonoBehaviour
    {
        [Serializable]
        public sealed class Delta
        {
            public Text Label;
            public RawImage Icon;
            public CanvasGroup Group;
            public Text Value;
            public Graphic ValueIcon;
        }

        [SerializeField] private Slider health;
        [SerializeField] private Image healthFill;
        [SerializeField] private Slider healthTrail;
        [SerializeField] private Image healthTrailFill;
        [SerializeField] private Vector3 healthBaseScale = Vector3.one;
        [SerializeField] private Delta[] deltas = new Delta[3];
        [SerializeField] private Color currencyGain = new Color(1f, 0.82f, 0.16f);
        [SerializeField] private Color healthGain = new Color(0.3f, 1f, 0.48f);
        [SerializeField] private Color loss = new Color(1f, 0.25f, 0.22f);
        [SerializeField, Range(0f, 1f)] private float trailBrightness = 0.45f;
        [SerializeField, Range(0.01f, 0.5f)] private float fadeSeconds = 0.15f;

        private readonly double[] _deltaStartedAt =
            { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
        private readonly BoardHudHealthAnimation _healthAnimation = new BoardHudHealthAnimation();
        public bool HasRequiredReferences
        {
            get
            {
                if (health == null || healthFill == null || healthTrail == null ||
                    healthTrailFill == null || healthTrail == health ||
                    healthTrail.fillRect != healthTrailFill.rectTransform ||
                    deltas == null || (deltas.Length != 1 && deltas.Length != 3)) return false;
                for (var kind = 0; kind < deltas.Length; kind++)
                {
                    var delta = deltas[kind];
                    if (delta == null || delta.Label == null || delta.Icon == null ||
                        delta.Icon.texture == null || delta.Group == null || delta.Value == null ||
                        (kind > 0 && delta.ValueIcon == null)) return false;
                }
                return true;
            }
        }

        public void Configure(Slider main, Image fill, Slider trail, Image trailFill, Delta[] values)
        {
            health = main;
            healthFill = fill;
            healthTrail = trail;
            healthTrailFill = trailFill;
            healthBaseScale = main.transform.localScale;
            deltas = values;
        }

        public void ResetHealth(float ratio)
        {
            _healthAnimation.Reset(ratio);
            if (health != null)
            {
                health.SetValueWithoutNotify(ratio);
                health.transform.localScale = healthBaseScale;
            }
            if (healthTrail != null) healthTrail.SetValueWithoutNotify(ratio);
        }

        public void PresentHealth(int before, int after, int maximum, double now)
        {
            _healthAnimation.Change(before, after, maximum, now);
            health.SetValueWithoutNotify(after / (float)Math.Max(1, maximum));
        }

        public void PresentDelta(int kind, int amount, double now)
        {
            if (!HasRequiredReferences || kind < 0 || kind >= deltas.Length || amount == 0) return;
            var delta = deltas[kind];
            delta.Label.text = (amount > 0 ? "+" : "-") + Math.Abs((long)amount).ToString("00");
            var color = amount < 0 ? loss : kind == 0 ? healthGain : currencyGain;
            delta.Label.color = delta.Icon.color = color;
            _deltaStartedAt[kind] = now;
            delta.Label.gameObject.SetActive(true);
            delta.Value.enabled = false;
            if (delta.ValueIcon != null) delta.ValueIcon.enabled = false;
            delta.Group.alpha = 0f;
        }

        public void Tick(double now, bool showHealth = true)
        {
            if (!HasRequiredReferences) return;
            healthTrail.gameObject.SetActive(showHealth);
            healthTrail.SetValueWithoutNotify(_healthAnimation.Trail(now));
            var color = healthFill.color;
            healthTrailFill.color = new Color(color.r * trailBrightness,
                color.g * trailBrightness, color.b * trailBrightness, color.a);
            health.transform.localScale = healthBaseScale * _healthAnimation.Scale(now);
            for (var kind = 0; kind < deltas.Length; kind++)
            {
                var elapsed = now - _deltaStartedAt[kind];
                if (elapsed >= 1d) { HideDelta(kind); continue; }
                var delta = deltas[kind];
                delta.Group.alpha = Mathf.Clamp01((float)Math.Min(
                    elapsed / fadeSeconds, (1d - elapsed) / fadeSeconds));
            }
        }

        public void HideDelta(int kind)
        {
            _deltaStartedAt[kind] = double.NegativeInfinity;
            if (deltas == null || kind >= deltas.Length || deltas[kind] == null) return;
            var delta = deltas[kind];
            if (delta.Label != null) delta.Label.gameObject.SetActive(false);
            if (delta.Value != null) delta.Value.enabled = true;
            if (delta.ValueIcon != null) delta.ValueIcon.enabled = true;
        }

        private void OnDisable()
        {
            for (var kind = 0; kind < (deltas?.Length ?? 0); kind++) HideDelta(kind);
            if (health != null) health.transform.localScale = healthBaseScale;
        }
    }
}
