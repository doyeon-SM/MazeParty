using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Selects the authored lobby logo that matches the local display language.
    /// Korean uses its dedicated wordmark; every other supported language uses
    /// the English wordmark until a dedicated asset is authored.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocalizedLogo : MonoBehaviour
    {
        [SerializeField] private GameObject englishLogo;
        [SerializeField] private GameObject koreanLogo;

        public bool HasRequiredReferences =>
            englishLogo != null &&
            koreanLogo != null &&
            englishLogo != koreanLogo;

        public void Configure(GameObject english, GameObject korean)
        {
            englishLogo = english;
            koreanLogo = korean;
            ApplyLanguage();
        }

        private void OnEnable()
        {
            GameText.LanguageChanged += ApplyLanguage;
            ApplyLanguage();
        }

        private void OnDisable()
        {
            GameText.LanguageChanged -= ApplyLanguage;
        }

        private void ApplyLanguage()
        {
            if (!HasRequiredReferences)
            {
                return;
            }

            var useKoreanLogo = GameText.Language == GameLanguage.Korean;
            englishLogo.SetActive(!useKoreanLogo);
            koreanLogo.SetActive(useKoreanLogo);
        }
    }
}
