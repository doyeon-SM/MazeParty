using System;
using System.Linq;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class CeremonyStatisticsProjectSetup
    {
        public const string PrefabPath = "Assets/MazeParty/Prefabs/Board/UI/CeremonyStatisticsCanvas.prefab";
        public const string RigPath = "Assets/MazeParty/Prefabs/Board/World/StatisticsPortraitRig.prefab";
        private const string ScenePath = "Assets/MazeParty/Scenes/Board/Board.unity";
        private static readonly Color Panel = new Color(.025f, .04f, .075f, 1);
        private static readonly Color Row = new Color(.07f, .1f, .16f, 1);
        private static readonly Color Accent = new Color(.12f, .34f, .62f, 1);
        private static Sprite _rounded;

        [MenuItem("MazeParty/UI/Install Ceremony Statistics")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            var current = SceneManager.GetSceneByPath(ScenePath);
            if (current.IsValid() && current.isLoaded && current.isDirty) throw new InvalidOperationException("Save the Board scene first.");
            EnsureAssets();
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                EnsureSceneInstance(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            AssetDatabase.SaveAssets();
        }

        public static void EnsureSceneInstance(Scene scene)
        {
            EnsureAssets();
            if (scene.GetRootGameObjects().Any(x => x.GetComponentInChildren<CeremonyStatisticsBindings>(true) != null)) return;
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
        }

        private static void EnsureAssets()
        {
            EnsureRig();
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                if (!existing.GetComponent<CeremonyStatisticsBindings>().HasRequiredReferences)
                    throw new InvalidOperationException("Statistics prefab bindings require repair; design was preserved.");
                return;
            }
            _rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Ignore/Modern UI Pack/Textures/Border/Rounded/1024px/Rounded Filled 1024px.png");
            if (_rounded == null) throw new InvalidOperationException("Modern UI rounded sprite is missing.");
            var root = new GameObject("Ceremony Statistics Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CeremonyStatisticsBindings), typeof(StatisticsPortraitRenderer), typeof(CeremonyStatisticsView), typeof(LocalizedFontScope));
            try
            {
                var b = root.GetComponent<CeremonyStatisticsBindings>();
                b.RootCanvas = root.GetComponent<Canvas>(); b.RootCanvas.renderMode = RenderMode.ScreenSpaceOverlay; b.RootCanvas.sortingOrder = 920;
                b.RootRaycaster = root.GetComponent<GraphicRaycaster>();
                var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
                b.OpenButton = Button(root.transform, "Statistics Button", "Statistics", .70f, .16f, .92f, .225f);
                b.OpenButtonText = b.OpenButton.GetComponentInChildren<Text>();
                b.PopupRoot = PanelAt(root.transform, "Statistics Popup", 0, 0, 1, 1, new Color(0, 0, 0, .65f), false).gameObject;
                // 87% of each dimension occupies about three quarters of the screen.
                var panel = PanelAt(b.PopupRoot.transform, "Panel", .065f, .065f, .935f, .935f, Panel);
                b.Title = Text(panel, "Title", "Match statistics", .035f, .925f, .67f, .98f, 32);
                b.Countdown = Text(panel, "Countdown", "", .64f, .925f, .93f, .98f, 20, TextAnchor.MiddleRight);
                b.CloseButton = Button(panel, "Close Button", "×", .945f, .93f, .985f, .98f);
                b.Tabs = new Button[4]; b.TabLabels = new Text[4];
                var titles = new[] { "Results", "Gold", "Minigames", "Items & combat" };
                for (var i = 0; i < 4; i++)
                {
                    b.Tabs[i] = Button(panel, "Tab " + i, titles[i], .035f + i * .235f, .845f, .26f + i * .235f, .905f);
                    b.TabLabels[i] = b.Tabs[i].GetComponentInChildren<Text>();
                }
                b.AxisTitle = Text(panel, "Axis Title", "", .08f, .782f, .9f, .835f, 22);
                var chartRect = Rect(panel, "Chart", .10f, .335f, .855f, .765f);
                chartRect.gameObject.AddComponent<CanvasRenderer>();
                b.Chart = chartRect.gameObject.AddComponent<StatisticsChartGraphic>(); b.Chart.raycastTarget = true;
                b.XLabels = Labels(chartRect, "X Label", 21, 74, 28, 17);
                b.YLabels = Labels(chartRect, "Y Label", 5, 80, 28, 17);
                b.BarValues = Labels(chartRect, "Bar Value", 16, 70, 28, 18);
                b.CombatNames = Labels(chartRect, "Player Label", 12, 180, 25, 17);
                b.CombatTitles = Labels(chartRect, "Group Title", 3, 260, 34, 18);
                b.CombatPlayerNames = Labels(chartRect, "Combat Player", 4, 180, 36, 20);
                b.Heads = new RawImage[4];
                for (var slot = 0; slot < 4; slot++)
                {
                    var head = Rect(chartRect, "Head " + slot, 0, 0, 0, 0);
                    head.sizeDelta = new Vector2(58, 58);
                    b.Heads[slot] = head.gameObject.AddComponent<RawImage>(); b.Heads[slot].raycastTarget = false;
                }
                b.Description = Text(panel, "Description", "Loading match records…", .035f, .257f, .965f, .30f, 19);
                b.PlayerNames = new Text[4]; b.Summaries = new Text[4];
                for (var slot = 0; slot < 4; slot++)
                {
                    var card = PanelAt(panel, "Player Summary " + slot, .035f + slot * .235f, .075f, .26f + slot * .235f, .25f, Row);
                    var nameplate = PanelAt(card, "Name Background", .025f, .72f, .975f, .96f, new Color(.88f, .91f, .95f));
                    b.PlayerNames[slot] = Text(nameplate, "Name", "", .035f, 0, .965f, 1, 23);
                    b.PlayerNames[slot].supportRichText = false;
                    b.Summaries[slot] = Text(card, "Values", "", .045f, .04f, .955f, .70f, 18, TextAnchor.UpperLeft);
                }
                b.GoldOption = Button(panel, "Gold Option", "Show total earned", .72f, .015f, .965f, .065f);
                b.GoldOptionText = b.GoldOption.GetComponentInChildren<Text>();
                b.TooltipRoot = PanelAt(panel, "Damage Details", .45f, .38f, .94f, .77f, new Color(.035f, .06f, .11f, .99f)).gameObject;
                b.TooltipTitle = Text(b.TooltipRoot.transform, "Details Title", "", .035f, .84f, .965f, .97f, 22);
                b.TooltipTitle.supportRichText = false;
                var viewport = Rect(b.TooltipRoot.transform, "Viewport", .035f, .05f, .965f, .81f);
                viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .001f);
                viewport.gameObject.AddComponent<RectMask2D>();
                var content = Rect(viewport, "Details Content", 0, 1, 1, 1);
                content.pivot = new Vector2(.5f, 1);
                b.TooltipText = content.gameObject.AddComponent<Text>(); Style(b.TooltipText, 20, TextAnchor.UpperLeft);
                b.TooltipText.supportRichText = false; b.TooltipText.raycastTarget = true;
                var fitter = content.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                b.TooltipScroll = b.TooltipRoot.AddComponent<ScrollRect>(); b.TooltipScroll.viewport = viewport; b.TooltipScroll.content = content;
                b.TooltipScroll.horizontal = false; b.TooltipScroll.movementType = ScrollRect.MovementType.Clamped; b.TooltipScroll.scrollSensitivity = 25;
                b.Portraits = root.GetComponent<StatisticsPortraitRenderer>(); b.Portraits.Configure(AssetDatabase.LoadAssetAtPath<StatisticsPortraitRig>(RigPath));
                root.GetComponent<CeremonyStatisticsView>().Configure(b);
                b.TooltipRoot.SetActive(false); b.PopupRoot.SetActive(false); b.RootCanvas.enabled = false; b.RootRaycaster.enabled = false;
                root.GetComponent<LocalizedFontScope>().Apply();
                if (!b.HasRequiredReferences) throw new InvalidOperationException("Incomplete statistics UI.");
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void EnsureRig()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(RigPath) != null) return;
            var root = new GameObject("Statistics Portrait Rig", typeof(StatisticsPortraitRig));
            try
            {
                var rig = root.GetComponent<StatisticsPortraitRig>();
                var model = new GameObject("Portrait Model", typeof(PlayerAvatarVisual)); model.transform.SetParent(root.transform, false);
                var presentation = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(MultiplayerPresentationPrefabProjectSetup.PlayerPresentationPrefabPath));
                presentation.transform.SetParent(model.transform, false);
                rig.Presentation = presentation.GetComponent<PlayerAvatarPresentationBindings>();
                rig.Visual = model.GetComponent<PlayerAvatarVisual>(); rig.Visual.ConfigurePresentationBindings(rig.Presentation);
                var camera = new GameObject("Portrait Camera", typeof(Camera)); camera.transform.SetParent(root.transform, false);
                rig.PortraitCamera = camera.GetComponent<Camera>();
                rig.PortraitCamera.enabled = false; rig.PortraitCamera.orthographic = true;
                rig.PortraitCamera.cullingMask = 1 << 31; rig.PortraitCamera.clearFlags = CameraClearFlags.SolidColor;
                rig.PortraitCamera.backgroundColor = Color.clear; rig.PortraitCamera.allowHDR = false; rig.PortraitCamera.allowMSAA = false;
                rig.PortraitCamera.nearClipPlane = .01f; rig.PortraitCamera.farClipPlane = 20f;
                for (var i = 0; i < 2; i++)
                {
                    var light = new GameObject("Portrait Light " + i, typeof(Light)); light.transform.SetParent(root.transform, false);
                    light.transform.localRotation = Quaternion.Euler(25, i == 0 ? 155 : -30, 0);
                    var l = light.GetComponent<Light>(); l.type = LightType.Directional; l.intensity = i == 0 ? 1.5f : .6f; l.cullingMask = 1 << 31;
                }
                foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                PrefabUtility.SaveAsPrefabAsset(root, RigPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static RectTransform Rect(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false);
            r.anchorMin = new Vector2(x0, y0); r.anchorMax = new Vector2(x1, y1); r.offsetMin = r.offsetMax = Vector2.zero;
            return r;
        }
        private static RectTransform PanelAt(Transform parent, string name, float x0, float y0, float x1, float y1, Color color, bool rounded = true)
        {
            var r = Rect(parent, name, x0, y0, x1, y1); var image = r.gameObject.AddComponent<Image>(); image.color = color;
            if (rounded) { image.sprite = _rounded; image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 4; }
            return r;
        }
        private static Text Text(Transform parent, string name, string key, float x0, float y0, float x1, float y1, int size, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var r = Rect(parent, name, x0, y0, x1, y1); var t = r.gameObject.AddComponent<Text>(); Style(t, size, align); t.text = GameText.T(key); return t;
        }
        private static void Style(Text t, int size, TextAnchor align)
        {
            t.font = GameFonts.Current; t.fontSize = size; t.alignment = align; t.color = new Color(.92f, .95f, 1);
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate; t.raycastTarget = false;
        }
        private static Button Button(Transform parent, string name, string key, float x0, float y0, float x1, float y1)
        {
            var r = PanelAt(parent, name, x0, y0, x1, y1, Accent); var button = r.gameObject.AddComponent<Button>();
            button.targetGraphic = r.GetComponent<Image>(); Text(r, "Label", key, .035f, .05f, .965f, .95f, 23, TextAnchor.MiddleCenter);
            return button;
        }
        private static Text[] Labels(Transform parent, string prefix, int count, int width, int height, int fontSize)
        {
            var result = new Text[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = Text(parent, prefix + " " + i, "", 0, 0, 0, 0, fontSize, TextAnchor.MiddleCenter);
                result[i].rectTransform.sizeDelta = new Vector2(width, height);
            }
            return result;
        }
    }
}
