using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BoardMapMineGraphic : MaskableGraphic
    {
        [SerializeField, Min(2f)] private float iconRadius = 6f;
        [SerializeField] private Sprite iconSprite;
        private readonly List<Vector3> _positions = new List<Vector3>();
        private Bounds _bounds;
        private Quaternion _iconRotation = Quaternion.identity;
        public int MarkerCount => _positions.Count;
        public Sprite IconSprite => iconSprite;
        public override Texture mainTexture =>
            iconSprite != null ? iconSprite.texture : base.mainTexture;

        public void SetIcon(Sprite sprite)
        {
            if (iconSprite == sprite) return;
            iconSprite = sprite;
            SetVerticesDirty();
            SetMaterialDirty();
        }

        public void SetIconRotation(Quaternion rotation)
        {
            if (Quaternion.Angle(_iconRotation, rotation) < 0.001f) return;
            _iconRotation = rotation;
            SetVerticesDirty();
        }

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
                if (iconSprite != null)
                {
                    AddSprite(vh, center);
                    continue;
                }

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

        private void AddSprite(VertexHelper vh, Vector2 center)
        {
            var uv = DataUtility.GetOuterUV(iconSprite);
            var right = (Vector2)(_iconRotation * Vector3.right) * iconRadius;
            var up = (Vector2)(_iconRotation * Vector3.up) * iconRadius;
            var start = vh.currentVertCount;
            vh.AddVert(center - right - up, color,
                new Vector2(uv.x, uv.y));
            vh.AddVert(center - right + up, color,
                new Vector2(uv.x, uv.w));
            vh.AddVert(center + right + up, color,
                new Vector2(uv.z, uv.w));
            vh.AddVert(center + right - up, color,
                new Vector2(uv.z, uv.y));
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
