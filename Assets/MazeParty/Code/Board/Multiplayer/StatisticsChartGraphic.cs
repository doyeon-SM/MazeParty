using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>Data-driven mesh on an authored UI Graphic; creates no UI objects.</summary>
    public sealed class StatisticsChartGraphic : MaskableGraphic, IPointerMoveHandler
    {
        [SerializeField] private Color gridColor = new Color(.3f, .4f, .55f, .25f);
        [SerializeField] private float lineWidth = 4f;
        [SerializeField, Range(0, .5f)] private float lowerBarLightening = .28f;
        [SerializeField] private float combatFirstRow = .94f;
        [SerializeField] private float combatPlayerSpacing = .24f;
        [SerializeField] private float combatMetricSpacing = .055f;
        [SerializeField] private float combatBarHeight = .034f;
        private readonly List<Vector2>[] _lines = { new List<Vector2>(), new List<Vector2>(), new List<Vector2>(), new List<Vector2>() };
        private MatchStatistics _data;
        private int _tab;
        private float _reveal = 1;
        public float Maximum { get; private set; } = 1;
        public float MovementMaximum { get; private set; } = 1;
        public event Action<int, int> HoverBar;
        public int Tab => _tab;

        public void Present(MatchStatistics data, int tab, bool earned)
        {
            _data = data; _tab = tab; Maximum = 1; MovementMaximum = 1;
            foreach (var line in _lines) line.Clear();
            if (data == null) { SetVerticesDirty(); return; }
            if (tab < 2)
            {
                var perTurn = new int[data.turns + 1];
                foreach (var point in data.balances) perTurn[point.turn]++;
                var indices = new int[data.turns + 1];
                foreach (var point in data.balances)
                {
                    indices[point.turn]++;
                    var value = tab == 0 ? point.keys : earned ? point.earned : point.gold;
                    var x = point.turn == 0 ? 0f : (point.turn - 1f + (float)indices[point.turn] / (perTurn[point.turn] + 1)) / data.turns;
                    var line = _lines[point.slot];
                    if (line.Count == 0 || !Mathf.Approximately(line[line.Count - 1].y, value)) line.Add(new Vector2(x, value));
                    Maximum = Mathf.Max(Maximum, value);
                }
                foreach (var line in _lines)
                    if (line.Count > 0) line.Add(new Vector2(1, line[line.Count - 1].y));
            }
            else if (tab == 2) { foreach (var value in data.placements) Maximum = Mathf.Max(Maximum, value); }
            else { foreach (var player in data.players) { Maximum = Mathf.Max(Maximum, player.dealt, player.received); MovementMaximum = Mathf.Max(MovementMaximum, player.moves); } }
            var step = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(Maximum)));
            Maximum = Mathf.Max(1, Mathf.Ceil(Maximum / step) * step);
            Maximum = Mathf.Ceil(Maximum / 4f) * 4f;
            var movementStep = Mathf.Max(1, Mathf.Pow(10, Mathf.Floor(Mathf.Log10(MovementMaximum / 4f))));
            MovementMaximum = Mathf.Ceil(MovementMaximum / (4 * movementStep)) * 4 * movementStep;
            SetVerticesDirty();
        }

        public void Reveal(float amount)
        {
            if (Mathf.Approximately(_reveal, amount)) return;
            _reveal = Mathf.Clamp01(amount); SetVerticesDirty();
        }

        public bool TryGetHeadPoint(int slot, out Vector2 point)
        {
            point = default;
            var line = _lines[slot];
            if (_data == null || _tab > 1 || line.Count == 0 || _reveal < line[0].x) return false;
            var value = line[0];
            for (var i = 1; i < line.Count; i++)
            {
                if (line[i].x >= _reveal)
                {
                    value = Vector2.Lerp(line[i - 1], line[i], Mathf.InverseLerp(line[i - 1].x, line[i].x, _reveal)); break;
                }
                value = line[i];
            }
            point = new Vector2(value.x, value.y / Maximum);
            return true;
        }

        public Vector2 ValuePosition(int index)
        {
            if (_tab == 2)
            {
                var rank = index / 4; var slot = index % 4;
                return new Vector2((rank + .18f + slot * .17f) / 4f, _data.placements[slot * 4 + rank] / Maximum * _reveal);
            }
            var group = index / 4; var player = index % 4;
            var amount = group == 0 ? _data.players[player].dealt : group == 1 ? _data.players[player].received : _data.players[player].moves;
            return new Vector2(amount / (group == 2 ? MovementMaximum : Maximum) * _reveal, CombatY(group, player));
        }

        public float CombatY(int group, int slot) => combatFirstRow - slot * combatPlayerSpacing - group * combatMetricSpacing;
        private Rect BarRect(int group, int slot)
        {
            var value = group == 0 ? _data.players[slot].dealt : group == 1 ? _data.players[slot].received : _data.players[slot].moves;
            var scale = group == 2 ? MovementMaximum : Maximum;
            return new Rect(0, CombatY(group, slot) - combatBarHeight * .5f, value / scale * _reveal, combatBarHeight);
        }

        public void OnPointerMove(PointerEventData e)
        {
            if (_data == null || _tab != 3 || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, e.position, e.pressEventCamera, out var p)) return;
            var rect = rectTransform.rect;
            p = new Vector2((p.x - rect.xMin) / rect.width, (p.y - rect.yMin) / rect.height);
            for (var group = 0; group < 3; group++)
                for (var slot = 0; slot < 4; slot++)
                    if (BarRect(group, slot).Contains(p)) { HoverBar?.Invoke(slot, group); return; }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_data == null) return;
            for (var i = 0; i <= 4; i++)
            {
                if (_tab == 3) Segment(vh, new Vector2(i / 4f, 0), new Vector2(i / 4f, 1), gridColor, 1);
                else Segment(vh, new Vector2(0, i / 4f), new Vector2(1, i / 4f), gridColor, 1);
            }
            if (_tab < 2)
            {
                for (var slot = 0; slot < 4; slot++)
                {
                    var line = _lines[slot];
                    for (var i = 1; i < line.Count && vh.currentVertCount < 60000; i++)
                    {
                        var a = line[i - 1]; var b = line[i];
                        if (a.x > _reveal) break;
                        if (b.x > _reveal) b = Vector2.Lerp(a, b, Mathf.InverseLerp(a.x, b.x, _reveal));
                        a.y /= Maximum; b.y /= Maximum;
                        Segment(vh, a, b, new Color(1, 1, 1, .55f), lineWidth + 2);
                        Segment(vh, a, b, _data.players[slot].color, lineWidth);
                    }
                }
            }
            else if (_tab == 2)
            {
                for (var rank = 0; rank < 4; rank++) for (var slot = 0; slot < 4; slot++)
                {
                    var x = (rank + .18f + slot * .17f) / 4f;
                    Box(vh, new Rect(x - .016f, 0, .032f, _data.placements[slot * 4 + rank] / Maximum * _reveal), _data.players[slot].color);
                }
            }
            else for (var group = 0; group < 3; group++) for (var slot = 0; slot < 4; slot++)
                Box(vh, BarRect(group, slot), Color.Lerp(_data.players[slot].color, Color.white, group * lowerBarLightening));
        }

        private Vector2 Pixel(Vector2 p) => rectTransform.rect.min + Vector2.Scale(p, rectTransform.rect.size);
        private void Segment(VertexHelper vh, Vector2 a, Vector2 b, Color tint, float width)
        {
            a = Pixel(a); b = Pixel(b);
            if ((b - a).sqrMagnitude < .001f) return;
            var normal = new Vector2(a.y - b.y, b.x - a.x).normalized * width * .5f;
            Quad(vh, a - normal, a + normal, b + normal, b - normal, tint);
        }
        private void Box(VertexHelper vh, Rect r, Color tint)
        {
            if (r.width <= 0 || r.height <= 0) return;
            Quad(vh, Pixel(new Vector2(r.xMin, r.yMin)), Pixel(new Vector2(r.xMin, r.yMax)),
                Pixel(new Vector2(r.xMax, r.yMax)), Pixel(new Vector2(r.xMax, r.yMin)), tint);
        }
        private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
        {
            var first = vh.currentVertCount;
            vh.AddVert(a, tint, Vector2.zero); vh.AddVert(b, tint, Vector2.zero);
            vh.AddVert(c, tint, Vector2.zero); vh.AddVert(d, tint, Vector2.zero);
            vh.AddTriangle(first, first + 1, first + 2); vh.AddTriangle(first, first + 2, first + 3);
        }
    }
}
