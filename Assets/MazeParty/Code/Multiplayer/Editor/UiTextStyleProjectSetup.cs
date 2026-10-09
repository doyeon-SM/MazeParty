using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Editor
{
    internal static class UiTextStyleProjectSetup
    {
        private const string MultiplayerUiRoot =
            "Assets/MazeParty/Prefabs/Multiplayer/UI";
        private const string BoardUiRoot =
            "Assets/MazeParty/Prefabs/Board/UI";
        private const string MinigamePrefabRoot =
            "Assets/MazeParty/Prefabs/Minigames";

        private static readonly string[] SearchRoots =
        {
            MultiplayerUiRoot,
            BoardUiRoot,
            MinigamePrefabRoot
        };

        [MenuItem("MazeParty/UI/Normalize Player-Facing Text Styles")]
        private static void NormalizePlayerFacingTextStyles()
        {
            var prefabPaths = AssetDatabase.FindAssets(
                    "t:Prefab",
                    SearchRoots)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(IsAllowedUiPrefabPath)
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(GetPathDepth)
                .ThenBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var changedPrefabCount = 0;
            var changedTextCount = 0;
            foreach (var path in prefabPaths)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var prefabChanged = false;
                    foreach (var text in
                             root.GetComponentsInChildren<Text>(true))
                    {
                        if (text.fontStyle == FontStyle.Normal)
                        {
                            continue;
                        }

                        text.fontStyle = FontStyle.Normal;
                        EditorUtility.SetDirty(text);
                        prefabChanged = true;
                        changedTextCount++;
                    }

                    foreach (var text in
                             root.GetComponentsInChildren<TMP_Text>(true))
                    {
                        if (!(text is TextMeshProUGUI) ||
                            (text.fontStyle == FontStyles.Normal &&
                             text.fontWeight == FontWeight.Regular))
                        {
                            continue;
                        }

                        text.fontStyle = FontStyles.Normal;
                        text.fontWeight = FontWeight.Regular;
                        EditorUtility.SetDirty(text);
                        prefabChanged = true;
                        changedTextCount++;
                    }

                    if (!prefabChanged)
                    {
                        continue;
                    }

                    if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                    {
                        throw new InvalidOperationException(
                            "Could not save normalized UI prefab: " + path);
                    }

                    changedPrefabCount++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                "Normalized player-facing Canvas text styles in " +
                changedPrefabCount + " prefab(s), " +
                changedTextCount + " text component(s). Scanned " +
                prefabPaths.Length + " allowed UI prefab(s).");
        }

        private static bool IsAllowedUiPrefabPath(string path)
        {
            return path.StartsWith(
                       MultiplayerUiRoot + "/",
                       StringComparison.Ordinal) ||
                   path.StartsWith(
                       BoardUiRoot + "/",
                       StringComparison.Ordinal) ||
                   (path.StartsWith(
                        MinigamePrefabRoot + "/",
                        StringComparison.Ordinal) &&
                    path.IndexOf("/UI/", StringComparison.Ordinal) >= 0);
        }

        private static int GetPathDepth(string path)
        {
            return path.Count(character => character == '/');
        }
    }
}
