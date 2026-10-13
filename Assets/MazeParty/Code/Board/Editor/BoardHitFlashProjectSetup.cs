using System;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    /// <summary>
    /// Installs the authored board hit-flash prefab without rewriting its design.
    /// </summary>
    public static class BoardHitFlashProjectSetup
    {
        public const string PrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardHitFlash.prefab";
        public const string ScenePath =
            "Assets/MazeParty/Scenes/Board/Board.unity";

        [MenuItem("MazeParty/Board/Install Board Hit Flash")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Stop Play mode before installing the board hit-flash UI.");
            }

            var scene = SceneManager.GetSceneByPath(ScenePath);
            var openedForSetup = !scene.IsValid() || !scene.isLoaded;
            if (openedForSetup)
            {
                scene = EditorSceneManager.OpenScene(
                    ScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                if (EnsureSceneInstance(scene))
                {
                    EditorSceneManager.SaveScene(scene);
                }

                AssetDatabase.SaveAssets();
                Debug.Log("Board hit-flash UI is installed.");
            }
            finally
            {
                if (openedForSetup)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [MenuItem("MazeParty/Board/Install Board Hit Flash", true)]
        private static bool CanInstall()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        internal static bool EnsureSceneInstance(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException(
                    "A loaded scene is required to install the board hit flash.");
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var prefabView = prefab != null
                ? prefab.GetComponent<ArenaCombatHitFlashView>()
                : null;
            if (prefabView == null || !prefabView.HasRequiredReferences ||
                prefab.GetComponent<Canvas>() == null)
            {
                throw new InvalidOperationException(
                    "BoardHitFlash.prefab requires its authored Canvas and " +
                    "serialized bindings.");
            }

            ArenaCombatHitFlashView view = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var candidates =
                    root.GetComponentsInChildren<ArenaCombatHitFlashView>(true);
                for (var index = 0; index < candidates.Length; index++)
                {
                    if (view != null)
                    {
                        throw new InvalidOperationException(
                            "Board scene has multiple hit-flash views.");
                    }

                    view = candidates[index];
                }
            }

            var created = false;
            if (view == null)
            {
                var instance = PrefabUtility.InstantiatePrefab(
                    prefab,
                    scene) as GameObject;
                view = instance != null
                    ? instance.GetComponent<ArenaCombatHitFlashView>()
                    : null;
                if (view == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate BoardHitFlash.prefab.");
                }

                instance.name = "Board Hit Flash";
                EditorSceneManager.MarkSceneDirty(scene);
                created = true;
            }

            if (view.transform.parent != null ||
                !view.HasRequiredReferences ||
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    view.gameObject) != PrefabPath)
            {
                throw new InvalidOperationException(
                    "Board hit flash must be a standalone BoardHitFlash.prefab " +
                    "instance with authored bindings.");
            }

            return created;
        }
    }
}
