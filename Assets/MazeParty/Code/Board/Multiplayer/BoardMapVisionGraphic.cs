using System;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Prefab-authored minimap overlay. The camera cone remains clear and bright,
    /// while space outside the cone and space hidden behind world geometry is
    /// darkened without covering player markers rendered above this graphic.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BoardMapVisionGraphic : MaskableGraphic
    {
        private const int CircleSegments = 96;

        [SerializeField] private Color visibleColor =
            new Color(1f, 1f, 1f, 0.08f);
        [SerializeField] private Color edgeColor =
            new Color(0.72f, 0.94f, 1f, 0.72f);
        [SerializeField, Min(0.25f)] private float edgeWidth = 1.5f;

        private float _halfAngleDegrees;
        private float[] _clearFractions = Array.Empty<float>();
        private bool _hasCameraView;

        public bool HasCameraView => _hasCameraView;
        public int SampleCount => _clearFractions.Length;

        public void PresentUnavailable()
        {
            if (!_hasCameraView && _clearFractions.Length == 0)
                return;
            _hasCameraView = false;
            _halfAngleDegrees = 0f;
            _clearFractions = Array.Empty<float>();
            SetVerticesDirty();
        }

        public void Present(float halfAngleDegrees, float[] clearFractions)
        {
            if (clearFractions == null || clearFractions.Length < 2)
            {
                PresentUnavailable();
                return;
            }

            _hasCameraView = true;
            _halfAngleDegrees = Mathf.Clamp(halfAngleDegrees, 1f, 179f);
            if (_clearFractions.Length != clearFractions.Length)
                _clearFractions = new float[clearFractions.Length];
            for (var index = 0; index < clearFractions.Length; index++)
                _clearFractions[index] = Mathf.Clamp01(clearFractions[index]);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            var radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            if (radius <= 0f)
                return;

            if (!_hasCameraView || _clearFractions.Length < 2)
            {
                AddFan(mesh, rect.center, radius, 0f, 360f, color,
                    CircleSegments);
                return;
            }

            var outsideArc = 360f - _halfAngleDegrees * 2f;
            AddFan(
                mesh,
                rect.center,
                radius,
                _halfAngleDegrees,
                360f - _halfAngleDegrees,
                color,
                Mathf.Max(3,
                    Mathf.CeilToInt(CircleSegments * outsideArc / 360f)));

            var last = _clearFractions.Length - 1;
            for (var index = 0; index < last; index++)
            {
                var angleA = Mathf.Lerp(-_halfAngleDegrees,
                    _halfAngleDegrees, index / (float)last);
                var angleB = Mathf.Lerp(-_halfAngleDegrees,
                    _halfAngleDegrees, (index + 1) / (float)last);
                var directionA = Direction(angleA);
                var directionB = Direction(angleB);
                var clearA = radius * _clearFractions[index];
                var clearB = radius * _clearFractions[index + 1];

                AddTriangle(mesh, rect.center,
                    rect.center + directionA * clearA,
                    rect.center + directionB * clearB, visibleColor);
                if (clearA < radius || clearB < radius)
                {
                    AddQuad(mesh,
                        rect.center + directionA * clearA,
                        rect.center + directionA * radius,
                        rect.center + directionB * radius,
                        rect.center + directionB * clearB, color);
                }
            }

            AddLine(mesh, rect.center,
                rect.center + Direction(-_halfAngleDegrees) * radius,
                edgeWidth, edgeColor);
            AddLine(mesh, rect.center,
                rect.center + Direction(_halfAngleDegrees) * radius,
                edgeWidth, edgeColor);
        }

        private static void AddFan(VertexHelper mesh, Vector2 center,
            float radius, float startDegrees, float endDegrees,
            Color32 tint, int segments)
        {
            for (var index = 0; index < segments; index++)
            {
                var angleA = Mathf.Lerp(startDegrees, endDegrees,
                    index / (float)segments);
                var angleB = Mathf.Lerp(startDegrees, endDegrees,
                    (index + 1) / (float)segments);
                AddTriangle(mesh, center,
                    center + Direction(angleA) * radius,
                    center + Direction(angleB) * radius, tint);
            }
        }

        private static Vector2 Direction(float degrees)
        {
            var radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }

        private static void AddTriangle(VertexHelper mesh, Vector2 a,
            Vector2 b, Vector2 c, Color32 tint)
        {
            var start = mesh.currentVertCount;
            mesh.AddVert(a, tint, Vector2.zero);
            mesh.AddVert(b, tint, Vector2.zero);
            mesh.AddVert(c, tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
        }

        private static void AddQuad(VertexHelper mesh, Vector2 a,
            Vector2 b, Vector2 c, Vector2 d, Color32 tint)
        {
            var start = mesh.currentVertCount;
            mesh.AddVert(a, tint, Vector2.zero);
            mesh.AddVert(b, tint, Vector2.zero);
            mesh.AddVert(c, tint, Vector2.zero);
            mesh.AddVert(d, tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }

        private static void AddLine(VertexHelper mesh, Vector2 start,
            Vector2 end, float width, Color32 tint)
        {
            var direction = (end - start).normalized;
            var normal = new Vector2(-direction.y, direction.x) * width * 0.5f;
            AddQuad(mesh, start - normal, end - normal, end + normal,
                start + normal, tint);
        }
    }
}
