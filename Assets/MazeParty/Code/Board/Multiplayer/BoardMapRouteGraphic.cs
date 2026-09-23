using System.Collections.Generic;
using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>Top-down dots for the same directed tile-center route as the world hemispheres.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BoardMapRouteGraphic : MaskableGraphic
    {
        [SerializeField, Min(.1f)] private float worldDotSpacing = .8f;
        [SerializeField, Min(.01f)] private float worldDotDiameter = .32f;
        [SerializeField, Min(1f)] private float minimumPixelDiameter = 4f;
        private readonly List<Vector3> _points = new List<Vector3>();
        private Bounds _worldBounds;
        public int PointCount => _points.Count;

        public void Present(IReadOnlyList<BoardTile> route, Bounds worldBounds)
        {
            _worldBounds = worldBounds;
            BoardShopRouteView.SampleRoute(route, worldDotSpacing, 0f, _points);
            SetVerticesDirty();
        }

        public Vector2 ProjectPoint(int index)
        {
            var point = _points[index] - _worldBounds.center;
            var rect = rectTransform.rect;
            return rect.center + new Vector2(point.x * rect.width / _worldBounds.size.x,
                point.z * rect.height / _worldBounds.size.z);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_worldBounds.size.x <= 0f || _worldBounds.size.z <= 0f) return;
            var radius = Mathf.Max(minimumPixelDiameter, worldDotDiameter * rectTransform.rect.width / _worldBounds.size.x) * .5f;
            for (var index = 0; index < _points.Count; index++)
            {
                var center = ProjectPoint(index);
                var start = vh.currentVertCount;
                vh.AddVert(center, color, Vector2.zero);
                const int sides = 10;
                for (var side = 0; side < sides; side++)
                {
                    var angle = side * Mathf.PI * 2f / sides;
                    vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector2.zero);
                }
                for (var side = 0; side < sides; side++)
                    vh.AddTriangle(start, start + 1 + side, start + 1 + (side + 1) % sides);
            }
        }
    }
}
