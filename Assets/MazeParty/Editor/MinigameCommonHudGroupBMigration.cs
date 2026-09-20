using System;
using System.Linq;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    /// <summary>
    /// One-time, idempotent removal of duplicated Group B Canvas elements.
    /// The Tag, Race and Cliff HUD prefab assets are deliberately preserved.
    /// </summary>
    public static class MinigameCommonHudGroupBMigration
    {
        private const string MenuPath =
            "MazeParty/Setup/Migrate Group B To Common HUD";
        private const string UiFolder = "Assets/MazeParty/UI/Prefabs/";

        [MenuItem(MenuPath)]
        public static void Migrate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Group B HUD migration cannot run in Play Mode.");
            }

            // Validate every scene source before touching any prefab or scene.
            ProcessSceneHud(
                "Assets/MazeParty/Scenes/TagChase.unity",
                UiFolder + "TagChaseHud.prefab",
                typeof(TagChaseHudBindings), true);
            ProcessSceneHud(
                "Assets/MazeParty/Scenes/Race.unity",
                UiFolder + "RaceHud.prefab",
                typeof(RaceHudBindings), true);
            ProcessSceneHud(
                "Assets/MazeParty/Scenes/Minigames/CliffBarrage.unity",
                UiFolder + "CliffBarrageHud.prefab",
                typeof(CliffBarrageHudView), true);

            TrimTerritoryPaint();
            TrimSequenceMemory();
            TrimBouncingBalls();
            ProcessSceneHud(
                "Assets/MazeParty/Scenes/TagChase.unity",
                UiFolder + "TagChaseHud.prefab",
                typeof(TagChaseHudBindings), false);
            ProcessSceneHud(
                "Assets/MazeParty/Scenes/Race.unity",
                UiFolder + "RaceHud.prefab",
                typeof(RaceHudBindings), false);
            ProcessSceneHud(
                "Assets/MazeParty/Scenes/Minigames/CliffBarrage.unity",
                UiFolder + "CliffBarrageHud.prefab",
                typeof(CliffBarrageHudView), false);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Group B common HUD migration completed.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanMigrate() =>
            !EditorApplication.isPlayingOrWillChangePlaymode;

        private static void TrimTerritoryPaint()
        {
            EditPrefab(UiFolder + "TerritoryPaintHud.prefab", root =>
            {
                var changed = Remove(root.transform.Find("Timer Panel"));
                changed |= RemoveTimer(root);
                return changed;
            });
        }

        private static void TrimSequenceMemory()
        {
            EditPrefab(UiFolder + "SequenceMemoryHud.prefab", root =>
            {
                var hudRoot = root.transform.Find("HUD Root");
                if (hudRoot == null)
                {
                    throw new InvalidOperationException(
                        "Sequence Memory HUD Root is missing.");
                }
                var changed = Remove(hudRoot.Find("Header Panel"));
                changed |= Remove(hudRoot.Find("Controls Panel"));
                changed |= RemoveTimer(root);
                var problem = hudRoot.Find("NPC Problem Panel");
                if (problem == null)
                {
                    throw new InvalidOperationException(
                        "Sequence Memory NPC panel is missing.");
                }
                changed |= Remove(problem.Find("Instructions"));
                var rect = problem.GetComponent<RectTransform>();
                if (rect.anchoredPosition != new Vector2(0f, -22f) ||
                    rect.sizeDelta != new Vector2(1080f, 160f))
                {
                    rect.anchoredPosition = new Vector2(0f, -22f);
                    rect.sizeDelta = new Vector2(1080f, 160f);
                    changed = true;
                }
                return changed;
            });
        }

        private static void TrimBouncingBalls()
        {
            EditPrefab(UiFolder + "BouncingBallsHud.prefab", root =>
            {
                var hudRoot = root.transform.Find("HUD Root");
                if (hudRoot == null)
                {
                    throw new InvalidOperationException(
                        "Bouncing Balls HUD Root is missing.");
                }
                var changed = Remove(hudRoot.Find("Header Panel"));
                changed |= Remove(hudRoot.Find("Controls Panel"));
                changed |= RemoveTimer(root);
                for (var slot = 1; slot <= 4; slot++)
                {
                    var card = hudRoot.Find("Player Card " + slot);
                    if (card == null)
                    {
                        throw new InvalidOperationException(
                            "Bouncing Balls Player Card " + slot +
                            " is missing.");
                    }
                    changed |= Remove(card.Find("Conceded"));
                    var rect = card.GetComponent<RectTransform>();
                    if (rect.sizeDelta.y != 120f)
                    {
                        rect.sizeDelta = new Vector2(rect.sizeDelta.x, 120f);
                        changed = true;
                    }
                }
                return changed;
            });
        }

        private static void EditPrefab(
            string path, Func<GameObject, bool> edit)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                throw new InvalidOperationException(
                    "Expected HUD prefab is missing: " + path);
            }
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (edit(contents))
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static bool RemoveTimer(GameObject root)
        {
            var timer = root.GetComponentInChildren<MinigameTimerDial>(true);
            return timer != null && Remove(timer.transform);
        }

        private static bool Remove(Transform target)
        {
            if (target == null)
            {
                return false;
            }
            UnityEngine.Object.DestroyImmediate(target.gameObject);
            return true;
        }

        private static void ProcessSceneHud(
            string scenePath, string prefabPath, Type hudType,
            bool validateOnly)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                throw new InvalidOperationException(
                    "Recovery HUD prefab must be retained: " + prefabPath);
            }

            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere)
            {
                scene = EditorSceneManager.OpenScene(
                    scenePath, OpenSceneMode.Additive);
            }
            try
            {
                if (scene.isDirty)
                {
                    throw new InvalidOperationException(
                        "Save or discard pending scene edits first: " +
                        scenePath);
                }
                var huds = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren(
                        hudType, true))
                    .OfType<Component>()
                    .ToArray();
                if (huds.Length > 1)
                {
                    throw new InvalidOperationException(
                        "Expected at most one dedicated HUD in " + scenePath);
                }
                if (huds.Length == 1)
                {
                    var instance = PrefabUtility.GetNearestPrefabInstanceRoot(
                        huds[0].gameObject);
                    if (instance == null ||
                        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                            instance) != prefabPath)
                    {
                        throw new InvalidOperationException(
                            "HUD source does not match expected prefab in " +
                            scenePath);
                    }
                    if (!validateOnly)
                    {
                        UnityEngine.Object.DestroyImmediate(instance);
                        EditorSceneManager.SaveScene(scene);
                    }
                }
                if (!validateOnly && scene.GetRootGameObjects().Any(root =>
                    root.GetComponentInChildren<Canvas>(true) != null))
                {
                    throw new InvalidOperationException(
                        "Dedicated Canvas remains in " + scenePath);
                }
            }
            finally
            {
                if (openedHere && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
