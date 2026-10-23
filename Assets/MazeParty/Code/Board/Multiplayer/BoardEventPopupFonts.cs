using MazeParty.Gameplay;
using TMPro;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>Authored event message and language fonts; no runtime UI creation.</summary>
    [DisallowMultipleComponent]
    public sealed class BoardEventPopupFonts : MonoBehaviour
    {
        [SerializeField] private TMP_Text message;
        [SerializeField] private TMP_Text[] additionalTexts = System.Array.Empty<TMP_Text>();
        [SerializeField] private TMP_FontAsset englishKoreanFont;
        [SerializeField] private TMP_FontAsset japaneseFont;
        [SerializeField] private TMP_FontAsset chineseFont;
        [SerializeField] private TMP_FontAsset chineseTraditionalFont;

        public bool HasRequiredReferences => message != null &&
            message.spriteAsset != null && englishKoreanFont != null &&
            japaneseFont != null && chineseFont != null && chineseTraditionalFont != null;

        private void OnEnable()
        {
            GameText.LanguageChanged += Apply;
            Apply();
        }

        private void OnDisable() => GameText.LanguageChanged -= Apply;

        public void Apply()
        {
            if (!HasRequiredReferences) return;
            message.font = GameText.Language == GameLanguage.Japanese ? japaneseFont :
                GameText.Language == GameLanguage.ChineseSimplified ? chineseFont :
                GameText.Language == GameLanguage.ChineseTraditional ? chineseTraditionalFont : englishKoreanFont;
            foreach (var text in additionalTexts) if (text != null) text.font = message.font;
        }
    }
}
