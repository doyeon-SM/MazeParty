using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Resolves the dynamic font used by player-facing text for each supported
    /// language. Font files live under Assets/Ignore/Resources/Font so their
    /// download packages remain local while runtime paths stay stable.
    /// </summary>
    public static class GameFonts
    {
        public const string EnglishKoreanResourcePath = "Font/KCCMurukmuruk";
        public const string JapaneseResourcePath = "Font/NotoSansJP-Regular";
        public const string ChineseSimplifiedResourcePath = "Font/NotoSansSC-Regular";
        public const string EnglishKoreanAssetPath =
            "Assets/Ignore/Resources/Font/KCCMurukmuruk.otf";
        public const string JapaneseAssetPath =
            "Assets/Ignore/Resources/Font/NotoSansJP-Regular.ttf";
        public const string ChineseSimplifiedAssetPath =
            "Assets/Ignore/Resources/Font/NotoSansSC-Regular.ttf";

        private static readonly Font[] Cache = new Font[GameLanguages.Count];
        private static readonly bool[] LoadAttempted = new bool[GameLanguages.Count];

        public static Font Current => Get(GameText.Language);

        public static string GetResourcePath(GameLanguage language)
        {
            switch (GameLanguages.Sanitize(language))
            {
                case GameLanguage.Japanese:
                    return JapaneseResourcePath;
                case GameLanguage.ChineseSimplified:
                    return ChineseSimplifiedResourcePath;
                default:
                    return EnglishKoreanResourcePath;
            }
        }

        public static Font Get(GameLanguage language)
        {
            language = GameLanguages.Sanitize(language);
            var index = GameLanguages.ToIndex(language);
            if (LoadAttempted[index])
            {
                return Cache[index];
            }

            LoadAttempted[index] = true;
            var path = GetResourcePath(language);
            Cache[index] = Resources.Load<Font>(path);
            if (Cache[index] != null)
            {
                return Cache[index];
            }

            Debug.LogWarning(
                "Localized font is missing from Resources: " + path +
                ". Falling back to LegacyRuntime.ttf.");
            Cache[index] = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return Cache[index];
        }

        public static void Apply(TextMesh textMesh)
        {
            if (textMesh == null)
            {
                return;
            }

            var font = Current;
            if (font == null)
            {
                return;
            }

            textMesh.font = font;
            var renderer = textMesh.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = font.material;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Array.Clear(Cache, 0, Cache.Length);
            Array.Clear(LoadAttempted, 0, LoadAttempted.Length);
        }
    }
}
