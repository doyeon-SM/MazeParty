using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Applies the current language font to every authored Text and TextMesh
    /// below a player-facing prefab root. The hierarchy is authored in the
    /// prefab; this component only updates its presentation when language
    /// changes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocalizedFontScope : MonoBehaviour
    {
        private void OnEnable()
        {
            GameText.LanguageChanged += Apply;
            Apply();
        }

        private void OnDisable()
        {
            GameText.LanguageChanged -= Apply;
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

            foreach (var text in GetComponentsInChildren<Text>(true))
            {
                if (text != null && text.font != font)
                {
                    text.font = font;
                }
            }

            foreach (var textMesh in GetComponentsInChildren<TextMesh>(true))
            {
                GameFonts.Apply(textMesh);
            }
        }
    }
}
