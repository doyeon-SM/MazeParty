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
        public const string AimSpritePath =
            "Assets/Ignore/Modern UI Pack/Textures/Icon/Navigation/Add.png";

        private const string AimSpriteName = "Add";

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

            var hitFlashView = prefab != null
                ? prefab.GetComponent<ArenaCombatHitFlashView>()
                : null;
            if (hitFlashView == null ||
                !hitFlashView.HasRequiredReferences ||
                prefab.GetComponent<Canvas>() == null)
            {
                throw new InvalidOperationException(
                    "Hit-flash prefab requires its authored Canvas and " +
                    "serialized bindings.");
            }

            RequireAimView(
                prefab,
                RequireAimSprite(),
                "Arena Combat hit-flash prefab");
            return prefab;
        }

        public static void EnsureSceneInstance()
        {
            var prefab = EnsurePrefabExists();
            var aimSprite = RequireAimSprite();
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var openedForSetup = !scene.IsValid() || !scene.isLoaded;
            if (openedForSetup)
            {
                scene = EditorSceneManager.OpenScene(
                    ScenePath, OpenSceneMode.Additive);
            }

            try
            {
                ArenaCombatHitFlashView hitFlashView = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var candidate =
                        root.GetComponent<ArenaCombatHitFlashView>();
                    if (candidate == null)
                    {
                        continue;
                    }

                    if (hitFlashView != null)
                    {
                        throw new InvalidOperationException(
                            "Arena Combat scene has multiple hit-flash views.");
                    }

                    hitFlashView = candidate;
                }

                var sceneChanged = false;
                if (hitFlashView == null)
                {
                    var instance = PrefabUtility.InstantiatePrefab(
                        prefab, scene) as GameObject;
                    hitFlashView = instance != null
                        ? instance.GetComponent<ArenaCombatHitFlashView>()
                        : null;
                    if (hitFlashView == null)
                    {
                        throw new InvalidOperationException(
                            "Could not instantiate hit-flash prefab.");
                    }

                    instance.name = "Arena Combat Hit Flash";
                    sceneChanged = true;
                }

                if (hitFlashView.transform.parent != null ||
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        hitFlashView.gameObject) != PrefabPath)
                {
                    throw new InvalidOperationException(
                        "Hit flash must be a standalone prefab instance " +
                        "in Arena Combat scene.");
                }

                var aimView = RequireAimView(
                    hitFlashView.gameObject,
                    aimSprite,
                    "Arena Combat scene hit-flash instance");
                var networkView = RequireArenaNetworkView(scene);
                if (networkView.AimView != aimView)
                {
                    networkView.ConfigureAim(aimView);
                    EditorUtility.SetDirty(networkView);
                    sceneChanged = true;
                }

                if (sceneChanged)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
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
                typeof(ArenaCombatHitFlashView),
                typeof(ArenaCombatAimView));
            root.transform.localScale = Vector3.one;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 550;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var flash = new GameObject(
                "Red Hit Flash",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(CanvasRenderer),
                typeof(Image));
            flash.transform.SetParent(root.transform, false);
            var flashRect = flash.GetComponent<RectTransform>();
            flashRect.anchorMin = Vector2.zero;
            flashRect.anchorMax = Vector2.one;
            flashRect.offsetMin = Vector2.zero;
            flashRect.offsetMax = Vector2.zero;
            var group = flash.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            var flashImage = flash.GetComponent<Image>();
            flashImage.color = new Color(0.95f, 0.025f, 0.025f, 1f);
            flashImage.raycastTarget = false;

            var aim = new GameObject(
                "Aim",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            aim.transform.SetParent(root.transform, false);
            var aimRect = aim.GetComponent<RectTransform>();
            aimRect.anchorMin = new Vector2(0.5f, 0.5f);
            aimRect.anchorMax = new Vector2(0.5f, 0.5f);
            aimRect.pivot = new Vector2(0.5f, 0.5f);
            aimRect.anchoredPosition = Vector2.zero;
            aimRect.sizeDelta = new Vector2(50f, 50f);
            var aimImage = aim.GetComponent<Image>();
            aimImage.sprite = RequireAimSprite();
            aimImage.color = Color.white;
            aimImage.raycastTarget = false;
            aimImage.maskable = false;
            aim.SetActive(false);

            root.GetComponent<ArenaCombatHitFlashView>()
                .ConfigureUiBindings(canvas, group, flashImage);
            root.GetComponent<ArenaCombatAimView>().Configure(aimImage);
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

        private static ArenaCombatNetworkView RequireArenaNetworkView(
            Scene scene)
        {
            ArenaCombatNetworkView result = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var candidates =
                    root.GetComponentsInChildren<ArenaCombatNetworkView>(true);
                for (var index = 0; index < candidates.Length; index++)
                {
                    if (result != null)
                    {
                        throw new InvalidOperationException(
                            "Arena Combat scene has multiple network views.");
                    }

                    result = candidates[index];
                }
            }

            if (result == null)
            {
                throw new InvalidOperationException(
                    "Arena Combat scene requires ArenaCombatNetworkView.");
            }

            return result;
        }

        private static Sprite RequireAimSprite()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(AimSpritePath);
            for (var index = 0; index < assets.Length; index++)
            {
                if (assets[index] is Sprite sprite &&
                    sprite.name == AimSpriteName)
                {
                    return sprite;
                }
            }

            throw new InvalidOperationException(
                "Arena Combat aim sprite is missing at '" +
                AimSpritePath + "'.");
        }

        private static ArenaCombatAimView RequireAimView(
            GameObject root,
            Sprite expectedSprite,
            string context)
        {
            var aimViews = root != null
                ? root.GetComponents<ArenaCombatAimView>()
                : Array.Empty<ArenaCombatAimView>();
            if (aimViews.Length != 1 ||
                !aimViews[0].HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    context + " requires one root ArenaCombatAimView with " +
                    "an authored Image binding.");
            }

            var aimView = aimViews[0];
            var aimImage = aimView.AimImage;
            if (aimImage.sprite != expectedSprite ||
                aimImage.raycastTarget ||
                aimImage.gameObject.activeSelf)
            {
                throw new InvalidOperationException(
                    context + " requires the authored Add aim sprite, " +
                    "disabled by default and non-blocking. Repair the prefab " +
                    "directly; setup will not overwrite its design.");
            }

            return aimView;
        }
    }
}
