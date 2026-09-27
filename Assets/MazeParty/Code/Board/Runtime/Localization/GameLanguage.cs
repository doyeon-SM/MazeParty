using System;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Supported display languages. Serialized numeric values are persisted in
    /// player settings and must not be reordered or reused.
    /// </summary>
    public enum GameLanguage : byte
    {
        English = 0,
        Korean = 1,
        Japanese = 2,
        ChineseSimplified = 3
    }

    public static class GameLanguages
    {
        public const int Count = 4;

        private static readonly GameLanguage[] AllLanguages =
        {
            GameLanguage.English,
            GameLanguage.Korean,
            GameLanguage.Japanese,
            GameLanguage.ChineseSimplified
        };

        public static GameLanguage[] All => (GameLanguage[])AllLanguages.Clone();

        public static bool IsDefined(GameLanguage language)
        {
            return (byte)language < Count;
        }

        public static GameLanguage Sanitize(GameLanguage language)
        {
            return IsDefined(language) ? language : GameLanguage.English;
        }

        public static GameLanguage FromIndex(int index)
        {
            return index >= 0 && index < Count
                ? AllLanguages[index]
                : GameLanguage.English;
        }

        public static int ToIndex(GameLanguage language)
        {
            return (int)Sanitize(language);
        }

        /// <summary>
        /// Language names are shown in their own language so a player can always
        /// find their language regardless of the current selection.
        /// </summary>
        public static string GetNativeName(GameLanguage language)
        {
            switch (Sanitize(language))
            {
                case GameLanguage.Korean:
                    return "한국어";
                case GameLanguage.Japanese:
                    return "日本語";
                case GameLanguage.ChineseSimplified:
                    return "简体中文";
                default:
                    return "English";
            }
        }

        /// <summary>
        /// Column identifiers used by the string table header.
        /// </summary>
        public static string GetColumnId(GameLanguage language)
        {
            switch (Sanitize(language))
            {
                case GameLanguage.Korean:
                    return "ko";
                case GameLanguage.Japanese:
                    return "ja";
                case GameLanguage.ChineseSimplified:
                    return "zh-Hans";
                default:
                    return "en";
            }
        }

        public static bool TryParseColumnId(string columnId, out GameLanguage language)
        {
            for (var index = 0; index < AllLanguages.Length; index++)
            {
                if (string.Equals(
                        GetColumnId(AllLanguages[index]),
                        columnId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    language = AllLanguages[index];
                    return true;
                }
            }

            language = GameLanguage.English;
            return false;
        }
    }
}
