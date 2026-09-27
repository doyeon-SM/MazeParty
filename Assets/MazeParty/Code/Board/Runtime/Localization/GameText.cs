using System;
using System.Globalization;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// A player-visible message that keeps its English source and formatting
    /// arguments so it can be resolved again after the local language changes.
    /// </summary>
    public readonly struct LocalizedMessage
    {
        private readonly object[] _arguments;

        public LocalizedMessage(string source, params object[] arguments)
        {
            Source = source ?? string.Empty;
            _arguments = arguments != null && arguments.Length > 0
                ? (object[])arguments.Clone()
                : Array.Empty<object>();
        }

        public string Source { get; }

        public string Resolve()
        {
            return GameText.F(Source, _arguments ?? Array.Empty<object>());
        }
    }

    /// <summary>
    /// Runtime text lookup for every player-visible string.
    /// Code passes the English source text; <see cref="T"/> returns the
    /// translation for the current language from
    /// <c>Resources/MazeParty/Localization/StringTable.csv</c> and falls back to
    /// the English source when a row or cell is missing.
    /// Use <see cref="F"/> for text with values: <c>GameText.F("TURN {0}", turn)</c>.
    /// </summary>
    public static class GameText
    {
        public const string TableResourcePath = "MazeParty/Localization/StringTable";

        private static StringTable _table;
        private static bool _loadAttempted;
        private static bool _tableOverridden;

        public static GameLanguage Language { get; private set; } = GameLanguage.English;

        /// <summary>Raised after <see cref="Language"/> changes.</summary>
        public static event Action LanguageChanged;

        public static StringTable Table
        {
            get
            {
                EnsureLoaded();
                return _table ?? StringTable.Empty;
            }
        }

        public static void SetLanguage(GameLanguage language)
        {
            language = GameLanguages.Sanitize(language);
            if (language == Language)
            {
                return;
            }

            Language = language;
            LanguageChanged?.Invoke();
        }

        public static string T(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source ?? string.Empty;
            }

            if (Language == GameLanguage.English)
            {
                return source;
            }

            return Table.TryGet(source, Language, out var value) ? value : source;
        }

        /// <summary>
        /// Marks an English source for the string table without translating it.
        /// Use where a call is impossible or too early (field initializers,
        /// arrays, catalog data); translate at display time with <see cref="T"/>.
        /// </summary>
        public static string N(string source)
        {
            return source;
        }

        public static string F(string sourceFormat, params object[] args)
        {
            if (string.IsNullOrEmpty(sourceFormat))
            {
                return sourceFormat ?? string.Empty;
            }

            var format = T(sourceFormat);
            try
            {
                return string.Format(CultureInfo.InvariantCulture, format, args);
            }
            catch (FormatException)
            {
                try
                {
                    return string.Format(CultureInfo.InvariantCulture, sourceFormat, args);
                }
                catch (FormatException)
                {
                    return sourceFormat;
                }
            }
        }

        /// <summary>EditMode tests inject a table without touching Resources.</summary>
        public static void UseTableForTests(StringTable table)
        {
            _table = table ?? StringTable.Empty;
            _loadAttempted = true;
            _tableOverridden = true;
        }

        public static void ResetForTests()
        {
            _table = null;
            _loadAttempted = false;
            _tableOverridden = false;
            Language = GameLanguage.English;
        }

        public static void ReloadTable()
        {
            if (_tableOverridden)
            {
                return;
            }

            _table = null;
            _loadAttempted = false;
            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            if (_loadAttempted)
            {
                return;
            }

            _loadAttempted = true;
            var asset = Resources.Load<TextAsset>(TableResourcePath);
            if (asset == null)
            {
                Debug.LogWarning(
                    "GameText string table is missing from Resources: " + TableResourcePath);
                _table = StringTable.Empty;
                return;
            }

            try
            {
                _table = StringTable.Parse(asset.text);
            }
            catch (FormatException exception)
            {
                Debug.LogError("GameText string table is invalid: " + exception.Message);
                _table = StringTable.Empty;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            // Supports Enter Play Mode without a domain reload.
            _table = null;
            _loadAttempted = false;
            _tableOverridden = false;
            Language = GameLanguage.English;
            LanguageChanged = null;
        }
    }
}
