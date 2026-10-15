using System;
using System.Text;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace MazeParty.Editor
{
    /// <summary>
    /// Removes retired authored hints and localizes the ready controls heading.
    /// The pass is idempotent and does not alter layout or remaining UI design.
    /// </summary>
    public static class BoardUiCopyCleanup
    {
        private const string ReadyPanelPrefabPath =
            MinigameReadyPanelProjectSetup.ModulePrefabPath;
        private const string EnglishControlFontPath =
            MinigameReadyPanelProjectSetup.ControlFontAssetPath;
        private const string FontFolderPath = "Assets/MazeParty/Fonts";
        private const string KoreanControlFontPath =
            FontFolderPath + "/BoardReadyKorean SDF.asset";
        private const string JapaneseControlFontPath =
            FontFolderPath + "/BoardReadyJapanese SDF.asset";
        private const string ChineseControlFontPath =
            FontFolderPath + "/BoardReadyChineseSimplified SDF.asset";
        private const string TablePath =
            "Assets/MazeParty/Resources/MazeParty/Localization/StringTable.csv";

        private static readonly string[] ReadyControlLabels =
        {
            "CONTROLS", "MOVE", "SCAN", "PRESS SHOWN KEY", "MOVE / STOP",
            "PUSH", "HOLD / RELEASE", "THROW / PUSH", "MOVE / PAINT",
            "TAGGER LOOK", "TAGGER CATCH", "ALTERNATE", "REPEAT",
            "MOVE SHIELD", "PASS / STUN", "ROLL", "LOOK", "PUNCH"
        };

        [MenuItem("MazeParty/UI/Clean Board Map Item Ready Copy")]
        public static void Clean()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Board UI copy cleanup requires Edit Mode.");
            }

            RemoveObjects(
                BoardCanvasSplitProjectSetup.MapPrefabPath,
                "Minimap Legend");
            RemoveObjects(
                BoardCanvasSplitProjectSetup.ItemPrefabPath,
                "Selection Rule",
                "ItemShopRule");
            LocalizeReadyPanel();
            AssetDatabase.SaveAssets();
            Debug.Log(
                "Retired map and item hints were removed; the ready controls " +
                "heading now follows the selected language.");
        }

        private static void RemoveObjects(string path, params string[] names)
        {
            RequirePrefab(path);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var changed = false;
                foreach (var name in names)
                {
                    var target = FindDescendant(root.transform, name);
                    if (target == null)
                    {
                        continue;
                    }

                    UnityEngine.Object.DestroyImmediate(target.gameObject);
                    changed = true;
                }

                if (changed)
                {
                    Save(root, path);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void LocalizeReadyPanel()
        {
            RequirePrefab(ReadyPanelPrefabPath);
            var english = RequireFontAsset(EnglishControlFontPath);
            var korean = EnsureDynamicFont(
                KoreanControlFontPath,
                GameFonts.EnglishKoreanAssetPath,
                GameLanguage.Korean);
            var japanese = EnsureDynamicFont(
                JapaneseControlFontPath,
                GameFonts.JapaneseAssetPath,
                GameLanguage.Japanese);
            var chinese = EnsureDynamicFont(
                ChineseControlFontPath,
                GameFonts.ChineseSimplifiedAssetPath,
                GameLanguage.ChineseSimplified);
            var root = PrefabUtility.LoadPrefabContents(ReadyPanelPrefabPath);
            try
            {
                var title = FindDescendant(root.transform,
                    "MinigameControlsTitle");
                if (title == null)
                {
                    throw new InvalidOperationException(
                        "Minigame ready controls title is missing.");
                }

                var localized = title.GetComponent<LocalizedText>();
                var fontBinding = root.GetComponent<MinigameReadyControlFonts>();
                var needsTitle = localized == null ||
                                 localized.SourceText != "CONTROLS";
                var needsFonts = fontBinding == null ||
                                 !fontBinding.HasRequiredReferences;
                if (!needsTitle && !needsFonts)
                {
                    return;
                }

                if (needsTitle)
                {
                    if (localized == null)
                    {
                        localized = title.gameObject.AddComponent<LocalizedText>();
                    }

                    localized.Configure("CONTROLS");
                }

                if (needsFonts)
                {
                    if (fontBinding == null)
                    {
                        fontBinding =
                            root.AddComponent<MinigameReadyControlFonts>();
                    }

                    var rows = new TMP_Text[4];
                    for (var index = 0; index < rows.Length; index++)
                    {
                        var row = FindDescendant(root.transform,
                            "MinigameControlRow" + index);
                        if (row == null ||
                            (rows[index] = row.GetComponent<TMP_Text>()) == null)
                        {
                            throw new InvalidOperationException(
                                "Minigame ready control row is missing: " +
                                index);
                        }
                    }

                    fontBinding.Configure(rows, english, korean, japanese,
                        chinese);
                }

                Save(root, ReadyPanelPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static TMP_FontAsset RequireFontAsset(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    "Required control font asset is missing: " + path);
            }

            return asset;
        }

        private static TMP_FontAsset EnsureDynamicFont(
            string path,
            string sourcePath,
            GameLanguage language)
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
            var tableAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(TablePath);
            if (source == null || tableAsset == null)
            {
                throw new InvalidOperationException(
                    "Ready control font source or string table is missing: " +
                    sourcePath);
            }

            var table = StringTable.Parse(tableAsset.text);
            var characters = new StringBuilder();
            foreach (var label in ReadyControlLabels)
            {
                if (!table.TryGet(label, language, out var translated))
                {
                    throw new InvalidOperationException(
                        "Ready control translation is missing: " +
                        language + " / " + label);
                }

                characters.Append(translated);
            }

            if (!AssetDatabase.IsValidFolder(FontFolderPath))
            {
                AssetDatabase.CreateFolder("Assets/MazeParty", "Fonts");
            }

            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (asset == null)
            {
                asset = TMP_FontAsset.CreateFontAsset(
                    source, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                    AtlasPopulationMode.Dynamic, true);
                if (asset == null)
                {
                    throw new InvalidOperationException(
                        "Could not create ready control font from " + sourcePath);
                }

                asset.name = System.IO.Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
                AssetDatabase.AddObjectToAsset(asset.material, asset);
            }

            if (!asset.TryAddCharacters(characters.ToString(),
                    out var missingCharacters) &&
                !string.IsNullOrWhiteSpace(missingCharacters))
            {
                throw new InvalidOperationException(
                    "Ready control font lacks translated glyphs: " +
                    language + " / " + missingCharacters);
            }

            EditorUtility.SetDirty(asset);
            foreach (var atlas in asset.atlasTextures)
            {
                EditorUtility.SetDirty(atlas);
            }
            AssetDatabase.SaveAssets();
            return RequireFontAsset(path);
        }

        private static void RequirePrefab(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                throw new InvalidOperationException(
                    "Required authored UI prefab is missing: " + path);
            }
        }

        private static void Save(GameObject root, string path)
        {
            if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
            {
                throw new InvalidOperationException(
                    "Could not save authored UI prefab: " + path);
            }
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }
    }
}
