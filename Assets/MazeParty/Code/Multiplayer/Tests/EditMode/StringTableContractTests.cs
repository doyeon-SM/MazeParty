using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    /// <summary>
    /// Keeps the shipped string table complete: every English source used by
    /// runtime code or by a localized prefab label has Korean, Japanese and
    /// Simplified Chinese text with the same placeholders and line breaks.
    /// </summary>
    public sealed class StringTableContractTests
    {
        private const string TableAssetPath =
            "Assets/MazeParty/Resources/MazeParty/Localization/StringTable.csv";

        private static readonly string[] CodeRoots =
        {
            "Assets/MazeParty/Code/Board",
            "Assets/MazeParty/Code/Minigames",
            "Assets/MazeParty/Code/Multiplayer"
        };

        private static readonly Regex CallPattern =
            new Regex(
                @"(?:GameText\s*\.\s*[TFN]|SetLocalizedStatus)\s*\(\s*",
                RegexOptions.Compiled);

        private static readonly Regex LiteralPattern =
            new Regex(@"\G""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

        private static readonly Regex PlaceholderPattern =
            new Regex(@"\{\d+(?::[^}]*)?\}", RegexOptions.Compiled);

        private static StringTable LoadTable()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(TableAssetPath);
            Assert.That(asset, Is.Not.Null, TableAssetPath);
            return StringTable.Parse(asset.text);
        }

        [Test]
        public void Table_HasEveryLanguageWithMatchingPlaceholdersAndLineBreaks()
        {
            var table = LoadTable();
            Assert.That(table.Count, Is.GreaterThan(0));
            var problems = new List<string>();
            foreach (var source in table.Sources)
            {
                var expected = Placeholders(source);
                foreach (var language in new[]
                         {
                             GameLanguage.Korean,
                             GameLanguage.Japanese,
                             GameLanguage.ChineseSimplified
                         })
                {
                    if (!table.TryGet(source, language, out var translated))
                    {
                        problems.Add(language + " missing: " + source);
                        continue;
                    }

                    if (Placeholders(translated) != expected)
                    {
                        problems.Add(language + " placeholders differ: " + source);
                    }

                    if (Count(translated, '\n') != Count(source, '\n'))
                    {
                        problems.Add(language + " line breaks differ: " + source);
                    }
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void Table_CoversEveryGameTextLiteralInRuntimeCode()
        {
            var table = LoadTable();
            var missing = new SortedSet<string>();
            foreach (var root in CodeRoots)
            {
                foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    var normalized = file.Replace('\\', '/');
                    if (normalized.Contains("/Editor/") || normalized.Contains("/Tests/"))
                    {
                        continue;
                    }

                    foreach (var source in ExtractSources(File.ReadAllText(file)))
                    {
                        if (!table.Contains(source))
                        {
                            missing.Add(Path.GetFileName(file) + ": " + source);
                        }
                    }
                }
            }

            Assert.That(missing, Is.Empty,
                "Add these sources to StringTable.csv:\n" + string.Join("\n", missing));
        }

        [Test]
        public void Table_CoversLocalizedPrefabLabelsAndDataDrivenNames()
        {
            var table = LoadTable();
            var sources = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/MazeParty/Prefabs" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Dev/"))
                {
                    continue;
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var label in prefab.GetComponentsInChildren<LocalizedText>(true))
                {
                    sources.Add(label.SourceText);
                }

                foreach (var label in prefab.GetComponentsInChildren<LocalizedTextMesh>(true))
                {
                    sources.Add(label.SourceText);
                }
            }

            foreach (var item in PrototypeItemCatalog.All)
            {
                sources.Add(item.DisplayName);
                sources.Add(item.Description);
            }

            var appearanceCatalog = PlayerExpressionCatalog.Instance;
            Assert.That(appearanceCatalog, Is.Not.Null);
            foreach (var face in appearanceCatalog.Faces)
            {
                sources.Add(face.Name);
            }
            foreach (var hat in appearanceCatalog.Hats)
            {
                sources.Add(hat.Name);
            }
            sources.Add("None");

            for (var index = 0; index < DisplayModeOptions.Count; index++)
            {
                sources.Add(DisplayModeOptions.GetLabelSource((DisplayModeOption)index));
            }
            for (var index = 0; index < QualityPresetOptions.Count; index++)
            {
                sources.Add(QualityPresetOptions.GetLabelSource(
                    (QualityPresetOption)index));
            }
            for (var index = 0; index < FrameRateCapOptions.Count; index++)
            {
                sources.Add(FrameRateCapOptions.GetLabelSource(
                    (FrameRateCapOption)index));
            }

            sources.Add(GameMenuRules.GetExitLabelSource(GameMenuContext.Lobby));
            sources.Add(GameMenuRules.GetExitLabelSource(GameMenuContext.InGame));
            foreach (var definition in MinigameCatalog.RegisteredMinigames)
            {
                sources.Add(definition.DisplayName);
            }

            var boardMapCatalog = Resources.Load<BoardMapCatalog>(
                BoardMapRuntimeLoader.CatalogResourcesPath);
            Assert.That(boardMapCatalog, Is.Not.Null);
            foreach (var definition in boardMapCatalog.Maps)
            {
                Assert.That(definition, Is.Not.Null);
                sources.Add(definition.DisplayName);
            }

            var missing = sources
                .Where(source => !string.IsNullOrEmpty(source) && !table.Contains(source))
                .ToArray();
            Assert.That(missing, Is.Empty,
                "Add these sources to StringTable.csv:\n" + string.Join("\n", missing));
        }

        /// <summary>
        /// Returns the first-argument literal of each GameText.T/F/N or
        /// SetLocalizedStatus call, joining adjacent literals concatenated
        /// with '+'.
        /// </summary>
        private static IEnumerable<string> ExtractSources(string code)
        {
            foreach (Match call in CallPattern.Matches(code))
            {
                if (IsInsideLineComment(code, call.Index))
                {
                    continue;
                }

                var index = call.Index + call.Length;
                var builder = new StringBuilder();
                var any = false;
                while (true)
                {
                    var literal = LiteralPattern.Match(code, index);
                    if (!literal.Success)
                    {
                        break;
                    }

                    any = true;
                    builder.Append(Regex.Unescape(literal.Groups[1].Value));
                    index = literal.Index + literal.Length;
                    var next = index;
                    while (next < code.Length && char.IsWhiteSpace(code[next]))
                    {
                        next++;
                    }

                    if (next >= code.Length || code[next] != '+')
                    {
                        break;
                    }

                    next++;
                    while (next < code.Length && char.IsWhiteSpace(code[next]))
                    {
                        next++;
                    }

                    if (next >= code.Length || code[next] != '"')
                    {
                        break;
                    }

                    index = next;
                }

                if (any)
                {
                    yield return builder.ToString();
                }
            }
        }

        /// <summary>Skips examples in // and /// comments.</summary>
        private static bool IsInsideLineComment(string code, int index)
        {
            var lineStart = code.LastIndexOf('\n', System.Math.Max(0, index - 1)) + 1;
            var prefix = code.Substring(lineStart, index - lineStart);
            return prefix.Contains("//") || prefix.TrimStart().StartsWith("*");
        }

        private static string Placeholders(string text)
        {
            return string.Join("|", PlaceholderPattern.Matches(text)
                .Cast<Match>()
                .Select(match => match.Value)
                .OrderBy(value => value, System.StringComparer.Ordinal));
        }

        private static int Count(string text, char value)
        {
            return text.Count(character => character == value);
        }
    }
}
