using System;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    /// <summary>
    /// Authors the hit-flash prefab once and installs a linked scene instance.
    /// Re-running setup never overwrites an existing prefab's design.
    /// </summary>
    public static class ArenaCombatHitFlashProjectSetup
    {
        public const string PrefabPath =
            "Assets/MazeParty/Prefabs/Minigames/ArenaCombat/UI/ArenaCombatHitFlash.prefab";
        public const string ScenePath =
            "Assets/MazeParty/Scenes/Minigames/ArenaCombat/ArenaCombat.unity";

        [MenuItem("MazeParty/Minigames/Install Arena Combat Hit Flash")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Stop Play mode before installing the hit-flash UI.");
            }

            EnsurePrefabExists();
            EnsureSceneInstance();
            AssetDatabase.SaveAssets();
            Debug.Log("Arena Combat hit-flash UI is installed.");
        }

        [MenuItem("MazeParty/Minigames/Install Arena Combat Hit Flash", true)]
        private static bool CanInstall()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static GameObject EnsurePrefabExists()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                var template = CreateTemplate();
                try
                {
                    prefab = PrefabUtility.SaveAsPrefabAsset(
                        template, PrefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(template);
                }
            }

            var view = prefab != null
                ? prefab.GetComponent<ArenaCombatHitFlashView>()
                : null;
            if (view == null || !view.HasRequiredReferences ||
                prefab.GetComponent<Canvas>() == null)
            {
                throw new InvalidOperationException(
                    "Hit-flash prefab requires its authored Canvas and " +
                    "serialized bindings.");
            }

            return prefab;
        }

        public static void EnsureSceneInstance()
        {
            var prefab = EnsurePrefabExists();
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var openedForSetup = !scene.IsValid() || !scene.isLoaded;
            if (openedForSetup)
            {
                scene = EditorSceneManager.OpenScene(
                    ScenePath, OpenSceneMode.Additive);
            }

            try
            {
                ArenaCombatHitFlashView view = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var candidate = root.GetComponent<ArenaCombatHitFlashView>();
                    if (candidate == null)
                    {
                        continue;
                    }

                    if (view != null)
                    {
                        throw new InvalidOperationException(
                            "Arena Combat scene has multiple hit-flash views.");
                    }

                    view = candidate;
                }

                if (view == null)
                {
                    var instance = PrefabUtility.InstantiatePrefab(
                        prefab, scene) as GameObject;
                    view = instance != null
                        ? instance.GetComponent<ArenaCombatHitFlashView>()
                        : null;
                    if (view == null)
                    {
                        throw new InvalidOperationException(
                            "Could not instantiate hit-flash prefab.");
                    }

                    instance.name = "Arena Combat Hit Flash";
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }

                if (view.transform.parent != null ||
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        view.gameObject) != PrefabPath)
                {
                    throw new InvalidOperationException(
                        "Hit flash must be a standalone prefab instance " +
                        "in Arena Combat scene.");
                }
            }
            finally
            {
                if (openedForSetup)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static GameObject CreateTemplate()
        {
            var root = new GameObject(
                "Arena Combat Hit Flash",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(ArenaCombatHitFlashView));
            root.transform.localScale = Vector3.one;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 550;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var flash = new GameObject(
                "Red Hit Flash",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(CanvasRenderer),
                typeof(Image));
            flash.transform.SetParent(root.transform, false);
            var rect = flash.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var group = flash.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            var image = flash.GetComponent<Image>();
            image.color = new Color(0.95f, 0.025f, 0.025f, 1f);
            image.raycastTarget = false;

            root.GetComponent<ArenaCombatHitFlashView>()
                .ConfigureUiBindings(canvas, group, image);
            SetUiLayer(root);
            return root;
        }

        private static void SetUiLayer(GameObject root)
        {
            root.layer = LayerMask.NameToLayer("UI");
            for (var index = 0; index < root.transform.childCount; index++)
            {
                SetUiLayer(root.transform.GetChild(index).gameObject);
            }
        }
    }
}
