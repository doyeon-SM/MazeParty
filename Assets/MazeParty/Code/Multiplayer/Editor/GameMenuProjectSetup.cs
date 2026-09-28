using System;
using System.Linq;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MazeParty.Editor
{
    /// <summary>
    /// Creates the initial common game menu prefab (only when it is missing),
    /// installs one prefab instance into OnlineBootstrap.unity and removes the
    /// legacy lobby Leave/Quit buttons that the menu replaces. Existing prefab
    /// designs are validated but never restyled.
    /// </summary>
    public static class GameMenuProjectSetup
    {
        public const string CanvasPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/UI/GameMenuCanvas.prefab";
        public const string GearIconPath = "Assets/MazeParty/Art/UI/GearIcon.png";

        private const string BootstrapScenePath =
            "Assets/MazeParty/Scenes/Multiplayer/OnlineBootstrap.unity";
        private const string LobbyCanvasPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab";
        private const int CanvasSortingOrder = 1000;

        private static readonly string[] LegacyLobbyButtonNames =
        {
            "Leave Session Button",
            "Quit Game Button"
        };

        private static readonly Color PanelColor = new Color(0.025f, 0.04f, 0.075f, 1f);
        private static readonly Color RowColor = new Color(0.07f, 0.1f, 0.16f, 1f);
        private static readonly Color TextColor = new Color(0.92f, 0.95f, 1f, 1f);
        private static readonly Color MutedTextColor = new Color(0.62f, 0.7f, 0.82f, 1f);
        private static readonly Color AccentColor = new Color(0.12f, 0.34f, 0.62f, 1f);
        private static readonly Color ApplyColor = new Color(0.14f, 0.5f, 0.3f, 1f);
        private static readonly Color DangerColor = new Color(0.62f, 0.14f, 0.16f, 1f);
        private static readonly Color PauseColor = new Color(0.72f, 0.5f, 0.08f, 1f);

        [MenuItem("MazeParty/UI/Install Game Menu")]
        public static void InstallGameMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Game menu setup requires Edit Mode.");
            }

            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                if (SceneManager.GetSceneAt(index).isDirty)
                {
                    throw new InvalidOperationException(
                        "Save all open scenes before installing the game menu.");
                }
            }

            EnsureAssets();
            RemoveLegacyLobbyExitButtons();
            var scene = SceneManager.GetSceneByPath(BootstrapScenePath);
            var openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere)
            {
                scene = EditorSceneManager.OpenScene(
                    BootstrapScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                EnsureSceneInstance(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedHere)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("GameMenuCanvas.prefab and its OnlineBootstrap instance are ready.");
        }

        [MenuItem("MazeParty/UI/Install Game Menu", true)]
        private static bool ValidateInstallGameMenu()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        internal static GameObject EnsureAssets()
        {
            var gearSprite = EnsureGearSprite();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CanvasPrefabPath);
            if (prefab == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(CanvasPrefabPath) != null)
                {
                    throw new InvalidOperationException(
                        "An incompatible asset already exists at " + CanvasPrefabPath + ".");
                }

                var template = CreateCanvasTemplate(gearSprite);
                try
                {
                    prefab = PrefabUtility.SaveAsPrefabAsset(template, CanvasPrefabPath);
                }
                finally
                {
                    Object.DestroyImmediate(template);
                }
            }

            ValidateCanvasPrefab(prefab);
            return prefab;
        }

        internal static void EnsureSceneInstance(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException(
                    "Game menu installation requires a loaded scene.");
            }

            var prefab = EnsureAssets();
            var views = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<GameMenuView>(true))
                .ToArray();
            if (views.Length > 1)
            {
                throw new InvalidOperationException(scene.path + " has multiple game menus.");
            }

            if (views.Length == 0)
            {
                var instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (instance == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate GameMenuCanvas.prefab.");
                }

                instance.name = "Game Menu Canvas";
                views = new[] { instance.GetComponent<GameMenuView>() };
                EditorSceneManager.MarkSceneDirty(scene);
            }

            var root = views[0].gameObject;
            if (root.transform.parent != null ||
                views[0].Bindings == null ||
                !views[0].Bindings.HasRequiredReferences ||
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) != CanvasPrefabPath)
            {
                throw new InvalidOperationException(scene.path + " has an invalid game menu.");
            }
        }

        /// <summary>The common menu owns leaving and quitting; drop the old lobby buttons.</summary>
        internal static void RemoveLegacyLobbyExitButtons()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(LobbyCanvasPrefabPath) == null)
            {
                return;
            }

            var contents = PrefabUtility.LoadPrefabContents(LobbyCanvasPrefabPath);
            try
            {
                var removed = false;
                foreach (var name in LegacyLobbyButtonNames)
                {
                    var target = contents.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(candidate => candidate.name == name);
                    if (target != null)
                    {
                        Object.DestroyImmediate(target.gameObject);
                        removed = true;
                    }
                }

                if (removed)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, LobbyCanvasPrefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static Sprite EnsureGearSprite()
        {
            var importer = AssetImporter.GetAtPath(GearIconPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    "The gear icon texture is missing at " + GearIconPath + ".");
            }

            if (importer.textureType != TextureImporterType.Sprite ||
                importer.spriteImportMode != SpriteImportMode.Single ||
                !importer.alphaIsTransparency ||
                importer.mipmapEnabled)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(GearIconPath);
            if (sprite == null)
            {
                throw new InvalidOperationException("Could not load the gear icon sprite.");
            }

            return sprite;
        }

        private static GameObject CreateCanvasTemplate(Sprite gearSprite)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            var root = new GameObject(
                "GameMenuCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(LocalizedFontScope),
                typeof(GameMenuBindings),
                typeof(GameMenuView));
            root.layer = LayerMask.NameToLayer("UI");
            root.transform.localScale = Vector3.one;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            var raycaster = root.GetComponent<GraphicRaycaster>();

            // Player pause banner: visible to everyone while a player pause runs.
            var bannerRoot = CreateRect("Pause Banner", root.transform);
            Stretch(bannerRoot);
            var bannerDim = bannerRoot.gameObject.AddComponent<Image>();
            bannerDim.color = new Color(0f, 0f, 0f, 0.35f);
            bannerDim.raycastTarget = false;
            var bannerPanel = CreatePanel("Pause Panel", bannerRoot, uiSprite, new Vector2(0f, 250f), 760f);
            CreateLabel(bannerPanel, "Pause Title", "GAME PAUSED", font, 34, PauseColor, 48f, true);
            var bannerMessage = CreateLabel(bannerPanel, "Pause Message", "Paused by Player.", font, 26, TextColor, 40f, false);
            var bannerTimer = CreateLabel(bannerPanel, "Pause Timer", "5:00", font, 52, TextColor, 64f, false);
            var releaseButton = CreateButton(bannerPanel, "Pause Release Button", "Release Pause", font, uiSprite, PauseColor, 56f, true, out _);

            // Gear button (lobby and waiting room).
            var gearRect = CreateRect("Gear Button", root.transform);
            gearRect.anchorMin = new Vector2(1f, 1f);
            gearRect.anchorMax = new Vector2(1f, 1f);
            gearRect.pivot = new Vector2(1f, 1f);
            gearRect.anchoredPosition = new Vector2(-24f, -24f);
            gearRect.sizeDelta = new Vector2(72f, 72f);
            var gearBackground = gearRect.gameObject.AddComponent<Image>();
            gearBackground.sprite = uiSprite;
            gearBackground.type = Image.Type.Sliced;
            gearBackground.color = PanelColor;
            var gearButton = gearRect.gameObject.AddComponent<Button>();
            gearButton.targetGraphic = gearBackground;
            ApplyButtonColors(gearButton);
            var gearIconRect = CreateRect("Gear Icon", gearRect);
            Stretch(gearIconRect, 12f);
            var gearIcon = gearIconRect.gameObject.AddComponent<Image>();
            gearIcon.sprite = gearSprite;
            gearIcon.preserveAspect = true;
            gearIcon.raycastTarget = false;
            gearIcon.color = TextColor;

            // Settings menu.
            var menuRoot = CreateBlocker("Menu", root.transform, 0.55f);
            var menuPanel = CreatePanel("Menu Panel", menuRoot, uiSprite, Vector2.zero, 760f);
            var header = CreateRow(menuPanel, "Header", 56f);
            var title = CreateText(header, "Title", "SETTINGS", font, 34, TextColor, TextAnchor.MiddleLeft, true);
            Anchor(title.rectTransform, 0f, 0.8f);
            var closeButton = CreateButton(header, "Close Button", "X", font, uiSprite, RowColor, 0f, false, out var closeText);
            var closeRect = (RectTransform)closeButton.transform;
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(52f, 52f);
            closeRect.anchoredPosition = Vector2.zero;
            closeText.fontSize = 26;
            closeText.resizeTextMaxSize = 26;

            CreateLabel(menuPanel, "Sound Header", "SOUND", font, 20, MutedTextColor, 30f, true)
                .alignment = TextAnchor.MiddleLeft;
            var master = CreateSliderRow(menuPanel, "Master Row", "Master", font, uiSprite, out var masterValue);
            var sfx = CreateSliderRow(menuPanel, "Sfx Row", "SFX", font, uiSprite, out var sfxValue);
            var bgm = CreateSliderRow(menuPanel, "Bgm Row", "BGM", font, uiSprite, out var bgmValue);

            var languageRow = CreateRow(menuPanel, "Language Row", 52f);
            var languageLabel = CreateText(languageRow, "Label", "Language", font, 24, TextColor, TextAnchor.MiddleLeft, true);
            Anchor(languageLabel.rectTransform, 0f, 0.34f);
            var dropdown = CreateDropdown(languageRow, font);
            Anchor((RectTransform)dropdown.transform, 0.36f, 1f, 4f);

            var displayRow = CreateRow(menuPanel, "Display Row", 52f);
            var displayLabel = CreateText(displayRow, "Label", "Screen Mode", font, 24, TextColor, TextAnchor.MiddleLeft, true);
            Anchor(displayLabel.rectTransform, 0f, 0.34f);
            var previous = CreateButton(displayRow, "Previous Button", "<", font, uiSprite, RowColor, 0f, false, out _);
            Anchor((RectTransform)previous.transform, 0.36f, 0.44f, 4f);
            var displayValueBackground = CreateRect("Value Background", displayRow);
            Anchor(displayValueBackground, 0.45f, 0.91f, 4f);
            var valueImage = displayValueBackground.gameObject.AddComponent<Image>();
            valueImage.sprite = uiSprite;
            valueImage.type = Image.Type.Sliced;
            valueImage.color = RowColor;
            valueImage.raycastTarget = false;
            var displayValue = CreateText(displayValueBackground, "Value", "Borderless Fullscreen", font, 22, TextColor, TextAnchor.MiddleCenter, false);
            Stretch(displayValue.rectTransform);
            var next = CreateButton(displayRow, "Next Button", ">", font, uiSprite, RowColor, 0f, false, out _);
            Anchor((RectTransform)next.transform, 0.92f, 1f, 4f);

            CreateSelectorRow(
                menuPanel, "Resolution Row", "Resolution", "1920 x 1080",
                font, uiSprite, out var resolutionPrevious,
                out var resolutionNext, out var resolutionValue);
            CreateSelectorRow(
                menuPanel, "Quality Row", "Quality", "High",
                font, uiSprite, out var qualityPrevious,
                out var qualityNext, out var qualityValue);
            CreateSelectorRow(
                menuPanel, "Frame Rate Row", "Frame Limit", "60 FPS",
                font, uiSprite, out var framePrevious,
                out var frameNext, out var frameValue);
            var mouseSensitivity = CreateSliderRow(
                menuPanel, "Mouse Sensitivity Row", "Mouse Sensitivity",
                font, uiSprite, out var mouseSensitivityValue);
            mouseSensitivity.minValue = GameSettingsData.MinimumMouseSensitivity;
            mouseSensitivity.maxValue = GameSettingsData.MaximumMouseSensitivity;
            mouseSensitivity.value = GameSettingsData.DefaultMouseSensitivity;
            mouseSensitivityValue.text = "1.00x";
            var invertY = CreateToggleRow(
                menuPanel, "Invert Y Row", "Invert Y", font, uiSprite);
            var reduceShake = CreateToggleRow(
                menuPanel, "Reduce Screen Shake Row", "Reduce Screen Shake",
                font, uiSprite);
            var reduceFlashes = CreateToggleRow(
                menuPanel, "Reduce Flashes Row", "Reduce Flashes",
                font, uiSprite);

            CreateSpacer(menuPanel, 6f);
            var pauseButton = CreateButton(menuPanel, "Pause Button", "Request Pause", font, uiSprite, PauseColor, 54f, false, out var pauseText);
            var applyButton = CreateButton(menuPanel, "Apply Button", "Apply", font, uiSprite, ApplyColor, 54f, true, out var applyText);
            applyText.verticalOverflow = VerticalWrapMode.Overflow;
            applyText.rectTransform.offsetMin = new Vector2(8f, 4f);
            applyText.rectTransform.offsetMax = new Vector2(-8f, -4f);
            var exitButton = CreateButton(menuPanel, "Exit Button", "Quit Game", font, uiSprite, DangerColor, 54f, false, out var exitText);

            // In-game leave confirmation.
            var confirmRoot = CreateBlocker("Leave Confirm", root.transform, 0.6f);
            var confirmPanel = CreatePanel("Confirm Panel", confirmRoot, uiSprite, Vector2.zero, 620f);
            CreateLabel(confirmPanel, "Message", "Are you sure you want to leave?", font, 28, TextColor, 90f, true);
            var confirmButtons = CreateRow(confirmPanel, "Buttons", 56f);
            var confirmLeave = CreateButton(confirmButtons, "Leave Button", "Leave", font, uiSprite, DangerColor, 0f, true, out _);
            Anchor((RectTransform)confirmLeave.transform, 0f, 0.48f);
            var confirmCancel = CreateButton(confirmButtons, "Cancel Button", "Cancel", font, uiSprite, AccentColor, 0f, true, out _);
            Anchor((RectTransform)confirmCancel.transform, 0.52f, 1f);

            // Notice popup (for example who ended the match).
            var noticeRoot = CreateBlocker("Notice", root.transform, 0.6f);
            var noticePanel = CreatePanel("Notice Panel", noticeRoot, uiSprite, Vector2.zero, 680f);
            var noticeMessage = CreateLabel(noticePanel, "Message", "The game was ended by Player.", font, 28, TextColor, 110f, false);
            var noticeOk = CreateButton(noticePanel, "OK Button", "OK", font, uiSprite, AccentColor, 56f, true, out _);

            bannerRoot.gameObject.SetActive(false);
            menuRoot.gameObject.SetActive(false);
            confirmRoot.gameObject.SetActive(false);
            noticeRoot.gameObject.SetActive(false);

            var bindings = root.GetComponent<GameMenuBindings>();
            bindings.Configure(
                canvas,
                raycaster,
                gearButton,
                menuRoot.gameObject,
                closeButton,
                master,
                masterValue,
                sfx,
                sfxValue,
                bgm,
                bgmValue,
                dropdown,
                previous,
                next,
                displayValue,
                resolutionPrevious,
                resolutionNext,
                resolutionValue,
                qualityPrevious,
                qualityNext,
                qualityValue,
                framePrevious,
                frameNext,
                frameValue,
                mouseSensitivity,
                mouseSensitivityValue,
                invertY,
                reduceShake,
                reduceFlashes,
                pauseButton,
                pauseText,
                applyButton,
                exitButton,
                exitText,
                confirmRoot.gameObject,
                confirmLeave,
                confirmCancel,
                noticeRoot.gameObject,
                noticeMessage,
                noticeOk,
                bannerRoot.gameObject,
                bannerMessage,
                bannerTimer,
                releaseButton);
            root.GetComponent<GameMenuView>().Configure(bindings);
            SetLayerRecursively(root.transform, LayerMask.NameToLayer("UI"));
            return root;
        }

        private static Slider CreateSliderRow(
            RectTransform parent,
            string name,
            string label,
            Font font,
            Sprite uiSprite,
            out Text valueText)
        {
            var row = CreateRow(parent, name, 48f);
            var labelText = CreateText(row, "Label", label, font, 24, TextColor, TextAnchor.MiddleLeft, true);
            Anchor(labelText.rectTransform, 0f, 0.34f);

            var slider = DefaultControls.CreateSlider(DefaultResources(uiSprite)).GetComponent<Slider>();
            slider.name = "Slider";
            var sliderRect = (RectTransform)slider.transform;
            sliderRect.SetParent(row, false);
            Anchor(sliderRect, 0.36f, 0.86f, 14f);
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.value = 1f;
            var fill = slider.fillRect != null ? slider.fillRect.GetComponent<Image>() : null;
            if (fill != null)
            {
                fill.color = new Color(0.25f, 0.6f, 1f, 1f);
            }

            var background = sliderRect.Find("Background");
            if (background != null)
            {
                background.GetComponent<Image>().color = RowColor;
            }

            valueText = CreateText(row, "Value", "100", font, 22, MutedTextColor, TextAnchor.MiddleRight, false);
            Anchor(valueText.rectTransform, 0.88f, 1f);
            return slider;
        }

        private static void CreateSelectorRow(
            RectTransform parent,
            string name,
            string label,
            string value,
            Font font,
            Sprite uiSprite,
            out Button previous,
            out Button next,
            out Text valueText)
        {
            var row = CreateRow(parent, name, 52f);
            var labelText = CreateText(
                row, "Label", label, font, 24, TextColor,
                TextAnchor.MiddleLeft, true);
            Anchor(labelText.rectTransform, 0f, 0.34f);
            previous = CreateButton(
                row, "Previous Button", "<", font, uiSprite,
                RowColor, 0f, false, out _);
            Anchor((RectTransform)previous.transform, 0.36f, 0.44f, 4f);
            var valueBackground = CreateRect("Value Background", row);
            Anchor(valueBackground, 0.45f, 0.91f, 4f);
            var image = valueBackground.gameObject.AddComponent<Image>();
            image.sprite = uiSprite;
            image.type = Image.Type.Sliced;
            image.color = RowColor;
            image.raycastTarget = false;
            valueText = CreateText(
                valueBackground, "Value", value, font, 22, TextColor,
                TextAnchor.MiddleCenter, false);
            Stretch(valueText.rectTransform);
            next = CreateButton(
                row, "Next Button", ">", font, uiSprite,
                RowColor, 0f, false, out _);
            Anchor((RectTransform)next.transform, 0.92f, 1f, 4f);
        }

        private static Toggle CreateToggleRow(
            RectTransform parent,
            string name,
            string label,
            Font font,
            Sprite uiSprite)
        {
            var row = CreateRow(parent, name, 44f);
            var labelText = CreateText(
                row, "Label", label, font, 24, TextColor,
                TextAnchor.MiddleLeft, true);
            Anchor(labelText.rectTransform, 0f, 0.82f);
            var toggleObject =
                DefaultControls.CreateToggle(DefaultResources(uiSprite));
            toggleObject.name = "Toggle";
            var rect = (RectTransform)toggleObject.transform;
            rect.SetParent(row, false);
            Anchor(rect, 0.9f, 1f, 4f);
            var toggle = toggleObject.GetComponent<Toggle>();
            var builtinLabel = toggleObject.transform.Find("Label");
            if (builtinLabel != null)
            {
                builtinLabel.gameObject.SetActive(false);
            }
            toggle.isOn = false;
            var soundEmitter = toggleObject.AddComponent<UiSoundEmitter>();
            soundEmitter.Configure(SoundKeys.UiHover, SoundKeys.UiClick);
            return toggle;
        }

        private static Dropdown CreateDropdown(RectTransform parent, Font font)
        {
            var uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            var dropdownObject = DefaultControls.CreateDropdown(DefaultResources(uiSprite));
            dropdownObject.name = "Language Dropdown";
            var rect = (RectTransform)dropdownObject.transform;
            rect.SetParent(parent, false);
            var dropdown = dropdownObject.GetComponent<Dropdown>();
            dropdownObject.GetComponent<Image>().color = RowColor;
            foreach (var text in dropdownObject.GetComponentsInChildren<Text>(true))
            {
                text.font = font;
                text.fontSize = 22;
                text.color = TextColor;
            }

            var template = dropdown.template;
            template.sizeDelta = new Vector2(template.sizeDelta.x, 4f * 40f + 8f);
            template.GetComponent<Image>().color = PanelColor;
            var item = template.GetComponentInChildren<Toggle>(true);
            if (item != null)
            {
                var itemRect = (RectTransform)item.transform;
                itemRect.sizeDelta = new Vector2(itemRect.sizeDelta.x, 40f);
                var content = (RectTransform)itemRect.parent;
                content.sizeDelta = new Vector2(content.sizeDelta.x, 40f);
                var itemBackground = item.targetGraphic as Image;
                if (itemBackground != null)
                {
                    itemBackground.color = RowColor;
                }
            }

            dropdown.ClearOptions();
            return dropdown;
        }

        private static DefaultControls.Resources DefaultResources(Sprite uiSprite)
        {
            return new DefaultControls.Resources
            {
                standard = uiSprite,
                background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
                inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
                knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
                dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
                mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd")
            };
        }

        private static RectTransform CreateBlocker(string name, Transform parent, float alpha)
        {
            var rect = CreateRect(name, parent);
            Stretch(rect);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, alpha);
            image.raycastTarget = true;
            return rect;
        }

        private static RectTransform CreatePanel(
            string name,
            RectTransform parent,
            Sprite sprite,
            Vector2 position,
            float width)
        {
            var rect = CreateRect(name, parent);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(width, 0f);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = PanelColor;
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(32, 32, 20, 20);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        private static RectTransform CreateRow(RectTransform parent, string name, float height)
        {
            var rect = CreateRect(name, parent);
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
            return rect;
        }

        private static void CreateSpacer(RectTransform parent, float height)
        {
            CreateRow(parent, "Spacer", height);
        }

        private static Text CreateLabel(
            RectTransform parent,
            string name,
            string value,
            Font font,
            int fontSize,
            Color color,
            float height,
            bool localized)
        {
            var row = CreateRow(parent, name, height);
            var text = row.gameObject.AddComponent<Text>();
            ConfigureText(text, value, font, fontSize, color, TextAnchor.MiddleCenter);
            if (localized)
            {
                row.gameObject.AddComponent<LocalizedText>().Configure(value);
            }

            return text;
        }

        private static Text CreateText(
            RectTransform parent,
            string name,
            string value,
            Font font,
            int fontSize,
            Color color,
            TextAnchor alignment,
            bool localized)
        {
            var rect = CreateRect(name, parent);
            Stretch(rect);
            var text = rect.gameObject.AddComponent<Text>();
            ConfigureText(text, value, font, fontSize, color, alignment);
            if (localized)
            {
                rect.gameObject.AddComponent<LocalizedText>().Configure(value);
            }

            return text;
        }

        private static void ConfigureText(
            Text text,
            string value,
            Font font,
            int fontSize,
            Color color,
            TextAnchor alignment)
        {
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.Max(12, fontSize / 2);
            text.resizeTextMaxSize = fontSize;
        }

        private static Button CreateButton(
            RectTransform parent,
            string name,
            string label,
            Font font,
            Sprite sprite,
            Color color,
            float layoutHeight,
            bool localizedLabel,
            out Text labelText)
        {
            var rect = CreateRect(name, parent);
            if (layoutHeight > 0f)
            {
                var element = rect.gameObject.AddComponent<LayoutElement>();
                element.preferredHeight = layoutHeight;
                element.minHeight = layoutHeight;
            }

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ApplyButtonColors(button);
            labelText = CreateText(rect, "Label", label, font, 24, Color.white, TextAnchor.MiddleCenter, localizedLabel);
            Stretch(labelText.rectTransform, 8f);
            return button;
        }

        private static void ApplyButtonColors(Button button)
        {
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.88f, 0.94f, 1f, 1f);
            colors.pressedColor = new Color(0.7f, 0.8f, 0.95f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.45f, 0.48f, 0.54f, 0.6f);
            button.colors = colors;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void Anchor(RectTransform rect, float minX, float maxX, float verticalInset = 0f)
        {
            rect.anchorMin = new Vector2(minX, 0f);
            rect.anchorMax = new Vector2(maxX, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(0f, verticalInset);
            rect.offsetMax = new Vector2(0f, -verticalInset);
        }

        private static void ValidateCanvasPrefab(GameObject prefab)
        {
            var bindings = prefab != null ? prefab.GetComponent<GameMenuBindings>() : null;
            var view = prefab != null ? prefab.GetComponent<GameMenuView>() : null;
            if (bindings == null || view == null ||
                !bindings.HasRequiredReferences ||
                view.Bindings != bindings ||
                prefab.GetComponentsInChildren<Canvas>(true).Length != 1 ||
                prefab.GetComponent<Canvas>() != bindings.RootCanvas ||
                prefab.GetComponent<GraphicRaycaster>() != bindings.RootRaycaster)
            {
                throw new InvalidOperationException(
                    "GameMenuCanvas.prefab has incomplete bindings. Repair the prefab " +
                    "without recreating it so designer changes are preserved.");
            }
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root)
            {
                SetLayerRecursively(child, layer);
            }
        }
    }
}
