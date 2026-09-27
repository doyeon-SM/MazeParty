using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    /// <summary>
    /// Adds <see cref="LocalizedText"/> / <see cref="LocalizedTextMesh"/> to the
    /// static labels of every player-facing prefab. A label is static when no
    /// component serializes a reference to it (runtime-written labels are always
    /// bound through a serialized field and localize themselves in code).
    /// Nested prefab instances are skipped; their own prefab asset is processed.
    /// Existing components and designer copy are never changed.
    /// </summary>
    public static class LocalizationProjectSetup
    {
        public const string PrefabRoot = "Assets/MazeParty/Prefabs";
        private const string SourceListPath = "Temp/MazeParty_LocalizedSources.txt";
        private static readonly Regex WordPattern = new Regex("[A-Za-z]{2,}", RegexOptions.Compiled);

        /// <summary>Static TextMesh labels that a component references but never rewrites.</summary>
        private static readonly string[] StaticReferencedTextMeshPrefabNames =
        {
            "KeyShop"
        };

        [MenuItem("MazeParty/Localization/Add Localized Labels To Prefabs")]
        public static void AddLocalizedLabels()
        {
            var added = 0;
            foreach (var path in PlayerFacingPrefabPaths())
            {
                added += AddToPrefab(path);
            }

            AssetDatabase.SaveAssets();
            WriteSourceList();
            Debug.Log("Localized labels added: " + added + ". Source list: " + SourceListPath);
        }

        [MenuItem("MazeParty/Localization/Write Localized Label Source List")]
        public static void WriteSourceList()
        {
            var sources = CollectPrefabSources();
            File.WriteAllText(SourceListPath, string.Join("\n", sources.Select(StringTable.Escape)), Encoding.UTF8);
        }

        /// <summary>English sources of every LocalizedText / LocalizedTextMesh in player-facing prefabs.</summary>
        public static SortedSet<string> CollectPrefabSources()
        {
            var sources = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (var path in PlayerFacingPrefabPaths())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                foreach (var label in prefab.GetComponentsInChildren<LocalizedText>(true))
                {
                    if (!string.IsNullOrEmpty(label.SourceText))
                    {
                        sources.Add(label.SourceText);
                    }
                }

                foreach (var label in prefab.GetComponentsInChildren<LocalizedTextMesh>(true))
                {
                    if (!string.IsNullOrEmpty(label.SourceText))
                    {
                        sources.Add(label.SourceText);
                    }
                }
            }

            return sources;
        }

        public static IEnumerable<string> PlayerFacingPrefabPaths()
        {
            return AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !path.Contains("/Dev/"))
                .OrderBy(path => path, System.StringComparer.Ordinal);
        }

        public static bool HasWords(string text)
        {
            return !string.IsNullOrEmpty(text) && WordPattern.IsMatch(text);
        }

        private static int AddToPrefab(string path)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var referenced = CollectReferencedObjects(contents);
                var staticTextMeshPrefab = StaticReferencedTextMeshPrefabNames
                    .Any(name => Path.GetFileNameWithoutExtension(path).Contains(name));
                var added = 0;
                foreach (var text in contents.GetComponentsInChildren<Text>(true))
                {
                    if (IsInsideNestedPrefab(contents, text) ||
                        text.GetComponent<LocalizedText>() != null ||
                        !HasWords(text.text) ||
                        (referenced.Contains(text) && !IsInputPlaceholder(contents, text)))
                    {
                        continue;
                    }

                    text.gameObject.AddComponent<LocalizedText>().Configure(text.text);
                    added++;
                }

                foreach (var textMesh in contents.GetComponentsInChildren<TextMesh>(true))
                {
                    if (IsInsideNestedPrefab(contents, textMesh) ||
                        textMesh.GetComponent<LocalizedTextMesh>() != null ||
                        !HasWords(textMesh.text) ||
                        (referenced.Contains(textMesh) && !staticTextMeshPrefab))
                    {
                        continue;
                    }

                    textMesh.gameObject.AddComponent<LocalizedTextMesh>().Configure(textMesh.text);
                    added++;
                }

                if (added > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }

                return added;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static HashSet<Object> CollectReferencedObjects(GameObject root)
        {
            var referenced = new HashSet<Object>();
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour is LocalizedText || behaviour is Graphic)
                {
                    continue;
                }

                var serialized = new SerializedObject(behaviour);
                var property = serialized.GetIterator();
                var enterChildren = true;
                while (property.Next(enterChildren))
                {
                    enterChildren = true;
                    if (property.propertyType == SerializedPropertyType.ObjectReference &&
                        property.objectReferenceValue != null)
                    {
                        referenced.Add(property.objectReferenceValue);
                    }
                }
            }

            return referenced;
        }

        private static bool IsInputPlaceholder(GameObject root, Text text)
        {
            return root.GetComponentsInChildren<InputField>(true)
                .Any(input => input.placeholder == text);
        }

        private static bool IsInsideNestedPrefab(GameObject contentsRoot, Component component)
        {
            var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(component.gameObject);
            return instanceRoot != null && instanceRoot != contentsRoot;
        }
    }
}
