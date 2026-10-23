using System;
using MazeParty.Multiplayer;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Editor
{
    public static class TraditionalChineseLocalizationSetup
    {
        [MenuItem("MazeParty/Localization/Configure Traditional Chinese Fonts")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Font setup requires Edit Mode.");
            LocalizationProjectSetup.AddLanguageFontScopes();
            var font = BoardUiCopyCleanup.EnsureTraditionalChineseFont();
            BoardEventPopupPresentationUpgrade.EnsureFontFallbacks();
            var changed = 0;
            foreach (var path in LocalizationProjectSetup.PlayerFacingPrefabPaths())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab.GetComponentsInChildren<MinigameReadyControlFonts>(true).Length == 0 &&
                    prefab.GetComponentsInChildren<BoardEventPopupFonts>(true).Length == 0)
                    continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var dirty = false;
                    foreach (var binding in root.GetComponentsInChildren<MinigameReadyControlFonts>(true))
                        dirty |= Bind(binding, font);
                    foreach (var binding in root.GetComponentsInChildren<BoardEventPopupFonts>(true))
                        dirty |= Bind(binding, font);
                    if (!dirty) continue;
                    if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                        throw new InvalidOperationException("Could not save font binding: " + path);
                    changed++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Traditional Chinese font bindings updated in " + changed + " prefabs.");
        }

        private static bool Bind(Component binding, TMP_FontAsset font)
        {
            // Configure the source prefab, leaving nested instances to inherit it.
            if (PrefabUtility.IsPartOfPrefabInstance(binding)) return false;
            var data = new SerializedObject(binding);
            var property = data.FindProperty("chineseTraditionalFont");
            if (property.objectReferenceValue != null) return false;
            property.objectReferenceValue = font;
            return data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
