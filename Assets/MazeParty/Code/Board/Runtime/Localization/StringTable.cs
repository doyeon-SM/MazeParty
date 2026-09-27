using System;
using System.Collections.Generic;
using System.Text;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Immutable string table keyed by the English source text.
    /// The CSV header is <c>source,ko,ja,zh-Hans</c>. Line breaks inside a
    /// cell are written as the two characters <c>\n</c> so every entry stays on
    /// one spreadsheet row; <see cref="Parse"/> converts them back.
    /// </summary>
    public sealed class StringTable
    {
        public const string SourceColumnId = "source";

        private readonly Dictionary<string, string[]> _rows;
        private readonly List<string> _orderedSources;

        private StringTable(
            Dictionary<string, string[]> rows,
            List<string> orderedSources)
        {
            _rows = rows;
            _orderedSources = orderedSources;
        }

        public static StringTable Empty { get; } = new StringTable(
            new Dictionary<string, string[]>(StringComparer.Ordinal),
            new List<string>());

        public int Count => _orderedSources.Count;

        public IReadOnlyList<string> Sources => _orderedSources;

        public bool Contains(string source)
        {
            return source != null && _rows.ContainsKey(source);
        }

        /// <summary>
        /// Returns the translated text. English always resolves to the source.
        /// A missing row or an empty translation cell returns false.
        /// </summary>
        public bool TryGet(string source, GameLanguage language, out string value)
        {
            value = source;
            if (source == null)
            {
                return false;
            }

            language = GameLanguages.Sanitize(language);
            if (language == GameLanguage.English)
            {
                return _rows.ContainsKey(source);
            }

            if (!_rows.TryGetValue(source, out var translations))
            {
                return false;
            }

            var translation = translations[(int)language];
            if (string.IsNullOrEmpty(translation))
            {
                return false;
            }

            value = translation;
            return true;
        }

        public static StringTable Parse(string csv)
        {
            if (csv == null)
            {
                throw new ArgumentNullException(nameof(csv));
            }

            var records = ParseCsvRecords(csv);
            if (records.Count == 0)
            {
                return Empty;
            }

            var header = records[0];
            if (header.Length == 0 ||
                !string.Equals(header[0].Trim(), SourceColumnId, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException(
                    "The string table must start with a '" + SourceColumnId + "' column.");
            }

            var languageColumns = new int[GameLanguages.Count];
            for (var index = 0; index < languageColumns.Length; index++)
            {
                languageColumns[index] = -1;
            }

            for (var column = 1; column < header.Length; column++)
            {
                if (GameLanguages.TryParseColumnId(header[column].Trim(), out var language) &&
                    language != GameLanguage.English)
                {
                    languageColumns[(int)language] = column;
                }
            }

            var rows = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var ordered = new List<string>(records.Count);
            for (var recordIndex = 1; recordIndex < records.Count; recordIndex++)
            {
                var record = records[recordIndex];
                if (record.Length == 0 || string.IsNullOrEmpty(record[0]))
                {
                    continue;
                }

                var source = Unescape(record[0]);
                if (rows.ContainsKey(source))
                {
                    throw new FormatException(
                        "Duplicate string table source on row " + (recordIndex + 1) +
                        ": " + record[0]);
                }

                var translations = new string[GameLanguages.Count];
                translations[(int)GameLanguage.English] = source;
                for (var language = 1; language < GameLanguages.Count; language++)
                {
                    var column = languageColumns[language];
                    translations[language] = column >= 0 && column < record.Length
                        ? Unescape(record[column])
                        : string.Empty;
                }

                rows.Add(source, translations);
                ordered.Add(source);
            }

            return new StringTable(rows, ordered);
        }

        public static string Unescape(string cell)
        {
            if (string.IsNullOrEmpty(cell) || cell.IndexOf('\\') < 0)
            {
                return cell ?? string.Empty;
            }

            var builder = new StringBuilder(cell.Length);
            for (var index = 0; index < cell.Length; index++)
            {
                var current = cell[index];
                if (current == '\\' && index + 1 < cell.Length)
                {
                    var next = cell[index + 1];
                    if (next == 'n')
                    {
                        builder.Append('\n');
                        index++;
                        continue;
                    }

                    if (next == '\\')
                    {
                        builder.Append('\\');
                        index++;
                        continue;
                    }
                }

                builder.Append(current);
            }

            return builder.ToString();
        }

        public static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            return text.Replace("\\", "\\\\").Replace("\r\n", "\n").Replace("\n", "\\n");
        }

        /// <summary>
        /// RFC 4180 reader. Quoted fields may contain commas, doubled quotes and
        /// raw line breaks. A leading UTF-8 byte order mark is ignored.
        /// </summary>
        public static List<string[]> ParseCsvRecords(string text)
        {
            var records = new List<string[]>();
            var fields = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;
            var fieldStarted = false;
            var start = text.Length > 0 && text[0] == '﻿' ? 1 : 0;

            for (var index = start; index < text.Length; index++)
            {
                var current = text[index];
                if (inQuotes)
                {
                    if (current == '"')
                    {
                        if (index + 1 < text.Length && text[index + 1] == '"')
                        {
                            field.Append('"');
                            index++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(current);
                    }

                    continue;
                }

                switch (current)
                {
                    case '"':
                        inQuotes = true;
                        fieldStarted = true;
                        break;
                    case ',':
                        fields.Add(field.ToString());
                        field.Length = 0;
                        fieldStarted = true;
                        break;
                    case '\r':
                        break;
                    case '\n':
                        if (fieldStarted || field.Length > 0 || fields.Count > 0)
                        {
                            fields.Add(field.ToString());
                            records.Add(fields.ToArray());
                        }

                        fields.Clear();
                        field.Length = 0;
                        fieldStarted = false;
                        break;
                    default:
                        field.Append(current);
                        fieldStarted = true;
                        break;
                }
            }

            if (inQuotes)
            {
                throw new FormatException("The string table ends inside a quoted field.");
            }

            if (fieldStarted || field.Length > 0 || fields.Count > 0)
            {
                fields.Add(field.ToString());
                records.Add(fields.ToArray());
            }

            return records;
        }
    }
}
