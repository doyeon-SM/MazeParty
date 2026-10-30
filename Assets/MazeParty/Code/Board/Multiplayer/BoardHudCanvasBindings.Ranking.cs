using System;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    public sealed partial class BoardHudCanvasBindings
    {
        [SerializeField, Min(0f)] private float rankSwapDuration = 0.65f;
        [SerializeField, Min(0f)] private float rankSwapArc = 38f;
        [SerializeField, Min(0f)] private float rankSwapStagger = 0.12f;

        private readonly int[] _rankOrder = { 0, 1, 2, 3 };
        private readonly int[] _nextRankOrder = new int[4];
        private readonly Vector2[] _rankFrom = new Vector2[4];
        private readonly Vector2[] _rankTo = new Vector2[4];
        private readonly float[] _rankDelay = new float[4];
        private bool _rankingInitialized;
        private bool _rankingLayoutDirty;
        private double _rankSwapStarted;

        public static string FormatRank(int rank) => rank switch
        {
            1 => "1st", 2 => "2nd", 3 => "3rd", 4 => "4th", _ => "--"
        };

        public void PresentPlayerRanking(int[] ranks, double now)
        {
            if (ranks == null || ranks.Length != _rankOrder.Length ||
                references.PlayerRankingLayout == null ||
                !HasArray(references.PlayerCards, _rankOrder.Length)) return;

            // Start from the current order so ties never shuffle identical ranks.
            Array.Copy(_rankOrder, _nextRankOrder, _rankOrder.Length);
            for (var index = 1; index < _nextRankOrder.Length; index++)
            {
                var slot = _nextRankOrder[index];
                var previous = index - 1;
                while (previous >= 0 && ranks[_nextRankOrder[previous]] > ranks[slot])
                {
                    _nextRankOrder[previous + 1] = _nextRankOrder[previous];
                    previous--;
                }
                _nextRankOrder[previous + 1] = slot;
            }

            TickPlayerRanking(now);
            var changed = !_rankingInitialized || _rankingLayoutDirty;
            for (var index = 0; index < _rankOrder.Length; index++)
                changed |= _rankOrder[index] != _nextRankOrder[index];
            if (!changed) return;

            for (var slot = 0; slot < _rankOrder.Length; slot++)
                _rankFrom[slot] = references.PlayerCards[slot].rectTransform.anchoredPosition;
            for (var row = 0; row < _rankOrder.Length; row++)
            {
                var slot = _rankOrder[row];
                _rankDelay[slot] = Mathf.Min(row, Array.IndexOf(_nextRankOrder, slot)) * rankSwapStagger;
            }
            Array.Copy(_nextRankOrder, _rankOrder, _rankOrder.Length);
            for (var index = 0; index < _rankOrder.Length; index++)
                references.PlayerCards[_rankOrder[index]].transform.SetSiblingIndex(index);

            // Measure the prefab's layout at its actual size, then animate the
            // bound cards between those positions without a competing layout pass.
            var layout = references.PlayerRankingLayout;
            layout.enabled = true;
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)layout.transform);
            for (var slot = 0; slot < _rankOrder.Length; slot++)
                _rankTo[slot] = references.PlayerCards[slot].rectTransform.anchoredPosition;
            layout.enabled = false;
            _rankSwapStarted = now;
            if (!_rankingInitialized)
                Array.Copy(_rankTo, _rankFrom, _rankTo.Length);
            _rankingInitialized = true;
            _rankingLayoutDirty = false;
            TickPlayerRanking(now);
        }

        public void TickPlayerRanking(double now)
        {
            if (!_rankingInitialized) return;
            for (var slot = 0; slot < _rankOrder.Length; slot++)
            {
                var progress = rankSwapDuration <= 0f ? 1f :
                    Mathf.Clamp01((float)((now - _rankSwapStarted - _rankDelay[slot]) / rankSwapDuration));
                var eased = progress * progress * (3f - 2f * progress);
                var card = references.PlayerCards[slot];
                if (card == null) continue;
                var position = Vector2.LerpUnclamped(_rankFrom[slot], _rankTo[slot], eased);
                var verticalTravel = _rankTo[slot].y - _rankFrom[slot].y;
                if (Mathf.Abs(verticalTravel) > 0.01f && progress < 1f)
                {
                    // The HUD is beside the left screen edge. Keep descending
                    // cards inside its authored inset while rising cards arc right.
                    var arc = verticalTravel > 0f ? rankSwapArc :
                        -Mathf.Min(rankSwapArc, references.PlayerRankingLayout.padding.left);
                    position.x += Mathf.Sin(progress * Mathf.PI) * arc;
                }
                card.rectTransform.anchoredPosition = position;
            }
        }

        private void LateUpdate()
        {
            TickPlayerRanking(Time.unscaledTimeAsDouble);
        }

        private void OnRectTransformDimensionsChange()
        {
            _rankingLayoutDirty = true;
        }

        private void OnDisable()
        {
            ResetGhostPassPresentation();
            _rankingInitialized = false;
            if (references?.PlayerRankingLayout != null)
                references.PlayerRankingLayout.enabled = true;
        }
    }
}
