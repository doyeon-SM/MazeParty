using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>Prefab-authored circular stencil surface for the local map.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BoardMapCircleGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            var radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            const int segments = 128;
            mesh.AddVert(rect.center, color, Vector2.one * 0.5f);
            for (var i = 0; i <= segments; i++)
            {
                var angle = i * Mathf.PI * 2f / segments;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                mesh.AddVert(rect.center + direction * radius, color, direction * 0.5f + Vector2.one * 0.5f);
                if (i > 0) mesh.AddTriangle(0, i, i + 1);
            }
        }
    }
}
