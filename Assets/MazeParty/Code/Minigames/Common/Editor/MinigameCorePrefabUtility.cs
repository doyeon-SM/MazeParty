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
        public static BoxCollider CreateSceneOwnedBoxCollider(
            string name,
            Transform parent,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            var colliderObject = new GameObject(name);
            colliderObject.transform.SetParent(parent, false);
            colliderObject.transform.SetLocalPositionAndRotation(
                localPosition, localRotation);
            colliderObject.transform.localScale = localScale;
            return colliderObject.AddComponent<BoxCollider>();
        }

        public static void StripColliders(GameObject root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                UnityEngine.Object.DestroyImmediate(colliders[index]);
            }
        }

        public static GameObject InstantiateOrSeed(
            string prefabPath,
            Transform parent,
            Func<GameObject> createMissingTemplate,
            string instanceName = null)
        {
            ValidatePrefabPath(prefabPath);
            if (createMissingTemplate == null)
            {
                throw new ArgumentNullException(nameof(createMissingTemplate));
            }

            EnsureFolders(prefabPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                GameObject template = null;
                try
                {
                    template = createMissingTemplate();
                    if (template == null)
                    {
                        throw new InvalidOperationException(
                            "The missing-prefab template factory returned null: " +
                            prefabPath);
                    }

                    if (template.transform.parent != null)
                    {
                        throw new InvalidOperationException(
                            "The missing-prefab template must be unparented: " +
                            prefabPath);
                    }

                    prefab = PrefabUtility.SaveAsPrefabAsset(template, prefabPath);
                    if (prefab == null)
                    {
                        throw new InvalidOperationException(
                            "Could not create core prefab: " + prefabPath);
                    }
                }
                finally
                {
                    if (template != null)
                    {
                        UnityEngine.Object.DestroyImmediate(template);
                    }
                }
            }

            var instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "Could not instantiate core prefab: " + prefabPath);
            }

            instance.transform.SetLocalPositionAndRotation(
                Vector3.zero, Quaternion.identity);
            if (!string.IsNullOrWhiteSpace(instanceName))
            {
                instance.name = instanceName;
            }
            return instance;
        }

        public static GameObject Connect(GameObject authored, string prefabPath)
        {
            if (authored == null)
            {
                throw new ArgumentNullException(nameof(authored));
            }

            ValidatePrefabPath(prefabPath);

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

        private static void ValidatePrefabPath(string prefabPath)
        {
            if (string.IsNullOrWhiteSpace(prefabPath) ||
                !prefabPath.StartsWith("Assets/MazeParty/Prefabs/Minigames/",
                    StringComparison.Ordinal) ||
                !prefabPath.EndsWith(".prefab", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Core prefab must live under the minigame prefab tree.",
                    nameof(prefabPath));
            }
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
