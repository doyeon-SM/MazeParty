using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>Prefab-authored outer ring; only its live color/width change.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BoardMinimapAmplifierOutline : MaskableGraphic
    {
        [SerializeField, Min(0f)] private float baseThickness = 4f;
        [SerializeField, Min(0f)] private float thicknessPerAdditionalMultiplier = 2f;
        [SerializeField, Min(0f)] private float circleGap = 1f;
        [SerializeField, Min(.1f)] private float pulsePeriod = 1.6f;
        [SerializeField, Range(0f, 1f)] private float pulseMinimumAlpha = .45f;
        [SerializeField, Range(0f, 1f)] private float pulseMinimumBrightness = .75f;
        [SerializeField, Min(1f)] private float pulseMaximumWidth = 1.2f;
        private Color _baseColor;
        private bool _cached, _visible;
        private double _startedAt;
        private float _thickness;

        public void Present(int multiplier, double now)
        {
            if (multiplier <= 1)
            {
                enabled = false;
                return;
            }
            if (!_cached) { _baseColor = color; _cached = true; }
            if (!_visible) { _startedAt = now; _visible = true; }
            var elapsed = System.Math.Max(0d, now - _startedAt);
            var phase = (float)(elapsed / Mathf.Max(.1f, pulsePeriod) % 1d);
            var pulse = .5f + .5f * Mathf.Cos(phase * Mathf.PI * 2f);
            var brightness = Mathf.Lerp(pulseMinimumBrightness, 1f, pulse);
            var tint = _baseColor;
            tint.r *= brightness; tint.g *= brightness; tint.b *= brightness;
            tint.a *= Mathf.Lerp(pulseMinimumAlpha, 1f, pulse);
            color = tint;
            _thickness = (baseThickness + (multiplier - 2) * thicknessPerAdditionalMultiplier) *
                Mathf.Lerp(1f, pulseMaximumWidth, pulse);
            enabled = true;
            SetVerticesDirty();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            _visible = false;
            if (_cached) color = _baseColor;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (!_visible) return;
            var rect = GetPixelAdjustedRect();
            var inner = Mathf.Min(rect.width, rect.height) * .5f + circleGap;
            var outer = inner + _thickness;
            const int segments = 128;
            for (var i = 0; i <= segments; i++)
            {
                var angle = i * Mathf.PI * 2f / segments;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                mesh.AddVert(rect.center + direction * inner, color, Vector2.zero);
                mesh.AddVert(rect.center + direction * outer, color, Vector2.zero);
                if (i == 0) continue;
                var current = i * 2;
                mesh.AddTriangle(current - 2, current - 1, current);
                mesh.AddTriangle(current, current - 1, current + 1);
            }
        }
    }
}
