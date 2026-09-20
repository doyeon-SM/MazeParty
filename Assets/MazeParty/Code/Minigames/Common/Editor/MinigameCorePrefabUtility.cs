using System;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Editor
{
    /// <summary>
    /// Keeps rebuildable minigame scene objects connected to their authored prefabs.
    /// A setup run seeds a missing prefab once; later runs use the existing asset.
    /// </summary>
    public static class MinigameCorePrefabUtility
    {
        public static GameObject Connect(GameObject authored, string prefabPath)
        {
            if (authored == null)
            {
                throw new ArgumentNullException(nameof(authored));
            }

            if (string.IsNullOrWhiteSpace(prefabPath) ||
                !prefabPath.StartsWith("Assets/MazeParty/Prefabs/Minigames/",
                    StringComparison.Ordinal) ||
                !prefabPath.EndsWith(".prefab", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Core prefab must live under the minigame prefab tree.",
                    nameof(prefabPath));
            }

            if (PrefabUtility.IsPartOfPrefabInstance(authored))
            {
                return authored;
            }

            EnsureFolders(prefabPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
                    authored, prefabPath, InteractionMode.AutomatedAction);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        "Could not create core prefab: " + prefabPath);
                }
                return authored;
            }

            var source = authored.transform;
            var parent = source.parent;
            var siblingIndex = source.GetSiblingIndex();
            var localPosition = source.localPosition;
            var localRotation = source.localRotation;
            var activeSelf = authored.activeSelf;
            var instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "Could not instantiate core prefab: " + prefabPath);
            }

            instance.name = authored.name;
            instance.transform.SetSiblingIndex(siblingIndex);
            instance.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            instance.SetActive(activeSelf);
            // Root scale belongs to the prefab. The rebuild controls placement,
            // while designers can change geometry and scale on the source asset.
            UnityEngine.Object.DestroyImmediate(authored);
            return instance;
        }

        private static void EnsureFolders(string prefabPath)
        {
            var slash = prefabPath.LastIndexOf('/');
            var folder = prefabPath.Substring(0, slash);
            var parts = folder.Split('/');
            var current = parts[0];
            for (var index = 1; index < parts.Length; index++)
            {
                var next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    var guid = AssetDatabase.CreateFolder(current, parts[index]);
                    if (string.IsNullOrEmpty(guid))
                    {
                        throw new InvalidOperationException(
                            "Could not create prefab folder: " + next);
                    }
                }
                current = next;
            }
        }
    }
}
