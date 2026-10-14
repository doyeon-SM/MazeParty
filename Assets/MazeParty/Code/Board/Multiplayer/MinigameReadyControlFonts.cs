using MazeParty.Gameplay;
using TMPro;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Switches only the ready guide font when the player changes language.
    /// The TMP rows and their input sprite assets remain authored in the prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinigameReadyControlFonts : MonoBehaviour
    {
        [SerializeField] private TMP_Text[] rows;
        [SerializeField] private TMP_FontAsset englishFont;
        [SerializeField] private TMP_FontAsset koreanFont;
        [SerializeField] private TMP_FontAsset japaneseFont;
        [SerializeField] private TMP_FontAsset chineseSimplifiedFont;

        public bool HasRequiredReferences =>
            rows != null && rows.Length == 4 &&
            englishFont != null && koreanFont != null &&
            japaneseFont != null && chineseSimplifiedFont != null &&
            System.Array.TrueForAll(rows, row => row != null);

        public void Configure(
            TMP_Text[] controlRows,
            TMP_FontAsset english,
            TMP_FontAsset korean,
            TMP_FontAsset japanese,
            TMP_FontAsset chineseSimplified)
        {
            rows = controlRows;
            englishFont = english;
            koreanFont = korean;
            japaneseFont = japanese;
            chineseSimplifiedFont = chineseSimplified;
        }

        public TMP_FontAsset GetFont(GameLanguage language)
        {
            switch (GameLanguages.Sanitize(language))
            {
                case GameLanguage.Korean:
                    return koreanFont;
                case GameLanguage.Japanese:
                    return japaneseFont;
                case GameLanguage.ChineseSimplified:
                    return chineseSimplifiedFont;
                default:
                    return englishFont;
            }
        }

        private void OnEnable()
        {
            GameText.LanguageChanged += Apply;
            Apply();
        }

        private void OnDisable()
        {
            GameText.LanguageChanged -= Apply;
        }

        public void Apply()
        {
            var font = GetFont(GameText.Language);
            if (font == null || rows == null)
            {
                return;
            }

            foreach (var row in rows)
            {
                if (row != null && row.font != font)
                {
                    row.font = font;
                }
            }
        }
    }
}
