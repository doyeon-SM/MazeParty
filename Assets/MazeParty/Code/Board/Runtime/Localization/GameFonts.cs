using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Resolves the dynamic font used by player-facing text for each supported
    /// language. Runtime resource paths stay stable; downloaded source packages
    /// remain separate from the fonts included in the player.
    /// </summary>
    public static class GameFonts
    {
        public const string EnglishKoreanResourcePath = "Font/KCCMurukmuruk";
        public const string JapaneseResourcePath = "Font/NotoSansJP-Regular";
        public const string ChineseSimplifiedResourcePath = "Font/NotoSansSC-Regular";
        public const string ChineseTraditionalResourcePath = "Font/NotoSansTC-Regular";
        public const string EnglishKoreanAssetPath =
            "Assets/MazeParty/Resources/Font/KCCMurukmuruk.otf";
        public const string JapaneseAssetPath =
            "Assets/MazeParty/Resources/Font/NotoSansJP-Regular.ttf";
        public const string ChineseSimplifiedAssetPath =
            "Assets/MazeParty/Resources/Font/NotoSansSC-Regular.ttf";
        public const string ChineseTraditionalAssetPath =
            "Assets/MazeParty/Resources/Font/NotoSansTC-Regular.ttf";

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
                case GameLanguage.ChineseTraditional:
                    return ChineseTraditionalResourcePath;
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
                var occlusion = textMesh.GetComponent<WorldTextOcclusion>();
                if (occlusion != null)
                {
                    // Keep authored world labels on their depth-tested shader
                    // while still replacing the atlas for the active language.
                    occlusion.RefreshMaterial();
                }
                else
                {
                    renderer.sharedMaterial = font.material;
                }
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
