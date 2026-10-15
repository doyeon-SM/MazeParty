using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>Hollow self-location ring with a forward-pointing triangle.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BoardMapPlayerDirectionGraphic : MaskableGraphic
    {
        [SerializeField, Range(1f, 8f)] private float ringWidth = 2.5f;
        [SerializeField, Range(2f, 12f)] private float pointerLength = 7f;
        [SerializeField, Min(0.1f)] private float pointerScale = 1.5f;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            var radius = Mathf.Min(rect.width, rect.height) * .5f - pointerLength;
            if (radius <= ringWidth) return;
            const int sides = 48;
            for (var i = 0; i < sides; i++)
            {
                var a = i * Mathf.PI * 2f / sides;
                var b = (i + 1) * Mathf.PI * 2f / sides;
                var outerA = rect.center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                var outerB = rect.center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius;
                var innerA = rect.center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (radius - ringWidth);
                var innerB = rect.center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * (radius - ringWidth);
                var start = mesh.currentVertCount;
                mesh.AddVert(outerA, color, Vector2.zero);
                mesh.AddVert(outerB, color, Vector2.zero);
                mesh.AddVert(innerB, color, Vector2.zero);
                mesh.AddVert(innerA, color, Vector2.zero);
                mesh.AddTriangle(start, start + 1, start + 2);
                mesh.AddTriangle(start, start + 2, start + 3);
            }
            var baseY = radius - 1f;
            var tip = rect.center + Vector2.up * (baseY + (pointerLength + 1f) * pointerScale);
            var halfWidth = pointerLength * .65f * pointerScale;
            var left = rect.center + new Vector2(-halfWidth, baseY);
            var right = rect.center + new Vector2(halfWidth, baseY);
            var triangle = mesh.currentVertCount;
            mesh.AddVert(tip, color, Vector2.zero);
            mesh.AddVert(left, color, Vector2.zero);
            mesh.AddVert(right, color, Vector2.zero);
            mesh.AddTriangle(triangle, triangle + 1, triangle + 2);
        }
    }
}
