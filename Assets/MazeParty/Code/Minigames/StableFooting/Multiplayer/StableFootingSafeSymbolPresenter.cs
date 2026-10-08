using System;
using MazeParty.Gameplay.Minigames.StableFooting;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Reuses the three authored SafeSymbolDisplay positions as left, center
    /// and right slots. Runtime presentation changes only position, visibility
    /// and semantic color; the prefab remains the source of layout and art.
    /// </summary>
    public sealed class StableFootingSafeSymbolPresenter
    {
        private static readonly Color CorrectColor = Color.green;
        private static readonly int[,] ShuffleLayouts =
        {
            { 0, 1, 2 },
            { 1, 2, 0 },
            { 2, 0, 1 }
        };

        private readonly SpriteRenderer[] _renderers;
        private readonly Transform[] _roots;
        private readonly Vector3[] _authoredPositions;
        private readonly Vector3[] _slots;
        private readonly Color[] _authoredColors;
        private readonly bool[] _authoredActiveStates;

        public StableFootingSafeSymbolPresenter(
            Renderer cross,
            Renderer circle,
            Renderer square)
        {
            _renderers = new[]
            {
                RequireSpriteRenderer(cross, nameof(cross)),
                RequireSpriteRenderer(circle, nameof(circle)),
                RequireSpriteRenderer(square, nameof(square))
            };
            _roots = new Transform[_renderers.Length];
            _authoredPositions = new Vector3[_renderers.Length];
            _slots = new Vector3[_renderers.Length];
            _authoredColors = new Color[_renderers.Length];
            _authoredActiveStates = new bool[_renderers.Length];

            for (var index = 0; index < _renderers.Length; index++)
            {
                var renderer = _renderers[index];
                var root = ResolveSymbolRoot(renderer.transform);
                _roots[index] = root;
                _authoredPositions[index] = root.localPosition;
                _slots[index] = root.localPosition;
                _authoredColors[index] = renderer.color;
                _authoredActiveStates[index] = root.gameObject.activeSelf;
            }

            Array.Sort(
                _slots,
                (left, right) => left.x.CompareTo(right.x));
        }

        public void ApplyShuffle(
            int cycleNumber,
            double shuffleElapsedSeconds)
        {
            ApplyShuffleLayout(
                cycleNumber,
                GetShuffleStep(shuffleElapsedSeconds));
            for (var symbolIndex = 0;
                 symbolIndex < _renderers.Length;
                 symbolIndex++)
            {
                _renderers[symbolIndex].color =
                    _authoredColors[symbolIndex];
                _roots[symbolIndex].gameObject.SetActive(true);
            }
        }

        public void ApplyReveal(
            StableFootingSymbol safeSymbol,
            int cycleNumber)
        {
            var safeIndex = ToIndex(safeSymbol);
            ApplyShuffleLayout(
                cycleNumber,
                StableFootingRules.SymbolShuffleStepCount - 1);
            for (var symbolIndex = 0;
                 symbolIndex < _renderers.Length;
                 symbolIndex++)
            {
                _renderers[symbolIndex].color = symbolIndex == safeIndex
                    ? CorrectColor
                    : _authoredColors[symbolIndex];
                _roots[symbolIndex].gameObject.SetActive(
                    symbolIndex == safeIndex);
            }
        }

        public void Hide()
        {
            for (var index = 0; index < _roots.Length; index++)
            {
                _roots[index].gameObject.SetActive(false);
            }
        }

        public void RestoreAuthoredState()
        {
            for (var index = 0; index < _renderers.Length; index++)
            {
                _roots[index].localPosition = _authoredPositions[index];
                _renderers[index].color = _authoredColors[index];
                _roots[index].gameObject.SetActive(
                    _authoredActiveStates[index]);
            }
        }

        private static int ToIndex(StableFootingSymbol symbol)
        {
            var index = (int)symbol;
            if (index < 0 || index > 2)
            {
                throw new ArgumentOutOfRangeException(nameof(symbol));
            }

            return index;
        }

        private void ApplyShuffleLayout(
            int cycleNumber,
            int shuffleStep)
        {
            if (cycleNumber < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cycleNumber),
                    cycleNumber,
                    "Cycle number must be positive.");
            }

            var layoutCount = ShuffleLayouts.GetLength(0);
            var layoutIndex =
                ((cycleNumber - 1) + shuffleStep) % layoutCount;
            for (var symbolIndex = 0;
                 symbolIndex < _roots.Length;
                 symbolIndex++)
            {
                var slotIndex = ShuffleLayouts[
                    layoutIndex,
                    symbolIndex];
                _roots[symbolIndex].localPosition = _slots[slotIndex];
            }
        }

        private static int GetShuffleStep(double shuffleElapsedSeconds)
        {
            if (double.IsNaN(shuffleElapsedSeconds) ||
                double.IsInfinity(shuffleElapsedSeconds) ||
                shuffleElapsedSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(shuffleElapsedSeconds),
                    shuffleElapsedSeconds,
                    "Shuffle time must be a finite non-negative value.");
            }

            if (shuffleElapsedSeconds >=
                StableFootingRules.ShuffleRevealSeconds)
            {
                return StableFootingRules.SymbolShuffleStepCount - 1;
            }

            return (int)Math.Floor(
                shuffleElapsedSeconds /
                StableFootingRules.SymbolShuffleIntervalSeconds);
        }

        private static SpriteRenderer RequireSpriteRenderer(
            Renderer renderer,
            string parameterName)
        {
            if (renderer is SpriteRenderer spriteRenderer)
            {
                return spriteRenderer;
            }

            throw new ArgumentException(
                "Safe symbols must use their authored SpriteRenderer.",
                parameterName);
        }

        private static Transform ResolveSymbolRoot(Transform rendererRoot)
        {
            var parent = rendererRoot.parent;
            return parent != null &&
                   parent.name.EndsWith(
                       "Mark",
                       StringComparison.Ordinal)
                ? parent
                : rendererRoot;
        }
    }
}
