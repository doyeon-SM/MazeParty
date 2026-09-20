using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Prefab-authored annular countdown graphic. The visible arc retreats
    /// counterclockwise from twelve o'clock as time elapses.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    [DisallowMultipleComponent]
    public sealed class MinigameTimerRingGraphic : MaskableGraphic
    {
        [SerializeField, Range(0f, 1f)] private float fillAmount = 1f;
        [SerializeField, Min(1f)] private float thickness = 8f;
        [SerializeField, Range(12, 256)] private int segments = 96;

        public float FillAmount => fillAmount;
        public float Thickness => thickness;

        public void SetFillAmount(float value)
        {
            var clamped = Mathf.Clamp01(value);
            if (Mathf.Approximately(fillAmount, clamped))
            {
                return;
            }

            fillAmount = clamped;
            SetVerticesDirty();
        }

        public void Configure(float lineThickness, int segmentCount)
        {
            thickness = Mathf.Max(1f, lineThickness);
            segments = Mathf.Clamp(segmentCount, 12, 256);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (fillAmount <= 0.0001f)
            {
                return;
            }

            var rect = rectTransform.rect;
            var outerRadius = Mathf.Max(
                0f,
                Mathf.Min(rect.width, rect.height) * 0.5f);
            var innerRadius = Mathf.Max(
                0f,
                outerRadius - Mathf.Min(thickness, outerRadius));
            if (outerRadius <= 0.0001f ||
                innerRadius >= outerRadius)
            {
                return;
            }

            var elapsed = 1f - fillAmount;
            var startAngle =
                Mathf.PI * 0.5f + elapsed * Mathf.PI * 2f;
            var span = fillAmount * Mathf.PI * 2f;
            var sliceCount = Mathf.Max(
                1,
                Mathf.CeilToInt(segments * fillAmount));
            var vertexColor = (Color32)color;

            for (var slice = 0; slice <= sliceCount; slice++)
            {
                var t = slice / (float)sliceCount;
                var angle = startAngle + span * t;
                var direction =
                    new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vertexHelper.AddVert(
                    direction * outerRadius,
                    vertexColor,
                    Vector2.zero);
                vertexHelper.AddVert(
                    direction * innerRadius,
                    vertexColor,
                    Vector2.zero);

                if (slice == 0)
                {
                    continue;
                }

                var first = (slice - 1) * 2;
                var next = slice * 2;
                vertexHelper.AddTriangle(
                    first,
                    next,
                    first + 1);
                vertexHelper.AddTriangle(
                    next,
                    next + 1,
                    first + 1);
            }
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            fillAmount = Mathf.Clamp01(fillAmount);
            thickness = Mathf.Max(1f, thickness);
            segments = Mathf.Clamp(segments, 12, 256);
            SetVerticesDirty();
        }
#endif
    }
}
