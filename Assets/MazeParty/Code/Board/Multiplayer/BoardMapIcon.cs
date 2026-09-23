using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    public enum BoardMapIconKind { Room, Start, Respawn, Key, GoldGain, GoldLoss, Item, Healing, Arrow }

    /// <summary>Small vector icons authored on the board UI prefab, including stencil clipping.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BoardMapIcon : MaskableGraphic
    {
        [SerializeField] private BoardMapIconKind kind;
        public void SetIcon(BoardMapIconKind value)
        {
            if (kind == value) return;
            kind = value;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            switch (kind)
            {
                case BoardMapIconKind.Room:
                    Box(mesh, -.65f, -.65f, 1.3f, .18f); Box(mesh, -.65f, .47f, 1.3f, .18f);
                    Box(mesh, -.65f, -.65f, .18f, 1.3f); Box(mesh, .47f, -.65f, .18f, 1.3f); break;
                case BoardMapIconKind.Start:
                    Box(mesh, -.6f, -.85f, .2f, 1.7f);
                    Triangle(mesh, new Vector2(-.4f, .8f), new Vector2(.8f, .4f), new Vector2(-.4f, 0f)); break;
                case BoardMapIconKind.Respawn:
                    Ring(mesh, Vector2.zero, .63f, .19f, 35f, 320f);
                    Triangle(mesh, new Vector2(.25f, .7f), new Vector2(.9f, .72f), new Vector2(.65f, .13f)); break;
                case BoardMapIconKind.Key:
                    Ring(mesh, new Vector2(-.35f, .3f), .4f, .18f, 0f, 360f);
                    Line(mesh, new Vector2(-.07f, .02f), new Vector2(.65f, -.7f), .22f);
                    Line(mesh, new Vector2(.38f, -.43f), new Vector2(.66f, -.17f), .2f); break;
                case BoardMapIconKind.GoldGain:
                case BoardMapIconKind.GoldLoss:
                    Ring(mesh, Vector2.zero, .78f, .17f, 0f, 360f);
                    Box(mesh, -.42f, -.09f, .84f, .18f);
                    if (kind == BoardMapIconKind.GoldGain) Box(mesh, -.09f, -.42f, .18f, .84f);
                    break;
                case BoardMapIconKind.Item:
                    Box(mesh, -.7f, -.65f, .55f, 1.05f); Box(mesh, .15f, -.65f, .55f, 1.05f);
                    Box(mesh, -.8f, .47f, 1.6f, .22f); Box(mesh, -.1f, -.7f, .2f, 1.65f); break;
                case BoardMapIconKind.Healing:
                    Box(mesh, -.23f, -.85f, .46f, 1.7f); Box(mesh, -.85f, -.23f, 1.7f, .46f); break;
                case BoardMapIconKind.Arrow:
                    Box(mesh, -.23f, -.8f, .46f, 1f);
                    Triangle(mesh, new Vector2(-.75f, .1f), new Vector2(.75f, .1f), new Vector2(0f, .95f)); break;
            }
        }

        private Vector2 Point(Vector2 p)
        {
            var rect = GetPixelAdjustedRect();
            return rect.center + p * (Mathf.Min(rect.width, rect.height) * .5f);
        }
        private void Triangle(VertexHelper m, Vector2 a, Vector2 b, Vector2 c)
        {
            var i = m.currentVertCount;
            m.AddVert(Point(a), color, Vector2.zero); m.AddVert(Point(b), color, Vector2.zero);
            m.AddVert(Point(c), color, Vector2.zero); m.AddTriangle(i, i + 1, i + 2);
        }
        private void Box(VertexHelper m, float x, float y, float w, float h)
        {
            Quad(m, new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h));
        }
        private void Quad(VertexHelper m, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            var i = m.currentVertCount;
            m.AddVert(Point(a), color, Vector2.zero); m.AddVert(Point(b), color, Vector2.zero);
            m.AddVert(Point(c), color, Vector2.zero); m.AddVert(Point(d), color, Vector2.zero);
            m.AddTriangle(i, i + 1, i + 2); m.AddTriangle(i, i + 2, i + 3);
        }
        private void Line(VertexHelper m, Vector2 a, Vector2 b, float width)
        {
            var v = (b - a).normalized; var n = new Vector2(-v.y, v.x) * width * .5f;
            Quad(m, a - n, b - n, b + n, a + n);
        }
        private void Ring(VertexHelper m, Vector2 center, float radius, float width, float start, float end)
        {
            for (var i = 0; i < 32; i++)
            {
                var a = Mathf.Lerp(start, end, i / 32f) * Mathf.Deg2Rad;
                var b = Mathf.Lerp(start, end, (i + 1) / 32f) * Mathf.Deg2Rad;
                var u = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); var v = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                Quad(m, center + u * radius, center + v * radius, center + v * (radius - width), center + u * (radius - width));
            }
        }
    }
}
