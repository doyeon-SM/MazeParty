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
    /// Creates the shared UI prefab once and links one instance in Board.unity.
    /// Re-running setup preserves authored prefab design.
    /// </summary>
    public static class MinigameStartCountdownProjectSetup
    {
        public const string PrefabPath =
            "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigameCommonHud.prefab";
        private const string LegacyPrefabPath =
            "Assets/MazeParty/UI/Prefabs/MinigameStartCountdown.prefab";
        public const string BoardScenePath =
            "Assets/MazeParty/Scenes/Board/Board.unity";

        [MenuItem("MazeParty/Minigames/Install Shared Minigame HUD")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Stop Play mode before installing the shared HUD.");
            }

            EnsurePrefabExists();
            EnsureBoardSceneInstance();
            AssetDatabase.SaveAssets();
            Debug.Log("Shared minigame HUD is installed.");
        }

        [MenuItem("MazeParty/Minigames/Install Shared Minigame HUD", true)]
        private static bool CanInstall()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        public static GameObject EnsurePrefabExists()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(
                        LegacyPrefabPath) != null)
                {
                    var moveError = AssetDatabase.MoveAsset(
                        LegacyPrefabPath, PrefabPath);
                    if (!string.IsNullOrEmpty(moveError))
                    {
                        throw new InvalidOperationException(moveError);
                    }
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        PrefabPath);
                }
                else
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
            }

            EnsureCommonHudBindings(prefab);
            EnsureSpectatorControls();
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            var view = prefab != null
                ? prefab.GetComponent<MinigameStartCountdownView>()
                : null;
            var commonHud = prefab != null
                ? prefab.GetComponent<MinigameCommonHudView>()
                : null;
            if (view == null || !view.HasRequiredReferences ||
                commonHud == null || !commonHud.HasRequiredReferences ||
                prefab.GetComponent<Canvas>() == null)
            {
                throw new InvalidOperationException(
                    "The shared minigame HUD prefab needs its Canvas and " +
                    "serialized UI bindings.");
            }

            return prefab;
        }

        public static void EnsureBoardSceneInstance()
        {
            var prefab = EnsurePrefabExists();
            var scene = SceneManager.GetSceneByPath(BoardScenePath);
            var openedForSetup = !scene.IsValid() || !scene.isLoaded;
            if (openedForSetup)
            {
                scene = EditorSceneManager.OpenScene(
                    BoardScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                var match = FindRootComponent<NetworkMatchState>(scene);
                if (match == null)
                {
                    throw new InvalidOperationException(
                        "Board scene has no NetworkMatchState.");
                }

                var view = FindRootComponent<MinigameStartCountdownView>(scene);
                var changed = false;
                if (view == null)
                {
                    var instance = PrefabUtility.InstantiatePrefab(
                        prefab,
                        scene) as GameObject;
                    view = instance != null
                        ? instance.GetComponent<MinigameStartCountdownView>()
                        : null;
                    if (view == null)
                    {
                        throw new InvalidOperationException(
                            "Could not instantiate the countdown prefab.");
                    }

                    instance.name = "Minigame Common HUD";
                    changed = true;
                }

                if (view.transform.parent != null ||
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        view.gameObject) != PrefabPath)
                {
                    throw new InvalidOperationException(
                        "Common HUD must be a standalone Board scene " +
                        "instance of its authored prefab.");
                }

                if (view.MatchState != match)
                {
                    view.ConfigureMatch(match);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(view);
                    changed = true;
                }

                if (view.transform.localScale != Vector3.one)
                {
                    view.transform.localScale = Vector3.one;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(
                        view.transform);
                    changed = true;
                }

                if (changed)
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

        private static T FindRootComponent<T>(Scene scene)
            where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var component = root.GetComponent<T>();
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static void EnsureCommonHudBindings(GameObject prefab)
        {
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Shared minigame HUD prefab is missing.");
            }
            var existing = prefab.GetComponent<MinigameCommonHudView>();
            if (existing != null)
            {
                if (HasCommonHudCoreReferences(existing) && existing.TaggerAimRoot != null && existing.SpectatorText != null)
                {
                    return;
                }

                if (HasCommonHudCoreReferences(existing) &&
                    existing.TaggerAimRoot != null &&
                    existing.SpectatorText == null)
                {
                    AddMissingSpectatorLabel();
                    return;
                }

                if (!HasCommonHudCoreReferences(existing) ||
                    existing.TaggerAimRoot != null)
                {
                    throw new InvalidOperationException(
                        "Repair the shared HUD's serialized bindings " +
                        "on its prefab; setup will not replace its design.");
                }

                AddMissingTaggerAim();
                return;
            }

            MinigameTimerDialProjectSetup.EnsurePrefabExists();
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                root.name = "Minigame Common HUD";
                root.transform.localScale = Vector3.one;
                var timer = MinigameTimerDialProjectSetup.InstantiateTimer(
                    root.transform);
                var font = Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
                if (font == null)
                {
                    throw new InvalidOperationException(
                        "Unity LegacyRuntime.ttf was not found.");
                }

                var round = new GameObject("Round Label",
                    typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(Image));
                round.transform.SetParent(root.transform, false);
                var roundRect = round.GetComponent<RectTransform>();
                roundRect.anchorMin = roundRect.anchorMax =
                    new Vector2(1f, 1f);
                roundRect.pivot = new Vector2(1f, 1f);
                roundRect.anchoredPosition = new Vector2(-30f, -185f);
                roundRect.sizeDelta = new Vector2(144f, 34f);
                var background = round.GetComponent<Image>();
                background.color = new Color(0.018f, 0.029f,
                    0.064f, 0.86f);
                background.raycastTarget = false;

                var label = new GameObject("Round",
                    typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(Text));
                label.transform.SetParent(round.transform, false);
                var labelRect = label.GetComponent<RectTransform>();
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
                var text = label.GetComponent<Text>();
                text.font = font;
                text.fontSize = 19;
                text.fontStyle = FontStyle.Normal;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = new Color(0.92f, 0.97f, 1f);
                text.raycastTarget = false;
                text.text = "ROUND 1 / 3";

                var taggerAim = CreateTaggerAim(root.transform, font);
                var spectatorLabel = CreateSpectatorLabel(root.transform, font);
                var common = root.AddComponent<MinigameCommonHudView>();
                common.Configure(root.GetComponent<Canvas>(),
                    timer, round, text, taggerAim, spectatorLabel);
                round.SetActive(false);
                timer.gameObject.SetActive(false);
                SetUiLayer(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool HasCommonHudCoreReferences(
            MinigameCommonHudView common)
        {
            if (common == null || common.RootCanvas == null ||
                common.TimerDial == null ||
                !common.TimerDial.HasRequiredReferences ||
                common.RoundText == null)
            {
                return false;
            }

            var serialized = new SerializedObject(common);
            var roundProperty = serialized.FindProperty("roundRoot");
            var round = roundProperty != null
                ? roundProperty.objectReferenceValue as GameObject
                : null;
            return round != null &&
                   common.TimerDial.transform.IsChildOf(common.transform) &&
                   round.transform.IsChildOf(common.transform) &&
                   common.RoundText.transform.IsChildOf(round.transform);
        }

        private static void AddMissingTaggerAim()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var common = root.GetComponent<MinigameCommonHudView>();
                if (common == null ||
                    !HasCommonHudCoreReferences(common))
                {
                    throw new InvalidOperationException(
                        "Shared minigame HUD core bindings are incomplete.");
                }

                if (common.TaggerAimRoot != null)
                {
                    return;
                }

                var aimTransform = FindDescendant(
                    root.transform,
                    "Tagger Aim");
                var taggerAim = aimTransform != null
                    ? aimTransform.gameObject
                    : CreateTaggerAim(
                        root.transform,
                        RequireLegacyRuntimeFont());
                var serialized = new SerializedObject(common);
                var aimProperty = serialized.FindProperty("taggerAimRoot");
                if (aimProperty == null)
                {
                    throw new InvalidOperationException(
                        "MinigameCommonHudView has no taggerAimRoot binding.");
                }

                aimProperty.objectReferenceValue = taggerAim;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                SetUiLayer(taggerAim);
                root.transform.localScale = Vector3.one;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static GameObject CreateTaggerAim(Transform parent, Font font)
        {
            var aim = new GameObject("Tagger Aim", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            aim.transform.SetParent(parent, false);
            var rect = aim.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(50f, 50f);
            var image = aim.GetComponent<Image>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(
                         ArenaCombatHitFlashProjectSetup.AimSpritePath))
                if (asset is Sprite sprite && sprite.name == "Add") image.sprite = sprite;
            if (image.sprite == null)
                throw new InvalidOperationException("Tagger Aim requires the first-person Add sprite.");
            image.color = Color.white;
            image.raycastTarget = false;
            image.maskable = false;
            aim.SetActive(false);
            return aim;
        }

        private static void EnsureSpectatorControls()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab.GetComponent<MinigameCommonHudView>().HasSpectatorControls) return;
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var common = root.GetComponent<MinigameCommonHudView>();
                var label = common.SpectatorText;
                if (label == null) throw new InvalidOperationException("Spectator label binding is required.");
                // Only migrate the old label once. A damaged authored row must be repaired in place.
                if (common.SpectatorRoot != null || common.SpectatorSwitchIcon != null ||
                    common.SpectatorSwitchText != null || FindDescendant(root.transform, "Spectator Hint") != null)
                    throw new InvalidOperationException("Repair the spectator hint bindings on its existing prefab.");
                var sprites = AssetDatabase.LoadAssetAtPath<TMPro.TMP_SpriteAsset>(ItemVisualFeedbackProjectSetup.InputSpritePath);
                if (sprites == null) throw new InvalidOperationException("Shared input icon asset is required.");
                var character = sprites.spriteCharacterTable.Find(c => c.name == "ml");
                if (character == null) throw new InvalidOperationException("Shared left mouse icon is required.");
                var glyph = character.glyph.glyphRect;
                var row = new GameObject("Spectator Hint", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                row.transform.SetParent(root.transform, false);
                var rect = (RectTransform)row.transform;
                rect.anchorMin = rect.anchorMax = label.rectTransform.anchorMin;
                rect.pivot = label.rectTransform.pivot;
                rect.anchoredPosition = label.rectTransform.anchoredPosition;
                rect.sizeDelta = new Vector2(1100f, 42f);
                var layout = row.GetComponent<HorizontalLayoutGroup>();
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.spacing = 12f;
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = layout.childForceExpandHeight = false;
                label.transform.SetParent(row.transform, false);
                label.rectTransform.sizeDelta = new Vector2(500f, 42f);
                var icon = new GameObject("Mouse Left Icon", typeof(RectTransform), typeof(RawImage), typeof(LayoutElement)).GetComponent<RawImage>();
                icon.transform.SetParent(row.transform, false);
                icon.texture = sprites.spriteSheet; icon.raycastTarget = false;
                icon.uvRect = new Rect((float)glyph.x / icon.texture.width, (float)glyph.y / icon.texture.height,
                    (float)glyph.width / icon.texture.width, (float)glyph.height / icon.texture.height);
                var iconLayout = icon.GetComponent<LayoutElement>();
                iconLayout.preferredHeight = 38f; iconLayout.preferredWidth = 38f * glyph.width / glyph.height;
                var change = new GameObject("Switch Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
                change.transform.SetParent(row.transform, false); change.font = label.font;
                change.fontSize = label.fontSize; change.color = label.color;
                change.alignment = TextAnchor.MiddleCenter; change.raycastTarget = false; change.text = "SWITCH";
                common.ConfigureSpectatorControls(row, icon, change);
                row.SetActive(false); SetUiLayer(row);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static Text CreateSpectatorLabel(Transform parent, Font font)
        {
            var label = new GameObject("Spectator Label",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            label.transform.SetParent(parent, false);
            var rect = label.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -26f);
            rect.sizeDelta = new Vector2(500f, 42f);
            var text = label.GetComponent<Text>();
            text.font = font;
            text.fontSize = 26;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.supportRichText = true;
            label.SetActive(false);
            return text;
        }

        private static void AddMissingSpectatorLabel()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var common = root.GetComponent<MinigameCommonHudView>();
                if (!HasCommonHudCoreReferences(common) ||
                    common.TaggerAimRoot == null)
                {
                    throw new InvalidOperationException(
                        "Shared minigame HUD core bindings are incomplete.");
                }

                var existing = FindDescendant(root.transform,
                    "Spectator Label");
                var label = existing != null
                    ? existing.GetComponent<Text>()
                    : CreateSpectatorLabel(root.transform,
                        RequireLegacyRuntimeFont());
                if (label == null)
                {
                    throw new InvalidOperationException(
                        "Spectator Label has no Text component.");
                }
                var serialized = new SerializedObject(common);
                serialized.FindProperty("spectatorText").objectReferenceValue =
                    label;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                SetUiLayer(label.gameObject);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Font RequireLegacyRuntimeFont()
        {
            var font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            if (font == null)
            {
                throw new InvalidOperationException(
                    "Unity LegacyRuntime.ttf was not found.");
            }
            return font;
        }

        private static Transform FindDescendant(
            Transform root,
            string name)
        {
            for (var index = 0; index < root.childCount; index++)
            {
                var child = root.GetChild(index);
                if (child.name == name)
                {
                    return child;
                }

                var nested = FindDescendant(child, name);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static GameObject CreateTemplate()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                throw new InvalidOperationException(
                    "Unity LegacyRuntime.ttf was not found.");
            }

            var root = new GameObject(
                "Minigame Start Countdown",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(MinigameStartCountdownView));
            root.transform.localScale = Vector3.one;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var content = new GameObject(
                "Countdown Content",
                typeof(RectTransform));
            content.transform.SetParent(root.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0.5f, 0.5f);
            contentRect.anchorMax = new Vector2(0.5f, 0.5f);
            contentRect.pivot = new Vector2(0.5f, 0.5f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(220f, 220f);

            var backdrop = new GameObject(
                "Backdrop",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            backdrop.transform.SetParent(content.transform, false);
            var backdropRect = backdrop.GetComponent<RectTransform>();
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = Vector2.zero;
            backdropRect.offsetMax = Vector2.zero;
            var backdropImage = backdrop.GetComponent<Image>();
            backdropImage.color = new Color(0.035f, 0.075f, 0.13f, 0.78f);
            backdropImage.raycastTarget = false;

            var numeralObject = new GameObject(
                "Numeral",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            numeralObject.transform.SetParent(content.transform, false);
            var numeralRect = numeralObject.GetComponent<RectTransform>();
            numeralRect.anchorMin = Vector2.zero;
            numeralRect.anchorMax = Vector2.one;
            numeralRect.offsetMin = new Vector2(12f, 12f);
            numeralRect.offsetMax = new Vector2(-12f, -12f);
            var numeral = numeralObject.GetComponent<Text>();
            numeral.font = font;
            numeral.fontSize = 120;
            numeral.fontStyle = FontStyle.Normal;
            numeral.alignment = TextAnchor.MiddleCenter;
            numeral.color = Color.white;
            numeral.raycastTarget = false;
            numeral.text = "3";

            var promptObject = new GameObject("Combat Prompt", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text), typeof(LocalizedText));
            promptObject.transform.SetParent(content.transform, false);
            var prompt = promptObject.GetComponent<Text>();
            prompt.font = font;
            prompt.fontSize = 42;
            prompt.fontStyle = FontStyle.Normal;
            prompt.alignment = TextAnchor.MiddleCenter;
            prompt.color = numeral.color;
            prompt.raycastTarget = false;
            prompt.text = "FIGHT!";
            promptObject.GetComponent<LocalizedText>().Configure("FIGHT!");
            var promptRect = prompt.rectTransform;
            promptRect.anchorMin = promptRect.anchorMax = new Vector2(0.5f, 0f);
            promptRect.pivot = new Vector2(0.5f, 1f);
            promptRect.anchoredPosition = new Vector2(0f, -16f);
            promptRect.sizeDelta = new Vector2(500f, 70f);
            promptObject.SetActive(false);
            root.GetComponent<MinigameStartCountdownView>()
                .ConfigureUiBindings(canvas, content, numeral, prompt);
            content.SetActive(false);
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
