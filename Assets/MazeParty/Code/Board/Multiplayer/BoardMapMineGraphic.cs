using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BoardMapMineGraphic : MaskableGraphic
    {
        [SerializeField, Min(2f)] private float iconRadius = 6f;
        private readonly List<Vector3> _positions = new List<Vector3>();
        private Bounds _bounds;
        public int MarkerCount => _positions.Count;
        public void Present(IReadOnlyList<Vector3> positions, Bounds bounds, float visibleRadius)
        {
            _bounds = bounds;
            _positions.Clear();
            if (positions != null) foreach (var p in positions)
            {
                var delta = p - bounds.center;
                if (visibleRadius <= 0 || new Vector2(delta.x, delta.z).magnitude <= visibleRadius) _positions.Add(p);
            }
            SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_bounds.size.x <= 0 || _bounds.size.z <= 0) return;
            foreach (var p in _positions)
            {
                var delta = p - _bounds.center;
                var center = rectTransform.rect.center + new Vector2(delta.x * rectTransform.rect.width / _bounds.size.x,
                    delta.z * rectTransform.rect.height / _bounds.size.z);
                int start = vh.currentVertCount;
                // Diamond with a contrasting center remains legible while the map rotates.
                vh.AddVert(center + Vector2.up * iconRadius, color, Vector2.zero);
                vh.AddVert(center + Vector2.right * iconRadius, color, Vector2.zero);
                vh.AddVert(center - Vector2.up * iconRadius, color, Vector2.zero);
                vh.AddVert(center - Vector2.right * iconRadius, color, Vector2.zero);
                vh.AddVert(center, Color.black, Vector2.zero);
                for (int i = 0; i < 4; i++) vh.AddTriangle(start + i, start + (i + 1) % 4, start + 4);
            }
        }
    }
}
