using System;
using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Applies the current language font to authored Text and TextMesh labels,
    /// and regenerates UI glyphs when their canvas pixel density changes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocalizedFontScope : MonoBehaviour
    {
        private Text[] _uiTexts = Array.Empty<Text>();
        private float[] _textScales = Array.Empty<float>();

        private void OnEnable()
        {
            GameText.LanguageChanged += Apply;
            Apply();
        }

        private void OnDisable()
        {
            GameText.LanguageChanged -= Apply;
        }

        private void LateUpdate()
        {
            // With a fixed aspect ratio, CanvasScaler can change scaleFactor
            // without changing a label's RectTransform dimensions. Legacy Text
            // then keeps its old glyph mesh and stretches it at the new scale.
            // Observe the actual canvas density, after the asynchronous screen
            // resize, instead of the requested resolution in GameSettings.
            for (var index = 0; index < _uiTexts.Length; index++)
            {
                var text = _uiTexts[index];
                if (text == null || !text.isActiveAndEnabled)
                {
                    continue;
                }

                var scale = text.pixelsPerUnit;
                if (Mathf.Approximately(_textScales[index], scale))
                {
                    continue;
                }

                _textScales[index] = scale;
                // Rebuild layout as well: font hinting and best-fit rounding
                // may change at the new pixel density.
                text.SetAllDirty();
            }
        }

        private void OnTransformChildrenChanged()
        {
            if (isActiveAndEnabled)
            {
                Apply();
            }
        }

        public void Apply()
        {
            var font = GameFonts.Current;
            if (font == null)
            {
                return;
            }

            _uiTexts = GetComponentsInChildren<Text>(true);
            _textScales = new float[_uiTexts.Length];
            for (var index = 0; index < _uiTexts.Length; index++)
            {
                var text = _uiTexts[index];
                if (text.font != font)
                {
                    text.font = font;
                }

                _textScales[index] = text.pixelsPerUnit;
                text.SetAllDirty();
            }

            foreach (var textMesh in GetComponentsInChildren<TextMesh>(true))
            {
                GameFonts.Apply(textMesh);
            }
        }
    }
}
